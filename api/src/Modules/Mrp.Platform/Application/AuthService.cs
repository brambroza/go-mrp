using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Mrp.Platform.Domain;
using Mrp.Platform.Persistence;
using Mrp.SharedKernel.Domain;
using Mrp.SharedKernel.Persistence;
using Mrp.SharedKernel.Tenancy;
using Mrp.SharedKernel.Web;

namespace Mrp.Platform.Application;

/// <summary>Sign-up, sign-in, token refresh and two-factor setup.</summary>
public sealed class AuthService(
    PlatformDbContext db,
    DbSession session,
    ITenantContext tenantContext,
    UserManager<AppUser> users,
    RoleManager<AppRole> roles,
    TokenService tokens,
    TimeProvider clock)
{
    private const string AuthenticatorIssuer = "MRP";

    /// <summary>Creates a tenant, its Owner role and first user, then signs the user in.</summary>
    public Task<TokenResponse> SignupAsync(SignupRequest request, CancellationToken cancellationToken)
    {
        var slug = Tenant.NormalizeSlug(request.CompanyCode);
        return session.ExecuteInTransactionAsync(
            async ct =>
            {
                if (await db.Tenants.AnyAsync(t => t.Slug == slug, ct))
                {
                    throw DomainException.Conflict("platform.tenant.slug_taken", "This company code is already in use.");
                }

                var tenant = new Tenant(slug, request.CompanyName, request.Plan);
                db.Tenants.Add(tenant);
                await db.SaveChangesAsync(ct);
                tenantContext.Set(tenant.Id, null);

                var owner = new AppRole { Name = AppRole.OwnerRoleName, IsSystem = true };
                EnsureSucceeded(await roles.CreateAsync(owner));
                EnsureSucceeded(await roles.AddClaimAsync(owner, new Claim(MrpClaims.Permission, MrpClaims.AllPermissions)));

                var user = new AppUser
                {
                    UserName = request.Email.Trim(),
                    Email = request.Email.Trim(),
                    DisplayName = request.DisplayName.Trim(),
                    Language = tenant.DefaultLanguage,
                };
                EnsureSucceeded(await users.CreateAsync(user, request.Password));
                EnsureSucceeded(await users.AddToRoleAsync(user, AppRole.OwnerRoleName));
                tenantContext.Set(tenant.Id, user.Id);

                return await IssueAsync(user, tenant, mfa: false, device: null, ct);
            },
            cancellationToken);
    }

    /// <summary>Verifies credentials (and the 2FA code when enabled) and issues tokens.</summary>
    public async Task<TokenResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        var slug = request.CompanyCode.Trim().ToLowerInvariant();
        var tenant = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Slug == slug, cancellationToken);
        if (tenant is null)
        {
            // Same response as a wrong password so company codes cannot be enumerated.
            throw InvalidCredentials();
        }

        tenantContext.Set(tenant.Id, null);
        var user = await users.FindByNameAsync(request.UserName.Trim()) ?? await users.FindByEmailAsync(request.UserName.Trim());
        if (user is null || !user.IsActive)
        {
            throw InvalidCredentials();
        }

        if (await users.IsLockedOutAsync(user))
        {
            throw new DomainException("platform.auth.locked_out", "Too many failed attempts. Try again later.", 423);
        }

        if (!await users.CheckPasswordAsync(user, request.Password))
        {
            await users.AccessFailedAsync(user);
            throw InvalidCredentials();
        }

        if (!tenant.CanSignIn)
        {
            throw DomainException.Forbidden("platform.tenant.suspended", "This company account is suspended.");
        }

        var mfa = false;
        if (user.TwoFactorEnabled)
        {
            if (string.IsNullOrWhiteSpace(request.TwoFactorCode))
            {
                throw new DomainException("platform.auth.two_factor_required", "Two-factor code required.", 401);
            }

            var code = request.TwoFactorCode.Replace(" ", string.Empty, StringComparison.Ordinal);
            if (!await users.VerifyTwoFactorTokenAsync(user, TokenOptions.DefaultAuthenticatorProvider, code))
            {
                await users.AccessFailedAsync(user);
                throw new DomainException("platform.auth.two_factor_invalid", "Two-factor code is not valid.", 401);
            }

            mfa = true;
        }

        await users.ResetAccessFailedCountAsync(user);
        tenantContext.Set(tenant.Id, user.Id);
        return await IssueAsync(user, tenant, mfa, request.Device, cancellationToken);
    }

    /// <summary>Rotates a refresh token. Reuse of an already rotated token revokes every session of the user.</summary>
    public async Task<TokenResponse> RefreshAsync(RefreshRequest request, CancellationToken cancellationToken)
    {
        if (!TokenService.TryReadTenant(request.RefreshToken, out var tenantId))
        {
            throw InvalidRefreshToken();
        }

        tenantContext.Set(tenantId, null);
        var hash = TokenService.Hash(request.RefreshToken);
        var stored = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);
        if (stored is null)
        {
            throw InvalidRefreshToken();
        }

        var now = clock.GetUtcNow();
        if (!stored.IsUsable(now))
        {
            if (stored.RevokedAt is not null && stored.ReplacedById is not null)
            {
                await RevokeAllAsync(stored.UserId, now, cancellationToken);
            }

            throw InvalidRefreshToken();
        }

        var user = await users.FindByIdAsync(stored.UserId.ToString());
        var tenant = await db.Tenants.AsNoTracking().FirstAsync(t => t.Id == tenantId, cancellationToken);
        if (user is null || !user.IsActive || !tenant.CanSignIn)
        {
            stored.Revoke(now);
            await db.SaveChangesAsync(cancellationToken);
            throw InvalidRefreshToken();
        }

        tenantContext.Set(tenantId, user.Id);
        return await IssueAsync(user, tenant, stored.Mfa, stored.Device, cancellationToken, stored);
    }

    /// <summary>Revokes a refresh token; unknown tokens are ignored.</summary>
    public async Task LogoutAsync(RefreshRequest request, CancellationToken cancellationToken)
    {
        if (!TokenService.TryReadTenant(request.RefreshToken, out var tenantId))
        {
            return;
        }

        tenantContext.Set(tenantId, null);
        var hash = TokenService.Hash(request.RefreshToken);
        var stored = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);
        if (stored is not null)
        {
            stored.Revoke(clock.GetUtcNow());
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    /// <summary>Profile of the signed-in user.</summary>
    public async Task<UserProfile> GetProfileAsync(CancellationToken cancellationToken)
    {
        var user = await RequireCurrentUserAsync();
        var tenant = await db.Tenants.AsNoTracking().FirstAsync(t => t.Id == user.TenantId, cancellationToken);
        var (roleNames, permissions) = await LoadGrantsAsync(user, cancellationToken);
        return ToProfile(user, tenant, roleNames, permissions);
    }

    /// <summary>Generates (or returns) the authenticator key for the signed-in user.</summary>
    public async Task<TwoFactorSetupResponse> SetupTwoFactorAsync()
    {
        var user = await RequireCurrentUserAsync();
        if (user.TwoFactorEnabled)
        {
            throw DomainException.Conflict("platform.auth.two_factor_already_enabled", "Two-factor authentication is already enabled.");
        }

        EnsureSucceeded(await users.ResetAuthenticatorKeyAsync(user));
        var key = await users.GetAuthenticatorKeyAsync(user)
            ?? throw new InvalidOperationException("Authenticator key was not generated.");
        var label = Uri.EscapeDataString($"{AuthenticatorIssuer}:{user.UserName}");
        var uri = $"otpauth://totp/{label}?secret={key}&issuer={Uri.EscapeDataString(AuthenticatorIssuer)}&digits=6";
        return new TwoFactorSetupResponse(key, uri);
    }

    /// <summary>Turns 2FA on after verifying a code from the authenticator app.</summary>
    public async Task EnableTwoFactorAsync(TwoFactorCodeRequest request)
    {
        var user = await RequireCurrentUserAsync();
        await VerifyCodeAsync(user, request.Code);
        EnsureSucceeded(await users.SetTwoFactorEnabledAsync(user, true));
    }

    /// <summary>Turns 2FA off after verifying a current code; all sessions are revoked.</summary>
    public async Task DisableTwoFactorAsync(TwoFactorCodeRequest request, CancellationToken cancellationToken)
    {
        var user = await RequireCurrentUserAsync();
        await VerifyCodeAsync(user, request.Code);
        EnsureSucceeded(await users.SetTwoFactorEnabledAsync(user, false));
        EnsureSucceeded(await users.ResetAuthenticatorKeyAsync(user));
        await RevokeAllAsync(user.Id, clock.GetUtcNow(), cancellationToken);
    }

    /// <summary>Revokes every usable refresh token of a user.</summary>
    public async Task RevokeAllAsync(Guid userId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var active = await db.RefreshTokens.Where(t => t.UserId == userId && t.RevokedAt == null).ToListAsync(cancellationToken);
        foreach (var token in active)
        {
            token.Revoke(now);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Throws a validation error built from Identity errors.</summary>
    internal static void EnsureSucceeded(IdentityResult result)
    {
        if (result.Succeeded)
        {
            return;
        }

        var first = result.Errors.First();
        var status = first.Code.StartsWith("Duplicate", StringComparison.Ordinal) ? 409 : 400;
        throw new DomainException($"platform.identity.{first.Code}", string.Join(" ", result.Errors.Select(e => e.Description)), status);
    }

    private static DomainException InvalidCredentials() =>
        new("platform.auth.invalid_credentials", "Company code, user name or password is incorrect.", 401);

    private static DomainException InvalidRefreshToken() =>
        new("platform.auth.invalid_refresh_token", "Session expired. Sign in again.", 401);

    private static UserProfile ToProfile(AppUser user, Tenant tenant, IReadOnlyList<string> roleNames, IReadOnlyList<string> permissions) =>
        new(user.Id, user.UserName!, user.Email, user.DisplayName, user.Language, user.TwoFactorEnabled,
            tenant.Id, tenant.Name, tenant.Slug, tenant.Plan, tenant.TimeZone, roleNames, permissions);

    private async Task VerifyCodeAsync(AppUser user, string code)
    {
        var normalized = code.Replace(" ", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal);
        if (!await users.VerifyTwoFactorTokenAsync(user, TokenOptions.DefaultAuthenticatorProvider, normalized))
        {
            throw new DomainException("platform.auth.two_factor_invalid", "Two-factor code is not valid.", 400);
        }
    }

    private async Task<AppUser> RequireCurrentUserAsync() =>
        await users.FindByIdAsync(tenantContext.RequireUserId().ToString())
        ?? throw new DomainException("platform.auth.invalid_credentials", "User no longer exists.", 401);

    private async Task<(IReadOnlyList<string> Roles, IReadOnlyList<string> Permissions)> LoadGrantsAsync(AppUser user, CancellationToken cancellationToken)
    {
        var granted = await (
            from userRole in db.UserRoles
            join role in db.Roles on userRole.RoleId equals role.Id
            where userRole.UserId == user.Id
            select new { role.Id, role.Name }).ToListAsync(cancellationToken);
        var roleIds = granted.Select(g => g.Id).ToList();
        var permissions = await db.RoleClaims
            .Where(c => roleIds.Contains(c.RoleId) && c.ClaimType == MrpClaims.Permission)
            .Select(c => c.ClaimValue!)
            .Distinct()
            .ToListAsync(cancellationToken);
        return (granted.Select(g => g.Name!).Order(StringComparer.Ordinal).ToList(), permissions.Order(StringComparer.Ordinal).ToList());
    }

    private async Task<TokenResponse> IssueAsync(AppUser user, Tenant tenant, bool mfa, string? device, CancellationToken cancellationToken, RefreshToken? replaces = null)
    {
        var now = clock.GetUtcNow();
        var (roleNames, permissions) = await LoadGrantsAsync(user, cancellationToken);
        var refreshValue = tokens.CreateRefreshTokenValue(tenant.Id);
        var refresh = new RefreshToken(user.Id, TokenService.Hash(refreshValue), now.Add(tokens.RefreshTokenLifetime), mfa, device);
        db.RefreshTokens.Add(refresh);
        replaces?.Revoke(now, refresh.Id);
        await db.SaveChangesAsync(cancellationToken);

        var access = tokens.CreateAccessToken(user, permissions, mfa, now);
        return new TokenResponse(access, refreshValue, (int)tokens.AccessTokenLifetime.TotalSeconds, ToProfile(user, tenant, roleNames, permissions));
    }
}
