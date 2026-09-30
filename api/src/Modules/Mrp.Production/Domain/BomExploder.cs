using Mrp.SharedKernel.Domain;

namespace Mrp.Production.Domain;

/// <summary>Active BOM of an item as plain data for calculations.</summary>
/// <param name="ItemId">Item that is produced.</param>
/// <param name="BatchSize">Output quantity the line quantities refer to, in the item's stock unit.</param>
/// <param name="Lines">Components.</param>
public sealed record BomDefinition(Guid ItemId, decimal BatchSize, IReadOnlyList<BomComponent> Lines);

/// <summary>Component of a BOM as plain data.</summary>
/// <param name="ComponentItemId">Component item.</param>
/// <param name="StockQuantity">Quantity per batch in the component's stock unit.</param>
/// <param name="LossPercent">Expected loss added on top, in percent.</param>
public sealed record BomComponent(Guid ComponentItemId, decimal StockQuantity, decimal LossPercent);

/// <summary>One node of an exploded BOM.</summary>
/// <param name="ItemId">Component item.</param>
/// <param name="ParentItemId">Item the component goes into.</param>
/// <param name="Level">Depth, 1 = direct component of the top item.</param>
/// <param name="Quantity">Required quantity including loss, in stock unit.</param>
/// <param name="IsMadeInHouse">True when the component has its own active BOM and is exploded further.</param>
public sealed record ExplodedComponent(Guid ItemId, Guid ParentItemId, int Level, decimal Quantity, bool IsMadeInHouse);

/// <summary>
/// Explodes bills of materials with the formula
/// <c>component = round(line quantity × parent quantity ÷ batch size × (1 + loss%/100), 6)</c>.
/// </summary>
public static class BomExploder
{
    /// <summary>Deepest BOM structure that is accepted.</summary>
    public const int MaxLevels = 20;

    /// <summary>Quantity of one component for a parent quantity.</summary>
    public static decimal ComponentQuantity(decimal lineQuantity, decimal parentQuantity, decimal batchSize, decimal lossPercent)
    {
        var batch = batchSize <= 0 ? 1m : batchSize;
        return Math.Round(lineQuantity * parentQuantity / batch * (1m + (lossPercent / 100m)), 6, MidpointRounding.AwayFromZero);
    }

    /// <summary>Direct components of an item for a quantity.</summary>
    public static IReadOnlyList<ExplodedComponent> ExplodeOneLevel(BomDefinition bom, decimal quantity, Func<Guid, bool> isMadeInHouse) =>
        bom.Lines
            .Select(l => new ExplodedComponent(
                l.ComponentItemId, bom.ItemId, 1, ComponentQuantity(l.StockQuantity, quantity, bom.BatchSize, l.LossPercent),
                isMadeInHouse(l.ComponentItemId)))
            .ToList();

    /// <summary>All levels of an item for a quantity, depth first in line order.</summary>
    /// <param name="itemId">Top item.</param>
    /// <param name="quantity">Quantity to produce.</param>
    /// <param name="findBom">Returns the active BOM of a manufactured item, or null.</param>
    public static IReadOnlyList<ExplodedComponent> Explode(Guid itemId, decimal quantity, Func<Guid, BomDefinition?> findBom)
    {
        if (quantity <= 0)
        {
            throw new DomainException("production.invalid_quantity", "Quantity must be greater than zero.", 400);
        }

        var result = new List<ExplodedComponent>();
        Walk(itemId, quantity, 1, [itemId], findBom, result);
        return result;
    }

    /// <summary>Total requirement of purchased (leaf) materials.</summary>
    public static IReadOnlyDictionary<Guid, decimal> SummarizeMaterials(IEnumerable<ExplodedComponent> components) =>
        components.Where(c => !c.IsMadeInHouse)
            .GroupBy(c => c.ItemId)
            .ToDictionary(g => g.Key, g => g.Sum(c => c.Quantity));

    /// <summary>Throws when the BOM structure below an item refers back to one of its ancestors.</summary>
    public static void EnsureNoCycle(Guid itemId, Func<Guid, BomDefinition?> findBom) =>
        Walk(itemId, 1m, 1, [itemId], findBom, []);

    /// <summary>
    /// Low-level code per item: the deepest level at which the item appears in any structure.
    /// Items that are never a component have level 0.
    /// </summary>
    public static IReadOnlyDictionary<Guid, int> LowLevelCodes(IEnumerable<BomDefinition> boms)
    {
        var byItem = boms.ToDictionary(b => b.ItemId);
        var levels = new Dictionary<Guid, int>();

        void Visit(Guid item, int level, HashSet<Guid> path)
        {
            if (level > MaxLevels)
            {
                throw new DomainException("production.bom.too_deep", $"BOM is deeper than {MaxLevels} levels.");
            }

            if (!levels.TryGetValue(item, out var known) || known < level)
            {
                levels[item] = level;
            }
            else if (known >= level && level > 0)
            {
                // Already visited at this depth or deeper; the subtree cannot get deeper through this path.
                return;
            }

            if (!byItem.TryGetValue(item, out var bom))
            {
                return;
            }

            foreach (var line in bom.Lines)
            {
                if (!path.Add(line.ComponentItemId))
                {
                    throw new DomainException("production.bom.cycle", "BOM refers back to one of its parent items.");
                }

                Visit(line.ComponentItemId, level + 1, path);
                path.Remove(line.ComponentItemId);
            }
        }

        foreach (var bom in byItem.Values.OrderBy(b => b.ItemId))
        {
            if (!levels.ContainsKey(bom.ItemId))
            {
                levels[bom.ItemId] = 0;
            }

            Visit(bom.ItemId, levels[bom.ItemId], [bom.ItemId]);
        }

        return levels;
    }

    private static void Walk(Guid itemId, decimal quantity, int level, HashSet<Guid> path, Func<Guid, BomDefinition?> findBom, List<ExplodedComponent> result)
    {
        if (level > MaxLevels)
        {
            throw new DomainException("production.bom.too_deep", $"BOM is deeper than {MaxLevels} levels.");
        }

        var bom = findBom(itemId);
        if (bom is null)
        {
            return;
        }

        foreach (var line in bom.Lines)
        {
            if (path.Contains(line.ComponentItemId))
            {
                throw new DomainException("production.bom.cycle", "BOM refers back to one of its parent items.");
            }

            var required = ComponentQuantity(line.StockQuantity, quantity, bom.BatchSize, line.LossPercent);
            var childBom = findBom(line.ComponentItemId);
            result.Add(new ExplodedComponent(line.ComponentItemId, itemId, level, required, childBom is not null));
            if (childBom is not null)
            {
                path.Add(line.ComponentItemId);
                Walk(line.ComponentItemId, required, level + 1, path, findBom, result);
                path.Remove(line.ComponentItemId);
            }
        }
    }
}
