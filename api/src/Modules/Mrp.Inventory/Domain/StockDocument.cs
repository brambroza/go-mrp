using Mrp.SharedKernel.Domain;

namespace Mrp.Inventory.Domain;

/// <summary>Kind of warehouse document.</summary>
public enum StockDocumentType
{
    /// <summary>Goods receipt (optionally against a purchase order).</summary>
    Receipt,

    /// <summary>Goods issue.</summary>
    Issue,

    /// <summary>Transfer between warehouses or locations.</summary>
    Transfer,

    /// <summary>Stock adjustment; line quantity is signed.</summary>
    Adjustment,

    /// <summary>Stock count; line quantity is the counted quantity.</summary>
    Count,

    /// <summary>Return to supplier.</summary>
    SupplierReturn,
}

/// <summary>State of a warehouse document.</summary>
public enum StockDocumentStatus
{
    /// <summary>Editable.</summary>
    Draft,

    /// <summary>Waiting for approval.</summary>
    Submitted,

    /// <summary>Approved; ready to post.</summary>
    Approved,

    /// <summary>Rejected by an approver; may be revised.</summary>
    Rejected,

    /// <summary>Stock movements written.</summary>
    Posted,

    /// <summary>Posted movements reversed.</summary>
    Voided,
}

/// <summary>Action on a warehouse document.</summary>
public enum StockDocumentAction
{
    /// <summary>Send for approval.</summary>
    Submit,

    /// <summary>Approval finished positively (or no approval is configured).</summary>
    Approve,

    /// <summary>Approval finished negatively.</summary>
    Reject,

    /// <summary>Back to draft (withdrawn or revised after rejection).</summary>
    Revise,

    /// <summary>Write stock movements.</summary>
    Post,

    /// <summary>Reverse stock movements.</summary>
    Void,
}

/// <summary>Header values of a warehouse document.</summary>
public sealed record StockDocumentHeader(
    DateOnly DocumentDate,
    Guid WarehouseId,
    Guid? ToWarehouseId,
    Guid? SupplierId,
    string? ReferenceType,
    Guid? ReferenceId,
    string? ReferenceNo,
    string? Remark);

/// <summary>Line values of a warehouse document.</summary>
public sealed record StockDocumentLineData(
    Guid ItemId,
    Guid UnitId,
    decimal Quantity,
    decimal ConversionFactor,
    decimal StockQuantity,
    decimal UnitCost,
    Guid? LocationId,
    Guid? ToLocationId,
    Guid? LotId,
    string? LotNo,
    string? SupplierLot,
    DateOnly? MfgDate,
    DateOnly? ExpiryDate,
    Guid? PoLineId,
    string? ReasonCode,
    decimal? SystemQuantity,
    string? Remark);

/// <summary>Warehouse document: receipt, issue, transfer, adjustment, count or supplier return.</summary>
public sealed class StockDocument : Entity
{
    private static readonly StateMachine<StockDocumentStatus, StockDocumentAction> Machine =
        new StateMachine<StockDocumentStatus, StockDocumentAction>()
            .Permit(StockDocumentStatus.Draft, StockDocumentAction.Submit, StockDocumentStatus.Submitted)
            .Permit(StockDocumentStatus.Submitted, StockDocumentAction.Approve, StockDocumentStatus.Approved)
            .Permit(StockDocumentStatus.Submitted, StockDocumentAction.Reject, StockDocumentStatus.Rejected)
            .Permit(StockDocumentStatus.Submitted, StockDocumentAction.Revise, StockDocumentStatus.Draft)
            .Permit(StockDocumentStatus.Rejected, StockDocumentAction.Revise, StockDocumentStatus.Draft)
            .Permit(StockDocumentStatus.Approved, StockDocumentAction.Revise, StockDocumentStatus.Draft)
            .Permit(StockDocumentStatus.Approved, StockDocumentAction.Post, StockDocumentStatus.Posted)
            .Permit(StockDocumentStatus.Posted, StockDocumentAction.Void, StockDocumentStatus.Voided);

    private readonly List<StockDocumentLine> _lines = [];
    private readonly List<StockDocumentAllocation> _allocations = [];

    private StockDocument()
    {
    }

    /// <summary>Creates a draft document.</summary>
    public StockDocument(StockDocumentType type, string documentNo, StockDocumentHeader header)
    {
        DocumentType = type;
        DocumentNo = documentNo;
        SetHeader(header);
    }

