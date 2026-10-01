using Mrp.Masters.Application;
using Mrp.Masters.Domain;
using Mrp.Production.Domain;
using Mrp.SharedKernel.Domain;
using Mrp.SharedKernel.Persistence;

namespace Mrp.Production.Application;

/// <summary>Standard cost roll-up from BOMs, with sales price and margin per item.</summary>
public sealed class CostingService(MasterData masters, BomService boms, ITenantSettings settings, DbSession session)
{
    /// <summary>Cost breakdown of one item for a quantity.</summary>
    public async Task<CostBreakdownDto> GetAsync(Guid itemId, decimal quantity, CancellationToken cancellationToken)
    {
        var (items, active, overhead) = await LoadAsync(cancellationToken);
        var breakdown = CostRollup.Calculate(itemId, quantity, overhead, id => ToCostItem(items, id), id => active.GetValueOrDefault(id));
        var units = await masters.GetUnitCodesAsync(items.Values.Select(i => i.StockUnitId), cancellationToken);

        string Code(Guid id) => items.TryGetValue(id, out var item) ? item.Code : id.ToString();
        string Name(Guid id) => items.TryGetValue(id, out var item) ? item.Name : string.Empty;
        string Unit(Guid id) => items.TryGetValue(id, out var item) ? units.GetValueOrDefault(item.StockUnitId, string.Empty) : string.Empty;

        var item = items[itemId];
        return new CostBreakdownDto(
            itemId, item.Code, item.Name, Unit(itemId), breakdown.Quantity,
            breakdown.MaterialCost, breakdown.OverheadPercent, breakdown.OverheadCost, breakdown.TotalCost, breakdown.UnitCost,
            item.StandardCost, breakdown.SalesPrice, breakdown.UnitMargin, breakdown.MarginPercent,
            breakdown.Lines.Select(l => new CostLineDto(
                l.Level, l.ItemId, Code(l.ItemId), Name(l.ItemId), Unit(l.ItemId), l.ParentItemId, Code(l.ParentItemId),
                l.Quantity, l.UnitCost, l.Amount, l.IsMade)).ToList(),
            breakdown.Warnings);
    }

    /// <summary>Rolled-up cost, price and margin of every active manufactured item.</summary>
    public async Task<IReadOnlyList<ItemCostSummary>> ListAsync(CancellationToken cancellationToken)
    {
        var (items, active, overhead) = await LoadAsync(cancellationToken);
        var units = await masters.GetUnitCodesAsync(items.Values.Select(i => i.StockUnitId), cancellationToken);
        var result = new List<ItemCostSummary>();
        foreach (var item in items.Values.Where(i => i.IsActive && i.SupplyType == SupplyType.Make).OrderBy(i => i.Code, StringComparer.Ordinal))
        {
            var breakdown = CostRollup.Calculate(item.Id, 1m, overhead, id => ToCostItem(items, id), id => active.GetValueOrDefault(id));
            result.Add(new ItemCostSummary(
                item.Id, item.Code, item.Name, units.GetValueOrDefault(item.StockUnitId, string.Empty), breakdown.UnitCost, item.StandardCost,
                item.SalesPrice, breakdown.UnitMargin, breakdown.MarginPercent, breakdown.Warnings));
        }

        return result;
    }

    /// <summary>Writes the rolled-up unit cost into the standard cost of the given manufactured items.</summary>
    public Task<IReadOnlyList<ItemCostSummary>> ApplyAsync(ApplyCostsRequest request, CancellationToken cancellationToken) =>
        session.ExecuteInTransactionAsync<IReadOnlyList<ItemCostSummary>>(
            async ct =>
            {
                var (items, active, overhead) = await LoadAsync(ct);
                var costs = new Dictionary<Guid, decimal>();
                foreach (var id in request.ItemIds.Distinct())
                {
                    if (!items.TryGetValue(id, out var item))
                    {
                        throw new DomainException("masters.item.not_found", $"Item '{id}' does not exist.", 400);
                    }

                    if (item.SupplyType != SupplyType.Make || !active.ContainsKey(id))
                    {
                        throw new DomainException("production.costing.not_rolled_up", $"Item {item.Code} is not manufactured or has no active BOM.");
                    }

                    costs[id] = CostRollup.Calculate(id, 1m, overhead, itemId => ToCostItem(items, itemId), itemId => active.GetValueOrDefault(itemId)).UnitCost;
                }

                await masters.ApplyStandardCostsAsync(costs, ct);
                var all = await ListAsync(ct);
                return all.Where(s => costs.ContainsKey(s.ItemId)).ToList();
            },
            cancellationToken);

    private static CostItem? ToCostItem(IReadOnlyDictionary<Guid, ItemInfo> items, Guid id) =>
        items.TryGetValue(id, out var item) ? new CostItem(item.Id, item.Code, item.SupplyType == SupplyType.Make, item.StandardCost, item.SalesPrice) : null;

    private async Task<(IReadOnlyDictionary<Guid, ItemInfo> Items, Dictionary<Guid, BomDefinition> Active, decimal Overhead)> LoadAsync(CancellationToken cancellationToken)
    {
        var items = await masters.GetAllItemsAsync(cancellationToken);
        var active = await boms.ActiveDefinitionsAsync(cancellationToken);
        var overhead = await settings.GetAsync(SettingKeys.OverheadPercent, 0m, cancellationToken);
        return (items, active, overhead);
    }
}
