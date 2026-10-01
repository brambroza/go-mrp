using Mrp.SharedKernel.Domain;

namespace Mrp.Production.Domain;

/// <summary>Cost data of an item as plain input for the roll-up.</summary>
/// <param name="ItemId">Item.</param>
/// <param name="Code">Item code.</param>
/// <param name="IsMade">Manufactured in house (supply type Make).</param>
/// <param name="StandardCost">Cost per stock unit kept on the item master.</param>
/// <param name="SalesPrice">List price per stock unit; 0 when not priced.</param>
public sealed record CostItem(Guid ItemId, string Code, bool IsMade, decimal StandardCost, decimal SalesPrice);

/// <summary>One component line of a cost breakdown.</summary>
/// <param name="ItemId">Component.</param>
/// <param name="ParentItemId">Item the component goes into.</param>
/// <param name="Level">1 = direct component of the costed item.</param>
/// <param name="Quantity">Quantity for the costed quantity, including loss, in stock unit.</param>
/// <param name="UnitCost">Cost per stock unit (rolled up when the component is made in house).</param>
/// <param name="Amount">Quantity × unit cost.</param>
/// <param name="IsMade">Component has its own BOM and was rolled up.</param>
public sealed record CostLine(Guid ItemId, Guid ParentItemId, int Level, decimal Quantity, decimal UnitCost, decimal Amount, bool IsMade);

/// <summary>Standard cost of an item built from its BOM.</summary>
/// <param name="ItemId">Costed item.</param>
/// <param name="Quantity">Quantity the amounts refer to.</param>
/// <param name="MaterialCost">Sum of direct component amounts (components made in house carry their own overhead).</param>
/// <param name="OverheadPercent">Overhead rate applied to the material cost.</param>
/// <param name="OverheadCost">Material cost × overhead percent.</param>
/// <param name="TotalCost">Material plus overhead.</param>
/// <param name="UnitCost">Total cost ÷ quantity, rounded to 4 decimals.</param>
/// <param name="SalesPrice">List price per unit.</param>
/// <param name="UnitMargin">Sales price − unit cost; null when the item is not priced.</param>
/// <param name="MarginPercent">Unit margin ÷ sales price × 100; null when the item is not priced.</param>
/// <param name="Lines">All levels of the breakdown, parent before children.</param>
/// <param name="Warnings">Codes such as <c>NO_BOM</c> or <c>NO_PRICE</c>.</param>
public sealed record CostBreakdown(
    Guid ItemId,
    decimal Quantity,
    decimal MaterialCost,
    decimal OverheadPercent,
    decimal OverheadCost,
    decimal TotalCost,
    decimal UnitCost,
    decimal SalesPrice,
    decimal? UnitMargin,
    decimal? MarginPercent,
    IReadOnlyList<CostLine> Lines,
    IReadOnlyList<string> Warnings);

/// <summary>
/// Rolls BOM costs up: a purchased component costs its standard cost; a manufactured component costs
/// its own roll-up (materials plus overhead); overhead is a percentage of material cost and is added
/// at every manufacturing level. Formula per unit of a made item:
/// <c>unit_cost = Σ(line qty ÷ batch × (1 + loss%) × component unit cost) × (1 + overhead%)</c>.
/// </summary>
public static class CostRollup
{
    /// <summary>Warning: a manufactured item has no active BOM, so its standard cost was used.</summary>
    public const string NoBom = "NO_BOM";

    /// <summary>Warning: the item has no sales price, so no margin was computed.</summary>
    public const string NoPrice = "NO_PRICE";

    /// <summary>Warning: a component has no cost data; it was costed at zero.</summary>
    public const string UnknownItem = "UNKNOWN_ITEM";

    /// <summary>Rounds a money amount to 4 decimals, half away from zero.</summary>
    public static decimal RoundMoney(decimal amount) => Math.Round(amount, 4, MidpointRounding.AwayFromZero);

    /// <summary>Unrounded cost per one stock unit of an item.</summary>
    /// <param name="itemId">Item.</param>
    /// <param name="overheadPercent">Overhead in percent of material cost.</param>
    /// <param name="findItem">Cost data of an item, or null when unknown.</param>
    /// <param name="findBom">Active BOM of a manufactured item, or null.</param>
    /// <param name="warnings">Receives warning codes; may be null.</param>
    public static decimal UnitCostOf(Guid itemId, decimal overheadPercent, Func<Guid, CostItem?> findItem, Func<Guid, BomDefinition?> findBom, ICollection<string>? warnings = null) =>
        UnitCostCore(itemId, overheadPercent, findItem, findBom, [itemId], warnings ?? new List<string>(), 1);

