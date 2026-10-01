using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Mrp.Production.Domain;
using Mrp.SharedKernel.Persistence;
using Mrp.SharedKernel.Tenancy;

namespace Mrp.Production.Persistence;

/// <summary>Tables of the <c>production</c> schema.</summary>
public sealed class ProductionDbContext(DbContextOptions<ProductionDbContext> options, ITenantContext tenant, DbSession session)
    : ModuleDbContext(options, tenant, session)
{
    /// <summary>Schema name.</summary>
    public const string SchemaName = "production";

    /// <summary>BOM versions.</summary>
    public DbSet<BomVersion> Boms => Set<BomVersion>();

    /// <summary>BOM lines.</summary>
    public DbSet<BomLine> BomLines => Set<BomLine>();

    /// <summary>Independent demand.</summary>
    public DbSet<Demand> Demands => Set<Demand>();

    /// <summary>MRP runs.</summary>
    public DbSet<MrpRun> MrpRuns => Set<MrpRun>();

    /// <summary>Planned orders of MRP runs.</summary>
    public DbSet<MrpRunPlannedOrder> PlannedOrders => Set<MrpRunPlannedOrder>();

    /// <summary>Work orders.</summary>
    public DbSet<WorkOrder> WorkOrders => Set<WorkOrder>();

    /// <summary>Machines.</summary>
    public DbSet<Machine> Machines => Set<Machine>();

    /// <summary>Routings.</summary>
    public DbSet<Routing> Routings => Set<Routing>();

    /// <summary>Holidays.</summary>
    public DbSet<Holiday> Holidays => Set<Holiday>();

    /// <summary>Schedule slots.</summary>
    public DbSet<ScheduleSlot> ScheduleSlots => Set<ScheduleSlot>();

    /// <inheritdoc />
    protected override string Schema => SchemaName;
}

internal sealed class MachineConfiguration : IEntityTypeConfiguration<Machine>
{
    public void Configure(EntityTypeBuilder<Machine> builder)
    {
        builder.ToTable("machines");
        builder.Property(m => m.Code).HasMaxLength(40);
        builder.Property(m => m.Name).HasMaxLength(200);
        builder.Property(m => m.MachineGroup).HasMaxLength(40);
        builder.HasIndex(m => new { m.TenantId, m.Code }).IsUnique();
        builder.HasIndex(m => new { m.TenantId, m.MachineGroup });
    }
}

