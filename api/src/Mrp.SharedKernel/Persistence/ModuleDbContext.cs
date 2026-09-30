using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Mrp.SharedKernel.Domain;
using Mrp.SharedKernel.Tenancy;

namespace Mrp.SharedKernel.Persistence;

/// <summary>
/// Base DbContext of a module: own schema, tenant query filter on every <see cref="ITenantOwned"/> entity,
/// tenant and audit stamping on save, and a guard against writing another tenant's rows.
/// </summary>
public abstract class ModuleDbContext : DbContext
{
    private readonly ITenantContext _tenant;

    /// <summary>Creates the context and joins the scope's shared database session.</summary>
    protected ModuleDbContext(DbContextOptions options, ITenantContext tenant, DbSession session)
        : base(options)
    {
        _tenant = tenant;
        session.Register(this);
    }

    /// <summary>PostgreSQL schema that holds this module's tables.</summary>
    protected abstract string Schema { get; }

    /// <summary>Tables (without schema) the application role may only SELECT from and INSERT into.</summary>
    public virtual IReadOnlyCollection<string> AppendOnlyTables => [];

    /// <summary>Tenant used by the global query filter; empty when no tenant is resolved (matches nothing).</summary>
    public Guid CurrentTenantId => _tenant.TenantId ?? Guid.Empty;

    /// <inheritdoc />
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Quantities are numeric(18,6); money columns override to numeric(18,4) in their configuration.
        configurationBuilder.Properties<decimal>().HavePrecision(18, 6);
        configurationBuilder.Properties<Enum>().HaveConversion<string>().HaveMaxLength(32);
        configurationBuilder.Properties<string>().HaveMaxLength(256);
    }

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(GetType().Assembly, t => t.Namespace?.StartsWith(GetType().Namespace!, StringComparison.Ordinal) == true);
        base.OnModelCreating(modelBuilder);
        ApplyTenantFilters(modelBuilder);
        UseClientGeneratedKeys(modelBuilder);
    }

    /// <inheritdoc />
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        StampAndGuard();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    /// <inheritdoc />
    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        StampAndGuard();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>Adds <c>tenant_id = current tenant</c> to every tenant-owned root entity.</summary>
    protected void ApplyTenantFilters(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (entityType.BaseType is not null || entityType.IsOwned() || !typeof(ITenantOwned).IsAssignableFrom(entityType.ClrType))
            {
                continue;
            }

            var parameter = Expression.Parameter(entityType.ClrType, "e");
            var body = Expression.Equal(
                Expression.Property(parameter, nameof(ITenantOwned.TenantId)),
                Expression.Property(Expression.Constant(this), nameof(CurrentTenantId)));
            modelBuilder.Entity(entityType.ClrType).HasQueryFilter(Expression.Lambda(body, parameter));
            modelBuilder.Entity(entityType.ClrType).HasIndex(nameof(ITenantOwned.TenantId));
        }
    }

    /// <summary>
    /// Ids are created in the domain (UUID v7). Marking them as never store-generated makes EF track new
    /// children of an already tracked aggregate as Added instead of Modified.
    /// </summary>
    private static void UseClientGeneratedKeys(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            var key = entityType.FindPrimaryKey();
            if (key is { Properties: [{ ClrType: var type } property] } && type == typeof(Guid)
                && property.GetDefaultValueSql() is null)
            {
                property.ValueGenerated = Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.Never;
            }
        }
    }

    private void StampAndGuard()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
            {
                continue;
            }

            if (entry.Entity is ITenantOwned owned)
            {
                var tenantId = _tenant.RequireTenantId();
                if (entry.State == EntityState.Added && owned.TenantId == Guid.Empty)
                {
                    entry.Property(nameof(ITenantOwned.TenantId)).CurrentValue = tenantId;
                }
                else if (owned.TenantId != tenantId)
                {
                    throw new InvalidOperationException(
                        $"Cross-tenant write blocked: {entry.Metadata.ClrType.Name} belongs to another tenant.");
                }
            }

            if (entry.Entity is Entity entity)
            {
                if (entry.State == EntityState.Added)
                {
                    entity.CreatedAt = now;
                    entity.CreatedBy = _tenant.UserId;
                }
                else if (entry.State == EntityState.Modified)
                {
                    entity.UpdatedAt = now;
                    entity.UpdatedBy = _tenant.UserId;
                }
            }
        }
    }
}
