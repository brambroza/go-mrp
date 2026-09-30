namespace Mrp.SharedKernel.Domain;

/// <summary>Issues document numbers from a per-tenant, per-type sequence inside the caller's transaction.</summary>
public interface IDocumentNumberGenerator
{
    /// <summary>Returns the next number for <paramref name="documentType"/>, for example <c>PO-2610-0001</c>.</summary>
    /// <param name="documentType">Document type key such as <c>PO</c>.</param>
    /// <param name="documentDate">Date that decides the numbering period.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<string> NextAsync(string documentType, DateTimeOffset documentDate, CancellationToken cancellationToken = default);
}

/// <summary>Outcome of asking the approval engine to start a request.</summary>
/// <param name="RequestId">Approval request id, or <c>null</c> when no approval was needed.</param>
/// <param name="AutoApproved">True when the tenant has no applicable approval step.</param>
public sealed record ApprovalSubmission(Guid? RequestId, bool AutoApproved);

/// <summary>Document data the approval engine needs to route a request.</summary>
/// <param name="DocumentType">Document type key such as <c>PO</c>.</param>
/// <param name="DocumentId">Id of the document in its module.</param>
/// <param name="DocumentNo">Human readable number.</param>
/// <param name="Title">Short description shown in the approval inbox.</param>
/// <param name="Amount">Document amount used for amount based steps, if any.</param>
public sealed record ApprovalDocument(string DocumentType, Guid DocumentId, string DocumentNo, string Title, decimal? Amount);

/// <summary>Entry point modules use to send a document for approval.</summary>
public interface IApprovalService
{
    /// <summary>Starts an approval request following the tenant's route for the document type.</summary>
    Task<ApprovalSubmission> SubmitAsync(ApprovalDocument document, CancellationToken cancellationToken = default);

    /// <summary>Withdraws the pending request of a document, if one exists.</summary>
    Task WithdrawAsync(string documentType, Guid documentId, CancellationToken cancellationToken = default);
}

/// <summary>Final result of an approval request.</summary>
public enum ApprovalOutcome
{
    /// <summary>All steps approved.</summary>
    Approved,

    /// <summary>Rejected at some step.</summary>
    Rejected,

    /// <summary>Withdrawn by the requester.</summary>
    Withdrawn,
}

/// <summary>Implemented by the module that owns a document type to react when approval finishes.</summary>
public interface IApprovalSubscriber
{
    /// <summary>Document type key this subscriber handles.</summary>
    string DocumentType { get; }

    /// <summary>Called inside the approval transaction once the request reaches a final state.</summary>
    Task OnCompletedAsync(Guid documentId, ApprovalOutcome outcome, Guid actedBy, string? comment, CancellationToken cancellationToken);
}

/// <summary>Typed access to the current tenant's settings with defaults.</summary>
public interface ITenantSettings
{
    /// <summary>Returns the value stored under <paramref name="key"/> or <paramref name="defaultValue"/>.</summary>
    Task<T> GetAsync<T>(string key, T defaultValue, CancellationToken cancellationToken = default);

    /// <summary>IANA time zone of the current tenant.</summary>
    Task<TimeZoneInfo> GetTimeZoneAsync(CancellationToken cancellationToken = default);
}

/// <summary>Purchase order line data the inventory module needs when receiving.</summary>
/// <param name="LineId">PO line id.</param>
/// <param name="OrderId">PO id.</param>
/// <param name="OrderNo">PO number.</param>
/// <param name="SupplierId">Supplier of the PO.</param>
/// <param name="ItemId">Ordered item.</param>
/// <param name="OrderedStockQuantity">Ordered quantity in the item's stock unit.</param>
/// <param name="ReceivedStockQuantity">Net received quantity (receipts − supplier returns) in stock unit.</param>
/// <param name="StockUnitCost">Net price per stock unit.</param>
/// <param name="IsOpen">Whether the PO is approved and the line is not closed.</param>
public sealed record PurchaseOrderLineInfo(
    Guid LineId,
    Guid OrderId,
    string OrderNo,
    Guid SupplierId,
    Guid ItemId,
    decimal OrderedStockQuantity,
    decimal ReceivedStockQuantity,
    decimal StockUnitCost,
    bool IsOpen);

/// <summary>Implemented by the purchasing module so inventory can receive against purchase orders.</summary>
public interface IPurchaseOrderGateway
{
    /// <summary>Returns the requested PO lines; unknown ids are omitted.</summary>
    Task<IReadOnlyDictionary<Guid, PurchaseOrderLineInfo>> GetLinesAsync(IReadOnlyCollection<Guid> lineIds, CancellationToken cancellationToken);

    /// <summary>Adds (or, when negative, removes) received quantity on PO lines inside the caller's transaction.</summary>
    Task ApplyReceiptAsync(IReadOnlyDictionary<Guid, decimal> stockQuantityByLine, CancellationToken cancellationToken);
}

/// <summary>Stock posted (or reversed) by a warehouse document that refers to another module's document.</summary>
/// <param name="ReferenceId">Referenced document (for example a work order).</param>
/// <param name="StockDocumentType">Warehouse document type name: <c>Issue</c>, <c>Receipt</c>, …</param>
/// <param name="StockDocumentNo">Warehouse document number.</param>
/// <param name="Quantities">Stock quantity per item; positive when posted, negative when the posting is reversed.</param>
public sealed record StockReferenceEvent(Guid ReferenceId, string StockDocumentType, string StockDocumentNo, IReadOnlyDictionary<Guid, decimal> Quantities);

/// <summary>
/// Implemented by a module whose documents are referenced by warehouse documents. Called inside the
/// posting transaction; throwing a <see cref="DomainException"/> rejects the posting.
/// </summary>
public interface IStockReferenceHandler
{
    /// <summary>Reference type this handler owns, e.g. <c>WO</c>.</summary>
    string ReferenceType { get; }

    /// <summary>Applies the posted quantities to the referenced document.</summary>
    Task OnStockPostedAsync(StockReferenceEvent stockEvent, CancellationToken cancellationToken);
}
