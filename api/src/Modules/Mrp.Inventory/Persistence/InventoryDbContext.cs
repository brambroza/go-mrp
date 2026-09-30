using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Mrp.Inventory.Domain;
using Mrp.SharedKernel.Persistence;
using Mrp.SharedKernel.Tenancy;

namespace Mrp.Inventory.Persistence;

/// <summary>Tables of the <c>inventory</c> schema.</summary>
public sealed class InventoryDbContext(DbContextOptions<InventoryDbContext> options, ITenantContext tenant, DbSession session)
    : ModuleDbContext(options, tenant, session)
{
    /// <summary>Schema name.</summary>
    public const string SchemaName = "inventory";

    /// <summary>Lots.</summary>
    public DbSet<Lot> Lots => Set<Lot>();

    /// <summary>Stock ledger.</summary>
    public DbSet<Movement> Movements => Set<Movement>();

    /// <summary>On-hand cache.</summary>
    public DbSet<Balance> Balances => Set<Balance>();

    /// <summary>Warehouse documents.</summary>
    public DbSet<StockDocument> Documents => Set<StockDocument>();

    /// <summary>Stock periods.</summary>
    public DbSet<Period> Periods => Set<Period>();

    /// <summary>Period end snapshots.</summary>
    public DbSet<PeriodBalance> PeriodBalances => Set<PeriodBalance>();

    /// <inheritdoc />
    public override IReadOnlyCollection<string> AppendOnlyTables => ["movements"];

    /// <inheritdoc />
    protected override string Schema => SchemaName;
}

internal sealed class LotConfiguration : IEntityTypeConfiguration<Lot>
{
    public void Configure(EntityTypeBuilder<Lot> builder)
    {
        builder.ToTable("lots");
        builder.Property(l => l.LotNo).HasMaxLength(40);
        builder.Property(l => l.SupplierLot).HasMaxLength(60);
        builder.Property(l => l.QcRemark).HasMaxLength(500);
        builder.Property(l => l.UnitCost).HasPrecision(18, 4);
        builder.HasIndex(l => new { l.TenantId, l.LotNo }).IsUnique();
        builder.HasIndex(l => new { l.TenantId, l.ItemId, l.ReceivedAt });
    }
}

internal sealed class MovementConfiguration : IEntityTypeConfiguration<Movement>
{
    public void Configure(EntityTypeBuilder<Movement> builder)
    {
        builder.ToTable("movements");
        builder.Property(m => m.Seq).UseIdentityAlwaysColumn();
        builder.Property(m => m.Seq).Metadata.SetAfterSaveBehavior(Microsoft.EntityFrameworkCore.Metadata.PropertySaveBehavior.Throw);
        builder.Property(m => m.UnitCost).HasPrecision(18, 4);
        builder.Property(m => m.DocumentNo).HasMaxLength(40);
        builder.HasIndex(m => m.Seq).IsUnique();
        builder.HasIndex(m => new { m.TenantId, m.ItemId, m.PostingDate, m.Seq });
        builder.HasIndex(m => new { m.TenantId, m.DocumentId });
        builder.HasIndex(m => new { m.TenantId, m.LotId });
        builder.HasIndex(m => new { m.TenantId, m.PoLineId }).HasFilter("po_line_id IS NOT NULL");
        builder.HasOne<Lot>().WithMany().HasForeignKey(m => m.LotId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class BalanceConfiguration : IEntityTypeConfiguration<Balance>
{
    public void Configure(EntityTypeBuilder<Balance> builder)
    {
        builder.ToTable("balances");
        builder.Property(b => b.Id).HasDefaultValueSql("gen_random_uuid()");
        builder.HasIndex(b => new { b.TenantId, b.ItemId, b.LotId, b.WarehouseId, b.LocationId }).IsUnique().AreNullsDistinct(false);
        builder.HasIndex(b => new { b.TenantId, b.WarehouseId, b.ItemId });
        builder.HasOne<Lot>().WithMany().HasForeignKey(b => b.LotId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class StockDocumentConfiguration : IEntityTypeConfiguration<StockDocument>
{
    public void Configure(EntityTypeBuilder<StockDocument> builder)
    {
        builder.ToTable("stock_documents");
        builder.Property(d => d.DocumentNo).HasMaxLength(40);
        builder.Property(d => d.ReferenceType).HasMaxLength(16);
        builder.Property(d => d.ReferenceNo).HasMaxLength(40);
        builder.Property(d => d.Remark).HasMaxLength(500);
        builder.Property(d => d.StatusReason).HasMaxLength(500);
        builder.Property(d => d.Version).IsRowVersion();
        builder.Ignore(d => d.DocumentTypeKey);
        builder.Ignore(d => d.IsEditable);
        builder.HasIndex(d => new { d.TenantId, d.DocumentNo }).IsUnique();
        builder.HasIndex(d => new { d.TenantId, d.DocumentType, d.Status, d.DocumentDate });
        builder.HasIndex(d => new { d.TenantId, d.ReferenceId }).HasFilter("reference_id IS NOT NULL");
        builder.HasMany(d => d.Lines).WithOne().HasForeignKey(l => l.DocumentId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(d => d.Allocations).WithOne().HasForeignKey(a => a.DocumentId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(d => d.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(d => d.Allocations).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class StockDocumentLineConfiguration : IEntityTypeConfiguration<StockDocumentLine>
{
    public void Configure(EntityTypeBuilder<StockDocumentLine> builder)
    {
        builder.ToTable("stock_document_lines");
        builder.Property(l => l.UnitCost).HasPrecision(18, 4);
        builder.Property(l => l.ConversionFactor).HasPrecision(18, 9);
        builder.Property(l => l.LotNo).HasMaxLength(40);
        builder.Property(l => l.SupplierLot).HasMaxLength(60);
        builder.Property(l => l.ReasonCode).HasMaxLength(20);
        builder.Property(l => l.Remark).HasMaxLength(300);
        builder.HasIndex(l => new { l.DocumentId, l.LineNo }).IsUnique();
        builder.HasIndex(l => new { l.TenantId, l.PoLineId }).HasFilter("po_line_id IS NOT NULL");
    }
}

internal sealed class StockDocumentAllocationConfiguration : IEntityTypeConfiguration<StockDocumentAllocation>
{
    public void Configure(EntityTypeBuilder<StockDocumentAllocation> builder)
    {
        builder.ToTable("stock_document_allocations");
        builder.HasIndex(a => a.LineId);
        builder.HasOne<Lot>().WithMany().HasForeignKey(a => a.LotId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class PeriodConfiguration : IEntityTypeConfiguration<Period>
{
    public void Configure(EntityTypeBuilder<Period> builder)
    {
        builder.ToTable("periods");
        builder.Property(p => p.ReopenReason).HasMaxLength(500);
        builder.Ignore(p => p.FirstDay);
        builder.Ignore(p => p.LastDay);
        builder.HasIndex(p => new { p.TenantId, p.Year, p.Month }).IsUnique();
    }
}

internal sealed class PeriodBalanceConfiguration : IEntityTypeConfiguration<PeriodBalance>
{
    public void Configure(EntityTypeBuilder<PeriodBalance> builder)
    {
        builder.ToTable("period_balances");
        builder.Property(b => b.Id).HasDefaultValueSql("gen_random_uuid()");
        builder.Property(b => b.Value).HasPrecision(18, 4);
        builder.HasIndex(b => new { b.TenantId, b.Year, b.Month, b.ItemId });
    }
}
