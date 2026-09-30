using Mrp.SharedKernel.Domain;

namespace Mrp.Production.Domain;

/// <summary>How a planned order is fulfilled.</summary>
public enum PlannedOrderType
{
    /// <summary>Purchase; becomes a purchase request.</summary>
    Buy,

    /// <summary>Manufacture; becomes a work order.</summary>
    Make,
}

/// <summary>Planning parameters of an item.</summary>
/// <param name="ItemId">Item.</param>
/// <param name="Code">Item code (used for deterministic ordering and messages).</param>
/// <param name="OrderType">Buy or make.</param>
/// <param name="LeadTimeDays">Days between release and availability.</param>
/// <param name="SafetyStock">Quantity kept in reserve.</param>
/// <param name="MinOrderQty">Smallest order quantity; 0 = none.</param>
/// <param name="OrderMultiple">Order quantity is rounded up to a multiple of this; 0 = none.</param>
/// <param name="IsActive">Inactive items are planned but reported.</param>
public sealed record MrpItem(
    Guid ItemId, string Code, PlannedOrderType OrderType, int LeadTimeDays, decimal SafetyStock, decimal MinOrderQty,
    decimal OrderMultiple, bool IsActive = true);

/// <summary>Requirement of an item on a date.</summary>
/// <param name="ItemId">Item.</param>
/// <param name="Date">Date the quantity is needed.</param>
/// <param name="Quantity">Quantity in stock unit.</param>
/// <param name="Source">What causes the requirement (document number or parent item code).</param>
public sealed record MrpDemand(Guid ItemId, DateOnly Date, decimal Quantity, string Source);

/// <summary>Expected receipt of an item on a date (open purchase or work order).</summary>
/// <param name="ItemId">Item.</param>
/// <param name="Date">Expected date.</param>
/// <param name="Quantity">Quantity in stock unit.</param>
/// <param name="Source">Document number.</param>
public sealed record MrpSupply(Guid ItemId, DateOnly Date, decimal Quantity, string Source);

/// <summary>Everything an MRP run needs. Pure data: the engine does not read the database.</summary>
/// <param name="Today">Planning date in the tenant's time zone.</param>
/// <param name="Items">Planning parameters of every item that may appear.</param>
/// <param name="OnHand">Available stock per item.</param>
/// <param name="Demands">Independent demand and outstanding material requirements of released work orders.</param>
/// <param name="Supplies">Scheduled receipts.</param>
/// <param name="Boms">Active BOMs of manufactured items.</param>
public sealed record MrpInput(
    DateOnly Today,
    IReadOnlyDictionary<Guid, MrpItem> Items,
    IReadOnlyDictionary<Guid, decimal> OnHand,
    IReadOnlyList<MrpDemand> Demands,
    IReadOnlyList<MrpSupply> Supplies,
    IReadOnlyList<BomDefinition> Boms);

/// <summary>Proposal to buy or make a quantity.</summary>
/// <param name="ItemId">Item.</param>
/// <param name="OrderType">Buy or make.</param>
/// <param name="Quantity">Quantity after lot sizing, in stock unit.</param>
/// <param name="ReleaseDate">Date the order must be placed or started.</param>
/// <param name="DueDate">Date the quantity is needed.</param>
/// <param name="IsLate">True when the release date is before today.</param>
/// <param name="Pegging">Sources of the requirement that triggered the order.</param>
public sealed record MrpPlannedOrder(
    Guid ItemId, PlannedOrderType OrderType, decimal Quantity, DateOnly ReleaseDate, DateOnly DueDate, bool IsLate, string Pegging);

/// <summary>Netting result of one item.</summary>
/// <param name="ItemId">Item.</param>
/// <param name="Level">Low-level code.</param>
/// <param name="OnHand">Available stock at the start.</param>
/// <param name="SafetyStock">Safety stock.</param>
/// <param name="Gross">Total gross requirement.</param>
/// <param name="ScheduledReceipts">Total scheduled receipts inside the horizon.</param>
/// <param name="Net">Total net requirement.</param>
/// <param name="Planned">Total planned order quantity.</param>
public sealed record MrpRequirement(
    Guid ItemId, int Level, decimal OnHand, decimal SafetyStock, decimal Gross, decimal ScheduledReceipts, decimal Net, decimal Planned);

