using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Mrp.Purchasing.Domain;
using Mrp.SharedKernel.Persistence;
using Mrp.SharedKernel.Tenancy;

namespace Mrp.Purchasing.Persistence;

/// <summary>Tables of the <c>purchasing</c> schema.</summary>
public sealed class PurchasingDbContext(DbContextOptions<PurchasingDbContext> options, ITenantContext tenant, DbSession session)
    : ModuleDbContext(options, tenant, session)
{
    /// <summary>Schema name.</summary>
    public const string SchemaName = "purchasing";

    /// <summary>Purchase requests.</summary>
    public DbSet<PurchaseRequest> Requests => Set<PurchaseRequest>();

    /// <summary>Purchase request lines.</summary>
    public DbSet<PurchaseRequestLine> RequestLines => Set<PurchaseRequestLine>();

    /// <summary>Purchase orders.</summary>
    public DbSet<PurchaseOrder> Orders => Set<PurchaseOrder>();

    /// <summary>Purchase order lines.</summary>
    public DbSet<PurchaseOrderLine> OrderLines => Set<PurchaseOrderLine>();

    /// <inheritdoc />
    protected override string Schema => SchemaName;
}

internal sealed class PurchaseRequestConfiguration : IEntityTypeConfiguration<PurchaseRequest>
{
    public void Configure(EntityTypeBuilder<PurchaseRequest> builder)
    {
        builder.ToTable("purchase_requests");
        builder.Property(r => r.DocumentNo).HasMaxLength(40);
        builder.Property(r => r.SourceReference).HasMaxLength(40);
        builder.Property(r => r.Remark).HasMaxLength(500);
        builder.Property(r => r.StatusReason).HasMaxLength(500);
        builder.Property(r => r.Version).IsRowVersion();
        builder.Ignore(r => r.IsEditable);
        builder.Ignore(r => r.CanBeOrdered);
        builder.HasIndex(r => new { r.TenantId, r.DocumentNo }).IsUnique();
        builder.HasIndex(r => new { r.TenantId, r.Status, r.DocumentDate });
        builder.HasMany(r => r.Lines).WithOne().HasForeignKey(l => l.RequestId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(r => r.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class PurchaseRequestLineConfiguration : IEntityTypeConfiguration<PurchaseRequestLine>
{
    public void Configure(EntityTypeBuilder<PurchaseRequestLine> builder)
    {
        builder.ToTable("purchase_request_lines");
        builder.Property(l => l.ConversionFactor).HasPrecision(18, 9);
        builder.Property(l => l.Remark).HasMaxLength(300);
        builder.HasIndex(l => new { l.RequestId, l.LineNo }).IsUnique();
        builder.HasIndex(l => new { l.TenantId, l.ItemId });
    }
}

internal sealed class PurchaseOrderConfiguration : IEntityTypeConfiguration<PurchaseOrder>
{
    public void Configure(EntityTypeBuilder<PurchaseOrder> builder)
    {
        builder.ToTable("purchase_orders");
        builder.Property(o => o.DocumentNo).HasMaxLength(40);
        builder.Property(o => o.Currency).HasMaxLength(3);
        builder.Property(o => o.ExchangeRate).HasPrecision(18, 6);
        builder.Property(o => o.VatPercent).HasPrecision(5, 2);
        builder.Property(o => o.DiscountAmount).HasPrecision(18, 4);
        builder.Property(o => o.Subtotal).HasPrecision(18, 4);
        builder.Property(o => o.VatAmount).HasPrecision(18, 4);
        builder.Property(o => o.Total).HasPrecision(18, 4);
        builder.Property(o => o.Remark).HasMaxLength(500);
        builder.Property(o => o.StatusReason).HasMaxLength(500);
        builder.Property(o => o.Version).IsRowVersion();
        builder.Ignore(o => o.IsEditable);
        builder.Ignore(o => o.IsReceivable);
        builder.Ignore(o => o.CountsAsOrdered);
        builder.Ignore(o => o.TotalInBaseCurrency);
        builder.HasIndex(o => new { o.TenantId, o.DocumentNo }).IsUnique();
        builder.HasIndex(o => new { o.TenantId, o.Status, o.DocumentDate });
        builder.HasIndex(o => new { o.TenantId, o.SupplierId });
        builder.HasMany(o => o.Lines).WithOne().HasForeignKey(l => l.OrderId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(o => o.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class PurchaseOrderLineConfiguration : IEntityTypeConfiguration<PurchaseOrderLine>
{
    public void Configure(EntityTypeBuilder<PurchaseOrderLine> builder)
    {
        builder.ToTable("purchase_order_lines");
        builder.Property(l => l.ConversionFactor).HasPrecision(18, 9);
        builder.Property(l => l.UnitPrice).HasPrecision(18, 4);
        builder.Property(l => l.DiscountPercent).HasPrecision(5, 2);
        builder.Property(l => l.NetAmount).HasPrecision(18, 4);
        builder.Property(l => l.Remark).HasMaxLength(300);
        builder.Ignore(l => l.OutstandingStockQuantity);
        builder.HasIndex(l => new { l.OrderId, l.LineNo }).IsUnique();
        builder.HasIndex(l => new { l.TenantId, l.ItemId });
        builder.HasIndex(l => new { l.TenantId, l.PrLineId }).HasFilter("pr_line_id IS NOT NULL");
        builder.HasOne<PurchaseRequestLine>().WithMany().HasForeignKey(l => l.PrLineId).OnDelete(DeleteBehavior.Restrict);
    }
}
