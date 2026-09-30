using Microsoft.EntityFrameworkCore;
using Mrp.Masters.Application;
using Mrp.Production.Domain;
using Mrp.Production.Persistence;
using Mrp.SharedKernel.Domain;
using Mrp.SharedKernel.Persistence;
using Mrp.SharedKernel.Web;

namespace Mrp.Production.Application;

/// <summary>Work orders and independent demand.</summary>
public sealed class WorkOrderService(
    ProductionDbContext db,
    DbSession session,
    MasterData masters,
    BomService boms,
    IDocumentNumberGenerator numbers,
    TimeProvider clock)
{
    /// <summary>One page of demands ordered by due date.</summary>
    public async Task<PagedResult<DemandDto>> ListDemandsAsync(DemandStatus? status, Guid? itemId, int? page, int? pageSize, CancellationToken cancellationToken)
    {
        var query = db.Demands.AsNoTracking();
        if (status is { } s)
        {
            query = query.Where(d => d.Status == s);
        }

        if (itemId is { } item)
        {
            query = query.Where(d => d.ItemId == item);
        }

        var result = await query.OrderBy(d => d.DueDate).ThenBy(d => d.Id).ToPagedAsync(page, pageSize, cancellationToken);
        return new PagedResult<DemandDto>(await ToDtoAsync(result.Items, cancellationToken), result.Total, result.Page, result.PageSize);
    }

    /// <summary>Creates an open demand.</summary>
    public async Task<DemandDto> CreateDemandAsync(SaveDemandRequest request, CancellationToken cancellationToken)
    {
        await masters.GetItemsAsync([request.ItemId], requireActive: true, cancellationToken);
        var demand = new Demand(request.ItemId, request.Quantity, request.DueDate, request.Source, request.ReferenceNo, request.CustomerId, request.Remark);
        db.Demands.Add(demand);
        await db.SaveChangesAsync(cancellationToken);
        return (await ToDtoAsync([demand], cancellationToken))[0];
    }

    /// <summary>Changes an open demand.</summary>
    public async Task<DemandDto> UpdateDemandAsync(Guid id, SaveDemandRequest request, CancellationToken cancellationToken)
    {
        var demand = await db.Demands.FirstOrDefaultAsync(d => d.Id == id, cancellationToken) ?? throw DomainException.NotFound("Demand", id);
        if (demand.ItemId != request.ItemId)
        {
            throw DomainException.Conflict("production.demand.item_fixed", "The item of a demand cannot be changed.");
        }

        demand.Update(request.Quantity, request.DueDate, request.ReferenceNo, request.CustomerId, request.Remark);
        await db.SaveChangesAsync(cancellationToken);
        return (await ToDtoAsync([demand], cancellationToken))[0];
    }

    /// <summary>Cancels or closes a demand.</summary>
    public async Task<DemandDto> EndDemandAsync(Guid id, bool cancel, CancellationToken cancellationToken)
    {
        var demand = await db.Demands.FirstOrDefaultAsync(d => d.Id == id, cancellationToken) ?? throw DomainException.NotFound("Demand", id);
        if (cancel)
        {
            demand.Cancel();
        }
        else
        {
            demand.Close();
        }

        await db.SaveChangesAsync(cancellationToken);
        return (await ToDtoAsync([demand], cancellationToken))[0];
    }

    /// <summary>One page of work orders.</summary>
    public async Task<PagedResult<WorkOrderSummary>> ListAsync(WorkOrderStatus? status, Guid? itemId, bool? openOnly, string? search, int? page, int? pageSize, CancellationToken cancellationToken)
    {
        var query = db.WorkOrders.AsNoTracking();
        if (status is { } s)
        {
            query = query.Where(w => w.Status == s);
        }

        if (itemId is { } item)
        {
            query = query.Where(w => w.ItemId == item);
        }

        if (openOnly == true)
        {
            query = query.Where(w => w.Status == WorkOrderStatus.Released || w.Status == WorkOrderStatus.InProgress);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var text = search.Trim().ToUpperInvariant();
            query = query.Where(w => w.DocumentNo.StartsWith(text));
        }

        var result = await query.OrderBy(w => w.DueDate).ThenBy(w => w.DocumentNo).ToPagedAsync(page, pageSize, cancellationToken);
        var items = await masters.GetItemsAsync(result.Items.Select(w => w.ItemId), requireActive: false, cancellationToken);
        return new PagedResult<WorkOrderSummary>(result.Items.Select(w => ToSummary(w, items)).ToList(), result.Total, result.Page, result.PageSize);
    }

    /// <summary>One work order with materials and child orders.</summary>
    public async Task<WorkOrderDto> GetAsync(Guid id, CancellationToken cancellationToken) =>
        await ToDtoAsync(await LoadAsync(id, tracking: false, cancellationToken), cancellationToken);

    /// <summary>Creates a planned work order from the active BOM, optionally with work orders for semi-finished components.</summary>
    public Task<WorkOrderDto> CreateAsync(CreateWorkOrderRequest request, CancellationToken cancellationToken) =>
        session.ExecuteInTransactionAsync(
            async ct =>
            {
                Demand? demand = null;
                if (request.DemandId is { } demandId)
                {
                    demand = await db.Demands.FirstOrDefaultAsync(d => d.Id == demandId, ct) ?? throw DomainException.NotFound("Demand", demandId);
                    if (demand.ItemId != request.ItemId || demand.Status is DemandStatus.Cancelled or DemandStatus.Closed)
                    {
                        throw new DomainException("production.workorder.demand_mismatch", "The demand is for another item or is no longer open.");
                    }
                }

                if (request.WarehouseId is { } warehouse)
                {
                    await masters.GetWarehousesAsync([warehouse], ct);
                }

                var order = await CreateCoreAsync(
                    request.ItemId, request.Quantity, request.StartDate, request.DueDate,
                    demand is null ? WorkOrderSource.Manual : WorkOrderSource.Demand, demand?.ReferenceNo, demand?.Id, null,
                    request.WarehouseId, request.Remark, request.CreateChildOrders, depth: 0, ct);
                await db.SaveChangesAsync(ct);
                if (demand is not null)
                {
                    await RefreshDemandAsync(demand, ct);
                }

                return await ToDtoAsync(order, ct);
            },
            cancellationToken);

    /// <summary>Creates a work order for an MRP proposal (no child orders: MRP proposes those separately).</summary>
    public async Task<WorkOrder> CreateFromPlanAsync(Guid itemId, decimal quantity, DateOnly startDate, DateOnly dueDate, string runNo, CancellationToken cancellationToken)
    {
        var order = await CreateCoreAsync(
            itemId, quantity, startDate, dueDate, WorkOrderSource.Mrp, runNo, null, null, null, null, createChildren: false, depth: 0, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return order;
    }

    /// <summary>Applies a user action to a work order.</summary>
    public Task<WorkOrderDto> ApplyAsync(Guid id, WorkOrderAction action, string? reason, CancellationToken cancellationToken) =>
        session.ExecuteInTransactionAsync(
            async ct =>
            {
                var order = await LoadAsync(id, tracking: true, ct);
                if (action == WorkOrderAction.Cancel)
                {
                    var activeChildren = await db.WorkOrders.AnyAsync(
                        w => w.ParentWorkOrderId == order.Id && w.Status != WorkOrderStatus.Cancelled && w.Status != WorkOrderStatus.Planned, ct);
                    if (activeChildren)
                    {
                        throw DomainException.Conflict("production.workorder.child_in_progress", "Cancel or finish the released child work orders first.");
                    }
                }

                order.Apply(action, reason);
                await db.SaveChangesAsync(ct);
                if (order.DemandId is { } demandId && action == WorkOrderAction.Cancel)
                {
                    var demand = await db.Demands.FirstAsync(d => d.Id == demandId, ct);
                    await RefreshDemandAsync(demand, ct);
                }

                return await ToDtoAsync(order, ct);
            },
            cancellationToken);

    /// <summary>Open work orders as expected receipts, and their outstanding materials as requirements. Used by MRP.</summary>
    public async Task<(IReadOnlyList<MrpSupply> Supplies, IReadOnlyList<MrpDemand> MaterialDemands)> OpenOrdersForPlanningAsync(CancellationToken cancellationToken)
    {
        var orders = await db.WorkOrders.AsNoTracking().Include(w => w.Materials)
            .Where(w => w.Status == WorkOrderStatus.Planned || w.Status == WorkOrderStatus.Released
                || w.Status == WorkOrderStatus.InProgress || w.Status == WorkOrderStatus.OnHold)
            .ToListAsync(cancellationToken);
        var supplies = orders.Where(o => o.OutstandingQuantity > 0)
            .Select(o => new MrpSupply(o.ItemId, o.DueDate, o.OutstandingQuantity, o.DocumentNo))
            .ToList();
        var demands = orders
            .SelectMany(o => o.Materials.Where(m => m.OutstandingQuantity > 0)
                .Select(m => new MrpDemand(m.ComponentItemId, o.StartDate, m.OutstandingQuantity, o.DocumentNo)))
            .ToList();
        return (supplies, demands);
    }

    /// <summary>Quantity of each demand that is covered by work orders that were not cancelled.</summary>
    public async Task<Dictionary<Guid, decimal>> CoveredByDemandAsync(IReadOnlyCollection<Guid> demandIds, CancellationToken cancellationToken) =>
        await db.WorkOrders.AsNoTracking()
            .Where(w => w.DemandId != null && demandIds.Contains(w.DemandId.Value) && w.Status != WorkOrderStatus.Cancelled)
            .GroupBy(w => w.DemandId!.Value)
            .Select(g => new { DemandId = g.Key, Quantity = g.Sum(w => w.Quantity) })
            .ToDictionaryAsync(x => x.DemandId, x => x.Quantity, cancellationToken);

    private static WorkOrderSummary ToSummary(WorkOrder w, IReadOnlyDictionary<Guid, ItemInfo> items) =>
        new(w.Id, w.DocumentNo, w.ItemId, items[w.ItemId].Code, items[w.ItemId].Name, w.Quantity, w.ProducedQuantity, w.StartDate, w.DueDate,
            w.Status, w.ParentWorkOrderId, w.Source);

    private async Task<WorkOrder> CreateCoreAsync(
        Guid itemId, decimal quantity, DateOnly startDate, DateOnly dueDate, WorkOrderSource source, string? sourceReference, Guid? demandId,
        Guid? parentId, Guid? warehouseId, string? remark, bool createChildren, int depth, CancellationToken cancellationToken)
    {
        if (depth > BomExploder.MaxLevels)
        {
            throw new DomainException("production.bom.too_deep", $"BOM is deeper than {BomExploder.MaxLevels} levels.");
        }

        var items = await masters.GetAllItemsAsync(cancellationToken);
        if (!items.TryGetValue(itemId, out var item) || !item.IsActive)
        {
            throw new DomainException("masters.item.not_found", $"Item '{itemId}' does not exist or is inactive.", 400);
        }

        var bom = await boms.FindActiveAsync(itemId, cancellationToken)
            ?? throw new DomainException("production.workorder.no_active_bom", $"Item {item.Code} has no active BOM.");
        var active = await boms.ActiveDefinitionsAsync(cancellationToken);
        var materials = BomExploder.ExplodeOneLevel(bom.ToDefinition(), quantity, component => BomService.IsMade(items, active, component));

        var number = await numbers.NextAsync(DocumentTypes.WorkOrder, clock.GetUtcNow(), cancellationToken);
        var order = new WorkOrder(number, itemId, quantity, startDate, dueDate, bom.Id, materials, source, sourceReference, demandId, parentId, warehouseId, remark);
        db.WorkOrders.Add(order);

        if (createChildren)
        {
            foreach (var material in materials.Where(m => m.IsMadeInHouse))
            {
                var childStart = startDate.AddDays(-items[material.ItemId].LeadTimeDays);
                var child = await CreateCoreAsync(
                    material.ItemId, material.Quantity, childStart, startDate, WorkOrderSource.Parent, number, null, order.Id, warehouseId, null,
                    createChildren: true, depth + 1, cancellationToken);
                order.LinkChild(material.ItemId, child.Id);
            }
        }

        return order;
    }

    private async Task RefreshDemandAsync(Demand demand, CancellationToken cancellationToken)
    {
        var covered = await CoveredByDemandAsync([demand.Id], cancellationToken);
        demand.ApplyCoverage(covered.GetValueOrDefault(demand.Id));
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<WorkOrder> LoadAsync(Guid id, bool tracking, CancellationToken cancellationToken)
    {
        var query = db.WorkOrders.Include(w => w.Materials).AsQueryable();
        if (!tracking)
        {
            query = query.AsNoTracking();
        }

        return await query.FirstOrDefaultAsync(w => w.Id == id, cancellationToken) ?? throw DomainException.NotFound("Work order", id);
    }

    private async Task<IReadOnlyList<DemandDto>> ToDtoAsync(IReadOnlyList<Demand> demands, CancellationToken cancellationToken)
    {
        var items = await masters.GetItemsAsync(demands.Select(d => d.ItemId), requireActive: false, cancellationToken);
        var units = await masters.GetUnitCodesAsync(items.Values.Select(i => i.StockUnitId), cancellationToken);
        var covered = await CoveredByDemandAsync(demands.Select(d => d.Id).ToList(), cancellationToken);
        return demands.Select(d => new DemandDto(
            d.Id, d.ItemId, items[d.ItemId].Code, items[d.ItemId].Name, units.GetValueOrDefault(items[d.ItemId].StockUnitId, string.Empty), d.Quantity,
            covered.GetValueOrDefault(d.Id), d.DueDate, d.Source, d.ReferenceNo, d.CustomerId, d.Status, d.Remark)).ToList();
    }

    private async Task<WorkOrderDto> ToDtoAsync(WorkOrder order, CancellationToken cancellationToken)
    {
        var children = await db.WorkOrders.AsNoTracking().Where(w => w.ParentWorkOrderId == order.Id).OrderBy(w => w.DocumentNo).ToListAsync(cancellationToken);
        foreach (var local in db.WorkOrders.Local.Where(w => w.ParentWorkOrderId == order.Id && children.All(c => c.Id != w.Id)))
        {
            children.Add(local);
        }

        var items = await masters.GetItemsAsync(
            order.Materials.Select(m => m.ComponentItemId).Append(order.ItemId).Concat(children.Select(c => c.ItemId)), requireActive: false, cancellationToken);
        var units = await masters.GetUnitCodesAsync(items.Values.Select(i => i.StockUnitId), cancellationToken);
        return new WorkOrderDto(
            order.Id, order.DocumentNo, order.ItemId, items[order.ItemId].Code, items[order.ItemId].Name,
            units.GetValueOrDefault(items[order.ItemId].StockUnitId, string.Empty), order.Quantity, order.ProducedQuantity, order.StartDate, order.DueDate,
            order.Status, order.BomVersionId, order.ParentWorkOrderId, order.DemandId, order.Source, order.SourceReference, order.WarehouseId,
            order.Remark, order.StatusReason,
            order.Materials.OrderBy(m => m.LineNo).Select(m => new WorkOrderMaterialDto(
                m.Id, m.LineNo, m.ComponentItemId, items[m.ComponentItemId].Code, items[m.ComponentItemId].Name,
                units.GetValueOrDefault(items[m.ComponentItemId].StockUnitId, string.Empty), m.RequiredQuantity, m.IssuedQuantity, m.OutstandingQuantity,
                m.IsMadeInHouse, m.ChildWorkOrderId)).ToList(),
            children.Select(c => ToSummary(c, items)).ToList());
    }
}

/// <summary>Applies stock postings of warehouse documents that refer to a work order.</summary>
public sealed class WorkOrderStockHandler(ProductionDbContext db, MasterData masters, ITenantSettings settings) : IStockReferenceHandler
{
    /// <inheritdoc />
    public string ReferenceType => WorkOrder.ReferenceType;

    /// <inheritdoc />
    public async Task OnStockPostedAsync(StockReferenceEvent stockEvent, CancellationToken cancellationToken)
    {
        var order = await db.WorkOrders.Include(w => w.Materials).FirstOrDefaultAsync(w => w.Id == stockEvent.ReferenceId, cancellationToken)
            ?? throw new DomainException("production.workorder.not_found", "The referenced work order does not exist.", 400);
        var items = await masters.GetItemsAsync(stockEvent.Quantities.Keys, requireActive: false, cancellationToken);
        var tolerance = await settings.GetAsync(SettingKeys.OverIssuePercent, 0m, cancellationToken);
        foreach (var (itemId, quantity) in stockEvent.Quantities)
        {
            if (stockEvent.StockDocumentType == "Receipt")
            {
                order.ApplyOutput(itemId, quantity, items[itemId].Code);
            }
            else
            {
                order.ApplyIssue(itemId, quantity, tolerance, items[itemId].Code);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