/// <summary>Something the planner must look at.</summary>
/// <param name="ItemId">Item.</param>
/// <param name="Code">Exception code: <c>NO_BOM</c>, <c>LATE</c>, <c>INACTIVE_ITEM</c>, <c>UNKNOWN_ITEM</c>.</param>
/// <param name="Message">English description.</param>
public sealed record MrpException(Guid ItemId, string Code, string Message);

/// <summary>Output of an MRP run.</summary>
public sealed record MrpResult(
    IReadOnlyList<MrpRequirement> Requirements,
    IReadOnlyList<MrpPlannedOrder> PlannedOrders,
    IReadOnlyList<MrpException> Exceptions);

/// <summary>
/// Time-phased material requirements planning: items are processed by low-level code; for each item
/// demand is netted against on-hand stock and scheduled receipts in date order, shortages below the
/// safety stock become planned orders (lot sized, offset by lead time), and planned orders of
/// manufactured items become dependent demand of their components.
/// </summary>
public static class MrpEngine
{
    /// <summary>Exception code: manufactured item without an active BOM.</summary>
    public const string NoBom = "NO_BOM";

    /// <summary>Exception code: the order should have been released in the past.</summary>
    public const string Late = "LATE";

    /// <summary>Exception code: requirement for an inactive item.</summary>
    public const string InactiveItem = "INACTIVE_ITEM";

    /// <summary>Exception code: requirement for an item without planning data.</summary>
    public const string UnknownItem = "UNKNOWN_ITEM";

    /// <summary>Order quantity for a net requirement: at least the minimum, rounded up to the multiple.</summary>
    public static decimal LotSize(decimal net, decimal minOrderQty, decimal orderMultiple)
    {
        if (net <= 0)
        {
            return 0m;
        }

        var quantity = Math.Max(net, minOrderQty);
        if (orderMultiple > 0)
        {
            quantity = Math.Ceiling(quantity / orderMultiple) * orderMultiple;
        }

        return Math.Round(quantity, 6, MidpointRounding.AwayFromZero);
    }

    /// <summary>Runs the plan.</summary>
    public static MrpResult Run(MrpInput input)
    {
        var boms = input.Boms.ToDictionary(b => b.ItemId);
        var levels = BomExploder.LowLevelCodes(input.Boms);
        var demands = input.Demands.Where(d => d.Quantity > 0).GroupBy(d => d.ItemId).ToDictionary(g => g.Key, g => g.ToList());
        var supplies = input.Supplies.Where(s => s.Quantity > 0).GroupBy(s => s.ItemId).ToDictionary(g => g.Key, g => g.ToList());

        var requirements = new List<MrpRequirement>();
        var plannedOrders = new List<MrpPlannedOrder>();
        var exceptions = new List<MrpException>();
        var processed = new HashSet<Guid>();

        while (true)
        {
            // Dependent demand may introduce items that were not known at the start, so the set is re-read each round.
            var next = demands.Keys
                .Concat(input.Items.Values.Where(i => i.SafetyStock > 0).Select(i => i.ItemId))
                .Where(id => !processed.Contains(id))
                .Distinct()
                .Select(id => new { Id = id, Level = levels.GetValueOrDefault(id), Code = input.Items.GetValueOrDefault(id)?.Code ?? id.ToString() })
                .OrderBy(x => x.Level).ThenBy(x => x.Code, StringComparer.Ordinal)
                .FirstOrDefault();
            if (next is null)
            {
                break;
            }

            processed.Add(next.Id);
            if (!input.Items.TryGetValue(next.Id, out var item))
            {
                exceptions.Add(new MrpException(next.Id, UnknownItem, "There is a requirement for an item without planning data."));
                continue;
            }

            if (!item.IsActive)
            {
                exceptions.Add(new MrpException(item.ItemId, InactiveItem, $"Item {item.Code} is inactive but has requirements."));
            }

            var itemOrders = PlanItem(input, item, next.Level, demands.GetValueOrDefault(item.ItemId, []), supplies.GetValueOrDefault(item.ItemId, []), requirements);
            plannedOrders.AddRange(itemOrders);
            foreach (var order in itemOrders.Where(o => o.IsLate))
            {
                exceptions.Add(new MrpException(
                    item.ItemId, Late, $"Item {item.Code}: order for {Iso(order.DueDate)} should have been released on {Iso(order.ReleaseDate)}."));
            }

            if (item.OrderType != PlannedOrderType.Make || itemOrders.Count == 0)
            {
                continue;
            }

            if (!boms.TryGetValue(item.ItemId, out var bom))
            {
                exceptions.Add(new MrpException(item.ItemId, NoBom, $"Item {item.Code} is manufactured but has no active BOM."));
                continue;
            }

            foreach (var order in itemOrders)
            {
                foreach (var component in BomExploder.ExplodeOneLevel(bom, order.Quantity, boms.ContainsKey))
                {
                    if (!demands.TryGetValue(component.ItemId, out var list))
                    {
                        demands[component.ItemId] = list = [];
                    }

                    list.Add(new MrpDemand(component.ItemId, order.ReleaseDate, component.Quantity, item.Code));
                }
            }
        }

        return new MrpResult(
            requirements,
            plannedOrders,
            exceptions);
    }

