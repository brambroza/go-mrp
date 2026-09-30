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

/// <summary>User administration within the current tenant.</summary>
public sealed class UserAdminService(
    PlatformDbContext db,
    DbSession session,
    ITenantContext tenant,
    UserManager<AppUser> users,
    AuthService auth,
    TimeProvider clock)
{
    /// <summary>Users of the tenant, optionally filtered by text.</summary>
    public async Task<PagedResult<UserDto>> ListAsync(string? search, int? page, int? pageSize, CancellationToken cancellationToken)
    {
        var query = db.Users.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim()}%";
            query = query.Where(u => EF.Functions.ILike(u.UserName!, pattern) || EF.Functions.ILike(u.DisplayName, pattern));
        }

        var result = await query.OrderBy(u => u.UserName).ToPagedAsync(page, pageSize, cancellationToken);
        var ids = result.Items.Select(u => u.Id).ToList();
        var roleLinks = await db.UserRoles.AsNoTracking().Where(r => ids.Contains(r.UserId)).ToListAsync(cancellationToken);
        var rolesByUser = roleLinks.ToLookup(r => r.UserId, r => r.RoleId);
        return new PagedResult<UserDto>(
            result.Items.Select(u => ToDto(u, rolesByUser[u.Id].ToList())).ToList(), result.Total, result.Page, result.PageSize);
    }

    /// <summary>Creates a user, enforcing the package's user limit.</summary>
    public Task<UserDto> CreateAsync(CreateUserRequest request, CancellationToken cancellationToken) =>
        session.ExecuteInTransactionAsync(
            async ct =>
            {
                await EnsureSeatAvailableAsync(ct);
                var roleNames = await ResolveRoleNamesAsync(request.RoleIds, ct);
                var user = new AppUser
                {
                    UserName = request.UserName.Trim(),
                    Email = request.Email.Trim(),
                    DisplayName = request.DisplayName.Trim(),
                    Language = request.Language,
                };
                AuthService.EnsureSucceeded(await users.CreateAsync(user, request.Password));
                AuthService.EnsureSucceeded(await users.AddToRolesAsync(user, roleNames));
                return ToDto(user, request.RoleIds);
            },
            cancellationToken);

    /// <summary>Updates profile, roles, active flag and optionally the password.</summary>
    public Task<UserDto> UpdateAsync(Guid id, UpdateUserRequest request, CancellationToken cancellationToken) =>
        session.ExecuteInTransactionAsync(
            async ct =>
            {
                var user = await users.FindByIdAsync(id.ToString()) ?? throw DomainException.NotFound("User", id);
                var roleNames = await ResolveRoleNamesAsync(request.RoleIds, ct);
                var currentRoles = await users.GetRolesAsync(user);
                var wasOwner = currentRoles.Contains(AppRole.OwnerRoleName);
                var staysOwner = roleNames.Contains(AppRole.OwnerRoleName) && request.IsActive;
                if (wasOwner && !staysOwner)
                {
                    await EnsureAnotherOwnerAsync(user.Id, ct);
                }

                if (request.IsActive && !user.IsActive)
                {
                    await EnsureSeatAvailableAsync(ct);
                }

                user.Email = request.Email.Trim();
                user.DisplayName = request.DisplayName.Trim();
                user.Language = request.Language;
                var deactivated = user.IsActive && !request.IsActive;
                user.IsActive = request.IsActive;
                AuthService.EnsureSucceeded(await users.UpdateAsync(user));
                AuthService.EnsureSucceeded(await users.RemoveFromRolesAsync(user, currentRoles.Except(roleNames)));
                AuthService.EnsureSucceeded(await users.AddToRolesAsync(user, roleNames.Except(currentRoles)));

                if (!string.IsNullOrEmpty(request.NewPassword))
                {
                    var token = await users.GeneratePasswordResetTokenAsync(user);
                    AuthService.EnsureSucceeded(await users.ResetPasswordAsync(user, token, request.NewPassword));
                }

                if (deactivated || !string.IsNullOrEmpty(request.NewPassword))
                {
                    await auth.RevokeAllAsync(user.Id, clock.GetUtcNow(), ct);
                }

                return ToDto(user, request.RoleIds);
            },
            cancellationToken);

    private static UserDto ToDto(AppUser user, IReadOnlyList<Guid> roleIds) =>
        new(user.Id, user.UserName!, user.Email, user.DisplayName, user.Language, user.IsActive, user.TwoFactorEnabled, roleIds);

    private async Task EnsureSeatAvailableAsync(CancellationToken cancellationToken)
    {
        var tenantId = tenant.RequireTenantId();
        var maxUsers = await db.Tenants.Where(t => t.Id == tenantId).Select(t => t.MaxUsers).FirstAsync(cancellationToken);
        var active = await db.Users.CountAsync(u => u.IsActive, cancellationToken);
        if (active >= maxUsers)
        {
            throw DomainException.Conflict("platform.users.limit_reached", $"The package allows {maxUsers} active users.");
        }
    }

    private async Task EnsureAnotherOwnerAsync(Guid exceptUserId, CancellationToken cancellationToken)
    {
        var others = await (
            from link in db.UserRoles
            join role in db.Roles on link.RoleId equals role.Id
            join user in db.Users on link.UserId equals user.Id
            where role.IsSystem && role.Name == AppRole.OwnerRoleName && user.IsActive && user.Id != exceptUserId
            select user.Id).AnyAsync(cancellationToken);
        if (!others)
        {
            throw DomainException.Conflict("platform.users.last_owner", "The company must keep at least one active owner.");
        }
    }

    private async Task<List<string>> ResolveRoleNamesAsync(IReadOnlyList<Guid> roleIds, CancellationToken cancellationToken)
    {
        var distinct = roleIds.Distinct().ToList();
        var names = await db.Roles.Where(r => distinct.Contains(r.Id)).Select(r => r.Name!).ToListAsync(cancellationToken);
        if (names.Count != distinct.Count)
        {
            throw new DomainException("platform.roles.unknown", "One of the roles does not exist.", 400);
        }

        return names;
    }
}

