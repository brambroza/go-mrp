namespace Mrp.SharedKernel.Tenancy;

/// <summary>
/// Ambient information about the tenant and user that the current unit of work runs for.
/// </summary>
public interface ITenantContext
{
    /// <summary>Tenant of the current scope, or <c>null</c> before it has been resolved.</summary>
    Guid? TenantId { get; }

    /// <summary>Authenticated user of the current scope, or <c>null</c> for anonymous/system work.</summary>
    Guid? UserId { get; }

    /// <summary>Incremented on every change so database sessions know when to re-apply the tenant.</summary>
    int Version { get; }

    /// <summary>Tenant id, throwing when the scope has no tenant.</summary>
    Guid RequireTenantId();

    /// <summary>User id, throwing when the scope has no user.</summary>
    Guid RequireUserId();

    /// <summary>Switches the scope to the given tenant and user.</summary>
    void Set(Guid tenantId, Guid? userId);
}