    /// <summary>Kind of document.</summary>
    public StockDocumentType DocumentType { get; private set; }

    /// <summary>Document number.</summary>
    public string DocumentNo { get; private set; } = string.Empty;

    /// <summary>Document date in the tenant's time zone; also the posting date.</summary>
    public DateOnly DocumentDate { get; private set; }

    /// <summary>State.</summary>
    public StockDocumentStatus Status { get; private set; } = StockDocumentStatus.Draft;

    /// <summary>Warehouse stock enters or leaves.</summary>
    public Guid WarehouseId { get; private set; }

    /// <summary>Destination warehouse of a transfer.</summary>
    public Guid? ToWarehouseId { get; private set; }

    /// <summary>Supplier of a receipt or return.</summary>
    public Guid? SupplierId { get; private set; }

    /// <summary>Type of the referenced document (<c>PO</c>, <c>WO</c>, …).</summary>
    public string? ReferenceType { get; private set; }

    /// <summary>Referenced document.</summary>
    public Guid? ReferenceId { get; private set; }

    /// <summary>Number of the referenced document.</summary>
    public string? ReferenceNo { get; private set; }

    /// <summary>Remark.</summary>
    public string? Remark { get; private set; }

    /// <summary>Time stock was posted.</summary>
    public DateTimeOffset? PostedAt { get; private set; }

    /// <summary>User that posted.</summary>
    public Guid? PostedBy { get; private set; }

    /// <summary>Time the document was voided.</summary>
    public DateTimeOffset? VoidedAt { get; private set; }

    /// <summary>Reason the document was voided or rejected.</summary>
    public string? StatusReason { get; private set; }

    /// <summary>Optimistic concurrency token.</summary>
    public uint Version { get; private set; }

    /// <summary>Lines ordered by line number.</summary>
    public IReadOnlyList<StockDocumentLine> Lines => _lines;

    /// <summary>Lots consumed when the document was posted.</summary>
    public IReadOnlyList<StockDocumentAllocation> Allocations => _allocations;

    /// <summary>Document type key used for numbering and approval routes.</summary>
    public string DocumentTypeKey => KeyOf(DocumentType);

    /// <summary>Whether header and lines may be changed.</summary>
    public bool IsEditable => Status is StockDocumentStatus.Draft or StockDocumentStatus.Rejected;

    /// <summary>Document type key of a document type.</summary>
    public static string KeyOf(StockDocumentType type) => type switch
    {
        StockDocumentType.Receipt => DocumentTypes.GoodsReceipt,
        StockDocumentType.Issue => DocumentTypes.GoodsIssue,
        StockDocumentType.Transfer => DocumentTypes.StockTransfer,
        StockDocumentType.Adjustment => DocumentTypes.StockAdjustment,
        StockDocumentType.Count => DocumentTypes.StockCount,
        StockDocumentType.SupplierReturn => DocumentTypes.SupplierReturn,
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };

    /// <summary>Permission needed to create and post a document type.</summary>
    public static string PermissionOf(StockDocumentType type) => type switch
    {
        StockDocumentType.Receipt or StockDocumentType.SupplierReturn => Permissions.InventoryReceive,
        StockDocumentType.Issue => Permissions.InventoryIssue,
        StockDocumentType.Transfer => Permissions.InventoryTransfer,
        _ => Permissions.InventoryAdjust,
    };

    /// <summary>Replaces header and lines of an editable document.</summary>
    public void Update(StockDocumentHeader header, IReadOnlyList<StockDocumentLineData> lines)
    {
        if (!IsEditable)
        {
            throw DomainException.Conflict("inventory.document.not_editable", $"A document that is {Status} cannot be edited.");
        }

        SetHeader(header);
        SetLines(lines);
        if (Status == StockDocumentStatus.Rejected)
        {
            Status = Machine.Fire(Status, StockDocumentAction.Revise);
        }
    }

    /// <summary>Sends the document for approval.</summary>
    public void Submit()
    {
        EnsureHasLines();
        Status = Machine.Fire(Status, StockDocumentAction.Submit);
        StatusReason = null;
    }

    /// <summary>Marks the document approved.</summary>
    public void Approve() => Status = Machine.Fire(Status, StockDocumentAction.Approve);

    /// <summary>Marks the document rejected.</summary>
    public void Reject(string? reason)
    {
        Status = Machine.Fire(Status, StockDocumentAction.Reject);
        StatusReason = Truncate(reason);
    }