    private static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    private static List<MrpPlannedOrder> PlanItem(
        MrpInput input,
        MrpItem item,
        int level,
        List<MrpDemand> demands,
        List<MrpSupply> supplies,
        List<MrpRequirement> requirements)
    {
        var onHand = input.OnHand.GetValueOrDefault(item.ItemId);
        var projected = onHand;
        var orders = new List<MrpPlannedOrder>();
        var receipts = new Queue<MrpSupply>(supplies.OrderBy(s => s.Date).ThenBy(s => s.Source, StringComparer.Ordinal));
        decimal gross = 0m, scheduled = 0m, net = 0m;

        void Plan(DateOnly dueDate, string pegging)
        {
            if (projected >= item.SafetyStock)
            {
                return;
            }

            var shortage = item.SafetyStock - projected;
            var quantity = LotSize(shortage, item.MinOrderQty, item.OrderMultiple);
            var release = dueDate.AddDays(-item.LeadTimeDays);
            orders.Add(new MrpPlannedOrder(item.ItemId, item.OrderType, quantity, release, dueDate, release < input.Today, pegging));
            net += shortage;
            projected += quantity;
        }

        // Stock already below the safety level is replenished as soon as the lead time allows.
        if (projected < item.SafetyStock)
        {
            while (receipts.Count > 0 && receipts.Peek().Date <= input.Today.AddDays(item.LeadTimeDays))
            {
                var receipt = receipts.Dequeue();
                projected += receipt.Quantity;
                scheduled += receipt.Quantity;
            }

            Plan(input.Today.AddDays(item.LeadTimeDays), "SAFETY-STOCK");
        }

        foreach (var day in demands.GroupBy(d => d.Date).OrderBy(g => g.Key))
        {
            while (receipts.Count > 0 && receipts.Peek().Date <= day.Key)
            {
                var receipt = receipts.Dequeue();
                projected += receipt.Quantity;
                scheduled += receipt.Quantity;
            }

            var quantity = day.Sum(d => d.Quantity);
            gross += quantity;
            projected -= quantity;
            Plan(day.Key, string.Join(", ", day.Select(d => d.Source).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)));
        }

        scheduled += receipts.Sum(r => r.Quantity);
        requirements.Add(new MrpRequirement(item.ItemId, level, onHand, item.SafetyStock, gross, scheduled, net, orders.Sum(o => o.Quantity)));
        return orders;
    }
}
