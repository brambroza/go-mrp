using Mrp.SharedKernel.Domain;

namespace Mrp.Purchasing.Domain;

/// <summary>State of a purchase request.</summary>
public enum PurchaseRequestStatus
{
    /// <summary>Editable.</summary>
    Draft,

    /// <summary>Waiting for approval.</summary>
    Submitted,

    /// <summary>Approved; may be ordered.</summary>
    Approved,

    /// <summary>Rejected; may be revised.</summary>
    Rejected,

    /// <summary>Some quantity is on purchase orders.</summary>
    PartiallyOrdered,

    /// <summary>All quantity is on purchase orders.</summary>
    Ordered,

    /// <summary>Remaining quantity will not be ordered.</summary>
    Closed,

    /// <summary>Cancelled before ordering.</summary>
    Cancelled,
}

/// <summary>Action on a purchase request or order.</summary>
public enum PurchaseAction
{
    /// <summary>Send for approval.</summary>
    Submit,

    /// <summary>Approval finished positively.</summary>
    Approve,

    /// <summary>Approval finished negatively.</summary>
    Reject,

    /// <summary>Back to draft.</summary>
    Revise,

    /// <summary>Progress (ordered or received quantity) changed to partial.</summary>
    Progress,

    /// <summary>Progress reached completion.</summary>
    Complete,

    /// <summary>Progress went back to nothing.</summary>
    Regress,

    /// <summary>Close the remaining quantity.</summary>
    Close,

    /// <summary>Cancel the document.</summary>
    Cancel,
}

/// <summary>Where a purchase request came from.</summary>
public enum PurchaseRequestSource
{
    /// <summary>Entered by a user.</summary>
    Manual,

    /// <summary>Proposed by an MRP run.</summary>
    Mrp,

    /// <summary>Proposed by the minimum stock check.</summary>
    MinStock,
}

/// <summary>Line values of a purchase request.</summary>
public sealed record PurchaseRequestLineData(
    Guid ItemId, Guid UnitId, decimal Quantity, decimal ConversionFactor, decimal StockQuantity, DateOnly? RequiredDate,
    Guid? SuggestedSupplierId, string? Remark);

/// <summary>Purchase request.</summary>
public sealed class PurchaseRequest : Entity
{
    private static readonly StateMachine<PurchaseRequestStatus, PurchaseAction> Machine =
        new StateMachine<PurchaseRequestStatus, PurchaseAction>()
            .Permit(PurchaseRequestStatus.Draft, PurchaseAction.Submit, PurchaseRequestStatus.Submitted)
            .Permit(PurchaseRequestStatus.Submitted, PurchaseAction.Approve, PurchaseRequestStatus.Approved)
            .Permit(PurchaseRequestStatus.Submitted, PurchaseAction.Reject, PurchaseRequestStatus.Rejected)
            .Permit(PurchaseRequestStatus.Submitted, PurchaseAction.Revise, PurchaseRequestStatus.Draft)
            .Permit(PurchaseRequestStatus.Rejected, PurchaseAction.Revise, PurchaseRequestStatus.Draft)
            .Permit(PurchaseRequestStatus.Approved, PurchaseAction.Progress, PurchaseRequestStatus.PartiallyOrdered)
            .Permit(PurchaseRequestStatus.Approved, PurchaseAction.Complete, PurchaseRequestStatus.Ordered)
            .Permit(PurchaseRequestStatus.PartiallyOrdered, PurchaseAction.Progress, PurchaseRequestStatus.PartiallyOrdered)
            .Permit(PurchaseRequestStatus.PartiallyOrdered, PurchaseAction.Complete, PurchaseRequestStatus.Ordered)
            .Permit(PurchaseRequestStatus.PartiallyOrdered, PurchaseAction.Regress, PurchaseRequestStatus.Approved)
            .Permit(PurchaseRequestStatus.Ordered, PurchaseAction.Progress, PurchaseRequestStatus.PartiallyOrdered)
            .Permit(PurchaseRequestStatus.Ordered, PurchaseAction.Regress, PurchaseRequestStatus.Approved)
            .Permit(PurchaseRequestStatus.Ordered, PurchaseAction.Complete, PurchaseRequestStatus.Ordered)
            .Permit(PurchaseRequestStatus.Approved, PurchaseAction.Regress, PurchaseRequestStatus.Approved)
            .Permit(PurchaseRequestStatus.Approved, PurchaseAction.Close, PurchaseRequestStatus.Closed)
            .Permit(PurchaseRequestStatus.PartiallyOrdered, PurchaseAction.Close, PurchaseRequestStatus.Closed)
            .Permit(PurchaseRequestStatus.Draft, PurchaseAction.Cancel, PurchaseRequestStatus.Cancelled)
            .Permit(PurchaseRequestStatus.Rejected, PurchaseAction.Cancel, PurchaseRequestStatus.Cancelled)
            .Permit(PurchaseRequestStatus.Approved, PurchaseAction.Cancel, PurchaseRequestStatus.Cancelled);

