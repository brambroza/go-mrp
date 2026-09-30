using Microsoft.EntityFrameworkCore;
using Mrp.Inventory.Application;
using Mrp.Masters.Application;
using Mrp.Masters.Domain;
using Mrp.Production.Domain;
using Mrp.Production.Persistence;
using Mrp.SharedKernel.Domain;
using Mrp.SharedKernel.Persistence;
using Mrp.SharedKernel.Tenancy;
using Mrp.SharedKernel.Web;

namespace Mrp.Production.Application;

/// <summary>Bills of materials: versions, activation, explosion and where-used.</summary>
public sealed class BomService(ProductionDbContext db, DbSession session, ITenantContext tenant, MasterData masters, InventoryQueries inventory)
{
    /// <summary>One page of BOM versions.</summary>
    public async Task<PagedResult<BomSummary>> ListAsync(Guid? itemId, BomStatus? status, int? page, int? pageSize, CancellationToken cancellationToken)
    {
        var query = db.Boms.AsNoTracking();
        if (itemId is { } item)
        {
            query = query.Where(b => b.ItemId == item);
        }

        if (status is { } s)
        {
            query = query.Where(b => b.Status == s);
        }

        var result = await query.OrderBy(b => b.ItemId).ThenByDescending(b => b.VersionNo)
            .Select(b => new { b.Id, b.ItemId, b.VersionNo, b.Name, b.BatchSize, b.Status, b.EffectiveFrom, LineCount = b.Lines.Count })
            .ToPagedAsync(page, pageSize, cancellationToken);
        var items = await masters.GetItemsAsync(result.Items.Select(b => b.ItemId), requireActive: false, cancellationToken);
        return new PagedResult<BomSummary>(
            result.Items.Select(b => new BomSummary(
                b.Id, b.ItemId, items[b.ItemId].Code, items[b.ItemId].Name, b.VersionNo, b.Name, b.BatchSize, b.Status, b.EffectiveFrom, b.LineCount)).ToList(),
            result.Total, result.Page, result.PageSize);
    }

    /// <summary>One BOM version.</summary>
    public async Task<BomDto> GetAsync(Guid id, CancellationToken cancellationToken) =>
        await ToDtoAsync(await LoadAsync(id, tracking: false, cancellationToken), cancellationToken);

    /// <summary>Creates a draft version with the next version number of the item.</summary>
    public Task<BomDto> CreateAsync(SaveBomRequest request, CancellationToken cancellationToken) =>
        session.ExecuteInTransactionAsync(
            async ct =>
            {
                var lines = await BuildLinesAsync(request, ct);
                var last = await db.Boms.Where(b => b.ItemId == request.ItemId).MaxAsync(b => (int?)b.VersionNo, ct) ?? 0;
                var bom = new BomVersion(request.ItemId, last + 1);
                bom.Update(request.Name, request.BatchSize, request.EffectiveFrom, request.Remark, lines);
                db.Boms.Add(bom);
                await db.SaveChangesAsync(ct);
                return await ToDtoAsync(bom, ct);
            },
            cancellationToken);

    /// <summary>Replaces a draft version.</summary>
    public Task<BomDto> UpdateAsync(Guid id, SaveBomRequest request, CancellationToken cancellationToken) =>
        session.ExecuteInTransactionAsync(
            async ct =>
            {
                var bom = await LoadAsync(id, tracking: true, ct);
                if (bom.ItemId != request.ItemId)
                {
                    throw DomainException.Conflict("production.bom.item_fixed", "The item of a BOM cannot be changed.");
                }

                bom.Update(request.Name, request.BatchSize, request.EffectiveFrom, request.Remark, await BuildLinesAsync(request, ct));
                await db.SaveChangesAsync(ct);
                return await ToDtoAsync(bom, ct);
            },
            cancellationToken);

    /// <summary>Copies a version into a new draft.</summary>
    public Task<BomDto> CopyAsync(Guid id, CancellationToken cancellationToken) =>
        session.ExecuteInTransactionAsync(
            async ct =>
            {
                var source = await LoadAsync(id, tracking: false, ct);
                var last = await db.Boms.Where(b => b.ItemId == source.ItemId).MaxAsync(b => b.VersionNo, ct);
                var copy = new BomVersion(source.ItemId, last + 1);
                copy.Update(
                    source.Name, source.BatchSize, null, source.Remark,
                    source.Lines.OrderBy(l => l.LineNo)
                        .Select(l => new BomLineData(l.ComponentItemId, l.UnitId, l.Quantity, l.ConversionFactor, l.StockQuantity, l.LossPercent, l.Remark))
                        .ToList());
                db.Boms.Add(copy);
                await db.SaveChangesAsync(ct);
                return await ToDtoAsync(copy, ct);
            },
            cancellationToken);