    /// <summary>Full breakdown for a quantity of an item.</summary>
    /// <param name="itemId">Item to cost.</param>
    /// <param name="quantity">Quantity in stock unit; must be positive.</param>
    /// <param name="overheadPercent">Overhead in percent of material cost; must not be negative.</param>
    /// <param name="findItem">Cost data of an item, or null when unknown.</param>
    /// <param name="findBom">Active BOM of a manufactured item, or null.</param>
    public static CostBreakdown Calculate(
        Guid itemId,
        decimal quantity,
        decimal overheadPercent,
        Func<Guid, CostItem?> findItem,
        Func<Guid, BomDefinition?> findBom)
    {
        if (quantity <= 0)
        {
            throw new DomainException("production.invalid_quantity", "Quantity must be greater than zero.", 400);
        }

        if (overheadPercent < 0)
        {
            throw new DomainException("production.costing.invalid_overhead", "Overhead percent must not be negative.", 400);
        }

        var item = findItem(itemId) ?? throw new DomainException("masters.item.not_found", $"Item '{itemId}' does not exist.", 400);
        var warnings = new List<string>();
        var lines = new List<CostLine>();
        var bom = item.IsMade ? findBom(itemId) : null;

        decimal material;
        decimal overhead;
        if (bom is null)
        {
            // Nothing to explode: a purchased item, or a made item without a BOM (its standard cost stands in).
            if (item.IsMade)
            {
                warnings.Add(NoBom);
            }

            material = item.StandardCost * quantity;
            overhead = 0m;
        }
        else
        {
            material = Walk(itemId, bom, quantity, 1, overheadPercent, findItem, findBom, [itemId], warnings, lines);
            overhead = material * overheadPercent / 100m;
        }

        var total = material + overhead;
        var unit = RoundMoney(total / quantity);
        var priced = item.SalesPrice > 0;
        if (!priced)
        {
            warnings.Add(NoPrice);
        }

        return new CostBreakdown(
            itemId,
            quantity,
            RoundMoney(material),
            overheadPercent,
            RoundMoney(overhead),
            RoundMoney(total),
            unit,
            item.SalesPrice,
            priced ? RoundMoney(item.SalesPrice - unit) : null,
            priced ? Math.Round((item.SalesPrice - unit) / item.SalesPrice * 100m, 2, MidpointRounding.AwayFromZero) : null,
            lines,
            warnings.Distinct().ToList());
    }

    /// <summary>Adds the component lines of one BOM level (and, recursively, of made components) and returns the material amount.</summary>
    private static decimal Walk(
        Guid parentId,
        BomDefinition bom,
        decimal parentQuantity,
        int level,
        decimal overheadPercent,
        Func<Guid, CostItem?> findItem,
        Func<Guid, BomDefinition?> findBom,
        HashSet<Guid> path,
        List<string> warnings,
        List<CostLine> lines)
    {
        if (level > BomExploder.MaxLevels)
        {
            throw new DomainException("production.bom.too_deep", $"BOM is deeper than {BomExploder.MaxLevels} levels.");
        }

        var material = 0m;
        foreach (var line in bom.Lines)
        {
            if (!path.Add(line.ComponentItemId))
            {
                throw new DomainException("production.bom.cycle", "BOM refers back to one of its parent items.");
            }

            var quantity = BomExploder.ComponentQuantity(line.StockQuantity, parentQuantity, bom.BatchSize, line.LossPercent);
            var component = findItem(line.ComponentItemId);
            var componentBom = component is { IsMade: true } ? findBom(line.ComponentItemId) : null;
            var unitCost = UnitCostCore(line.ComponentItemId, overheadPercent, findItem, findBom, path, warnings, level + 1);
            var amount = quantity * unitCost;
            lines.Add(new CostLine(line.ComponentItemId, parentId, level, quantity, RoundMoney(unitCost), RoundMoney(amount), componentBom is not null));
            if (componentBom is not null)
            {
                Walk(line.ComponentItemId, componentBom, quantity, level + 1, overheadPercent, findItem, findBom, path, warnings, lines);
            }

            path.Remove(line.ComponentItemId);
            material += amount;
        }

        return material;
    }

    private static decimal UnitCostCore(
        Guid itemId,
        decimal overheadPercent,
        Func<Guid, CostItem?> findItem,
        Func<Guid, BomDefinition?> findBom,
        HashSet<Guid> path,
        ICollection<string> warnings,
        int level)
    {
        if (level > BomExploder.MaxLevels)
        {
            throw new DomainException("production.bom.too_deep", $"BOM is deeper than {BomExploder.MaxLevels} levels.");
        }

        var item = findItem(itemId);
        if (item is null)
        {
            warnings.Add(UnknownItem);
            return 0m;
        }

        var bom = item.IsMade ? findBom(itemId) : null;
        if (bom is null)
        {
            if (item.IsMade)
            {
                warnings.Add(NoBom);
            }

            return item.StandardCost;
        }

        var material = 0m;
        foreach (var line in bom.Lines)
        {
            if (!path.Add(line.ComponentItemId))
            {
                throw new DomainException("production.bom.cycle", "BOM refers back to one of its parent items.");
            }

            var perUnit = BomExploder.ComponentQuantity(line.StockQuantity, 1m, bom.BatchSize, line.LossPercent);
            material += perUnit * UnitCostCore(line.ComponentItemId, overheadPercent, findItem, findBom, path, warnings, level + 1);
            path.Remove(line.ComponentItemId);
        }

        return material * (1m + (overheadPercent / 100m));
    }
}
