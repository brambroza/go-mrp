namespace Mrp.SharedKernel.Tenancy;

/// <summary>Scoped, mutable implementation of <see cref="ITenantContext"/>.</summary>
public sealed class TenantContext : ITenantContext
{
    /// <inheritdoc />
    public Guid? TenantId { get; private set; }

    /// <inheritdoc />
    public Guid? UserId { get; private set; }

    /// <inheritdoc />
    public int Version { get; private set; }

    /// <inheritdoc />
    public Guid RequireTenantId() =>
        TenantId ?? throw new InvalidOperationException("No tenant has been resolved for the current scope.");

    /// <inheritdoc />
    public Guid RequireUserId() =>
        UserId ?? throw new InvalidOperationException("No user has been resolved for the current scope.");

    /// <inheritdoc />
    public void Set(Guid tenantId, Guid? userId)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("Tenant id must not be empty.", nameof(tenantId));
        }

        TenantId = tenantId;
        UserId = userId;
        Version++;
    }
}