    /// <summary>Returns the document to draft.</summary>
    public void Revise() => Status = Machine.Fire(Status, StockDocumentAction.Revise);

    /// <summary>Marks the document posted and records the consumed lots.</summary>
    public void MarkPosted(Guid? userId, IEnumerable<StockDocumentAllocation> allocations)
    {
        EnsureHasLines();
        Status = Machine.Fire(Status, StockDocumentAction.Post);
        PostedAt = DateTimeOffset.UtcNow;
        PostedBy = userId;
        _allocations.AddRange(allocations);
    }

    /// <summary>Marks the document voided; a reason is mandatory.</summary>
    public void MarkVoided(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new DomainException("inventory.document.void_reason_required", "A reason is required to void a document.", 400);
        }

        Status = Machine.Fire(Status, StockDocumentAction.Void);
        VoidedAt = DateTimeOffset.UtcNow;
        StatusReason = Truncate(reason);
    }

    private static string? Truncate(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim() is { Length: > 500 } text ? text[..500] : value.Trim();

    private void SetHeader(StockDocumentHeader header)
    {
        if (header.WarehouseId == Guid.Empty)
        {
            throw new DomainException("inventory.document.warehouse_required", "Warehouse is required.", 400);
        }

        if (DocumentType == StockDocumentType.Transfer && header.ToWarehouseId is null)
        {
            throw new DomainException("inventory.document.to_warehouse_required", "Destination warehouse is required for a transfer.", 400);
        }

        if (DocumentType == StockDocumentType.SupplierReturn && header.SupplierId is null)
        {
            throw new DomainException("inventory.document.supplier_required", "Supplier is required for a return to supplier.", 400);
        }

        DocumentDate = header.DocumentDate;
        WarehouseId = header.WarehouseId;
        ToWarehouseId = DocumentType == StockDocumentType.Transfer ? header.ToWarehouseId : null;
        SupplierId = header.SupplierId;
        ReferenceType = Truncate(header.ReferenceType);
        ReferenceId = header.ReferenceId;
        ReferenceNo = Truncate(header.ReferenceNo);
        Remark = Truncate(header.Remark);
    }

    private void SetLines(IReadOnlyList<StockDocumentLineData> lines)
    {
        _lines.Clear();
        var number = 0;
        foreach (var data in lines)
        {
            ValidateLine(data, ++number);
            _lines.Add(new StockDocumentLine(number, data));
        }
    }

    private void ValidateLine(StockDocumentLineData line, int number)
    {
        var signed = DocumentType == StockDocumentType.Adjustment;
        var counted = DocumentType == StockDocumentType.Count;
        if (line.ConversionFactor <= 0 || line.UnitCost < 0)
        {
            throw new DomainException("inventory.line.invalid_value", $"Line {number}: factor must be positive and cost must not be negative.", 400);
        }

        if (counted ? line.Quantity < 0 : signed ? line.Quantity == 0 : line.Quantity <= 0)
        {
            throw new DomainException("inventory.line.invalid_quantity", $"Line {number}: quantity is not valid for this document type.", 400);
        }

        if (DocumentType is StockDocumentType.Adjustment && string.IsNullOrWhiteSpace(line.ReasonCode))
        {
            throw new DomainException("inventory.line.reason_required", $"Line {number}: a reason is required for adjustments.", 400);
        }

        if (DocumentType is StockDocumentType.Count or StockDocumentType.SupplierReturn && line.LotId is null)
        {
            throw new DomainException("inventory.line.lot_required", $"Line {number}: a lot is required for this document type.", 400);
        }

        if (DocumentType == StockDocumentType.Transfer && ToWarehouseId == WarehouseId && line.ToLocationId == line.LocationId && line.LocationId is null && line.LotId is null)
        {
            throw new DomainException("inventory.line.same_place", $"Line {number}: source and destination are the same.", 400);
        }
    }

    private void EnsureHasLines()
    {
        if (_lines.Count == 0)
        {
            throw new DomainException("inventory.document.no_lines", "The document has no lines.");
        }
    }
}

/// <summary>Line of a warehouse document.</summary>
public sealed class StockDocumentLine : ITenantOwned
{
    private StockDocumentLine()
    {
    }

