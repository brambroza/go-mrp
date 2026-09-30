using Microsoft.EntityFrameworkCore;
using Mrp.Masters.Application;
using Mrp.Masters.Domain;
using Mrp.Purchasing.Domain;
using Mrp.Purchasing.Persistence;
using Mrp.SharedKernel.Domain;
using Mrp.SharedKernel.Persistence;
using Mrp.SharedKernel.Tenancy;
using Mrp.SharedKernel.Web;

namespace Mrp.Purchasing.Application;

/// <summary>Purchase requests and purchase orders.</summary>
public sealed class PurchasingService(
    PurchasingDbContext db,
    DbSession session,
    ITenantContext tenant,
    ITenantSettings settings,
    MasterData masters,
    IDocumentNumberGenerator numbers,
    IApprovalService approvals,
    TimeProvider clock)
{
    /// <summary>One page of purchase requests, newest first.</summary>
    public async Task<PagedResult<PurchaseRequestSummary>> ListRequestsAsync(PurchaseRequestStatus? status, string? search, int? page, int? pageSize, CancellationToken cancellationToken)
    {
        var query = db.Requests.AsNoTracking();
        if (status is { } s)
        {
            query = query.Where(r => r.Status == s);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var text = search.Trim().ToUpperInvariant();
            query = query.Where(r => r.DocumentNo.StartsWith(text));
        }

        return await query.OrderByDescending(r => r.DocumentDate).ThenByDescending(r => r.DocumentNo)
            .Select(r => new PurchaseRequestSummary(r.Id, r.DocumentNo, r.DocumentDate, r.Status, r.Source, r.RequiredDate, r.Remark, r.Lines.Count, r.RequestedBy))
            .ToPagedAsync(page, pageSize, cancellationToken);
    }

    /// <summary>One purchase request.</summary>
    public async Task<PurchaseRequestDto> GetRequestAsync(Guid id, CancellationToken cancellationToken) =>
        await ToDtoAsync(await LoadRequestAsync(id, tracking: false, cancellationToken), cancellationToken);

    /// <summary>Creates a draft purchase request.</summary>
    public Task<PurchaseRequestDto> CreateRequestAsync(SavePurchaseRequest request, CancellationToken cancellationToken) =>
        CreateRequestAsync(request, PurchaseRequestSource.Manual, null, cancellationToken);

    /// <summary>Creates a draft purchase request from a given source (manual entry or MRP).</summary>
    public Task<PurchaseRequestDto> CreateRequestAsync(SavePurchaseRequest request, PurchaseRequestSource source, string? sourceReference, CancellationToken cancellationToken) =>
        session.ExecuteInTransactionAsync(
            async ct =>
            {
                var lines = await BuildRequestLinesAsync(request, ct);
                var number = await numbers.NextAsync(DocumentTypes.PurchaseRequest, clock.GetUtcNow(), ct);
                var document = new PurchaseRequest(number, tenant.RequireUserId(), source, sourceReference);
                document.Update(request.DocumentDate, request.RequiredDate, request.Remark, lines);
                db.Requests.Add(document);
                await db.SaveChangesAsync(ct);
                return await ToDtoAsync(document, ct);
            },
            cancellationToken);

    /// <summary>Replaces a draft or rejected purchase request.</summary>
    public Task<PurchaseRequestDto> UpdateRequestAsync(Guid id, SavePurchaseRequest request, CancellationToken cancellationToken) =>
        session.ExecuteInTransactionAsync(
            async ct =>
            {
                var document = await LoadRequestAsync(id, tracking: true, ct);
                document.Update(request.DocumentDate, request.RequiredDate, request.Remark, await BuildRequestLinesAsync(request, ct));
                await db.SaveChangesAsync(ct);
                return await ToDtoAsync(document, ct);
            },
            cancellationToken);

    /// <summary>Submits a purchase request for approval.</summary>
    public Task<PurchaseRequestDto> SubmitRequestAsync(Guid id, CancellationToken cancellationToken) =>
        session.ExecuteInTransactionAsync(
            async ct =>
            {
                var document = await LoadRequestAsync(id, tracking: true, ct);
                document.Apply(PurchaseAction.Submit);
                var items = await masters.GetItemsAsync(document.Lines.Select(l => l.ItemId), requireActive: false, ct);
                var estimate = document.Lines.Sum(l => PurchaseCalculator.RoundMoney(l.StockQuantity * items[l.ItemId].StandardCost));
                var submission = await approvals.SubmitAsync(
                    new ApprovalDocument(DocumentTypes.PurchaseRequest, document.Id, document.DocumentNo, $"ใบขอซื้อ {document.DocumentNo}", estimate), ct);
                if (submission.AutoApproved)
                {
                    document.Apply(PurchaseAction.Approve);
                }

                await db.SaveChangesAsync(ct);
                return await ToDtoAsync(document, ct);
            },
            cancellationToken);

    /// <summary>Cancels or closes a purchase request.</summary>
    public Task<PurchaseRequestDto> EndRequestAsync(Guid id, PurchaseAction action, string reason, CancellationToken cancellationToken) =>
        session.ExecuteInTransactionAsync(
            async ct =>
            {
                var document = await LoadRequestAsync(id, tracking: true, ct);
                if (action == PurchaseAction.Cancel && await OrderedByRequestLineAsync(document.Lines.Select(l => l.Id).ToList(), ct) is { Count: > 0 })
                {
                    throw DomainException.Conflict("purchasing.pr.has_orders", "A request that is on purchase orders cannot be cancelled; close it instead.");
                }

                document.Apply(action, reason);
                await db.SaveChangesAsync(ct);
                return await ToDtoAsync(document, ct);
            },
            cancellationToken);

    /// <summary>Approved request lines that still have quantity to order.</summary>
    public async Task<IReadOnlyList<OpenRequestLineDto>> OpenRequestLinesAsync(Guid? supplierId, CancellationToken cancellationToken)
    {
        var rows = await (
            from request in db.Requests.AsNoTracking()
            from line in request.Lines
            where request.Status == PurchaseRequestStatus.Approved || request.Status == PurchaseRequestStatus.PartiallyOrdered
            where supplierId == null || line.SuggestedSupplierId == null || line.SuggestedSupplierId == supplierId
            orderby request.DocumentDate, request.DocumentNo, line.LineNo
            select new { request.Id, request.DocumentNo, Line = line }).Take(1000).ToListAsync(cancellationToken);
        var ordered = await OrderedByRequestLineAsync(rows.Select(r => r.Line.Id).ToList(), cancellationToken);
        var items = await masters.GetItemsAsync(rows.Select(r => r.Line.ItemId), requireActive: false, cancellationToken);
        var units = await masters.GetUnitCodesAsync(rows.Select(r => r.Line.UnitId), cancellationToken);
        return rows
            .Select(r => new { r.Id, r.DocumentNo, r.Line, Outstanding = r.Line.StockQuantity - ordered.GetValueOrDefault(r.Line.Id) })
            .Where(r => r.Outstanding > 0)
            .Select(r => new OpenRequestLineDto(
                r.Line.Id, r.Id, r.DocumentNo, r.Line.ItemId, items[r.Line.ItemId].Code, items[r.Line.ItemId].Name, r.Line.UnitId,
                units.GetValueOrDefault(r.Line.UnitId, string.Empty), UnitConverter.RoundQuantity(r.Outstanding / r.Line.ConversionFactor),
                r.Outstanding, r.Line.RequiredDate, r.Line.SuggestedSupplierId))
            .ToList();
    }

    /// <summary>One page of purchase orders, newest first.</summary>
    public async Task<PagedResult<PurchaseOrderSummary>> ListOrdersAsync(PurchaseOrderStatus? status, Guid? supplierId, bool? receivableOnly, string? search, int? page, int? pageSize, CancellationToken cancellationToken)
    {
        var query = db.Orders.AsNoTracking();
        if (status is { } s)
        {
            query = query.Where(o => o.Status == s);
        }

        if (supplierId is { } supplier)
        {
            query = query.Where(o => o.SupplierId == supplier);
        }

        if (receivableOnly == true)
        {
            query = query.Where(o => o.Status == PurchaseOrderStatus.Approved || o.Status == PurchaseOrderStatus.PartiallyReceived);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var text = search.Trim().ToUpperInvariant();
            query = query.Where(o => o.DocumentNo.StartsWith(text));
        }

        var result = await query.OrderByDescending(o => o.DocumentDate).ThenByDescending(o => o.DocumentNo)
            .Select(o => new { o.Id, o.DocumentNo, o.DocumentDate, o.SupplierId, o.Status, o.Currency, o.Total, o.DeliveryDate, LineCount = o.Lines.Count })
            .ToPagedAsync(page, pageSize, cancellationToken);
        var names = await SupplierNamesAsync(result.Items.Select(o => o.SupplierId), cancellationToken);
        return new PagedResult<PurchaseOrderSummary>(
            result.Items.Select(o => new PurchaseOrderSummary(
                o.Id, o.DocumentNo, o.DocumentDate, o.SupplierId, names.GetValueOrDefault(o.SupplierId, string.Empty), o.Status, o.Currency,
                o.Total, o.DeliveryDate, o.LineCount)).ToList(),
            result.Total, result.Page, result.PageSize);
    }

    /// <summary>One purchase order.</summary>
    public async Task<PurchaseOrderDto> GetOrderAsync(Guid id, CancellationToken cancellationToken) =>
        await ToDtoAsync(await LoadOrderAsync(id, tracking: false, cancellationToken), cancellationToken);

    /// <summary>Creates a draft purchase order.</summary>
    public Task<PurchaseOrderDto> CreateOrderAsync(SavePurchaseOrder request, CancellationToken cancellationToken) =>
        session.ExecuteInTransactionAsync(
            async ct =>
            {
                var (header, lines) = await BuildOrderAsync(request, null, ct);
                var number = await numbers.NextAsync(DocumentTypes.PurchaseOrder, clock.GetUtcNow(), ct);
                var document = new PurchaseOrder(number);
                document.Update(header, lines);
                db.Orders.Add(document);
                await db.SaveChangesAsync(ct);
                await RefreshRequestsAsync(lines.Where(l => l.PrLineId is not null).Select(l => l.PrLineId!.Value), ct);
                return await ToDtoAsync(document, ct);
            },
            cancellationToken);

    /// <summary>Replaces a draft or rejected purchase order.</summary>
    public Task<PurchaseOrderDto> UpdateOrderAsync(Guid id, SavePurchaseOrder request, CancellationToken cancellationToken) =>
        session.ExecuteInTransactionAsync(
            async ct =>
            {
                var document = await LoadOrderAsync(id, tracking: true, ct);
                var previous = document.Lines.Where(l => l.PrLineId is not null).Select(l => l.PrLineId!.Value).ToList();
                var (header, lines) = await BuildOrderAsync(request, document.Id, ct);
                document.Update(header, lines);
                await db.SaveChangesAsync(ct);
                await RefreshRequestsAsync(previous.Concat(lines.Where(l => l.PrLineId is not null).Select(l => l.PrLineId!.Value)), ct);
                return await ToDtoAsync(document, ct);
            },
            cancellationToken);

    /// <summary>Submits a purchase order for approval.</summary>
    public Task<PurchaseOrderDto> SubmitOrderAsync(Guid id, CancellationToken cancellationToken) =>
        session.ExecuteInTransactionAsync(
            async ct =>
            {
                var document = await LoadOrderAsync(id, tracking: true, ct);
                document.Apply(PurchaseAction.Submit);
                var supplier = await masters.GetSupplierAsync(document.SupplierId, ct);
                var submission = await approvals.SubmitAsync(
                    new ApprovalDocument(DocumentTypes.PurchaseOrder, document.Id, document.DocumentNo, $"ใบสั่งซื้อ {document.DocumentNo} — {supplier.Name}", document.TotalInBaseCurrency),
                    ct);
                if (submission.AutoApproved)
                {
                    document.Apply(PurchaseAction.Approve);
                }

                await db.SaveChangesAsync(ct);
                return await ToDtoAsync(document, ct);
            },
            cancellationToken);

    /// <summary>Cancels or closes a purchase order.</summary>
    public Task<PurchaseOrderDto> EndOrderAsync(Guid id, PurchaseAction action, string reason, CancellationToken cancellationToken) =>
        session.ExecuteInTransactionAsync(
            async ct =>
            {
                var document = await LoadOrderAsync(id, tracking: true, ct);
                document.Apply(action, reason);
                await db.SaveChangesAsync(ct);
                await RefreshRequestsAsync(document.Lines.Where(l => l.PrLineId is not null).Select(l => l.PrLineId!.Value), ct);
                return await ToDtoAsync(document, ct);
            },
            cancellationToken);

    /// <summary>Closes one line of a purchase order.</summary>
    public Task<PurchaseOrderDto> CloseOrderLineAsync(Guid id, Guid lineId, CancellationToken cancellationToken) =>
        session.ExecuteInTransactionAsync(
            async ct =>
            {
                var document = await LoadOrderAsync(id, tracking: true, ct);
                document.CloseLine(lineId);
                await db.SaveChangesAsync(ct);
                return await ToDtoAsync(document, ct);
            },
            cancellationToken);

    /// <summary>Called by the approval engine when a request for a PR or PO finishes.</summary>
    public async Task OnApprovalCompletedAsync(string documentType, Guid documentId, ApprovalOutcome outcome, string? comment, CancellationToken cancellationToken)
    {
        var action = outcome switch
        {
            ApprovalOutcome.Approved => PurchaseAction.Approve,
            ApprovalOutcome.Rejected => PurchaseAction.Reject,
            _ => PurchaseAction.Revise,
        };
        if (documentType == DocumentTypes.PurchaseRequest)
        {
            (await LoadRequestAsync(documentId, tracking: true, cancellationToken)).Apply(action, comment);
        }
        else
        {
            (await LoadOrderAsync(documentId, tracking: true, cancellationToken)).Apply(action, comment);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Outstanding quantity per item on open purchase orders, in stock unit. Used by MRP.</summary>
    public async Task<IReadOnlyList<OpenSupply>> OpenOrderSupplyAsync(CancellationToken cancellationToken)
    {
        var rows = await (
            from order in db.Orders.AsNoTracking()
            from line in order.Lines
            where (order.Status == PurchaseOrderStatus.Approved || order.Status == PurchaseOrderStatus.PartiallyReceived) && !line.IsClosed
            where line.StockQuantity > line.ReceivedStockQuantity
            select new { line.ItemId, Date = line.DeliveryDate ?? order.DeliveryDate ?? order.DocumentDate, Quantity = line.StockQuantity - line.ReceivedStockQuantity, order.DocumentNo })
            .ToListAsync(cancellationToken);
        return rows.Select(r => new OpenSupply(r.ItemId, r.Date, r.Quantity, r.DocumentNo)).ToList();
    }

    /// <summary>Quantity per item on purchase requests that is not yet on a purchase order, in stock unit. Used by MRP.</summary>
    public async Task<IReadOnlyList<OpenSupply>> OpenRequestSupplyAsync(CancellationToken cancellationToken)
    {
        var rows = await (
            from request in db.Requests.AsNoTracking()
            from line in request.Lines
            where request.Status == PurchaseRequestStatus.Draft || request.Status == PurchaseRequestStatus.Submitted
                || request.Status == PurchaseRequestStatus.Approved || request.Status == PurchaseRequestStatus.PartiallyOrdered
            select new { line.Id, line.ItemId, line.StockQuantity, Date = line.RequiredDate ?? request.RequiredDate ?? request.DocumentDate, request.DocumentNo })
            .ToListAsync(cancellationToken);
        var ordered = await OrderedByRequestLineAsync(rows.Select(r => r.Id).ToList(), cancellationToken);
        return rows
            .Select(r => new OpenSupply(r.ItemId, r.Date, r.StockQuantity - ordered.GetValueOrDefault(r.Id), r.DocumentNo))
            .Where(s => s.Quantity > 0)
            .ToList();
    }

    private static PurchaseOrderLineDto ToDto(PurchaseOrderLine l, IReadOnlyDictionary<Guid, ItemInfo> items, IReadOnlyDictionary<Guid, string> units) =>
        new(l.Id, l.LineNo, l.ItemId, items[l.ItemId].Code, items[l.ItemId].Name, l.UnitId, units.GetValueOrDefault(l.UnitId, string.Empty),
            l.Quantity, l.ConversionFactor, l.StockQuantity, l.UnitPrice, l.DiscountPercent, l.NetAmount, l.DeliveryDate, l.PrLineId,
            l.ReceivedStockQuantity, l.OutstandingStockQuantity, l.IsClosed, l.Remark);

    private async Task<List<PurchaseRequestLineData>> BuildRequestLinesAsync(SavePurchaseRequest request, CancellationToken cancellationToken)
    {
        var items = await masters.GetItemsAsync(request.Lines.Select(l => l.ItemId), requireActive: true, cancellationToken);
        var converter = await masters.GetConverterAsync(items.Keys, cancellationToken);
        await masters.EnsureUnitsAsync(request.Lines.Where(l => l.UnitId is not null).Select(l => l.UnitId!.Value), cancellationToken);
        var result = new List<PurchaseRequestLineData>();
        foreach (var (line, index) in request.Lines.Select((l, i) => (l, i + 1)))
        {
            var item = items[line.ItemId];
            if (line.SuggestedSupplierId is { } supplier)
            {
                await masters.GetSupplierAsync(supplier, cancellationToken);
            }

            var unitId = line.UnitId ?? item.PurchaseUnitId ?? item.StockUnitId;
            var factor = converter.FindFactor(item.Id, item.StockUnitId, unitId, item.StockUnitId)
                ?? throw new DomainException("masters.uom.no_conversion", $"Line {index}: no unit conversion to the stock unit of item {item.Code}.");
            result.Add(new PurchaseRequestLineData(
                line.ItemId, unitId, line.Quantity, factor, UnitConverter.RoundQuantity(line.Quantity * factor), line.RequiredDate,
                line.SuggestedSupplierId, line.Remark));
        }

        return result;
    }

    private async Task<(PurchaseOrderHeader Header, List<PurchaseOrderLineData> Lines)> BuildOrderAsync(SavePurchaseOrder request, Guid? orderId, CancellationToken cancellationToken)
    {
        var supplier = await masters.GetSupplierAsync(request.SupplierId, cancellationToken);
        var items = await masters.GetItemsAsync(request.Lines.Select(l => l.ItemId), requireActive: true, cancellationToken);
        var converter = await masters.GetConverterAsync(items.Keys, cancellationToken);
        await masters.EnsureUnitsAsync(request.Lines.Where(l => l.UnitId is not null).Select(l => l.UnitId!.Value), cancellationToken);

        var prLineIds = request.Lines.Where(l => l.PrLineId is not null).Select(l => l.PrLineId!.Value).Distinct().ToList();
        var prLines = await (
            from pr in db.Requests.AsNoTracking()
            from line in pr.Lines
            where prLineIds.Contains(line.Id)
            select new { Line = line, pr.Status, pr.DocumentNo }).ToDictionaryAsync(x => x.Line.Id, cancellationToken);
        var orderedElsewhere = await OrderedByRequestLineAsync(prLineIds, cancellationToken, orderId);

        var lines = new List<PurchaseOrderLineData>();
        var requestedByPrLine = new Dictionary<Guid, decimal>();
        foreach (var (line, index) in request.Lines.Select((l, i) => (l, i + 1)))
        {
            var item = items[line.ItemId];
            var unitId = line.UnitId ?? item.PurchaseUnitId ?? item.StockUnitId;
            var factor = converter.FindFactor(item.Id, item.StockUnitId, unitId, item.StockUnitId)
                ?? throw new DomainException("masters.uom.no_conversion", $"Line {index}: no unit conversion to the stock unit of item {item.Code}.");
            var stockQuantity = UnitConverter.RoundQuantity(line.Quantity * factor);

            if (line.PrLineId is { } prLineId)
            {
                if (!prLines.TryGetValue(prLineId, out var pr))
                {
                    throw new DomainException("purchasing.po.pr_line_not_found", $"Line {index}: the purchase request line does not exist.", 400);
                }

                if (pr.Line.ItemId != line.ItemId)
                {
                    throw new DomainException("purchasing.po.pr_item_mismatch", $"Line {index}: item differs from purchase request {pr.DocumentNo}.");
                }

                if (pr.Status is not (PurchaseRequestStatus.Approved or PurchaseRequestStatus.PartiallyOrdered or PurchaseRequestStatus.Ordered))
                {
                    throw DomainException.Conflict("purchasing.po.pr_not_approved", $"Line {index}: purchase request {pr.DocumentNo} is not approved.");
                }

                var total = requestedByPrLine.GetValueOrDefault(prLineId) + stockQuantity;
                requestedByPrLine[prLineId] = total;
                if (orderedElsewhere.GetValueOrDefault(prLineId) + total > pr.Line.StockQuantity)
                {
                    throw new DomainException("purchasing.po.exceeds_request", $"Line {index}: quantity exceeds what is left on purchase request {pr.DocumentNo}.");
                }
            }

            var price = line.UnitPrice
                ?? (await masters.ResolveSupplierPriceAsync(supplier.Id, item.Id, unitId, line.Quantity, request.DocumentDate, cancellationToken))?.UnitPrice
                ?? 0m;
            lines.Add(new PurchaseOrderLineData(
                line.ItemId, unitId, line.Quantity, factor, stockQuantity, price, line.DiscountPercent, line.DeliveryDate, line.PrLineId, line.Remark));
        }

        var vat = request.VatPercent ?? supplier.VatPercent ?? await settings.GetAsync(SettingKeys.VatPercent, 7m, cancellationToken);
        var rate = request.ExchangeRate ?? (supplier.Currency == "THB" ? 1m : 0m);
        var header = new PurchaseOrderHeader(
            request.DocumentDate, supplier.Id, supplier.Currency, rate, vat, request.DiscountAmount, request.CreditDays ?? supplier.CreditDays,
            request.DeliveryDate, request.Remark);
        return (header, lines);
    }

    private async Task<Dictionary<Guid, decimal>> OrderedByRequestLineAsync(IReadOnlyCollection<Guid> prLineIds, CancellationToken cancellationToken, Guid? exceptOrderId = null)
    {
        if (prLineIds.Count == 0)
        {
            return [];
        }

        return await (
            from order in db.Orders.AsNoTracking()
            from line in order.Lines
            where line.PrLineId != null && prLineIds.Contains(line.PrLineId.Value)
            where order.Status != PurchaseOrderStatus.Cancelled && order.Status != PurchaseOrderStatus.Rejected
            where exceptOrderId == null || order.Id != exceptOrderId
            group line by line.PrLineId!.Value into g
            select new { PrLineId = g.Key, Quantity = g.Sum(l => l.StockQuantity) })
            .ToDictionaryAsync(x => x.PrLineId, x => x.Quantity, cancellationToken);
    }

    private async Task RefreshRequestsAsync(IEnumerable<Guid> prLineIds, CancellationToken cancellationToken)
    {
        var ids = prLineIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return;
        }

        var requests = await db.Requests.Include(r => r.Lines).Where(r => r.Lines.Any(l => ids.Contains(l.Id))).ToListAsync(cancellationToken);
        foreach (var request in requests)
        {
            var ordered = await OrderedByRequestLineAsync(request.Lines.Select(l => l.Id).ToList(), cancellationToken);
            request.ApplyOrderedQuantities(ordered);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<IReadOnlyDictionary<Guid, string>> SupplierNamesAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken)
    {
        var result = new Dictionary<Guid, string>();
        foreach (var id in ids.Distinct())
        {
            try
            {
                result[id] = (await masters.GetSupplierAsync(id, cancellationToken)).Name;
            }
            catch (DomainException)
            {
                // Supplier was deactivated after the order was created; the list still shows the order.
                result[id] = string.Empty;
            }
        }

        return result;
    }

    private async Task<PurchaseRequest> LoadRequestAsync(Guid id, bool tracking, CancellationToken cancellationToken)
    {
        var query = db.Requests.Include(r => r.Lines).AsQueryable();
        if (!tracking)
        {
            query = query.AsNoTracking();
        }

        return await query.FirstOrDefaultAsync(r => r.Id == id, cancellationToken) ?? throw DomainException.NotFound("Purchase request", id);
    }

    private async Task<PurchaseOrder> LoadOrderAsync(Guid id, bool tracking, CancellationToken cancellationToken)
    {
        var query = db.Orders.Include(o => o.Lines).AsQueryable();
        if (!tracking)
        {
            query = query.AsNoTracking();
        }

        return await query.FirstOrDefaultAsync(o => o.Id == id, cancellationToken) ?? throw DomainException.NotFound("Purchase order", id);
    }

    private async Task<PurchaseRequestDto> ToDtoAsync(PurchaseRequest document, CancellationToken cancellationToken)
    {
        var items = await masters.GetItemsAsync(document.Lines.Select(l => l.ItemId), requireActive: false, cancellationToken);
        var units = await masters.GetUnitCodesAsync(document.Lines.Select(l => l.UnitId), cancellationToken);
        var ordered = await OrderedByRequestLineAsync(document.Lines.Select(l => l.Id).ToList(), cancellationToken);
        return new PurchaseRequestDto(
            document.Id, document.DocumentNo, document.DocumentDate, document.Status, document.Source, document.SourceReference,
            document.RequiredDate, document.Remark, document.StatusReason, document.RequestedBy,
            document.Lines.OrderBy(l => l.LineNo).Select(l => new PurchaseRequestLineDto(
                l.Id, l.LineNo, l.ItemId, items[l.ItemId].Code, items[l.ItemId].Name, l.UnitId, units.GetValueOrDefault(l.UnitId, string.Empty),
                l.Quantity, l.ConversionFactor, l.StockQuantity, ordered.GetValueOrDefault(l.Id),
                Math.Max(l.StockQuantity - ordered.GetValueOrDefault(l.Id), 0m), l.RequiredDate, l.SuggestedSupplierId, l.Remark)).ToList());
    }

    private async Task<PurchaseOrderDto> ToDtoAsync(PurchaseOrder document, CancellationToken cancellationToken)
    {
        var items = await masters.GetItemsAsync(document.Lines.Select(l => l.ItemId), requireActive: false, cancellationToken);
        var units = await masters.GetUnitCodesAsync(document.Lines.Select(l => l.UnitId), cancellationToken);
        var names = await SupplierNamesAsync([document.SupplierId], cancellationToken);
        return new PurchaseOrderDto(
            document.Id, document.DocumentNo, document.DocumentDate, document.SupplierId, names[document.SupplierId], document.Status,
            document.Currency, document.ExchangeRate, document.VatPercent, document.DiscountAmount, document.Subtotal, document.VatAmount,
            document.Total, document.CreditDays, document.DeliveryDate, document.Remark, document.StatusReason,
            document.Lines.OrderBy(l => l.LineNo).Select(l => ToDto(l, items, units)).ToList());
    }
}

/// <summary>Expected incoming quantity of an item.</summary>
/// <param name="ItemId">Item.</param>
/// <param name="Date">Expected date.</param>
/// <param name="Quantity">Quantity in stock unit.</param>
/// <param name="Reference">Document number.</param>
public sealed record OpenSupply(Guid ItemId, DateOnly Date, decimal Quantity, string Reference);

/// <summary>Lets the inventory module receive against purchase orders.</summary>
public sealed class PurchaseOrderGateway(PurchasingDbContext db) : IPurchaseOrderGateway
{
    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<Guid, PurchaseOrderLineInfo>> GetLinesAsync(IReadOnlyCollection<Guid> lineIds, CancellationToken cancellationToken)
    {
        var rows = await (
            from order in db.Orders.AsNoTracking()
            from line in order.Lines
            where lineIds.Contains(line.Id)
            select new { Order = order, Line = line }).ToListAsync(cancellationToken);
        return rows.ToDictionary(
            r => r.Line.Id,
            r => new PurchaseOrderLineInfo(
                r.Line.Id, r.Order.Id, r.Order.DocumentNo, r.Order.SupplierId, r.Line.ItemId, r.Line.StockQuantity, r.Line.ReceivedStockQuantity,
                PurchaseCalculator.StockUnitCost(r.Line.UnitPrice, r.Line.DiscountPercent, r.Order.ExchangeRate, r.Line.ConversionFactor),
                r.Order.IsReceivable && !r.Line.IsClosed));
    }

    /// <inheritdoc />
    public async Task ApplyReceiptAsync(IReadOnlyDictionary<Guid, decimal> stockQuantityByLine, CancellationToken cancellationToken)
    {
        var lineIds = stockQuantityByLine.Keys.ToList();
        var orders = await db.Orders.Include(o => o.Lines).Where(o => o.Lines.Any(l => lineIds.Contains(l.Id))).ToListAsync(cancellationToken);
        foreach (var order in orders)
        {
            foreach (var line in order.Lines.Where(l => stockQuantityByLine.ContainsKey(l.Id)).ToList())
            {
                order.ApplyReceipt(line.Id, stockQuantityByLine[line.Id]);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
