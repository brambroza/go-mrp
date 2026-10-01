using Microsoft.EntityFrameworkCore;
using Mrp.Masters.Domain;
using Mrp.Masters.Persistence;
using Mrp.SharedKernel.Domain;

namespace Mrp.Masters.Application;

/// <summary>Item data other modules need on documents.</summary>
public sealed record ItemInfo(
    Guid Id, string Code, string Name, ItemType ItemType, SupplyType SupplyType, Guid StockUnitId, Guid? PurchaseUnitId,
    bool IsLotTracked, int? ShelfLifeDays, int LeadTimeDays, decimal SafetyStock, decimal MinOrderQty, decimal OrderMultiple,
    decimal StandardCost, bool IsActive, decimal SalesPrice = 0m);

/// <summary>Warehouse data other modules need on documents.</summary>
public sealed record WarehouseInfo(Guid Id, string Code, string Name, WarehouseType WarehouseType, bool IsActive)
{
    /// <summary>Whether stock in the warehouse may be issued and counted as available by MRP.</summary>
    public bool IsAvailableStock => WarehouseType is WarehouseType.General or WarehouseType.Production;
}

/// <summary>Read-only facade over master data for the other modules of the monolith.</summary>
public sealed class MasterData(MastersDbContext db)
{
    /// <summary>Items by id. Throws when an id is unknown; optionally when an item is inactive.</summary>
    public async Task<IReadOnlyDictionary<Guid, ItemInfo>> GetItemsAsync(IEnumerable<Guid> ids, bool requireActive, CancellationToken cancellationToken)
    {
        var distinct = ids.Distinct().ToList();
        var items = await db.Items.AsNoTracking().Where(i => distinct.Contains(i.Id))
            .Select(i => new ItemInfo(
                i.Id, i.Code, i.Name, i.ItemType, i.SupplyType, i.StockUnitId, i.PurchaseUnitId, i.IsLotTracked, i.ShelfLifeDays,
                i.LeadTimeDays, i.SafetyStock, i.MinOrderQty, i.OrderMultiple, i.StandardCost, i.IsActive, i.SalesPrice))
            .ToDictionaryAsync(i => i.Id, cancellationToken);
        foreach (var id in distinct)
        {
            if (!items.TryGetValue(id, out var item))
            {
                throw new DomainException("masters.item.not_found", $"Item '{id}' does not exist.", 400);
            }

            if (requireActive && !item.IsActive)
            {
                throw new DomainException("masters.item.inactive", $"Item '{item.Code}' is inactive.");
            }
        }

        return items;
    }

    /// <summary>All items of the tenant (for MRP).</summary>
    public async Task<IReadOnlyDictionary<Guid, ItemInfo>> GetAllItemsAsync(CancellationToken cancellationToken) =>
        await db.Items.AsNoTracking()
            .Select(i => new ItemInfo(
                i.Id, i.Code, i.Name, i.ItemType, i.SupplyType, i.StockUnitId, i.PurchaseUnitId, i.IsLotTracked, i.ShelfLifeDays,
                i.LeadTimeDays, i.SafetyStock, i.MinOrderQty, i.OrderMultiple, i.StandardCost, i.IsActive, i.SalesPrice))
            .ToDictionaryAsync(i => i.Id, cancellationToken);

