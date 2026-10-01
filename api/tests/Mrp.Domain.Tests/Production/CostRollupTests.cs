using Mrp.Production.Domain;
using Mrp.SharedKernel.Domain;

namespace Mrp.Domain.Tests.Production;

/// <summary>
/// Golden data for the cost roll-up, calculated by hand on the cream jar of <see cref="Cream"/>:
/// bulk per kg = 0.7×3 + 0.2575×90 + 0.05×1,200 = 85.275, ×1.1 overhead = 93.8025;
/// jar per piece = 0.051×93.8025 + 1.01×2.5 + 1.005×0.4 = 7.7109275, ×1.1 = 8.48202025.
/// </summary>
public sealed class CostRollupTests
{
    private static readonly Dictionary<Guid, CostItem> Items = new CostItem[]
    {
        new(Cream.Jar50G, "FG-CREAM-50G", IsMade: true, StandardCost: 0m, SalesPrice: 18.5m),
        new(Cream.Bulk, "SM-BULK-CREAM", IsMade: true, StandardCost: 0m, SalesPrice: 0m),
        new(Cream.EmptyJar, "PK-JAR-50G", IsMade: false, StandardCost: 2.5m, SalesPrice: 0m),
        new(Cream.Label, "PK-LABEL", IsMade: false, StandardCost: 0.4m, SalesPrice: 0m),
        new(Cream.Water, "RM-WATER", IsMade: false, StandardCost: 3m, SalesPrice: 0m),
        new(Cream.Oil, "RM-OIL", IsMade: false, StandardCost: 90m, SalesPrice: 0m),
        new(Cream.Fragrance, "RM-FRAGRANCE", IsMade: false, StandardCost: 1200m, SalesPrice: 0m),
    }.ToDictionary(i => i.ItemId);

    private static CostItem? FindItem(Guid id) => Items.GetValueOrDefault(id);

    [Fact]
    public void Unit_cost_rolls_up_two_levels_with_overhead_at_each_level()
    {
        Assert.Equal(93.8025m, CostRollup.UnitCostOf(Cream.Bulk, 10m, FindItem, Cream.Find));
        Assert.Equal(8.48202025m, CostRollup.UnitCostOf(Cream.Jar50G, 10m, FindItem, Cream.Find));
    }

    [Fact]
    public void Breakdown_of_a_batch_lists_every_level_and_the_margin()
    {
        var result = CostRollup.Calculate(Cream.Jar50G, 1000m, 10m, FindItem, Cream.Find);

        Assert.Equal(7710.9275m, result.MaterialCost);
        Assert.Equal(771.0928m, result.OverheadCost);
        Assert.Equal(8482.0203m, result.TotalCost);
        Assert.Equal(8.4820m, result.UnitCost);
        Assert.Equal(18.5m, result.SalesPrice);
        Assert.Equal(10.018m, result.UnitMargin);
        Assert.Equal(54.15m, result.MarginPercent);
        Assert.Empty(result.Warnings);

        Assert.Equal(
        [
            (Cream.Bulk, 1, 51m, 93.8025m, 4783.9275m, true),
            (Cream.Water, 2, 35.7m, 3m, 107.1m, false),
            (Cream.Oil, 2, 13.1325m, 90m, 1181.925m, false),
            (Cream.Fragrance, 2, 2.55m, 1200m, 3060m, false),
            (Cream.EmptyJar, 1, 1010m, 2.5m, 2525m, false),
            (Cream.Label, 1, 1005m, 0.4m, 402m, false),
        ],
        result.Lines.Select(l => (l.ItemId, l.Level, l.Quantity, l.UnitCost, l.Amount, l.IsMade)));
    }

    [Fact]
    public void Zero_overhead_is_pure_material_cost()
    {
        var result = CostRollup.Calculate(Cream.Bulk, 100m, 0m, FindItem, Cream.Find);

        Assert.Equal(8527.5m, result.MaterialCost);
        Assert.Equal(0m, result.OverheadCost);
        Assert.Equal(85.275m, result.UnitCost);
        Assert.Null(result.UnitMargin);
        Assert.Contains(CostRollup.NoPrice, result.Warnings);
    }

    [Fact]
    public void Purchased_item_costs_its_standard_cost_without_overhead()
    {
        var result = CostRollup.Calculate(Cream.Oil, 3m, 10m, FindItem, Cream.Find);

        Assert.Equal(270m, result.TotalCost);
        Assert.Equal(90m, result.UnitCost);
        Assert.Empty(result.Lines);
    }

    [Fact]
    public void Made_item_without_bom_falls_back_to_its_standard_cost_and_warns()
    {
        var items = new Dictionary<Guid, CostItem>(Items) { [Cream.Bulk] = Items[Cream.Bulk] with { StandardCost = 80m } };

        var result = CostRollup.Calculate(Cream.Jar50G, 1m, 0m, id => items.GetValueOrDefault(id), id => id == Cream.Jar50G ? Cream.FinishedGoodBom : null);

        // 0.051 × 80 + 1.01 × 2.5 + 1.005 × 0.4 = 7.007
        Assert.Equal(7.007m, result.UnitCost);
        Assert.Contains(CostRollup.NoBom, result.Warnings);
    }

    [Fact]
    public void Unknown_component_is_costed_at_zero_and_reported()
    {
        var items = new Dictionary<Guid, CostItem>(Items);
        items.Remove(Cream.Label);

        var result = CostRollup.Calculate(Cream.Jar50G, 1m, 0m, id => items.GetValueOrDefault(id), Cream.Find);

        Assert.Contains(CostRollup.UnknownItem, result.Warnings);
        Assert.Equal(0m, result.Lines.Single(l => l.ItemId == Cream.Label).Amount);
    }

    [Fact]
    public void Cycle_is_refused()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var items = new Dictionary<Guid, CostItem> { [a] = new(a, "A", true, 0m, 0m), [b] = new(b, "B", true, 0m, 0m) };
        var boms = new Dictionary<Guid, BomDefinition>
        {
            [a] = new(a, 1m, [new BomComponent(b, 1m, 0m)]),
            [b] = new(b, 1m, [new BomComponent(a, 1m, 0m)]),
        };

        var error = Assert.Throws<DomainException>(() => CostRollup.Calculate(a, 1m, 0m, id => items.GetValueOrDefault(id), id => boms.GetValueOrDefault(id)));

        Assert.Equal("production.bom.cycle", error.Code);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Quantity_must_be_positive(decimal quantity)
    {
        Assert.Throws<DomainException>(() => CostRollup.Calculate(Cream.Jar50G, quantity, 0m, FindItem, Cream.Find));
    }

    [Fact]
    public void Overhead_must_not_be_negative()
    {
        Assert.Equal("production.costing.invalid_overhead",
            Assert.Throws<DomainException>(() => CostRollup.Calculate(Cream.Jar50G, 1m, -5m, FindItem, Cream.Find)).Code);
    }
}
