using Microsoft.EntityFrameworkCore;
using Mrp.Platform.Domain;
using Mrp.SharedKernel.Persistence;
using Mrp.SharedKernel.Tenancy;

namespace Mrp.Platform.Persistence;

/// <summary>Tables of the <c>platform</c> schema: tenants, identity, numbering, approvals, settings.</summary>
public sealed class PlatformDbContext(DbContextOptions<PlatformDbContext> options, ITenantContext tenant, DbSession session)
    : ModuleDbContext(options, tenant, session)
{
    /// <summary>Schema name.</summary>
    public const string SchemaName = "platform";

    /// <summary>Tenants (not tenant filtered).</summary>
    public DbSet<Tenant> Tenants => Set<Tenant>();

    /// <summary>Users.</summary>
    public DbSet<AppUser> Users => Set<AppUser>();

    /// <summary>Roles.</summary>
    public DbSet<AppRole> Roles => Set<AppRole>();

    /// <summary>User ↔ role links.</summary>
    public DbSet<AppUserRole> UserRoles => Set<AppUserRole>();

    /// <summary>User claims.</summary>
    public DbSet<AppUserClaim> UserClaims => Set<AppUserClaim>();

    /// <summary>External logins.</summary>
    public DbSet<AppUserLogin> UserLogins => Set<AppUserLogin>();

    /// <summary>User tokens.</summary>
    public DbSet<AppUserToken> UserTokens => Set<AppUserToken>();

    /// <summary>Role claims (permissions).</summary>
    public DbSet<AppRoleClaim> RoleClaims => Set<AppRoleClaim>();

    /// <summary>Refresh tokens.</summary>
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    /// <summary>Tenant settings.</summary>
    public DbSet<TenantSetting> Settings => Set<TenantSetting>();

    /// <summary>Document number formats.</summary>
    public DbSet<DocumentNumberFormat> DocumentNumberFormats => Set<DocumentNumberFormat>();

    /// <summary>Document sequences.</summary>
    public DbSet<DocumentSequence> DocumentSequences => Set<DocumentSequence>();

    /// <summary>Approval routes.</summary>
    public DbSet<ApprovalRoute> ApprovalRoutes => Set<ApprovalRoute>();

    /// <summary>Approval requests.</summary>
    public DbSet<ApprovalRequest> ApprovalRequests => Set<ApprovalRequest>();

    /// <inheritdoc />
    protected override string Schema => SchemaName;
}