    /// <summary>Writes a new standard cost on items (used to apply rolled-up BOM costs).</summary>
    public async Task ApplyStandardCostsAsync(IReadOnlyDictionary<Guid, decimal> costByItem, CancellationToken cancellationToken)
    {
        var ids = costByItem.Keys.ToList();
        var items = await db.Items.Where(i => ids.Contains(i.Id)).ToListAsync(cancellationToken);
        foreach (var item in items)
        {
            item.SetStandardCost(costByItem[item.Id]);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Warehouses by id. Throws when an id is unknown or inactive.</summary>
    public async Task<IReadOnlyDictionary<Guid, WarehouseInfo>> GetWarehousesAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken)
    {
        var distinct = ids.Distinct().ToList();
        var warehouses = await db.Warehouses.AsNoTracking().Where(w => distinct.Contains(w.Id))
            .Select(w => new WarehouseInfo(w.Id, w.Code, w.Name, w.WarehouseType, w.IsActive))
            .ToDictionaryAsync(w => w.Id, cancellationToken);
        foreach (var id in distinct)
        {
            if (!warehouses.TryGetValue(id, out var warehouse) || !warehouse.IsActive)
            {
                throw new DomainException("masters.warehouse.not_found", $"Warehouse '{id}' does not exist or is inactive.", 400);
            }
        }

        return warehouses;
    }

    /// <summary>Ids of warehouses whose stock counts as available.</summary>
    public async Task<IReadOnlyList<Guid>> GetAvailableWarehouseIdsAsync(CancellationToken cancellationToken) =>
        await db.Warehouses.AsNoTracking()
            .Where(w => w.IsActive && (w.WarehouseType == WarehouseType.General || w.WarehouseType == WarehouseType.Production))
            .Select(w => w.Id)
            .ToListAsync(cancellationToken);

    /// <summary>Validates that a location belongs to the warehouse.</summary>
    public async Task EnsureLocationAsync(Guid warehouseId, Guid? locationId, CancellationToken cancellationToken)
    {
        if (locationId is null)
        {
            return;
        }

        var valid = await db.Locations.AnyAsync(l => l.Id == locationId && l.WarehouseId == warehouseId && l.IsActive, cancellationToken);
        if (!valid)
        {
            throw new DomainException("masters.location.not_found", "Location does not exist in the warehouse or is inactive.", 400);
        }
    }

    /// <summary>Supplier by id; throws when unknown or inactive.</summary>
    public async Task<Supplier> GetSupplierAsync(Guid id, CancellationToken cancellationToken)
    {
        var supplier = await db.Suppliers.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (supplier is null || !supplier.IsActive)
        {
            throw new DomainException("masters.supplier.not_found", $"Supplier '{id}' does not exist or is inactive.", 400);
        }

        return supplier;
    }

    /// <summary>Names of records for display, keyed by id.</summary>
    public async Task<IReadOnlyDictionary<Guid, string>> GetUnitCodesAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken)
    {
        var distinct = ids.Distinct().ToList();
        return await db.Units.AsNoTracking().Where(u => distinct.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Code, cancellationToken);
    }

    /// <summary>Validates that units exist.</summary>
    public async Task EnsureUnitsAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken)
    {
        var distinct = ids.Distinct().ToList();
        var found = await db.Units.CountAsync(u => distinct.Contains(u.Id), cancellationToken);
        if (found != distinct.Count)
        {
            throw new DomainException("masters.unit.not_found", "A unit does not exist.", 400);
        }
    }

    /// <summary>Builds a converter with the global rules and the rules of the given items.</summary>
    public async Task<UnitConverter> GetConverterAsync(IEnumerable<Guid> itemIds, CancellationToken cancellationToken)
    {
        var ids = itemIds.Distinct().ToList();
        var rules = await db.UnitConversions.AsNoTracking()
            .Where(c => c.ItemId == null || ids.Contains(c.ItemId.Value))
            .Select(c => new ConversionRule(c.ItemId, c.FromUnitId, c.ToUnitId, c.Factor))
            .ToListAsync(cancellationToken);
        return new UnitConverter(rules);
    }

    /// <summary>Converts a quantity of an item between units.</summary>
    public async Task<ConvertedQuantity> ConvertAsync(Guid itemId, Guid fromUnitId, Guid toUnitId, decimal quantity, CancellationToken cancellationToken)
    {
        var item = (await GetItemsAsync([itemId], requireActive: false, cancellationToken))[itemId];
        var converter = await GetConverterAsync([itemId], cancellationToken);
        var factor = converter.FindFactor(itemId, item.StockUnitId, fromUnitId, toUnitId)
            ?? throw new DomainException("masters.uom.no_conversion", "No unit conversion is defined between these units for the item.");
        return new ConvertedQuantity(UnitConverter.RoundQuantity(quantity * factor), factor);
    }

    /// <summary>Purchase price tier for a supplier, item, quantity and date; null when none applies.</summary>
    public async Task<SupplierPriceDto?> ResolveSupplierPriceAsync(Guid supplierId, Guid itemId, Guid unitId, decimal quantity, DateOnly date, CancellationToken cancellationToken)
    {
        var item = (await GetItemsAsync([itemId], requireActive: false, cancellationToken))[itemId];
        var converter = await GetConverterAsync([itemId], cancellationToken);
        var tiers = await db.SupplierPrices.AsNoTracking()
            .Where(p => p.SupplierId == supplierId && p.ItemId == itemId)
            .ToListAsync(cancellationToken);
        var tier = SupplierPriceResolver.Resolve(
            tiers,
            date,
            tierUnit => converter.FindFactor(itemId, item.StockUnitId, unitId, tierUnit) is { } factor ? quantity * factor : null);
        return tier is null
            ? null
            : new SupplierPriceDto(tier.Id, tier.SupplierId, tier.ItemId, tier.UnitId, tier.MinQty, tier.UnitPrice, tier.Currency, tier.ValidFrom, tier.ValidTo);
    }
}
