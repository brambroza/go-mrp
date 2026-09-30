using Microsoft.AspNetCore.Identity;
using Mrp.SharedKernel.Domain;

namespace Mrp.Platform.Domain;

/// <summary>User account, scoped to one tenant.</summary>
public sealed class AppUser : IdentityUser<Guid>, ITenantOwned
{
    /// <summary>Creates a user with a time-ordered id.</summary>
    public AppUser()
    {
        Id = Guid.CreateVersion7();
    }

    /// <inheritdoc />
    public Guid TenantId { get; set; }

    /// <summary>Name shown in the UI and on documents.</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Preferred UI language (<c>th</c> or <c>en</c>).</summary>
    public string Language { get; set; } = "th";

    /// <summary>Inactive users cannot sign in; users are never deleted.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>Creation time in UTC.</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>Role defined by a tenant; permissions are stored as role claims.</summary>
public sealed class AppRole : IdentityRole<Guid>, ITenantOwned
{
    /// <summary>Name of the built-in role that holds every permission.</summary>
    public const string OwnerRoleName = "Owner";

    /// <summary>Creates a role with a time-ordered id.</summary>
    public AppRole()
    {
        Id = Guid.CreateVersion7();
    }

    /// <inheritdoc />
    public Guid TenantId { get; set; }

    /// <summary>System roles cannot be renamed or deleted.</summary>
    public bool IsSystem { get; set; }
}

/// <summary>User ↔ role link with tenant column for RLS.</summary>
public sealed class AppUserRole : IdentityUserRole<Guid>, ITenantOwned
{
    /// <inheritdoc />
    public Guid TenantId { get; set; }
}

/// <summary>User claim with tenant column for RLS.</summary>
public sealed class AppUserClaim : IdentityUserClaim<Guid>, ITenantOwned
{
    /// <inheritdoc />
    public Guid TenantId { get; set; }
}

/// <summary>External login with tenant column for RLS.</summary>
public sealed class AppUserLogin : IdentityUserLogin<Guid>, ITenantOwned
{
    /// <inheritdoc />
    public Guid TenantId { get; set; }
}

/// <summary>User token (authenticator key, recovery codes) with tenant column for RLS.</summary>
public sealed class AppUserToken : IdentityUserToken<Guid>, ITenantOwned
{
    /// <inheritdoc />
    public Guid TenantId { get; set; }
}

/// <summary>Role claim (permission) with tenant column for RLS.</summary>
public sealed class AppRoleClaim : IdentityRoleClaim<Guid>, ITenantOwned
{
    /// <inheritdoc />
    public Guid TenantId { get; set; }
}

/// <summary>Long-lived token used to obtain new access tokens. Only the SHA-256 hash is stored.</summary>
public sealed class RefreshToken : ITenantOwned
{
    private RefreshToken()
    {
    }

    /// <summary>Creates a refresh token record.</summary>
    public RefreshToken(Guid userId, string tokenHash, DateTimeOffset expiresAt, bool mfa, string? device)
    {
        UserId = userId;
        TokenHash = tokenHash;
        ExpiresAt = expiresAt;
        Mfa = mfa;
        Device = device is { Length: > 200 } ? device[..200] : device;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Primary key.</summary>
    public Guid Id { get; private set; } = Guid.CreateVersion7();

    /// <inheritdoc />
    public Guid TenantId { get; private set; }

    /// <summary>Owner of the token.</summary>
    public Guid UserId { get; private set; }

    /// <summary>Hex SHA-256 of the token value.</summary>
    public string TokenHash { get; private set; } = string.Empty;

    /// <summary>Expiry time.</summary>
    public DateTimeOffset ExpiresAt { get; private set; }

    /// <summary>Creation time.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Time the token was used or revoked.</summary>
    public DateTimeOffset? RevokedAt { get; private set; }

    /// <summary>Token that replaced this one on rotation.</summary>
    public Guid? ReplacedById { get; private set; }

    /// <summary>Whether the session passed two-factor authentication.</summary>
    public bool Mfa { get; private set; }

    /// <summary>Client description (user agent or device name).</summary>
    public string? Device { get; private set; }

    /// <summary>True while the token can still be exchanged.</summary>
    public bool IsUsable(DateTimeOffset now) => RevokedAt is null && ExpiresAt > now;

    /// <summary>Marks the token as used/revoked.</summary>
    public void Revoke(DateTimeOffset now, Guid? replacedById = null)
    {
        RevokedAt ??= now;
        ReplacedById ??= replacedById;
    }
}

/// <summary>Key/value configuration of a tenant; value is JSON.</summary>
public sealed class TenantSetting : Entity
{
    private TenantSetting()
    {
    }

    /// <summary>Creates a setting.</summary>
    public TenantSetting(string key, string jsonValue)
    {
        Key = key;
        Value = jsonValue;
    }

    /// <summary>Setting key such as <c>approval.allowSelfApprove</c>.</summary>
    public string Key { get; private set; } = string.Empty;

    /// <summary>JSON encoded value.</summary>
    public string Value { get; private set; } = "null";

    /// <summary>Replaces the value.</summary>
    public void SetValue(string jsonValue) => Value = jsonValue;
}
