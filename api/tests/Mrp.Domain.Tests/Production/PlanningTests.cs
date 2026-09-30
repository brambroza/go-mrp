using Mrp.Production.Domain;
using Mrp.SharedKernel.Domain;

namespace Mrp.Domain.Tests.Production;

/// <summary>
/// Golden data: a 50 g cream jar made from bulk cream (two BOM levels), the kind of product the legacy
/// factory makes. Quantities were calculated by hand with the legacy formula
/// <c>used × parent ÷ batch</c> plus loss percent.
/// </summary>
internal static class Cream
{
    public static readonly Guid Jar50G = Guid.Parse("00000000-0000-0000-0000-000000000001");
    public static readonly Guid Bulk = Guid.Parse("00000000-0000-0000-0000-000000000002");
    public static readonly Guid EmptyJar = Guid.Parse("00000000-0000-0000-0000-000000000003");
    public static readonly Guid Label = Guid.Parse("00000000-0000-0000-0000-000000000004");
    public static readonly Guid Water = Guid.Parse("00000000-0000-0000-0000-000000000005");
    public static readonly Guid Oil = Guid.Parse("00000000-0000-0000-0000-000000000006");
    public static readonly Guid Fragrance = Guid.Parse("00000000-0000-0000-0000-000000000007");

    public static readonly BomDefinition FinishedGoodBom = new(Jar50G, 1000m,
    [
        new BomComponent(Bulk, 50m, 2m),
        new BomComponent(EmptyJar, 1000m, 1m),
        new BomComponent(Label, 1000m, 0.5m),
    ]);

    public static readonly BomDefinition BulkBom = new(Bulk, 100m,
    [
        new BomComponent(Water, 70m, 0m),
        new BomComponent(Oil, 25m, 3m),
        new BomComponent(Fragrance, 5m, 0m),
    ]);

    public static BomDefinition? Find(Guid itemId) =>
        itemId == Jar50G ? FinishedGoodBom : itemId == Bulk ? BulkBom : null;
}

public sealed class BomExploderTests
{
    [Theory]
    [InlineData(50, 2500, 1000, 2, 127.5)]
    [InlineData(1000, 2500, 1000, 1, 2525)]
    [InlineData(1000, 2500, 1000, 0.5, 2512.5)]
    [InlineData(25, 127.5, 100, 3, 32.83125)]
    [InlineData(1, 1, 3, 0, 0.333333)]
    [InlineData(2, 1, 3, 0, 0.666667)]
    [InlineData(10, 5, 0, 0, 50)]
    public void Component_quantity_follows_the_legacy_formula(decimal used, decimal parent, decimal batch, decimal loss, decimal expected)
    {
        Assert.Equal(expected, BomExploder.ComponentQuantity(used, parent, batch, loss));
    }

    [Fact]
    public void Explodes_two_levels_with_loss_applied_at_each_level()
    {
        var result = BomExploder.Explode(Cream.Jar50G, 2500m, Cream.Find);

        Assert.Equal(
        [
            new ExplodedComponent(Cream.Bulk, Cream.Jar50G, 1, 127.5m, true),
            new ExplodedComponent(Cream.Water, Cream.Bulk, 2, 89.25m, false),
            new ExplodedComponent(Cream.Oil, Cream.Bulk, 2, 32.83125m, false),
            new ExplodedComponent(Cream.Fragrance, Cream.Bulk, 2, 6.375m, false),
            new ExplodedComponent(Cream.EmptyJar, Cream.Jar50G, 1, 2525m, false),
            new ExplodedComponent(Cream.Label, Cream.Jar50G, 1, 2512.5m, false),
        ],
        result);
    }

    [Fact]
    public void Material_summary_contains_only_purchased_items_and_adds_up_repeated_use()
    {
        var withWaterInBoth = new BomDefinition(Cream.Jar50G, 1000m, [.. Cream.FinishedGoodBom.Lines, new BomComponent(Cream.Water, 10m, 0m)]);
        BomDefinition? Find(Guid id) => id == Cream.Jar50G ? withWaterInBoth : Cream.Find(id);

        var summary = BomExploder.SummarizeMaterials(BomExploder.Explode(Cream.Jar50G, 2500m, Find));

        Assert.Equal(5, summary.Count);
        Assert.False(summary.ContainsKey(Cream.Bulk));
        Assert.Equal(89.25m + 25m, summary[Cream.Water]);
        Assert.Equal(32.83125m, summary[Cream.Oil]);
    }

    [Fact]
    public void One_level_explosion_marks_components_that_are_made_in_house()
    {
        var result = BomExploder.ExplodeOneLevel(Cream.FinishedGoodBom, 1000m, id => id == Cream.Bulk);

        Assert.Equal([(Cream.Bulk, 51m, true), (Cream.EmptyJar, 1010m, false), (Cream.Label, 1005m, false)],
            result.Select(c => (c.ItemId, c.Quantity, c.IsMadeInHouse)));
    }