    /// <summary>Approves, activates or cancels a version. Activation supersedes the current active version.</summary>
    public Task<BomDto> ApplyAsync(Guid id, BomAction action, CancellationToken cancellationToken) =>
        session.ExecuteInTransactionAsync(
            async ct =>
            {
                var bom = await LoadAsync(id, tracking: true, ct);
                if (action == BomAction.Activate)
                {
                    var active = await ActiveDefinitionsAsync(ct);
                    active[bom.ItemId] = bom.ToDefinition();
                    BomExploder.EnsureNoCycle(bom.ItemId, item => active.GetValueOrDefault(item));

                    var current = await db.Boms.FirstOrDefaultAsync(b => b.ItemId == bom.ItemId && b.Status == BomStatus.Active, ct);
                    if (current is not null)
                    {
                        current.Apply(BomAction.Supersede);
                        // The unique index allows one active version per item, so the old one is written first.
                        await db.SaveChangesAsync(ct);
                    }
                }

                bom.Apply(action, tenant.UserId);
                await db.SaveChangesAsync(ct);
                return await ToDtoAsync(bom, ct);
            },
            cancellationToken);

    /// <summary>Explodes a version (any status) for a quantity; lower levels use the active versions.</summary>
    public async Task<ExplosionDto> ExplodeAsync(Guid id, decimal quantity, CancellationToken cancellationToken)
    {
        var bom = await LoadAsync(id, tracking: false, cancellationToken);
        var active = await ActiveDefinitionsAsync(cancellationToken);
        active[bom.ItemId] = bom.ToDefinition();
        var allItems = await masters.GetAllItemsAsync(cancellationToken);
        var components = BomExploder.Explode(bom.ItemId, quantity, item => IsMade(allItems, active, item) || item == bom.ItemId ? active.GetValueOrDefault(item) : null);

        var units = await masters.GetUnitCodesAsync(allItems.Values.Select(i => i.StockUnitId), cancellationToken);
        var available = await inventory.AvailableByItemAsync(cancellationToken);
        var rows = components.Select(c => new ExplosionRow(
            c.Level, c.ItemId, allItems[c.ItemId].Code, allItems[c.ItemId].Name, allItems[c.ItemId].ItemType.ToString(), c.ParentItemId,
            allItems[c.ParentItemId].Code, c.Quantity, units.GetValueOrDefault(allItems[c.ItemId].StockUnitId, string.Empty), c.IsMadeInHouse,
            available.GetValueOrDefault(c.ItemId))).ToList();
        var materials = BomExploder.SummarizeMaterials(components)
            .Select(m => new MaterialSummaryRow(
                m.Key, allItems[m.Key].Code, allItems[m.Key].Name, units.GetValueOrDefault(allItems[m.Key].StockUnitId, string.Empty), m.Value,
                available.GetValueOrDefault(m.Key), Math.Max(m.Value - available.GetValueOrDefault(m.Key), 0m)))
            .OrderBy(m => m.ItemCode, StringComparer.Ordinal)
            .ToList();
        return new ExplosionDto(bom.ItemId, quantity, rows, materials);
    }

    /// <summary>BOM versions that use an item as a component.</summary>
    public async Task<IReadOnlyList<WhereUsedRow>> WhereUsedAsync(Guid itemId, bool activeOnly, CancellationToken cancellationToken)
    {
        var rows = await (
            from bom in db.Boms.AsNoTracking()
            from line in bom.Lines
            where line.ComponentItemId == itemId && (!activeOnly || bom.Status == BomStatus.Active)
            orderby bom.ItemId, bom.VersionNo descending
            select new { bom.Id, bom.ItemId, bom.VersionNo, bom.Status, line.StockQuantity, bom.BatchSize }).Take(500).ToListAsync(cancellationToken);
        var items = await masters.GetItemsAsync(rows.Select(r => r.ItemId), requireActive: false, cancellationToken);
        return rows.Select(r => new WhereUsedRow(r.Id, r.ItemId, items[r.ItemId].Code, items[r.ItemId].Name, r.VersionNo, r.Status, r.StockQuantity, r.BatchSize)).ToList();
    }

    /// <summary>Active BOM definitions keyed by item.</summary>
    public async Task<Dictionary<Guid, BomDefinition>> ActiveDefinitionsAsync(CancellationToken cancellationToken)
    {
        var boms = await db.Boms.AsNoTracking().Include(b => b.Lines).Where(b => b.Status == BomStatus.Active).ToListAsync(cancellationToken);
        return boms.ToDictionary(b => b.ItemId, b => b.ToDefinition());
    }