internal sealed class RoutingConfiguration : IEntityTypeConfiguration<Routing>
{
    public void Configure(EntityTypeBuilder<Routing> builder)
    {
        builder.ToTable("routings");
        builder.HasIndex(r => new { r.TenantId, r.ItemId }).IsUnique();
        builder.HasMany(r => r.Operations).WithOne().HasForeignKey(o => o.RoutingId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(r => r.Operations).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class RoutingOperationConfiguration : IEntityTypeConfiguration<RoutingOperation>
{
    public void Configure(EntityTypeBuilder<RoutingOperation> builder)
    {
        builder.ToTable("routing_operations");
        builder.Property(o => o.Name).HasMaxLength(100);
        builder.Property(o => o.MachineGroup).HasMaxLength(40);
        builder.Property(o => o.SetupMinutes).HasPrecision(10, 2);
        builder.Property(o => o.MinutesPerUnit).HasPrecision(12, 6);
        builder.HasIndex(o => new { o.RoutingId, o.Seq }).IsUnique();
    }
}

internal sealed class HolidayConfiguration : IEntityTypeConfiguration<Holiday>
{
    public void Configure(EntityTypeBuilder<Holiday> builder)
    {
        builder.ToTable("holidays");
        builder.Property(h => h.Name).HasMaxLength(200);
        builder.HasIndex(h => new { h.TenantId, h.Date }).IsUnique();
    }
}

internal sealed class ScheduleSlotConfiguration : IEntityTypeConfiguration<ScheduleSlot>
{
    public void Configure(EntityTypeBuilder<ScheduleSlot> builder)
    {
        builder.ToTable("schedule_slots");
        builder.Property(s => s.OperationName).HasMaxLength(100);
        builder.Property(s => s.RunNo).HasMaxLength(40);
        builder.HasIndex(s => new { s.TenantId, s.MachineId, s.StartAt });
        builder.HasIndex(s => new { s.TenantId, s.WorkOrderId });
        builder.HasOne<WorkOrder>().WithMany().HasForeignKey(s => s.WorkOrderId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Machine>().WithMany().HasForeignKey(s => s.MachineId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class BomVersionConfiguration : IEntityTypeConfiguration<BomVersion>
{
    public void Configure(EntityTypeBuilder<BomVersion> builder)
    {
        builder.ToTable("bom_versions");
        builder.Property(b => b.Name).HasMaxLength(200);
        builder.Property(b => b.Remark).HasMaxLength(500);
        builder.Property(b => b.Version).IsRowVersion();
        builder.HasIndex(b => new { b.TenantId, b.ItemId, b.VersionNo }).IsUnique();
        builder.HasIndex(b => new { b.TenantId, b.ItemId }).IsUnique().HasFilter("status = 'Active'").HasDatabaseName("ux_bom_versions_active_item");
        builder.HasMany(b => b.Lines).WithOne().HasForeignKey(l => l.BomVersionId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(b => b.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class BomLineConfiguration : IEntityTypeConfiguration<BomLine>
{
    public void Configure(EntityTypeBuilder<BomLine> builder)
    {
        builder.ToTable("bom_lines");
        builder.Property(l => l.ConversionFactor).HasPrecision(18, 9);
        builder.Property(l => l.LossPercent).HasPrecision(7, 4);
        builder.Property(l => l.Remark).HasMaxLength(300);
        builder.HasIndex(l => new { l.BomVersionId, l.LineNo }).IsUnique();
        builder.HasIndex(l => new { l.TenantId, l.ComponentItemId });
    }
}

internal sealed class DemandConfiguration : IEntityTypeConfiguration<Demand>
{
    public void Configure(EntityTypeBuilder<Demand> builder)
    {
        builder.ToTable("demands");
        builder.Property(d => d.ReferenceNo).HasMaxLength(40);
        builder.Property(d => d.Remark).HasMaxLength(500);
        builder.HasIndex(d => new { d.TenantId, d.Status, d.DueDate });
        builder.HasIndex(d => new { d.TenantId, d.ItemId });
    }
}

internal sealed class MrpRunConfiguration : IEntityTypeConfiguration<MrpRun>
{
    public void Configure(EntityTypeBuilder<MrpRun> builder)
    {
        builder.ToTable("mrp_runs");
        builder.Property(r => r.DocumentNo).HasMaxLength(40);
        builder.HasIndex(r => new { r.TenantId, r.DocumentNo }).IsUnique();
        builder.HasMany(r => r.Requirements).WithOne().HasForeignKey(x => x.RunId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(r => r.PlannedOrders).WithOne().HasForeignKey(x => x.RunId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(r => r.Exceptions).WithOne().HasForeignKey(x => x.RunId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(r => r.Requirements).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(r => r.PlannedOrders).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(r => r.Exceptions).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class MrpRunRequirementConfiguration : IEntityTypeConfiguration<MrpRunRequirement>
{
    public void Configure(EntityTypeBuilder<MrpRunRequirement> builder)
    {
        builder.ToTable("mrp_requirements");
        builder.HasIndex(r => new { r.RunId, r.ItemId });
    }
}

internal sealed class MrpRunPlannedOrderConfiguration : IEntityTypeConfiguration<MrpRunPlannedOrder>
{
    public void Configure(EntityTypeBuilder<MrpRunPlannedOrder> builder)
    {
        builder.ToTable("mrp_planned_orders");
        builder.Property(o => o.Pegging).HasMaxLength(300);
        builder.Property(o => o.ConvertedDocumentType).HasMaxLength(16);
        builder.Property(o => o.ConvertedDocumentNo).HasMaxLength(40);
        builder.HasIndex(o => new { o.RunId, o.Status });
    }
}

internal sealed class MrpRunExceptionConfiguration : IEntityTypeConfiguration<MrpRunException>
{
    public void Configure(EntityTypeBuilder<MrpRunException> builder)
    {
        builder.ToTable("mrp_exceptions");
        builder.Property(e => e.Code).HasMaxLength(32);
        builder.Property(e => e.Message).HasMaxLength(500);
        builder.HasIndex(e => e.RunId);
    }
}

internal sealed class WorkOrderConfiguration : IEntityTypeConfiguration<WorkOrder>
{
    public void Configure(EntityTypeBuilder<WorkOrder> builder)
    {
        builder.ToTable("work_orders");
        builder.Property(w => w.DocumentNo).HasMaxLength(40);
        builder.Property(w => w.SourceReference).HasMaxLength(40);
        builder.Property(w => w.Remark).HasMaxLength(500);
        builder.Property(w => w.StatusReason).HasMaxLength(500);
        builder.Property(w => w.Version).IsRowVersion();
        builder.Ignore(w => w.HasPostings);
        builder.Ignore(w => w.IsOpen);
        builder.Ignore(w => w.OutstandingQuantity);
        builder.HasIndex(w => new { w.TenantId, w.DocumentNo }).IsUnique();
        builder.HasIndex(w => new { w.TenantId, w.Status, w.DueDate });
        builder.HasIndex(w => new { w.TenantId, w.ItemId });
        builder.HasIndex(w => new { w.TenantId, w.DemandId }).HasFilter("demand_id IS NOT NULL");
        builder.HasOne<WorkOrder>().WithMany().HasForeignKey(w => w.ParentWorkOrderId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<BomVersion>().WithMany().HasForeignKey(w => w.BomVersionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Demand>().WithMany().HasForeignKey(w => w.DemandId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(w => w.Materials).WithOne().HasForeignKey(m => m.WorkOrderId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(w => w.Materials).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class WorkOrderMaterialConfiguration : IEntityTypeConfiguration<WorkOrderMaterial>
{
    public void Configure(EntityTypeBuilder<WorkOrderMaterial> builder)
    {
        builder.ToTable("work_order_materials");
        builder.Ignore(m => m.OutstandingQuantity);
        builder.HasIndex(m => new { m.WorkOrderId, m.LineNo }).IsUnique();
        builder.HasIndex(m => new { m.TenantId, m.ComponentItemId });
    }
}
