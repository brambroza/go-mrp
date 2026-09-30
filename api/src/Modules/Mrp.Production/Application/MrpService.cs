using Microsoft.EntityFrameworkCore;
using Mrp.Inventory.Application;
using Mrp.Masters.Application;
using Mrp.Masters.Domain;
using Mrp.Production.Domain;
using Mrp.Production.Persistence;
using Mrp.Purchasing.Application;
using Mrp.Purchasing.Domain;
using Mrp.SharedKernel.Domain;
using Mrp.SharedKernel.Persistence;
using Mrp.SharedKernel.Tenancy;
using Mrp.SharedKernel.Web;

namespace Mrp.Production.Application;

/// <summary>Collects planning data, runs the MRP engine, stores the result and converts proposals into documents.</summary>
public sealed class MrpService(
    ProductionDbContext db,
    DbSession session,
    ITenantContext tenant,
    ITenantSettings settings,
    MasterData masters,
    InventoryQueries inventory,
    PurchasingService purchasing,
    BomService boms,
    WorkOrderService workOrders,
    IDocumentNumberGenerator numbers,
    TimeProvider clock)
{
    /// <summary>One page of runs, newest first.</summary>
    public Task<PagedResult<MrpRunSummary>> ListAsync(int? page, int? pageSize, CancellationToken cancellationToken) =>
        db.MrpRuns.AsNoTracking().OrderByDescending(r => r.CreatedAt)
            .Select(r => new MrpRunSummary(r.Id, r.DocumentNo, r.RunDate, r.HorizonDays, r.ItemCount, r.PlannedOrderCount, r.ExceptionCount, r.CreatedAt))
            .ToPagedAsync(page, pageSize, cancellationToken);

    /// <summary>One run with results.</summary>
    public async Task<MrpRunDto> GetAsync(Guid id, CancellationToken cancellationToken) =>
        await ToDtoAsync(await LoadAsync(id, tracking: false, cancellationToken), cancellationToken);

    /// <summary>Runs MRP for open demand inside the horizon and stores the result.</summary>
    public Task<MrpRunDto> RunAsync(StartMrpRunRequest request, CancellationToken cancellationToken) =>
        session.ExecuteInTransactionAsync(
            async ct =>
            {
                var zone = await settings.GetTimeZoneAsync(ct);
                var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), zone).DateTime);
                var input = await BuildInputAsync(today, request.HorizonDays, ct);
                var result = MrpEngine.Run(input);
                var number = await numbers.NextAsync(DocumentTypes.MrpRun, clock.GetUtcNow(), ct);
                var run = new MrpRun(number, today, request.HorizonDays, tenant.RequireUserId(), result);
                db.MrpRuns.Add(run);
                await db.SaveChangesAsync(ct);
                return await ToDtoAsync(run, ct);
            },
            cancellationToken);

    /// <summary>
    /// Turns proposals into documents: all selected purchase proposals become one purchase request,
    /// each manufacturing proposal becomes a work order.
    /// </summary>
    public Task<ConversionResult> ConvertAsync(Guid runId, ConvertPlannedOrdersRequest request, CancellationToken cancellationToken) =>
        session.ExecuteInTransactionAsync(
            async ct =>
            {
                var run = await LoadAsync(runId, tracking: true, ct);
                var selected = run.PlannedOrders.Where(o => request.PlannedOrderIds.Contains(o.Id)).ToList();
                if (selected.Count != request.PlannedOrderIds.Distinct().Count())
                {
                    throw new DomainException("production.mrp.planned_order_not_found", "A planned order does not belong to this run.", 400);
                }

                var items = await masters.GetItemsAsync(selected.Select(o => o.ItemId), requireActive: true, ct);
                var documents = new List<CreatedDocument>();
                var zone = await settings.GetTimeZoneAsync(ct);
                var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), zone).DateTime);

                var purchases = selected.Where(o => o.OrderType == PlannedOrderType.Buy).OrderBy(o => o.DueDate).ToList();
                if (purchases.Count > 0)
                {
                    var pr = await purchasing.CreateRequestAsync(
                        new SavePurchaseRequest(
                            today,
                            purchases.Select(o => new SavePurchaseRequestLine(
                                o.ItemId, o.Quantity, items[o.ItemId].StockUnitId, o.DueDate < today ? today : o.DueDate, null, Shorten($"MRP {run.DocumentNo}: {o.Pegging}", 300))).ToList(),
                            null,
                            $"สร้างจาก MRP {run.DocumentNo}"),
                        PurchaseRequestSource.Mrp,
                        run.DocumentNo,
                        ct);
                    foreach (var order in purchases)
                    {
                        order.MarkConverted(DocumentTypes.PurchaseRequest, pr.Id, pr.DocumentNo);
                    }

                    documents.Add(new CreatedDocument(DocumentTypes.PurchaseRequest, pr.Id, pr.DocumentNo, purchases.Count));
                }

                foreach (var order in selected.Where(o => o.OrderType == PlannedOrderType.Make).OrderBy(o => o.DueDate))
                {
                    var start = order.ReleaseDate < today ? today : order.ReleaseDate;
                    var due = order.DueDate < start ? start : order.DueDate;
                    var workOrder = await workOrders.CreateFromPlanAsync(order.ItemId, order.Quantity, start, due, run.DocumentNo, ct);
                    order.MarkConverted(DocumentTypes.WorkOrder, workOrder.Id, workOrder.DocumentNo);
                    documents.Add(new CreatedDocument(DocumentTypes.WorkOrder, workOrder.Id, workOrder.DocumentNo, 1));
                }

                await db.SaveChangesAsync(ct);
                return new ConversionResult(documents);
            },
            cancellationToken);

    /// <summary>Rejects a proposal.</summary>
    public async Task DismissAsync(Guid plannedOrderId, CancellationToken cancellationToken)
    {
        var order = await db.PlannedOrders.FirstOrDefaultAsync(o => o.Id == plannedOrderId, cancellationToken)
            ?? throw DomainException.NotFound("Planned order", plannedOrderId);
        order.Dismiss();
        await db.SaveChangesAsync(cancellationToken);
    }

    private static string Shorten(string text, int maxLength) => text.Length > maxLength ? text[..maxLength] : text;

    private async Task<MrpInput> BuildInputAsync(DateOnly today, int horizonDays, CancellationToken cancellationToken)
    {
        var horizon = today.AddDays(horizonDays);
        var allItems = await masters.GetAllItemsAsync(cancellationToken);
        var items = allItems.Values
            .Where(i => i.ItemType != ItemType.Service)
            .ToDictionary(
                i => i.Id,
                i => new MrpItem(
                    i.Id, i.Code, i.SupplyType == SupplyType.Make ? PlannedOrderType.Make : PlannedOrderType.Buy, i.LeadTimeDays, i.SafetyStock,
                    i.MinOrderQty, i.OrderMultiple, i.IsActive));

        var open = await db.Demands.AsNoTracking()
            .Where(d => d.Status == DemandStatus.Open && d.DueDate <= horizon)
            .OrderBy(d => d.DueDate).ThenBy(d => d.Id)
            .ToListAsync(cancellationToken);
        var covered = await workOrders.CoveredByDemandAsync(open.Select(d => d.Id).ToList(), cancellationToken);
        var demands = open
            .Select(d => new MrpDemand(d.ItemId, d.DueDate, d.Quantity - covered.GetValueOrDefault(d.Id), d.ReferenceNo ?? d.Source.ToString()))
            .Where(d => d.Quantity > 0)
            .ToList();

        var (orderSupplies, materialDemands) = await workOrders.OpenOrdersForPlanningAsync(cancellationToken);
        demands.AddRange(materialDemands);

        var supplies = new List<MrpSupply>(orderSupplies);
        supplies.AddRange((await purchasing.OpenOrderSupplyAsync(cancellationToken)).Select(s => new MrpSupply(s.ItemId, s.Date, s.Quantity, s.Reference)));
        supplies.AddRange((await purchasing.OpenRequestSupplyAsync(cancellationToken)).Select(s => new MrpSupply(s.ItemId, s.Date, s.Quantity, s.Reference)));

        var active = await boms.ActiveDefinitionsAsync(cancellationToken);
        var usable = active.Values.Where(b => BomService.IsMade(allItems, active, b.ItemId)).ToList();

        return new MrpInput(today, items, await inventory.AvailableByItemAsync(cancellationToken), demands, supplies, usable);
    }

    private async Task<MrpRun> LoadAsync(Guid id, bool tracking, CancellationToken cancellationToken)
    {
        var query = db.MrpRuns.Include(r => r.Requirements).Include(r => r.PlannedOrders).Include(r => r.Exceptions).AsSplitQuery();
        if (!tracking)
        {
            query = query.AsNoTracking();
        }

        return await query.FirstOrDefaultAsync(r => r.Id == id, cancellationToken) ?? throw DomainException.NotFound("MRP run", id);
    }

    private async Task<MrpRunDto> ToDtoAsync(MrpRun run, CancellationToken cancellationToken)
    {
        var items = await masters.GetAllItemsAsync(cancellationToken);
        var units = await masters.GetUnitCodesAsync(items.Values.Select(i => i.StockUnitId), cancellationToken);
        string Code(Guid id) => items.TryGetValue(id, out var item) ? item.Code : id.ToString();
        string Name(Guid id) => items.TryGetValue(id, out var item) ? item.Name : string.Empty;
        string Unit(Guid id) => items.TryGetValue(id, out var item) ? units.GetValueOrDefault(item.StockUnitId, string.Empty) : string.Empty;

        return new MrpRunDto(
            run.Id, run.DocumentNo, run.RunDate, run.HorizonDays, run.CreatedAt,
            run.Requirements.OrderBy(r => r.Level).ThenBy(r => Code(r.ItemId), StringComparer.Ordinal)
                .Select(r => new MrpRequirementDto(r.ItemId, Code(r.ItemId), Name(r.ItemId), Unit(r.ItemId), r.Level, r.OnHand, r.SafetyStock, r.Gross, r.ScheduledReceipts, r.Net, r.Planned))
                .ToList(),
            run.PlannedOrders.OrderBy(o => o.ReleaseDate).ThenBy(o => Code(o.ItemId), StringComparer.Ordinal)
                .Select(o => new PlannedOrderDto(
                    o.Id, o.ItemId, Code(o.ItemId), Name(o.ItemId), Unit(o.ItemId), o.OrderType, o.Quantity, o.ReleaseDate, o.DueDate, o.IsLate, o.Pegging,
                    o.Status, o.ConvertedDocumentType, o.ConvertedDocumentId, o.ConvertedDocumentNo))
                .ToList(),
            run.Exceptions.OrderBy(e => Code(e.ItemId), StringComparer.Ordinal).ThenBy(e => e.Code, StringComparer.Ordinal)
                .Select(e => new MrpExceptionDto(e.ItemId, Code(e.ItemId), e.Code, e.Message))
                .ToList());
    }
}
