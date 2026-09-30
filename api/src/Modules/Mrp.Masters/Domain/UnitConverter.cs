using Mrp.SharedKernel.Domain;

namespace Mrp.Masters.Domain;

/// <summary>A conversion rule as plain data.</summary>
/// <param name="ItemId">Item the rule applies to; null = every item.</param>
/// <param name="FromUnitId">Source unit.</param>
/// <param name="ToUnitId">Target unit.</param>
/// <param name="Factor">Multiplier from source to target.</param>
public sealed record ConversionRule(Guid? ItemId, Guid FromUnitId, Guid ToUnitId, decimal Factor);

/// <summary>
/// Converts quantities between units with one formula: <c>qty_to = round(qty_from × factor, 6)</c>.
/// Lookup order: same unit → item rule (either direction) → global rule (either direction) → via the stock unit.
/// </summary>
public sealed class UnitConverter
{
    /// <summary>Decimal places of quantities.</summary>
    public const int QuantityScale = 6;

    private readonly Dictionary<(Guid? Item, Guid From, Guid To), decimal> _rules = new();

    /// <summary>Creates a converter over the given rules.</summary>
    public UnitConverter(IEnumerable<ConversionRule> rules)
    {
        foreach (var rule in rules)
        {
            _rules[(rule.ItemId, rule.FromUnitId, rule.ToUnitId)] = rule.Factor;
        }
    }

    /// <summary>Rounds a quantity to the ledger scale (half away from zero).</summary>
    public static decimal RoundQuantity(decimal quantity) => Math.Round(quantity, QuantityScale, MidpointRounding.AwayFromZero);

    /// <summary>Returns the multiplier from one unit to another for an item, or null when no path exists.</summary>
    public decimal? FindFactor(Guid itemId, Guid stockUnitId, Guid fromUnitId, Guid toUnitId)
    {
        if (fromUnitId == toUnitId)
        {
            return 1m;
        }

        if (Direct(itemId, fromUnitId, toUnitId) is { } direct)
        {
            return direct;
        }

        if (fromUnitId != stockUnitId && toUnitId != stockUnitId
            && Direct(itemId, fromUnitId, stockUnitId) is { } toStock
            && Direct(itemId, stockUnitId, toUnitId) is { } fromStock)
        {
            return toStock * fromStock;
        }

        return null;
    }

    /// <summary>Converts a quantity, throwing when no conversion is defined.</summary>
    public decimal Convert(Guid itemId, Guid stockUnitId, Guid fromUnitId, Guid toUnitId, decimal quantity)
    {
        var factor = FindFactor(itemId, stockUnitId, fromUnitId, toUnitId)
            ?? throw new DomainException("masters.uom.no_conversion", "No unit conversion is defined between these units for the item.");
        return RoundQuantity(quantity * factor);
    }

    /// <summary>Converts a quantity to the item's stock unit.</summary>
    public decimal ToStockUnit(Guid itemId, Guid stockUnitId, Guid fromUnitId, decimal quantity) =>
        Convert(itemId, stockUnitId, fromUnitId, stockUnitId, quantity);

    private decimal? Direct(Guid itemId, Guid from, Guid to)
    {
        if (_rules.TryGetValue((itemId, from, to), out var itemForward))
        {
            return itemForward;
        }

        if (_rules.TryGetValue((itemId, to, from), out var itemBackward))
        {
            return 1m / itemBackward;
        }

        if (_rules.TryGetValue((null, from, to), out var globalForward))
        {
            return globalForward;
        }

        if (_rules.TryGetValue((null, to, from), out var globalBackward))
        {
            return 1m / globalBackward;
        }

        return null;
    }
}

/// <summary>Picks the purchase price tier for a quantity and date.</summary>
public static class SupplierPriceResolver
{
    /// <summary>
    /// Returns the valid tier with the largest minimum quantity not above the ordered quantity,
    /// after converting the ordered quantity to each tier's unit. Returns null when nothing matches.
    /// </summary>
    /// <param name="tiers">Tiers of one supplier and item.</param>
    /// <param name="date">Document date.</param>
    /// <param name="quantityIn">Function converting the ordered quantity into a tier's unit; null when not convertible.</param>
    public static SupplierPrice? Resolve(IEnumerable<SupplierPrice> tiers, DateOnly date, Func<Guid, decimal?> quantityIn) =>
        tiers.Where(t => t.IsValidOn(date))
            .Select(t => new { Tier = t, Quantity = quantityIn(t.UnitId) })
            .Where(x => x.Quantity is { } quantity && x.Tier.MinQty <= quantity)
            .OrderByDescending(x => x.Tier.MinQty)
            .ThenByDescending(x => x.Tier.ValidFrom)
            .Select(x => x.Tier)
            .FirstOrDefault();
}
