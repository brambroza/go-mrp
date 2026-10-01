using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Mrp.Masters.Domain;
using Mrp.SharedKernel.Persistence;
using Mrp.SharedKernel.Tenancy;

namespace Mrp.Masters.Persistence;

/// <summary>Tables of the <c>masters</c> schema.</summary>
public sealed class MastersDbContext(DbContextOptions<MastersDbContext> options, ITenantContext tenant, DbSession session)
    : ModuleDbContext(options, tenant, session)
{
    /// <summary>Schema name.</summary>
    public const string SchemaName = "masters";

    /// <summary>Units.</summary>
    public DbSet<Unit> Units => Set<Unit>();

    /// <summary>Unit conversions.</summary>
    public DbSet<UnitConversion> UnitConversions => Set<UnitConversion>();

    /// <summary>Item groups.</summary>
    public DbSet<ItemGroup> ItemGroups => Set<ItemGroup>();

    /// <summary>Items.</summary>
    public DbSet<Item> Items => Set<Item>();

    /// <summary>Warehouses.</summary>
    public DbSet<Warehouse> Warehouses => Set<Warehouse>();

    /// <summary>Locations.</summary>
    public DbSet<Location> Locations => Set<Location>();

    /// <summary>Customers.</summary>
    public DbSet<Customer> Customers => Set<Customer>();

    /// <summary>Suppliers.</summary>
    public DbSet<Supplier> Suppliers => Set<Supplier>();

    /// <summary>Supplier price tiers.</summary>
    public DbSet<SupplierPrice> SupplierPrices => Set<SupplierPrice>();

    /// <inheritdoc />
    protected override string Schema => SchemaName;
}

internal static class MasterConfiguration
{
    public static void ConfigureCoded<T>(EntityTypeBuilder<T> builder, string table)
        where T : CodedMaster
    {
        builder.ToTable(table);
        builder.Property(e => e.Code).HasMaxLength(40);
        builder.Property(e => e.Name).HasMaxLength(200);
        builder.Property(e => e.NameEn).HasMaxLength(200);
    }

    public static void ConfigurePartner<T>(EntityTypeBuilder<T> builder, string table)
        where T : BusinessPartner
    {
        ConfigureCoded(builder, table);
        builder.HasIndex(e => new { e.TenantId, e.Code }).IsUnique();
        builder.Property(e => e.TaxId).HasMaxLength(20);
        builder.Property(e => e.BranchNo).HasMaxLength(10);
        builder.Property(e => e.Address).HasMaxLength(500);
        builder.Property(e => e.Phone).HasMaxLength(50);
        builder.Property(e => e.Email).HasMaxLength(200);
        builder.Property(e => e.ContactName).HasMaxLength(100);
    }
}

internal sealed class UnitConfiguration : IEntityTypeConfiguration<Unit>
{
    public void Configure(EntityTypeBuilder<Unit> builder)
    {
        MasterConfiguration.ConfigureCoded(builder, "units");
        builder.HasIndex(e => new { e.TenantId, e.Code }).IsUnique();
    }
}

internal sealed class ItemGroupConfiguration : IEntityTypeConfiguration<ItemGroup>
{
    public void Configure(EntityTypeBuilder<ItemGroup> builder)
    {
        MasterConfiguration.ConfigureCoded(builder, "item_groups");
        builder.HasIndex(e => new { e.TenantId, e.Code }).IsUnique();
    }
}

internal sealed class ItemConfiguration : IEntityTypeConfiguration<Item>
{
    public void Configure(EntityTypeBuilder<Item> builder)
    {
        MasterConfiguration.ConfigureCoded(builder, "items");
        builder.HasIndex(e => new { e.TenantId, e.Code }).IsUnique();
        builder.HasIndex(e => new { e.TenantId, e.Barcode }).IsUnique().HasFilter("barcode IS NOT NULL");
        builder.HasIndex(e => new { e.TenantId, e.ItemType });
        builder.Property(e => e.Barcode).HasMaxLength(50);
        builder.Property(e => e.StandardCost).HasPrecision(18, 4);
        builder.Property(e => e.SalesPrice).HasPrecision(18, 4);
        builder.HasOne<Unit>().WithMany().HasForeignKey(e => e.StockUnitId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Unit>().WithMany().HasForeignKey(e => e.PurchaseUnitId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ItemGroup>().WithMany().HasForeignKey(e => e.ItemGroupId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class UnitConversionConfiguration : IEntityTypeConfiguration<UnitConversion>
{
    public void Configure(EntityTypeBuilder<UnitConversion> builder)
    {
        builder.ToTable("unit_conversions");
        builder.HasIndex(e => new { e.TenantId, e.ItemId, e.FromUnitId, e.ToUnitId }).IsUnique().AreNullsDistinct(false);
        builder.HasOne<Item>().WithMany().HasForeignKey(e => e.ItemId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Unit>().WithMany().HasForeignKey(e => e.FromUnitId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Unit>().WithMany().HasForeignKey(e => e.ToUnitId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class WarehouseConfiguration : IEntityTypeConfiguration<Warehouse>
{
    public void Configure(EntityTypeBuilder<Warehouse> builder)
    {
        MasterConfiguration.ConfigureCoded(builder, "warehouses");
        builder.HasIndex(e => new { e.TenantId, e.Code }).IsUnique();
    }
}

internal sealed class LocationConfiguration : IEntityTypeConfiguration<Location>
{
    public void Configure(EntityTypeBuilder<Location> builder)
    {
        MasterConfiguration.ConfigureCoded(builder, "locations");
        builder.HasIndex(e => new { e.TenantId, e.WarehouseId, e.Code }).IsUnique();
        builder.HasOne<Warehouse>().WithMany().HasForeignKey(e => e.WarehouseId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        MasterConfiguration.ConfigurePartner(builder, "customers");
        builder.Property(e => e.CreditLimit).HasPrecision(18, 4);
    }
}

internal sealed class SupplierConfiguration : IEntityTypeConfiguration<Supplier>
{
    public void Configure(EntityTypeBuilder<Supplier> builder)
    {
        MasterConfiguration.ConfigurePartner(builder, "suppliers");
        builder.Property(e => e.Currency).HasMaxLength(3);
        builder.Property(e => e.VatPercent).HasPrecision(5, 2);
    }
}

internal sealed class SupplierPriceConfiguration : IEntityTypeConfiguration<SupplierPrice>
{
    public void Configure(EntityTypeBuilder<SupplierPrice> builder)
    {
        builder.ToTable("supplier_prices");
        builder.Property(e => e.UnitPrice).HasPrecision(18, 4);
        builder.Property(e => e.Currency).HasMaxLength(3);
        builder.HasIndex(e => new { e.TenantId, e.SupplierId, e.ItemId });
        builder.HasOne<Supplier>().WithMany().HasForeignKey(e => e.SupplierId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Item>().WithMany().HasForeignKey(e => e.ItemId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Unit>().WithMany().HasForeignKey(e => e.UnitId).OnDelete(DeleteBehavior.Restrict);
    }
}