/// <summary>Role administration within the current tenant.</summary>
public sealed class RoleAdminService(PlatformDbContext db, DbSession session, RoleManager<AppRole> roles)
{
    /// <summary>All roles with their permissions.</summary>
    public async Task<IReadOnlyList<RoleDto>> ListAsync(CancellationToken cancellationToken)
    {
        var all = await db.Roles.AsNoTracking().OrderBy(r => r.Name).ToListAsync(cancellationToken);
        var claims = await db.RoleClaims.AsNoTracking().Where(c => c.ClaimType == MrpClaims.Permission).ToListAsync(cancellationToken);
        var byRole = claims.ToLookup(c => c.RoleId, c => c.ClaimValue!);
        return all.Select(r => new RoleDto(r.Id, r.Name!, r.IsSystem, byRole[r.Id].Order(StringComparer.Ordinal).ToList())).ToList();
    }

    /// <summary>Creates a role with permissions.</summary>
    public Task<RoleDto> CreateAsync(SaveRoleRequest request, CancellationToken cancellationToken) =>
        session.ExecuteInTransactionAsync(
            async _ =>
            {
                var permissions = ValidatePermissions(request.Permissions);
                var role = new AppRole { Name = request.Name.Trim() };
                AuthService.EnsureSucceeded(await roles.CreateAsync(role));
                foreach (var permission in permissions)
                {
                    AuthService.EnsureSucceeded(await roles.AddClaimAsync(role, new Claim(MrpClaims.Permission, permission)));
                }

                return new RoleDto(role.Id, role.Name!, role.IsSystem, permissions);
            },
            cancellationToken);

    /// <summary>Renames a role and replaces its permissions. System roles are read-only.</summary>
    public Task<RoleDto> UpdateAsync(Guid id, SaveRoleRequest request, CancellationToken cancellationToken) =>
        session.ExecuteInTransactionAsync(
            async ct =>
            {
                var role = await roles.FindByIdAsync(id.ToString()) ?? throw DomainException.NotFound("Role", id);
                if (role.IsSystem)
                {
                    throw DomainException.Conflict("platform.roles.system_readonly", "System roles cannot be changed.");
                }

                var permissions = ValidatePermissions(request.Permissions);
                role.Name = request.Name.Trim();
                AuthService.EnsureSucceeded(await roles.UpdateAsync(role));

                var existing = await db.RoleClaims.Where(c => c.RoleId == role.Id && c.ClaimType == MrpClaims.Permission).ToListAsync(ct);
                db.RoleClaims.RemoveRange(existing.Where(c => !permissions.Contains(c.ClaimValue!)));
                foreach (var permission in permissions.Except(existing.Select(c => c.ClaimValue!)))
                {
                    db.RoleClaims.Add(new AppRoleClaim { RoleId = role.Id, ClaimType = MrpClaims.Permission, ClaimValue = permission });
                }

                await db.SaveChangesAsync(ct);
                return new RoleDto(role.Id, role.Name!, role.IsSystem, permissions);
            },
            cancellationToken);

    private static List<string> ValidatePermissions(IReadOnlyList<string> requested)
    {
        var distinct = requested.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        var unknown = distinct.FirstOrDefault(p => !Permissions.All.Contains(p));
        if (unknown is not null)
        {
            throw new DomainException("platform.roles.unknown_permission", $"Permission '{unknown}' does not exist.", 400);
        }

        return distinct;
    }
}

/// <summary>Document number format administration.</summary>
public sealed class NumberingAdminService(PlatformDbContext db)
{
    /// <summary>Formats of every known document type (configured or default).</summary>
    public async Task<IReadOnlyList<DocumentNumberFormatDto>> ListAsync(CancellationToken cancellationToken)
    {
        var configured = await db.DocumentNumberFormats.AsNoTracking().ToDictionaryAsync(f => f.DocumentType, cancellationToken);
        return DocumentTypes.All.Order(StringComparer.Ordinal)
            .Select(type => ToDto(configured.GetValueOrDefault(type) ?? DocumentNumberFormat.Default(type)))
            .ToList();
    }

    /// <summary>Stores the format of a document type. Existing numbers are not changed.</summary>
    public async Task<DocumentNumberFormatDto> SaveAsync(string documentType, SaveDocumentNumberFormatRequest request, CancellationToken cancellationToken)
    {
        if (!DocumentTypes.All.Contains(documentType))
        {
            throw new DomainException("platform.numbering.unknown_document_type", $"Document type '{documentType}' is not known.", 400);
        }

        var format = await db.DocumentNumberFormats.FirstOrDefaultAsync(f => f.DocumentType == documentType, cancellationToken);
        if (format is null)
        {
            format = new DocumentNumberFormat(documentType, request.Prefix, request.Period, request.Digits, request.Separator);
            db.DocumentNumberFormats.Add(format);
        }
        else
        {
            format.Update(request.Prefix, request.Period, request.Digits, request.Separator);
        }

        await db.SaveChangesAsync(cancellationToken);
        return ToDto(format);
    }

    private static DocumentNumberFormatDto ToDto(DocumentNumberFormat format) =>
        new(format.DocumentType, format.Prefix, format.Period, format.Digits, format.Separator,
            format.Format(new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Unspecified), 1));
}
