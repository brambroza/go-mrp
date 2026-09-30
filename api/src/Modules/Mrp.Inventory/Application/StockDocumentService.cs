using Microsoft.EntityFrameworkCore;
using Mrp.Inventory.Domain;
using Mrp.Inventory.Persistence;
using Mrp.Masters.Application;
using Mrp.Masters.Domain;
using Mrp.SharedKernel.Domain;
using Mrp.SharedKernel.Persistence;
using Mrp.SharedKernel.Tenancy;
using Mrp.SharedKernel.Web;

namespace Mrp.Inventory.Application;

/// <summary>Creates, approves, posts and voids warehouse documents.</summary>
public sealed class StockDocumentService(
    InventoryDbContext db,
    DbSession session,
    ITenantContext tenant,
    ITenantSettings settings,
    ICurrentUser currentUser,
    MasterData masters,
    IDocumentNumberGenerator numbers,
    IApprovalService approvals,
    IPurchaseOrderGateway purchaseOrders,
    IEnumerable<IStockReferenceHandler> referenceHandlers,
    StockLedger ledger,
    TimeProvider clock)
{
    private const string CountReason = "COUNT";

    /// <summary>One page of documents, newest first.</summary>
    public async Task<PagedResult<StockDocumentSummary>> ListAsync(
        StockDocumentType? type, StockDocumentStatus? status, string? search, DateOnly? from, DateOnly? to, Guid? referenceId,
        int? page, int? pageSize, CancellationToken cancellationToken)
    {
        var query = db.Documents.AsNoTracking();
        if (type is { } t)
        {
            query = query.Where(d => d.DocumentType == t);
        }

        if (status is { } s)
        {
            query = query.Where(d => d.Status == s);
        }

        if (from is { } f)
        {
            query = query.Where(d => d.DocumentDate >= f);
        }

        if (to is { } u)
        {
            query = query.Where(d => d.DocumentDate <= u);
        }

        if (referenceId is { } reference)
        {
            query = query.Where(d => d.ReferenceId == reference);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var text = search.Trim().ToUpperInvariant();
            query = query.Where(d => d.DocumentNo.StartsWith(text) || (d.ReferenceNo != null && d.ReferenceNo.StartsWith(text)));
        }

        return await query
            .OrderByDescending(d => d.DocumentDate).ThenByDescending(d => d.DocumentNo)
            .Select(d => new StockDocumentSummary(
                d.Id, d.DocumentType, d.DocumentNo, d.DocumentDate, d.Status, d.WarehouseId, d.ToWarehouseId, d.SupplierId,
                d.ReferenceNo, d.Remark, d.Lines.Count, d.CreatedAt))
            .ToPagedAsync(page, pageSize, cancellationToken);
    }

    /// <summary>One document with lines and allocations.</summary>
    public async Task<StockDocumentDto> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var document = await LoadAsync(id, tracking: false, cancellationToken);
        return await ToDtoAsync(document, cancellationToken);
    }

    /// <summary>Creates a draft document.</summary>
    public Task<StockDocumentDto> CreateAsync(SaveStockDocumentRequest request, CancellationToken cancellationToken)
    {
        currentUser.Require(StockDocument.PermissionOf(request.DocumentType));
        return session.ExecuteInTransactionAsync(
            async ct =>
            {
                var header = await BuildHeaderAsync(request, ct);
                var lines = await BuildLinesAsync(request, ct);
                var number = await numbers.NextAsync(StockDocument.KeyOf(request.DocumentType), clock.GetUtcNow(), ct);
                var document = new StockDocument(request.DocumentType, number, header);
                document.Update(header, lines);
                db.Documents.Add(document);
                await db.SaveChangesAsync(ct);
                return await ToDtoAsync(document, ct);
            },
            cancellationToken);
    }

    /// <summary>Replaces header and lines of a draft or rejected document.</summary>
    public Task<StockDocumentDto> UpdateAsync(Guid id, SaveStockDocumentRequest request, CancellationToken cancellationToken) =>
        session.ExecuteInTransactionAsync(
            async ct =>
            {
                var document = await LoadAsync(id, tracking: true, ct);
                currentUser.Require(StockDocument.PermissionOf(document.DocumentType));
                if (document.DocumentType != request.DocumentType)
                {
                    throw DomainException.Conflict("inventory.document.type_fixed", "The type of a document cannot be changed.");
                }

                var header = await BuildHeaderAsync(request, ct);
                var lines = KeepSystemQuantities(document, await BuildLinesAsync(request, ct));
                document.Update(header, lines);
                await db.SaveChangesAsync(ct);
                return await ToDtoAsync(document, ct);
            },
            cancellationToken);

    /// <summary>Creates a count sheet with one line per lot and location that has stock.</summary>
    public Task<StockDocumentDto> CreateCountSheetAsync(CreateCountSheetRequest request, CancellationToken cancellationToken)
    {
        currentUser.Require(Permissions.InventoryAdjust);
        return session.ExecuteInTransactionAsync(
            async ct =>
            {
                await masters.GetWarehousesAsync([request.WarehouseId], ct);
                await masters.EnsureLocationAsync(request.WarehouseId, request.LocationId, ct);
                await EnsureDateAsync(request.DocumentDate, ct);

                var balances = db.Balances.AsNoTracking().Where(b => b.WarehouseId == request.WarehouseId && b.Quantity != 0);
                if (request.LocationId is { } location)
                {
                    balances = balances.Where(b => b.LocationId == location);
                }

                if (request.ItemIds is { Count: > 0 } itemIds)
                {
                    balances = balances.Where(b => itemIds.Contains(b.ItemId));
                }

                var rows = await balances.OrderBy(b => b.ItemId).ThenBy(b => b.LotId).Take(501).ToListAsync(ct);
                if (rows.Count == 0)
                {
                    throw new DomainException("inventory.count.nothing_to_count", "There is no stock to count for this selection.");
                }

                if (rows.Count > 500)
                {
                    throw new DomainException("inventory.count.too_many_lines", "More than 500 lots match; count by location or item.");
                }

                var items = await masters.GetItemsAsync(rows.Select(r => r.ItemId), requireActive: false, ct);
                var lines = rows.Select(r => new StockDocumentLineData(
                    r.ItemId, items[r.ItemId].StockUnitId, r.Quantity, 1m, r.Quantity, 0m, r.LocationId, null, r.LotId,
                    null, null, null, null, null, CountReason, r.Quantity, null)).ToList();

                var header = new StockDocumentHeader(request.DocumentDate, request.WarehouseId, null, null, null, null, null, request.Remark);
                var number = await numbers.NextAsync(DocumentTypes.StockCount, clock.GetUtcNow(), ct);
                var document = new StockDocument(StockDocumentType.Count, number, header);
                document.Update(header, lines);
                db.Documents.Add(document);
                await db.SaveChangesAsync(ct);
                return await ToDtoAsync(document, ct);
            },
            cancellationToken);
    }

    /// <summary>Sends a draft for approval; without an approval route the document becomes approved at once.</summary>
    public Task<StockDocumentDto> SubmitAsync(Guid id, CancellationToken cancellationToken) =>
        session.ExecuteInTransactionAsync(
            async ct =>
            {
                var document = await LoadAsync(id, tracking: true, ct);
                currentUser.Require(StockDocument.PermissionOf(document.DocumentType));
                await SubmitCoreAsync(document, ct);
                await db.SaveChangesAsync(ct);
                return await ToDtoAsync(document, ct);
            },
            cancellationToken);

    /// <summary>
    /// Posts stock. A draft is submitted first; when it needs approval the result stays <c>Submitted</c>
    /// and nothing is posted.
    /// </summary>
    public Task<StockDocumentDto> PostAsync(Guid id, CancellationToken cancellationToken) =>
        session.ExecuteInTransactionAsync(
            async ct =>
            {
                var document = await LoadAsync(id, tracking: true, ct);
                currentUser.Require(StockDocument.PermissionOf(document.DocumentType));
                if (document.Status == StockDocumentStatus.Draft)
                {
                    await SubmitCoreAsync(document, ct);
                }

                if (document.Status == StockDocumentStatus.Approved)
                {
                    await PostCoreAsync(document, ct);
                }
                else if (document.Status != StockDocumentStatus.Submitted)
                {
                    throw DomainException.Conflict("inventory.document.cannot_post", $"A document that is {document.Status} cannot be posted.");
                }

                await db.SaveChangesAsync(ct);
                return await ToDtoAsync(document, ct);
            },
            cancellationToken);

    /// <summary>Reverses a posted document with opposite movements dated today.</summary>
    public Task<StockDocumentDto> VoidAsync(Guid id, string reason, CancellationToken cancellationToken) =>
        session.ExecuteInTransactionAsync(
            async ct =>
            {
                var document = await LoadAsync(id, tracking: true, ct);
                currentUser.Require(StockDocument.PermissionOf(document.DocumentType));
                document.MarkVoided(reason);

                var today = await TodayAsync(ct);
                var movements = await db.Movements.AsNoTracking()
                    .Where(m => m.DocumentId == document.Id && m.ReversesMovementId == null)
                    .OrderBy(m => m.Seq)
                    .ToListAsync(ct);
                var reversals = movements.Select(m => m.Reverse(today, document, tenant.UserId)).ToList();
                await ledger.PostAsync(reversals, ct);

                var byPoLine = movements.Where(m => m.PoLineId is not null)
                    .GroupBy(m => m.PoLineId!.Value)
                    .ToDictionary(g => g.Key, g => -g.Sum(m => m.Quantity));
                if (byPoLine.Count > 0)
                {
                    await purchaseOrders.ApplyReceiptAsync(byPoLine, ct);
                }

                await NotifyReferenceAsync(document, reversals, ct);

                await db.SaveChangesAsync(ct);
                return await ToDtoAsync(document, ct);
            },
            cancellationToken);

    /// <summary>Called by the approval engine when a request for a warehouse document finishes.</summary>
    public async Task OnApprovalCompletedAsync(Guid documentId, ApprovalOutcome outcome, string? comment, CancellationToken cancellationToken)
    {
        var document = await LoadAsync(documentId, tracking: true, cancellationToken);
        switch (outcome)
        {
            case ApprovalOutcome.Approved:
                document.Approve();
                break;
            case ApprovalOutcome.Rejected:
                document.Reject(comment);
                break;
            default:
                document.Revise();
                break;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static decimal StockCost(decimal unitCost, decimal factor) =>
        Math.Round(unitCost / factor, 4, MidpointRounding.AwayFromZero);

    private async Task SubmitCoreAsync(StockDocument document, CancellationToken cancellationToken)
    {
        document.Submit();
        await EnsureDateAsync(document.DocumentDate, cancellationToken);
        var amount = document.Lines.Sum(l => Math.Abs(l.Quantity) * l.UnitCost);
        var submission = await approvals.SubmitAsync(
            new ApprovalDocument(document.DocumentTypeKey, document.Id, document.DocumentNo, $"{document.DocumentType} {document.DocumentNo}", amount),
            cancellationToken);
        if (submission.AutoApproved)
        {
            document.Approve();
        }
    }

    private async Task PostCoreAsync(StockDocument document, CancellationToken cancellationToken)
    {
        await EnsureDateAsync(document.DocumentDate, cancellationToken);
        var items = await masters.GetItemsAsync(document.Lines.Select(l => l.ItemId), requireActive: false, cancellationToken);
        var movements = new List<Movement>();
        var allocations = new List<StockDocumentAllocation>();
        var receiptsByPoLine = new Dictionary<Guid, decimal>();

        switch (document.DocumentType)
        {
            case StockDocumentType.Receipt:
                await PostReceiptAsync(document, items, movements, receiptsByPoLine, cancellationToken);
                break;
            case StockDocumentType.Count:
                await PostCountAsync(document, movements, cancellationToken);
                break;
            default:
                await PostOutgoingAsync(document, items, movements, allocations, receiptsByPoLine, cancellationToken);
                break;
        }

        document.MarkPosted(tenant.UserId, allocations);
        await ledger.PostAsync(movements, cancellationToken);
        if (receiptsByPoLine.Count > 0)
        {
            await purchaseOrders.ApplyReceiptAsync(receiptsByPoLine, cancellationToken);
        }

        await NotifyReferenceAsync(document, movements, cancellationToken);
    }

    /// <summary>Tells the owner of the referenced document (e.g. a work order) what was posted or reversed.</summary>
    private async Task NotifyReferenceAsync(StockDocument document, IReadOnlyCollection<Movement> movements, CancellationToken cancellationToken)
    {
        if (document.ReferenceType is null || document.ReferenceId is not { } referenceId)
        {
            return;
        }

        var handler = referenceHandlers.FirstOrDefault(h => h.ReferenceType == document.ReferenceType);
        if (handler is null)
        {
            return;
        }

        // Issues carry negative ledger quantities; the handler receives the issued amount as a positive number.
        var sign = document.DocumentType == StockDocumentType.Receipt ? 1m : -1m;
        var quantities = movements
            .Where(m => m.MovementType is MovementType.Receipt or MovementType.Issue)
            .GroupBy(m => m.ItemId)
            .ToDictionary(g => g.Key, g => g.Sum(m => m.Quantity) * sign);
        if (quantities.Count > 0)
        {
            await handler.OnStockPostedAsync(
                new StockReferenceEvent(referenceId, document.DocumentType.ToString(), document.DocumentNo, quantities), cancellationToken);
        }
    }

    private async Task PostReceiptAsync(
        StockDocument document,
        IReadOnlyDictionary<Guid, ItemInfo> items,
        List<Movement> movements,
        Dictionary<Guid, decimal> receiptsByPoLine,
        CancellationToken cancellationToken)
    {
        var qcOnReceive = await settings.GetAsync(SettingKeys.QcOnReceive, false, cancellationToken);
        await ValidatePurchaseOrderLinesAsync(document, cancellationToken);
        foreach (var line in document.Lines.OrderBy(l => l.LineNo))
        {
            var item = items[line.ItemId];
            var lot = await ResolveIncomingLotAsync(document, line, item, qcOnReceive ? LotQcStatus.Quarantine : LotQcStatus.Released, cancellationToken);
            line.SetCreatedLot(lot.Id);
            movements.Add(new Movement(
                MovementType.Receipt, document.DocumentDate, line.ItemId, lot.Id, document.WarehouseId, line.LocationId,
                line.StockQuantity, lot.UnitCost, document, line.Id, line.PoLineId, tenant.UserId));
            if (line.PoLineId is { } poLine)
            {
                receiptsByPoLine[poLine] = receiptsByPoLine.GetValueOrDefault(poLine) + line.StockQuantity;
            }
        }
    }

    private async Task PostCountAsync(StockDocument document, List<Movement> movements, CancellationToken cancellationToken)
    {
        var lotIds = document.Lines.Select(l => l.LotId!.Value).Distinct().ToList();
        var lots = await db.Lots.AsNoTracking().Where(l => lotIds.Contains(l.Id)).ToDictionaryAsync(l => l.Id, cancellationToken);
        var balances = await db.Balances.AsNoTracking()
            .Where(b => b.WarehouseId == document.WarehouseId && lotIds.Contains(b.LotId))
            .ToListAsync(cancellationToken);
        foreach (var line in document.Lines.OrderBy(l => l.LineNo))
        {
            var current = balances.Where(b => b.LotId == line.LotId && b.LocationId == line.LocationId).Sum(b => b.Quantity);
            var difference = line.StockQuantity - current;
            if (difference == 0)
            {
                continue;
            }

            movements.Add(new Movement(
                difference > 0 ? MovementType.AdjustIn : MovementType.AdjustOut, document.DocumentDate, line.ItemId, line.LotId!.Value,
                document.WarehouseId, line.LocationId, difference, lots[line.LotId!.Value].UnitCost, document, line.Id, null, tenant.UserId));
        }
    }

    private async Task PostOutgoingAsync(
        StockDocument document,
        IReadOnlyDictionary<Guid, ItemInfo> items,
        List<Movement> movements,
        List<StockDocumentAllocation> allocations,
        Dictionary<Guid, decimal> receiptsByPoLine,
        CancellationToken cancellationToken)
    {
        var strategy = Enum.TryParse<IssueStrategy>(
            await settings.GetAsync(SettingKeys.IssueStrategy, nameof(IssueStrategy.Fifo), cancellationToken), out var parsed)
            ? parsed
            : IssueStrategy.Fifo;
        var itemIds = document.Lines.Select(l => l.ItemId).Distinct().ToList();
        var stock = await LoadStockAsync(document.WarehouseId, itemIds, cancellationToken);
        var lotCosts = stock.Values.SelectMany(v => v).GroupBy(s => s.Stock.LotId).ToDictionary(g => g.Key, g => g.First().UnitCost);

        foreach (var line in document.Lines.OrderBy(l => l.LineNo))
        {
            var item = items[line.ItemId];
            if (document.DocumentType == StockDocumentType.Adjustment && line.StockQuantity > 0)
            {
                var lot = line.LotId is { } existing
                    ? await db.Lots.FirstAsync(l => l.Id == existing, cancellationToken)
                    : await ResolveIncomingLotAsync(document, line, item, LotQcStatus.Released, cancellationToken);
                line.SetCreatedLot(lot.Id);
                movements.Add(new Movement(
                    MovementType.AdjustIn, document.DocumentDate, line.ItemId, lot.Id, document.WarehouseId, line.LocationId,
                    line.StockQuantity, lot.UnitCost, document, line.Id, null, tenant.UserId));
                continue;
            }

            var required = Math.Abs(line.StockQuantity);
            var candidates = stock.GetValueOrDefault(line.ItemId, [])
                .Where(s => line.LotId is null || s.Stock.LotId == line.LotId)
                .Where(s => line.LocationId is null || s.Stock.LocationId == line.LocationId)
                .ToList();

            // Returns and adjustments may take stock that is not released; issues and transfers may not,
            // except that a transfer of a specific lot is how quarantined stock is moved.
            var usableOnly = document.DocumentType == StockDocumentType.Issue
                || (document.DocumentType == StockDocumentType.Transfer && line.LotId is null);
            var taken = LotAllocator.Allocate(candidates.Select(c => c.Stock), required, strategy, document.DocumentDate, usableOnly, item.Code);

            foreach (var allocation in taken)
            {
                var source = candidates.First(c => c.Stock.LotId == allocation.LotId && c.Stock.LocationId == allocation.LocationId);
                source.Take(allocation.Quantity);
                allocations.Add(new StockDocumentAllocation(line.Id, allocation.LotId, allocation.LocationId, allocation.Quantity));
                var cost = lotCosts[allocation.LotId];
                var type = document.DocumentType switch
                {
                    StockDocumentType.Issue => MovementType.Issue,
                    StockDocumentType.Transfer => MovementType.TransferOut,
                    StockDocumentType.SupplierReturn => MovementType.SupplierReturn,
                    _ => MovementType.AdjustOut,
                };
                movements.Add(new Movement(
                    type, document.DocumentDate, line.ItemId, allocation.LotId, document.WarehouseId, allocation.LocationId,
                    -allocation.Quantity, cost, document, line.Id, line.PoLineId, tenant.UserId));
                if (document.DocumentType == StockDocumentType.Transfer)
                {
                    movements.Add(new Movement(
                        MovementType.TransferIn, document.DocumentDate, line.ItemId, allocation.LotId, document.ToWarehouseId!.Value,
                        line.ToLocationId, allocation.Quantity, cost, document, line.Id, null, tenant.UserId));
                }
            }

            if (document.DocumentType == StockDocumentType.SupplierReturn && line.PoLineId is { } poLine)
            {
                receiptsByPoLine[poLine] = receiptsByPoLine.GetValueOrDefault(poLine) - required;
            }
        }
    }

    private async Task<Dictionary<Guid, List<WorkingStock>>> LoadStockAsync(Guid warehouseId, IReadOnlyCollection<Guid> itemIds, CancellationToken cancellationToken)
    {
        var rows = await (
            from balance in db.Balances.AsNoTracking()
            join lot in db.Lots.AsNoTracking() on balance.LotId equals lot.Id
            where balance.WarehouseId == warehouseId && itemIds.Contains(balance.ItemId) && balance.Quantity > 0
            select new { balance.ItemId, balance.LocationId, balance.Quantity, Lot = lot }).ToListAsync(cancellationToken);
        return rows.GroupBy(r => r.ItemId).ToDictionary(
            g => g.Key,
            g => g.Select(r => new WorkingStock(
                new LotStock(r.Lot.Id, r.Lot.LotNo, r.LocationId, r.LocationId?.ToString(), r.Lot.ReceivedAt, r.Lot.ExpiryDate, r.Lot.QcStatus, r.Quantity),
                r.Lot.UnitCost)).ToList());
    }

    private async Task<Lot> ResolveIncomingLotAsync(StockDocument document, StockDocumentLine line, ItemInfo item, LotQcStatus status, CancellationToken cancellationToken)
    {
        var unitCost = StockCost(line.UnitCost, line.ConversionFactor);
        if (!item.IsLotTracked)
        {
            var fixedNo = $"NL-{item.Code}";
            fixedNo = fixedNo.Length > 40 ? fixedNo[..40] : fixedNo;
            var shared = await db.Lots.FirstOrDefaultAsync(l => l.LotNo == fixedNo, cancellationToken);
            if (shared is not null)
            {
                return shared;
            }

            shared = new Lot(item.Id, fixedNo, LotQcStatus.Released, unitCost, clock.GetUtcNow(), null, null, null, document.Id);
            db.Lots.Add(shared);
            return shared;
        }

        var lotNo = line.LotNo ?? await numbers.NextAsync(DocumentTypes.Lot, clock.GetUtcNow(), cancellationToken);
        if (await db.Lots.AnyAsync(l => l.LotNo == lotNo, cancellationToken) || db.Lots.Local.Any(l => l.LotNo == lotNo))
        {
            throw DomainException.Conflict("inventory.lot.duplicate_number", $"Lot number '{lotNo}' already exists.");
        }

        var expiry = line.ExpiryDate ?? Lot.DefaultExpiry(line.MfgDate, document.DocumentDate, item.ShelfLifeDays);
        var lot = new Lot(item.Id, lotNo, status, unitCost, clock.GetUtcNow(), line.SupplierLot, line.MfgDate, expiry, document.Id);
        db.Lots.Add(lot);
        return lot;
    }

    private async Task ValidatePurchaseOrderLinesAsync(StockDocument document, CancellationToken cancellationToken)
    {
        var receiving = document.Lines.Where(l => l.PoLineId is not null)
            .GroupBy(l => l.PoLineId!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());
        if (receiving.Count == 0)
        {
            return;
        }

        var tolerance = await settings.GetAsync(SettingKeys.OverReceivePercent, 0m, cancellationToken);
        var poLines = await purchaseOrders.GetLinesAsync(receiving.Keys, cancellationToken);
        foreach (var (poLineId, lines) in receiving)
        {
            if (!poLines.TryGetValue(poLineId, out var poLine))
            {
                throw new DomainException("inventory.receipt.po_line_not_found", "A purchase order line does not exist.", 400);
            }

            if (!poLine.IsOpen)
            {
                throw DomainException.Conflict("inventory.receipt.po_not_open", $"Purchase order {poLine.OrderNo} is not approved or the line is closed.");
            }

            if (lines.Any(l => l.ItemId != poLine.ItemId) || (document.SupplierId is { } supplier && supplier != poLine.SupplierId))
            {
                throw new DomainException("inventory.receipt.po_mismatch", $"Item or supplier does not match purchase order {poLine.OrderNo}.");
            }

            var limit = UnitConverter.RoundQuantity(poLine.OrderedStockQuantity * (1 + (tolerance / 100m)));
            var total = poLine.ReceivedStockQuantity + lines.Sum(l => l.StockQuantity);
            if (total > limit)
            {
                throw new DomainException(
                    "inventory.receipt.over_receive",
                    $"Receiving {total:0.######} exceeds the limit {limit:0.######} of purchase order {poLine.OrderNo} (tolerance {tolerance:0.##}%).");
            }
        }
    }

    private async Task<StockDocumentHeader> BuildHeaderAsync(SaveStockDocumentRequest request, CancellationToken cancellationToken)
    {
        var warehouseIds = new List<Guid> { request.WarehouseId };
        if (request.DocumentType == StockDocumentType.Transfer && request.ToWarehouseId is { } destination)
        {
            warehouseIds.Add(destination);
        }

        await masters.GetWarehousesAsync(warehouseIds, cancellationToken);
        if (request.SupplierId is { } supplier)
        {
            await masters.GetSupplierAsync(supplier, cancellationToken);
        }

        await EnsureDateAsync(request.DocumentDate, cancellationToken);
        return new StockDocumentHeader(
            request.DocumentDate, request.WarehouseId, request.ToWarehouseId, request.SupplierId, request.ReferenceType,
            request.ReferenceId, request.ReferenceNo, request.Remark);
    }

    private async Task<List<StockDocumentLineData>> BuildLinesAsync(SaveStockDocumentRequest request, CancellationToken cancellationToken)
    {
        var items = await masters.GetItemsAsync(request.Lines.Select(l => l.ItemId), requireActive: true, cancellationToken);
        var converter = await masters.GetConverterAsync(items.Keys, cancellationToken);
        await masters.EnsureUnitsAsync(request.Lines.Where(l => l.UnitId is not null).Select(l => l.UnitId!.Value), cancellationToken);

        var lotIds = request.Lines.Where(l => l.LotId is not null).Select(l => l.LotId!.Value).Distinct().ToList();
        var lots = await db.Lots.AsNoTracking().Where(l => lotIds.Contains(l.Id)).ToDictionaryAsync(l => l.Id, cancellationToken);
        var poLineIds = request.Lines.Where(l => l.PoLineId is not null).Select(l => l.PoLineId!.Value).Distinct().ToList();
        var poLines = poLineIds.Count > 0
            ? await purchaseOrders.GetLinesAsync(poLineIds, cancellationToken)
            : new Dictionary<Guid, PurchaseOrderLineInfo>();

        var result = new List<StockDocumentLineData>(request.Lines.Count);
        foreach (var (line, index) in request.Lines.Select((l, i) => (l, i + 1)))
        {
            var item = items[line.ItemId];
            if (item.ItemType == ItemType.Service)
            {
                throw new DomainException("inventory.line.service_item", $"Line {index}: service items are not kept in stock.", 400);
            }

            if (line.LotId is { } lotId && (!lots.TryGetValue(lotId, out var lot) || lot.ItemId != line.ItemId))
            {
                throw new DomainException("inventory.line.lot_not_found", $"Line {index}: the lot does not exist for this item.", 400);
            }

            if (line.PoLineId is { } poLineId && !poLines.ContainsKey(poLineId))
            {
                throw new DomainException("inventory.receipt.po_line_not_found", $"Line {index}: the purchase order line does not exist.", 400);
            }

            await masters.EnsureLocationAsync(request.WarehouseId, line.LocationId, cancellationToken);
            if (request.DocumentType == StockDocumentType.Transfer && request.ToWarehouseId is { } destination)
            {
                await masters.EnsureLocationAsync(destination, line.ToLocationId, cancellationToken);
                if (destination == request.WarehouseId && line.ToLocationId == line.LocationId)
                {
                    throw new DomainException("inventory.line.same_place", $"Line {index}: source and destination are the same.", 400);
                }
            }

            var unitId = line.UnitId ?? item.StockUnitId;
            var factor = converter.FindFactor(item.Id, item.StockUnitId, unitId, item.StockUnitId)
                ?? throw new DomainException("masters.uom.no_conversion", $"Line {index}: no unit conversion to the stock unit of item {item.Code}.");
            var stockQuantity = UnitConverter.RoundQuantity(line.Quantity * factor);
            if (line.Quantity != 0 && stockQuantity == 0)
            {
                throw new DomainException("inventory.line.invalid_quantity", $"Line {index}: quantity is too small for the stock unit.", 400);
            }

            var unitCost = line.UnitCost
                ?? (line.PoLineId is { } id ? Math.Round(poLines[id].StockUnitCost * factor, 4, MidpointRounding.AwayFromZero) : item.StandardCost * factor);

            result.Add(new StockDocumentLineData(
                line.ItemId, unitId, line.Quantity, factor, stockQuantity, unitCost, line.LocationId,
                request.DocumentType == StockDocumentType.Transfer ? line.ToLocationId : null, line.LotId, line.LotNo, line.SupplierLot,
                line.MfgDate, line.ExpiryDate, line.PoLineId,
                request.DocumentType == StockDocumentType.Count ? CountReason : line.ReasonCode,
                null, line.Remark));
        }

        return result;
    }

    /// <summary>Carries the system quantity captured on the count sheet over to the edited lines.</summary>
    private static List<StockDocumentLineData> KeepSystemQuantities(StockDocument document, List<StockDocumentLineData> lines)
    {
        if (document.DocumentType != StockDocumentType.Count)
        {
            return lines;
        }

        var captured = document.Lines
            .GroupBy(l => (l.LotId, l.LocationId))
            .ToDictionary(g => g.Key, g => g.First().SystemQuantity);
        return lines.Select(l => l with { SystemQuantity = captured.GetValueOrDefault((l.LotId, l.LocationId)) ?? 0m }).ToList();
    }

    private async Task<DateOnly> TodayAsync(CancellationToken cancellationToken)
    {
        var zone = await settings.GetTimeZoneAsync(cancellationToken);
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), zone).DateTime);
    }

    private async Task EnsureDateAsync(DateOnly date, CancellationToken cancellationToken)
    {
        if (date > await TodayAsync(cancellationToken))
        {
            throw new DomainException("inventory.document.future_date", "Document date must not be in the future.", 400);
        }

        await ledger.EnsurePeriodOpenAsync(date, cancellationToken);
    }

    private async Task<StockDocument> LoadAsync(Guid id, bool tracking, CancellationToken cancellationToken)
    {
        var query = db.Documents.Include(d => d.Lines).Include(d => d.Allocations).AsSplitQuery();
        if (!tracking)
        {
            query = query.AsNoTracking();
        }

        return await query.FirstOrDefaultAsync(d => d.Id == id, cancellationToken)
            ?? throw DomainException.NotFound("Document", id);
    }

    private async Task<StockDocumentDto> ToDtoAsync(StockDocument document, CancellationToken cancellationToken)
    {
        var items = await masters.GetItemsAsync(document.Lines.Select(l => l.ItemId), requireActive: false, cancellationToken);
        var units = await masters.GetUnitCodesAsync(document.Lines.Select(l => l.UnitId), cancellationToken);
        var lotIds = document.Allocations.Select(a => a.LotId)
            .Concat(document.Lines.Where(l => (l.LotId ?? l.CreatedLotId) is not null).Select(l => (l.LotId ?? l.CreatedLotId)!.Value))
            .Distinct()
            .ToList();
        var lotNumbers = await db.Lots.AsNoTracking().Where(l => lotIds.Contains(l.Id)).ToDictionaryAsync(l => l.Id, l => l.LotNo, cancellationToken);
        foreach (var local in db.Lots.Local)
        {
            lotNumbers.TryAdd(local.Id, local.LotNo);
        }

        var allocations = document.Allocations.ToLookup(a => a.LineId);
        return new StockDocumentDto(
            document.Id, document.DocumentType, document.DocumentNo, document.DocumentDate, document.Status, document.WarehouseId,
            document.ToWarehouseId, document.SupplierId, document.ReferenceType, document.ReferenceId, document.ReferenceNo, document.Remark,
            document.StatusReason, document.PostedAt, document.CreatedAt,
            document.Lines.OrderBy(l => l.LineNo).Select(l =>
            {
                var lotId = l.LotId ?? l.CreatedLotId;
                return new StockDocumentLineDto(
                    l.Id, l.LineNo, l.ItemId, items[l.ItemId].Code, items[l.ItemId].Name, l.UnitId, units.GetValueOrDefault(l.UnitId, string.Empty),
                    l.Quantity, l.ConversionFactor, l.StockQuantity, l.UnitCost, l.LocationId, l.ToLocationId, lotId,
                    lotId is { } id ? lotNumbers.GetValueOrDefault(id, l.LotNo ?? string.Empty) : l.LotNo,
                    l.SupplierLot, l.MfgDate, l.ExpiryDate, l.PoLineId, l.ReasonCode, l.SystemQuantity, l.Remark,
                    allocations[l.Id].Select(a => new AllocationDto(a.LotId, lotNumbers.GetValueOrDefault(a.LotId, string.Empty), a.LocationId, a.Quantity)).ToList());
            }).ToList());
    }

    /// <summary>Stock of a lot/location that shrinks as lines of the same document consume it.</summary>
    private sealed class WorkingStock(LotStock stock, decimal unitCost)
    {
        public LotStock Stock { get; private set; } = stock;

        public decimal UnitCost { get; } = unitCost;

        public void Take(decimal quantity) => Stock = Stock with { Quantity = Stock.Quantity - quantity };
    }
}
