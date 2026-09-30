using Mrp.SharedKernel.Domain;

namespace Mrp.Inventory.Domain;

/// <summary>Kind of stock movement.</summary>
public enum MovementType
{
    /// <summary>Goods receipt.</summary>
    Receipt,

    /// <summary>Goods issue.</summary>
    Issue,

    /// <summary>Transfer leaving the source.</summary>
    TransferOut,

    /// <summary>Transfer arriving at the destination.</summary>
    TransferIn,

    /// <summary>Adjustment increasing stock.</summary>
    AdjustIn,

    /// <summary>Adjustment decreasing stock.</summary>
    AdjustOut,

    /// <summary>Return to supplier.</summary>
    SupplierReturn,
}

/// <summary>
/// One row of the append-only stock ledger. Never updated or deleted (enforced by a database trigger);
/// corrections are new rows that reference the row they reverse.
/// </summary>
public sealed class Movement : ITenantOwned
{
    private Movement()
    {
    }

    /// <summary>Creates a ledger row.</summary>
    public Movement(
        MovementType type,
        DateOnly postingDate,
        Guid itemId,
        Guid lotId,
        Guid warehouseId,
        Guid? locationId,
        decimal quantity,
        decimal unitCost,
        StockDocument document,
        Guid? documentLineId,
        Guid? poLineId,
        Guid? createdBy,
        Guid? reversesMovementId = null)
    {
        if (quantity == 0)
        {
            throw new DomainException("inventory.invalid_quantity", "Movement quantity must not be zero.", 400);
        }

        MovementType = type;
        PostingDate = postingDate;
        PostedAt = DateTimeOffset.UtcNow;
        ItemId = itemId;
        LotId = lotId;
        WarehouseId = warehouseId;
        LocationId = locationId;
        Quantity = quantity;
        UnitCost = unitCost;
        DocumentType = document.DocumentType;
        DocumentId = document.Id;
        DocumentNo = document.DocumentNo;
        DocumentLineId = documentLineId;
        PoLineId = poLineId;
        CreatedBy = createdBy;
        ReversesMovementId = reversesMovementId;
    }

    /// <summary>Primary key.</summary>
    public Guid Id { get; private set; } = Guid.CreateVersion7();

    /// <inheritdoc />
    public Guid TenantId { get; private set; }

    /// <summary>Insert order, used to sort movements of the same day.</summary>
    public long Seq { get; private set; }

    /// <summary>Accounting date in the tenant's time zone; decides the stock period.</summary>
    public DateOnly PostingDate { get; private set; }

    /// <summary>Time the row was written.</summary>
    public DateTimeOffset PostedAt { get; private set; }

    /// <summary>Kind of movement.</summary>
    public MovementType MovementType { get; private set; }

    /// <summary>Item.</summary>
    public Guid ItemId { get; private set; }

    /// <summary>Lot.</summary>
    public Guid LotId { get; private set; }

    /// <summary>Warehouse.</summary>
    public Guid WarehouseId { get; private set; }

    /// <summary>Location, if any.</summary>
    public Guid? LocationId { get; private set; }

    /// <summary>Signed quantity in the item's stock unit: positive in, negative out.</summary>
    public decimal Quantity { get; private set; }

    /// <summary>Cost per stock unit.</summary>
    public decimal UnitCost { get; private set; }

    /// <summary>Type of the source document.</summary>
    public StockDocumentType DocumentType { get; private set; }

    /// <summary>Source document.</summary>
    public Guid DocumentId { get; private set; }

    /// <summary>Number of the source document.</summary>
    public string DocumentNo { get; private set; } = string.Empty;

    /// <summary>Line of the source document.</summary>
    public Guid? DocumentLineId { get; private set; }

    /// <summary>Purchase order line received or returned.</summary>
    public Guid? PoLineId { get; private set; }

    /// <summary>Movement this row reverses (void).</summary>
    public Guid? ReversesMovementId { get; private set; }

    /// <summary>User that posted.</summary>
    public Guid? CreatedBy { get; private set; }

    /// <summary>Creates the row that cancels this one on the given date.</summary>
    public Movement Reverse(DateOnly postingDate, StockDocument document, Guid? userId) =>
        new(MovementType, postingDate, ItemId, LotId, WarehouseId, LocationId, -Quantity, UnitCost, document, DocumentLineId, PoLineId, userId, Id);
}

/// <summary>Cached on-hand quantity per item, lot, warehouse and location. Derived from the ledger only.</summary>
public sealed class Balance : ITenantOwned
{
    private Balance()
    {
    }