    private readonly List<PurchaseRequestLine> _lines = [];

    private PurchaseRequest()
    {
    }

    /// <summary>Creates a draft request.</summary>
    public PurchaseRequest(string documentNo, Guid requestedBy, PurchaseRequestSource source, string? sourceReference)
    {
        DocumentNo = documentNo;
        RequestedBy = requestedBy;
        Source = source;
        SourceReference = sourceReference;
    }

    /// <summary>Document number.</summary>
    public string DocumentNo { get; private set; } = string.Empty;

    /// <summary>Document date.</summary>
    public DateOnly DocumentDate { get; private set; }

    /// <summary>State.</summary>
    public PurchaseRequestStatus Status { get; private set; } = PurchaseRequestStatus.Draft;

    /// <summary>Origin.</summary>
    public PurchaseRequestSource Source { get; private set; }

    /// <summary>Number of the MRP run or other origin.</summary>
    public string? SourceReference { get; private set; }

    /// <summary>Date the goods are needed.</summary>
    public DateOnly? RequiredDate { get; private set; }

    /// <summary>Remark.</summary>
    public string? Remark { get; private set; }

    /// <summary>Requesting user.</summary>
    public Guid RequestedBy { get; private set; }

    /// <summary>Reason of rejection, cancellation or closing.</summary>
    public string? StatusReason { get; private set; }

    /// <summary>Optimistic concurrency token.</summary>
    public uint Version { get; private set; }

    /// <summary>Lines.</summary>
    public IReadOnlyList<PurchaseRequestLine> Lines => _lines;

    /// <summary>Whether header and lines may be changed.</summary>
    public bool IsEditable => Status is PurchaseRequestStatus.Draft or PurchaseRequestStatus.Rejected;

    /// <summary>Whether purchase orders may reference the request.</summary>
    public bool CanBeOrdered => Status is PurchaseRequestStatus.Approved or PurchaseRequestStatus.PartiallyOrdered;

    /// <summary>Replaces header and lines.</summary>
    public void Update(DateOnly documentDate, DateOnly? requiredDate, string? remark, IReadOnlyList<PurchaseRequestLineData> lines)
    {
        if (!IsEditable)
        {
            throw DomainException.Conflict("purchasing.document.not_editable", $"A request that is {Status} cannot be edited.");
        }

        if (requiredDate is { } required && required < documentDate)
        {
            throw new DomainException("purchasing.pr.required_before_document", "Required date must not be before the document date.", 400);
        }

        DocumentDate = documentDate;
        RequiredDate = requiredDate;
        Remark = Text.Clean(remark, 500);
        _lines.Clear();
        var number = 0;
        foreach (var line in lines)
        {
            number++;
            if (line.Quantity <= 0 || line.StockQuantity <= 0 || line.ConversionFactor <= 0)
            {
                throw new DomainException("purchasing.line.invalid_quantity", $"Line {number}: quantity must be greater than zero.", 400);
            }

            _lines.Add(new PurchaseRequestLine(number, line));
        }

        if (Status == PurchaseRequestStatus.Rejected)
        {
            Status = Machine.Fire(Status, PurchaseAction.Revise);
        }
    }

