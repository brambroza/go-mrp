namespace Mrp.SharedKernel.Domain;

/// <summary>Marks a row as belonging to exactly one tenant. Every such table is protected by RLS.</summary>
public interface ITenantOwned
{
    /// <summary>Owning tenant.</summary>
    Guid TenantId { get; }
}

/// <summary>Base class for tenant-owned entities with audit columns.</summary>
public abstract class Entity : ITenantOwned
{
    /// <summary>Primary key (UUID v7, time ordered).</summary>
    public Guid Id { get; protected set; } = Guid.CreateVersion7();

    /// <summary>Owning tenant. Stamped by the DbContext when the entity is added.</summary>
    public Guid TenantId { get; internal set; }

    /// <summary>Creation time in UTC.</summary>
    public DateTimeOffset CreatedAt { get; internal set; }

    /// <summary>User that created the row, if any.</summary>
    public Guid? CreatedBy { get; internal set; }

    /// <summary>Last modification time in UTC.</summary>
    public DateTimeOffset? UpdatedAt { get; internal set; }

    /// <summary>User that last modified the row, if any.</summary>
    public Guid? UpdatedBy { get; internal set; }
}