    /// <summary>Primary key.</summary>
    public Guid Id { get; private set; }

    /// <inheritdoc />
    public Guid TenantId { get; private set; }

    /// <summary>Item.</summary>
    public Guid ItemId { get; private set; }

    /// <summary>Lot.</summary>
    public Guid LotId { get; private set; }

    /// <summary>Warehouse.</summary>
    public Guid WarehouseId { get; private set; }

    /// <summary>Location, if any.</summary>
    public Guid? LocationId { get; private set; }

    /// <summary>On-hand quantity in stock unit.</summary>
    public decimal Quantity { get; private set; }

    /// <summary>Time of the last movement.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }
}

/// <summary>State of a stock period.</summary>
public enum PeriodStatus
{
    /// <summary>Movements may be posted.</summary>
    Open,

    /// <summary>Locked; a balance snapshot exists.</summary>
    Closed,
}

/// <summary>A calendar month of stock postings.</summary>
public sealed class Period : Entity
{
    private Period()
    {
    }

    /// <summary>Creates a closed period.</summary>
    public Period(int year, int month, Guid closedBy)
    {
        if (year is < 2000 or > 2100 || month is < 1 or > 12)
        {
            throw new DomainException("inventory.period.invalid", "Period is not valid.", 400);
        }

        Year = year;
        Month = month;
        Close(closedBy);
    }

    /// <summary>Calendar year.</summary>
    public int Year { get; private set; }

    /// <summary>Calendar month (1-12).</summary>
    public int Month { get; private set; }

    /// <summary>State.</summary>
    public PeriodStatus Status { get; private set; }

    /// <summary>Time of the last close.</summary>
    public DateTimeOffset? ClosedAt { get; private set; }

    /// <summary>User of the last close.</summary>
    public Guid? ClosedBy { get; private set; }

    /// <summary>Time of the last reopen.</summary>
    public DateTimeOffset? ReopenedAt { get; private set; }

    /// <summary>User of the last reopen.</summary>
    public Guid? ReopenedBy { get; private set; }

    /// <summary>Reason of the last reopen.</summary>
    public string? ReopenReason { get; private set; }

    /// <summary>First day of the period.</summary>
    public DateOnly FirstDay => new(Year, Month, 1);

    /// <summary>Last day of the period.</summary>
    public DateOnly LastDay => FirstDay.AddMonths(1).AddDays(-1);

    /// <summary>Locks the period.</summary>
    public void Close(Guid userId)
    {
        if (Status == PeriodStatus.Closed && ClosedAt is not null)
        {
            throw DomainException.Conflict("inventory.period.already_closed", "The period is already closed.");
        }

        Status = PeriodStatus.Closed;
        ClosedAt = DateTimeOffset.UtcNow;
        ClosedBy = userId;
    }

    /// <summary>Unlocks the period; a reason is mandatory.</summary>
    public void Reopen(Guid userId, string reason)
    {
        if (Status != PeriodStatus.Closed)
        {
            throw DomainException.Conflict("inventory.period.not_closed", "The period is not closed.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new DomainException("inventory.period.reason_required", "A reason is required to reopen a period.", 400);
        }

        Status = PeriodStatus.Open;
        ReopenedAt = DateTimeOffset.UtcNow;
        ReopenedBy = userId;
        ReopenReason = reason.Length > 500 ? reason[..500] : reason.Trim();
    }
}

/// <summary>Balance of one stock key at the end of a closed period.</summary>
public sealed class PeriodBalance : ITenantOwned
{
    private PeriodBalance()
    {
    }

    /// <summary>Primary key.</summary>
    public Guid Id { get; private set; }

    /// <inheritdoc />
    public Guid TenantId { get; private set; }

    /// <summary>Calendar year.</summary>
    public int Year { get; private set; }

    /// <summary>Calendar month.</summary>
    public int Month { get; private set; }

    /// <summary>Item.</summary>
    public Guid ItemId { get; private set; }

    /// <summary>Lot.</summary>
    public Guid LotId { get; private set; }

    /// <summary>Warehouse.</summary>
    public Guid WarehouseId { get; private set; }

    /// <summary>Location, if any.</summary>
    public Guid? LocationId { get; private set; }

    /// <summary>Quantity at period end.</summary>
    public decimal Quantity { get; private set; }

    /// <summary>Value at period end (quantity × lot cost).</summary>
    public decimal Value { get; private set; }
}