    /// <summary>Applies an action of the state machine.</summary>
    public void Apply(PurchaseAction action, string? reason = null)
    {
        if (action == PurchaseAction.Submit && _lines.Count == 0)
        {
            throw new DomainException("purchasing.document.no_lines", "The document has no lines.");
        }

        if (action is PurchaseAction.Cancel or PurchaseAction.Close && string.IsNullOrWhiteSpace(reason))
        {
            throw new DomainException("purchasing.document.reason_required", "A reason is required.", 400);
        }

        Status = Machine.Fire(Status, action);
        if (action is PurchaseAction.Cancel or PurchaseAction.Close or PurchaseAction.Reject)
        {
            StatusReason = Text.Clean(reason, 500);
        }
    }

    /// <summary>Updates the status from the quantity that is on purchase orders.</summary>
    /// <param name="orderedByLine">Ordered stock quantity per request line.</param>
    public void ApplyOrderedQuantities(IReadOnlyDictionary<Guid, decimal> orderedByLine)
    {
        if (!CanBeOrdered && Status != PurchaseRequestStatus.Ordered)
        {
            return;
        }

        var any = _lines.Any(l => orderedByLine.GetValueOrDefault(l.Id) > 0);
        var all = _lines.All(l => orderedByLine.GetValueOrDefault(l.Id) >= l.StockQuantity);
        var action = all ? PurchaseAction.Complete : any ? PurchaseAction.Progress : PurchaseAction.Regress;
        Status = Machine.Fire(Status, action);
    }
}

/// <summary>Line of a purchase request.</summary>
public sealed class PurchaseRequestLine : ITenantOwned
{
    private PurchaseRequestLine()
    {
    }

    internal PurchaseRequestLine(int lineNo, PurchaseRequestLineData data)
    {
        LineNo = lineNo;
        ItemId = data.ItemId;
        UnitId = data.UnitId;
        Quantity = data.Quantity;
        ConversionFactor = data.ConversionFactor;
        StockQuantity = data.StockQuantity;
        RequiredDate = data.RequiredDate;
        SuggestedSupplierId = data.SuggestedSupplierId;
        Remark = Text.Clean(data.Remark, 300);
    }

    /// <summary>Primary key.</summary>
    public Guid Id { get; private set; } = Guid.CreateVersion7();

    /// <inheritdoc />
    public Guid TenantId { get; private set; }

    /// <summary>Owning request.</summary>
    public Guid RequestId { get; private set; }

    /// <summary>Order within the request.</summary>
    public int LineNo { get; private set; }

    /// <summary>Item.</summary>
    public Guid ItemId { get; private set; }

    /// <summary>Unit of the quantity.</summary>
    public Guid UnitId { get; private set; }

    /// <summary>Requested quantity.</summary>
    public decimal Quantity { get; private set; }

    /// <summary>Multiplier to the stock unit.</summary>
    public decimal ConversionFactor { get; private set; }

    /// <summary>Requested quantity in stock unit.</summary>
    public decimal StockQuantity { get; private set; }

    /// <summary>Date the item is needed.</summary>
    public DateOnly? RequiredDate { get; private set; }

    /// <summary>Supplier proposed by the requester or MRP.</summary>
    public Guid? SuggestedSupplierId { get; private set; }

    /// <summary>Remark.</summary>
    public string? Remark { get; private set; }
}

/// <summary>Text helpers.</summary>
internal static class Text
{
    public static string? Clean(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length > maxLength ? trimmed[..maxLength] : trimmed;
    }
}