    [Fact]
    public void Cycle_is_detected()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var boms = new Dictionary<Guid, BomDefinition>
        {
            [a] = new(a, 1m, [new BomComponent(b, 1m, 0m)]),
            [b] = new(b, 1m, [new BomComponent(a, 1m, 0m)]),
        };

        var error = Assert.Throws<DomainException>(() => BomExploder.EnsureNoCycle(a, id => boms.GetValueOrDefault(id)));

        Assert.Equal("production.bom.cycle", error.Code);
        Assert.Equal("production.bom.cycle", Assert.Throws<DomainException>(() => BomExploder.LowLevelCodes(boms.Values)).Code);
    }

    [Fact]
    public void Item_that_contains_itself_is_a_cycle()
    {
        var a = Guid.NewGuid();
        var bom = new BomDefinition(a, 1m, [new BomComponent(a, 1m, 0m)]);

        Assert.Throws<DomainException>(() => BomExploder.EnsureNoCycle(a, id => id == a ? bom : null));
    }

    [Fact]
    public void Low_level_code_is_the_deepest_level_an_item_appears_at()
    {
        // Water is used directly in the finished good (level 1) and in the bulk (level 2).
        var withWaterInBoth = new BomDefinition(Cream.Jar50G, 1000m, [new BomComponent(Cream.Water, 10m, 0m), .. Cream.FinishedGoodBom.Lines]);

        var levels = BomExploder.LowLevelCodes([Cream.BulkBom, withWaterInBoth]);

        Assert.Equal(0, levels[Cream.Jar50G]);
        Assert.Equal(1, levels[Cream.Bulk]);
        Assert.Equal(1, levels[Cream.EmptyJar]);
        Assert.Equal(2, levels[Cream.Water]);
        Assert.Equal(2, levels[Cream.Oil]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Quantity_must_be_positive(decimal quantity)
    {
        Assert.Throws<DomainException>(() => BomExploder.Explode(Cream.Jar50G, quantity, Cream.Find));
    }
}

public sealed class MrpEngineTests
{
    private static readonly DateOnly Today = new(2026, 10, 1);

    private static readonly Dictionary<Guid, MrpItem> Items = new MrpItem[]
    {
        new(Cream.Jar50G, "FG-CREAM-50G", PlannedOrderType.Make, LeadTimeDays: 3, SafetyStock: 100m, MinOrderQty: 0m, OrderMultiple: 500m),
        new(Cream.Bulk, "SM-BULK-CREAM", PlannedOrderType.Make, LeadTimeDays: 2, SafetyStock: 0m, MinOrderQty: 100m, OrderMultiple: 0m),
        new(Cream.EmptyJar, "PK-JAR-50G", PlannedOrderType.Buy, LeadTimeDays: 14, SafetyStock: 500m, MinOrderQty: 0m, OrderMultiple: 1000m),
        new(Cream.Label, "PK-LABEL", PlannedOrderType.Buy, LeadTimeDays: 30, SafetyStock: 0m, MinOrderQty: 5000m, OrderMultiple: 0m),
        new(Cream.Water, "RM-WATER", PlannedOrderType.Buy, LeadTimeDays: 1, SafetyStock: 0m, MinOrderQty: 0m, OrderMultiple: 0m),
        new(Cream.Oil, "RM-OIL", PlannedOrderType.Buy, LeadTimeDays: 10, SafetyStock: 0m, MinOrderQty: 0m, OrderMultiple: 0m),
        new(Cream.Fragrance, "RM-FRAGRANCE", PlannedOrderType.Buy, LeadTimeDays: 7, SafetyStock: 10m, MinOrderQty: 0m, OrderMultiple: 0m),
    }.ToDictionary(i => i.ItemId);

    private static readonly MrpInput Scenario = new(
        Today,
        Items,
        OnHand: new Dictionary<Guid, decimal>
        {
            [Cream.Jar50G] = 300m,
            [Cream.Bulk] = 20m,
            [Cream.EmptyJar] = 1000m,
            [Cream.Oil] = 100m,
            [Cream.Fragrance] = 4m,
        },
        Demands: [new MrpDemand(Cream.Jar50G, Today.AddDays(20), 2500m, "SO-2610-0001")],
        Supplies: [new MrpSupply(Cream.EmptyJar, Today.AddDays(10), 1000m, "PO-2609-0007")],
        Boms: [Cream.FinishedGoodBom, Cream.BulkBom]);

    [Fact]
    public void Finished_good_is_netted_against_stock_and_safety_stock_then_lot_sized()
    {
        var result = MrpEngine.Run(Scenario);

        // 300 on hand − 2500 = −2200; below safety 100 by 2300; multiple of 500 → 2500.
        var order = Assert.Single(result.PlannedOrders, o => o.ItemId == Cream.Jar50G);
        Assert.Equal(new MrpPlannedOrder(Cream.Jar50G, PlannedOrderType.Make, 2500m, Today.AddDays(17), Today.AddDays(20), false, "SO-2610-0001"), order);
        Assert.Equal(
            new MrpRequirement(Cream.Jar50G, 0, 300m, 100m, 2500m, 0m, 2300m, 2500m),
            result.Requirements.Single(r => r.ItemId == Cream.Jar50G));
    }

    [Fact]
    public void Components_are_required_on_the_release_date_of_the_parent_order()
    {
        var result = MrpEngine.Run(Scenario);

        // Bulk: 50 × 2500 ÷ 1000 × 1.02 = 127.5 needed on day 17; 20 on hand → net 107.5; min order 100 → 107.5.
        var bulk = Assert.Single(result.PlannedOrders, o => o.ItemId == Cream.Bulk);
        Assert.Equal(new MrpPlannedOrder(Cream.Bulk, PlannedOrderType.Make, 107.5m, Today.AddDays(15), Today.AddDays(17), false, "FG-CREAM-50G"), bulk);

        // Water: 70 × 107.5 ÷ 100 = 75.25 needed on day 15; lead time 1.
        var water = Assert.Single(result.PlannedOrders, o => o.ItemId == Cream.Water);
        Assert.Equal(new MrpPlannedOrder(Cream.Water, PlannedOrderType.Buy, 75.25m, Today.AddDays(14), Today.AddDays(15), false, "SM-BULK-CREAM"), water);
    }

    [Fact]
    public void Scheduled_receipts_before_the_need_date_reduce_the_net_requirement()
    {
        var result = MrpEngine.Run(Scenario);

        // Jar: 1000 on hand + 1000 on PO − 2525 = −525; below safety 500 by 1025; multiple 1000 → 2000.
        var jar = Assert.Single(result.PlannedOrders, o => o.ItemId == Cream.EmptyJar);
        Assert.Equal(new MrpPlannedOrder(Cream.EmptyJar, PlannedOrderType.Buy, 2000m, Today.AddDays(3), Today.AddDays(17), false, "FG-CREAM-50G"), jar);
        Assert.Equal(
            new MrpRequirement(Cream.EmptyJar, 1, 1000m, 500m, 2525m, 1000m, 1025m, 2000m),
            result.Requirements.Single(r => r.ItemId == Cream.EmptyJar));
    }

    [Fact]
    public void Receipt_that_arrives_after_the_need_date_does_not_cover_it()
    {
        var late = Scenario with { Supplies = [new MrpSupply(Cream.EmptyJar, Today.AddDays(18), 1000m, "PO-2609-0007")] };

        var jar = Assert.Single(MrpEngine.Run(late).PlannedOrders, o => o.ItemId == Cream.EmptyJar);

        // 1000 − 2525 = −1525; below safety by 2025 → 3000.
        Assert.Equal(3000m, jar.Quantity);
    }

    [Fact]
    public void Stock_that_covers_the_requirement_creates_no_order()
    {
        var result = MrpEngine.Run(Scenario);

        Assert.DoesNotContain(result.PlannedOrders, o => o.ItemId == Cream.Oil);
        // Oil: 25 × 107.5 ÷ 100 × 1.03 = 27.68125.
        Assert.Equal(
            new MrpRequirement(Cream.Oil, 2, 100m, 0m, 27.68125m, 0m, 0m, 0m),
            result.Requirements.Single(r => r.ItemId == Cream.Oil));
    }

    [Fact]
    public void Order_that_should_have_been_released_in_the_past_is_flagged_late()
    {
        var result = MrpEngine.Run(Scenario);

        // Label: 2512.5 needed on day 17, lead time 30 → release 13 days ago; minimum order 5000.
        var label = Assert.Single(result.PlannedOrders, o => o.ItemId == Cream.Label);
        Assert.Equal(new MrpPlannedOrder(Cream.Label, PlannedOrderType.Buy, 5000m, Today.AddDays(-13), Today.AddDays(17), true, "FG-CREAM-50G"), label);
        var exception = Assert.Single(result.Exceptions);
        Assert.Equal((Cream.Label, MrpEngine.Late), (exception.ItemId, exception.Code));
        Assert.Contains("2026-09-18", exception.Message);
    }

    [Fact]
    public void Stock_below_safety_level_is_replenished_even_without_demand()
    {
        var result = MrpEngine.Run(Scenario);

        var orders = result.PlannedOrders.Where(o => o.ItemId == Cream.Fragrance).ToList();
        Assert.Equal(
        [
            // 4 on hand, safety 10 → 6 as soon as the lead time of 7 days allows.
            new MrpPlannedOrder(Cream.Fragrance, PlannedOrderType.Buy, 6m, Today, Today.AddDays(7), false, "SAFETY-STOCK"),
            // then 5 × 107.5 ÷ 100 = 5.375 for the bulk on day 15.
            new MrpPlannedOrder(Cream.Fragrance, PlannedOrderType.Buy, 5.375m, Today.AddDays(8), Today.AddDays(15), false, "SM-BULK-CREAM"),
        ],
        orders);
    }

    [Fact]
    public void Items_are_processed_level_by_level_and_the_result_is_deterministic()
    {
        var first = MrpEngine.Run(Scenario);
        var second = MrpEngine.Run(Scenario with { Boms = [Cream.BulkBom, Cream.FinishedGoodBom] });

        Assert.Equal(
            ["FG-CREAM-50G", "PK-JAR-50G", "PK-LABEL", "SM-BULK-CREAM", "RM-FRAGRANCE", "RM-OIL", "RM-WATER"],
            first.Requirements.Select(r => Items[r.ItemId].Code));
        Assert.Equal(first.PlannedOrders, second.PlannedOrders);
        Assert.Equal(first.Requirements, second.Requirements);
    }

    [Fact]
    public void Demands_on_several_dates_are_planned_in_date_order()
    {
        var input = Scenario with
        {
            Items = new Dictionary<Guid, MrpItem> { [Cream.Water] = Items[Cream.Water] },
            OnHand = new Dictionary<Guid, decimal> { [Cream.Water] = 50m },
            Supplies = [new MrpSupply(Cream.Water, Today.AddDays(6), 30m, "PO-1")],
            Boms = [],
            Demands =
            [
                new MrpDemand(Cream.Water, Today.AddDays(9), 40m, "WO-3"),
                new MrpDemand(Cream.Water, Today.AddDays(3), 40m, "WO-1"),
                new MrpDemand(Cream.Water, Today.AddDays(3), 5m, "WO-2"),
            ],
        };

        var result = MrpEngine.Run(input);

        // Day 3: 50 − 45 = 5. Day 9: 5 + 30 − 40 = −5 → order 5.
        var order = Assert.Single(result.PlannedOrders);
        Assert.Equal(new MrpPlannedOrder(Cream.Water, PlannedOrderType.Buy, 5m, Today.AddDays(8), Today.AddDays(9), false, "WO-3"), order);
        Assert.Equal(new MrpRequirement(Cream.Water, 0, 50m, 0m, 85m, 30m, 5m, 5m), Assert.Single(result.Requirements));
    }

    [Fact]
    public void Manufactured_item_without_bom_is_planned_and_reported()
    {
        var input = Scenario with { Boms = [Cream.BulkBom] };

        var result = MrpEngine.Run(input);

        Assert.Contains(result.PlannedOrders, o => o.ItemId == Cream.Jar50G);
        Assert.Contains(result.Exceptions, e => e.ItemId == Cream.Jar50G && e.Code == MrpEngine.NoBom);
        Assert.DoesNotContain(result.PlannedOrders, o => o.ItemId == Cream.Bulk);
    }

    [Fact]
    public void Unknown_and_inactive_items_are_reported()
    {
        var unknown = Guid.NewGuid();
        var items = new Dictionary<Guid, MrpItem>(Items) { [Cream.Water] = Items[Cream.Water] with { IsActive = false } };
        var input = new MrpInput(Today, items, new Dictionary<Guid, decimal>(),
            [new MrpDemand(unknown, Today, 1m, "X"), new MrpDemand(Cream.Water, Today.AddDays(5), 1m, "Y")], [], []);

        var result = MrpEngine.Run(input);

        Assert.Contains(result.Exceptions, e => e.ItemId == unknown && e.Code == MrpEngine.UnknownItem);
        Assert.Contains(result.Exceptions, e => e.ItemId == Cream.Water && e.Code == MrpEngine.InactiveItem);
    }

    [Theory]
    [InlineData(0, 0, 0, 0)]
    [InlineData(-5, 100, 50, 0)]
    [InlineData(7.5, 0, 0, 7.5)]
    [InlineData(7.5, 100, 0, 100)]
    [InlineData(101, 100, 50, 150)]
    [InlineData(100, 0, 50, 100)]
    [InlineData(100.000001, 0, 50, 150)]
    [InlineData(0.4, 0, 0.25, 0.5)]
    public void Lot_size_applies_minimum_then_rounds_up_to_the_multiple(decimal net, decimal minimum, decimal multiple, decimal expected)
    {
        Assert.Equal(expected, MrpEngine.LotSize(net, minimum, multiple));
    }
}