    /// <summary>The active version of an item, or null.</summary>
    public Task<BomVersion?> FindActiveAsync(Guid itemId, CancellationToken cancellationToken) =>
        db.Boms.AsNoTracking().Include(b => b.Lines).FirstOrDefaultAsync(b => b.ItemId == itemId && b.Status == BomStatus.Active, cancellationToken);

    /// <summary>A component is made in house when the item is manufactured and has an active BOM.</summary>
    internal static bool IsMade(IReadOnlyDictionary<Guid, ItemInfo> items, IReadOnlyDictionary<Guid, BomDefinition> active, Guid itemId) =>
        items.TryGetValue(itemId, out var item) && item.SupplyType == SupplyType.Make && active.ContainsKey(itemId);

    private async Task<List<BomLineData>> BuildLinesAsync(SaveBomRequest request, CancellationToken cancellationToken)
    {
        var parent = (await masters.GetItemsAsync([request.ItemId], requireActive: true, cancellationToken))[request.ItemId];
        if (parent.SupplyType != SupplyType.Make)
        {
            throw new DomainException("production.bom.item_not_manufactured", $"Item {parent.Code} is purchased; set its supply type to Make before creating a BOM.");
        }

        var items = await masters.GetItemsAsync(request.Lines.Select(l => l.ComponentItemId), requireActive: true, cancellationToken);
        var converter = await masters.GetConverterAsync(items.Keys, cancellationToken);
        await masters.EnsureUnitsAsync(request.Lines.Where(l => l.UnitId is not null).Select(l => l.UnitId!.Value), cancellationToken);
        var lines = new List<BomLineData>();
        foreach (var (line, index) in request.Lines.Select((l, i) => (l, i + 1)))
        {
            var item = items[line.ComponentItemId];
            if (item.ItemType == ItemType.Service)
            {
                throw new DomainException("production.bom.service_component", $"Line {index}: service items cannot be components.", 400);
            }

            var unitId = line.UnitId ?? item.StockUnitId;
            var factor = converter.FindFactor(item.Id, item.StockUnitId, unitId, item.StockUnitId)
                ?? throw new DomainException("masters.uom.no_conversion", $"Line {index}: no unit conversion to the stock unit of item {item.Code}.");
            lines.Add(new BomLineData(line.ComponentItemId, unitId, line.Quantity, factor, UnitConverter.RoundQuantity(line.Quantity * factor), line.LossPercent, line.Remark));
        }

        return lines;
    }

    private async Task<BomVersion> LoadAsync(Guid id, bool tracking, CancellationToken cancellationToken)
    {
        var query = db.Boms.Include(b => b.Lines).AsQueryable();
        if (!tracking)
        {
            query = query.AsNoTracking();
        }

        return await query.FirstOrDefaultAsync(b => b.Id == id, cancellationToken) ?? throw DomainException.NotFound("BOM", id);
    }

    private async Task<BomDto> ToDtoAsync(BomVersion bom, CancellationToken cancellationToken)
    {
        var items = await masters.GetItemsAsync(bom.Lines.Select(l => l.ComponentItemId).Append(bom.ItemId), requireActive: false, cancellationToken);
        var units = await masters.GetUnitCodesAsync(bom.Lines.Select(l => l.UnitId).Append(items[bom.ItemId].StockUnitId), cancellationToken);
        var componentIds = bom.Lines.Select(l => l.ComponentItemId).ToList();
        var withActiveBom = await db.Boms.AsNoTracking()
            .Where(b => b.Status == BomStatus.Active && componentIds.Contains(b.ItemId))
            .Select(b => b.ItemId)
            .ToListAsync(cancellationToken);
        return new BomDto(
            bom.Id, bom.ItemId, items[bom.ItemId].Code, items[bom.ItemId].Name, units.GetValueOrDefault(items[bom.ItemId].StockUnitId, string.Empty),
            bom.VersionNo, bom.Name, bom.BatchSize, bom.Status, bom.EffectiveFrom, bom.Remark,
            bom.Lines.OrderBy(l => l.LineNo).Select(l => new BomLineDto(
                l.Id, l.LineNo, l.ComponentItemId, items[l.ComponentItemId].Code, items[l.ComponentItemId].Name, items[l.ComponentItemId].ItemType.ToString(),
                l.UnitId, units.GetValueOrDefault(l.UnitId, string.Empty), l.Quantity, l.ConversionFactor, l.StockQuantity, l.LossPercent,
                items[l.ComponentItemId].SupplyType == SupplyType.Make && withActiveBom.Contains(l.ComponentItemId), l.Remark)).ToList());
    }
}