    internal StockDocumentLine(int lineNo, StockDocumentLineData data)
    {
        LineNo = lineNo;
        ItemId = data.ItemId;
        UnitId = data.UnitId;
        Quantity = data.Quantity;
        ConversionFactor = data.ConversionFactor;
        StockQuantity = data.StockQuantity;
        UnitCost = data.UnitCost;
        LocationId = data.LocationId;
        ToLocationId = data.ToLocationId;
        LotId = data.LotId;
        LotNo = string.IsNullOrWhiteSpace(data.LotNo) ? null : data.LotNo.Trim().ToUpperInvariant();
        SupplierLot = string.IsNullOrWhiteSpace(data.SupplierLot) ? null : data.SupplierLot.Trim();
        MfgDate = data.MfgDate;
        ExpiryDate = data.ExpiryDate;
        PoLineId = data.PoLineId;
        ReasonCode = string.IsNullOrWhiteSpace(data.ReasonCode) ? null : data.ReasonCode.Trim().ToUpperInvariant();
        SystemQuantity = data.SystemQuantity;
        Remark = string.IsNullOrWhiteSpace(data.Remark) ? null : data.Remark.Trim();
    }

    /// <summary>Primary key.</summary>
    public Guid Id { get; private set; } = Guid.CreateVersion7();

    /// <inheritdoc />
    public Guid TenantId { get; private set; }

    /// <summary>Owning document.</summary>
    public Guid DocumentId { get; private set; }

    /// <summary>Order within the document, starting at 1.</summary>
    public int LineNo { get; private set; }

    /// <summary>Item.</summary>
    public Guid ItemId { get; private set; }

    /// <summary>Unit the quantity was entered in.</summary>
    public Guid UnitId { get; private set; }

    /// <summary>Quantity as entered (signed for adjustments, counted quantity for counts).</summary>
    public decimal Quantity { get; private set; }

    /// <summary>Multiplier from the entered unit to the stock unit at document time.</summary>
    public decimal ConversionFactor { get; private set; }

    /// <summary>Quantity in the item's stock unit.</summary>
    public decimal StockQuantity { get; private set; }

    /// <summary>Cost per entered unit (receipts, positive adjustments).</summary>
    public decimal UnitCost { get; private set; }

    /// <summary>Location stock enters or leaves.</summary>
    public Guid? LocationId { get; private set; }

    /// <summary>Destination location of a transfer.</summary>
    public Guid? ToLocationId { get; private set; }

    /// <summary>Lot chosen by the user; null lets the system allocate (or create one on receipt).</summary>
    public Guid? LotId { get; private set; }

    /// <summary>Lot number for a new lot; generated when empty.</summary>
    public string? LotNo { get; private set; }

    /// <summary>Supplier's lot number.</summary>
    public string? SupplierLot { get; private set; }

    /// <summary>Manufacturing date of a new lot.</summary>
    public DateOnly? MfgDate { get; private set; }

    /// <summary>Expiry date of a new lot.</summary>
    public DateOnly? ExpiryDate { get; private set; }

    /// <summary>Purchase order line received or returned.</summary>
    public Guid? PoLineId { get; private set; }

    /// <summary>Reason code of an adjustment.</summary>
    public string? ReasonCode { get; private set; }

    /// <summary>System quantity when the count sheet was created.</summary>
    public decimal? SystemQuantity { get; private set; }

    /// <summary>Remark.</summary>
    public string? Remark { get; private set; }

    /// <summary>Lot created for the line when the document was posted.</summary>
    public Guid? CreatedLotId { get; private set; }

    /// <summary>Records the lot created on posting.</summary>
    public void SetCreatedLot(Guid lotId) => CreatedLotId = lotId;
}

/// <summary>Quantity of a lot consumed or moved by a document line.</summary>
public sealed class StockDocumentAllocation : ITenantOwned
{
    private StockDocumentAllocation()
    {
    }

    /// <summary>Creates an allocation.</summary>
    public StockDocumentAllocation(Guid lineId, Guid lotId, Guid? locationId, decimal quantity)
    {
        LineId = lineId;
        LotId = lotId;
        LocationId = locationId;
        Quantity = quantity;
    }

    /// <summary>Primary key.</summary>
    public Guid Id { get; private set; } = Guid.CreateVersion7();

    /// <inheritdoc />
    public Guid TenantId { get; private set; }

    /// <summary>Owning document.</summary>
    public Guid DocumentId { get; private set; }

    /// <summary>Document line.</summary>
    public Guid LineId { get; private set; }

    /// <summary>Lot.</summary>
    public Guid LotId { get; private set; }

    /// <summary>Source location.</summary>
    public Guid? LocationId { get; private set; }

    /// <summary>Quantity in stock unit.</summary>
    public decimal Quantity { get; private set; }
}
