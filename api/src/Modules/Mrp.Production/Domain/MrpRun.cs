using Mrp.SharedKernel.Domain;

namespace Mrp.Production.Domain;

/// <summary>State of a planned order.</summary>
public enum PlannedOrderStatus
{
    /// <summary>Waiting for the planner.</summary>
    Proposed,

    /// <summary>Turned into a purchase request or work order.</summary>
    Converted,

    /// <summary>Rejected by the planner.</summary>
    Dismissed,
}

/// <summary>A stored MRP run with its results, so plans can be reviewed later.</summary>
public sealed class MrpRun : Entity
{
    private readonly List<MrpRunRequirement> _requirements = [];
    private readonly List<MrpRunPlannedOrder> _plannedOrders = [];
    private readonly List<MrpRunException> _exceptions = [];

    private MrpRun()
    {
    }

    /// <summary>Stores the result of a run.</summary>
    public MrpRun(string documentNo, DateOnly runDate, int horizonDays, Guid runBy, MrpResult result)
    {
        DocumentNo = documentNo;
        RunDate = runDate;
        HorizonDays = horizonDays;
        RunBy = runBy;
        _requirements.AddRange(result.Requirements.Select(r => new MrpRunRequirement(r)));
        _plannedOrders.AddRange(result.PlannedOrders.Select(o => new MrpRunPlannedOrder(o)));
        _exceptions.AddRange(result.Exceptions.Select(e => new MrpRunException(e)));
        ItemCount = _requirements.Count;
        PlannedOrderCount = _plannedOrders.Count;
        ExceptionCount = _exceptions.Count;
    }

    /// <summary>Run number.</summary>
    public string DocumentNo { get; private set; } = string.Empty;

    /// <summary>Planning date.</summary>
    public DateOnly RunDate { get; private set; }

    /// <summary>Days of demand that were planned.</summary>
    public int HorizonDays { get; private set; }

    /// <summary>User that started the run.</summary>
    public Guid RunBy { get; private set; }

    /// <summary>Number of items with requirements.</summary>
    public int ItemCount { get; private set; }

    /// <summary>Number of planned orders.</summary>
    public int PlannedOrderCount { get; private set; }

    /// <summary>Number of exceptions.</summary>
    public int ExceptionCount { get; private set; }

    /// <summary>Netting result per item.</summary>
    public IReadOnlyList<MrpRunRequirement> Requirements => _requirements;

    /// <summary>Planned orders.</summary>
    public IReadOnlyList<MrpRunPlannedOrder> PlannedOrders => _plannedOrders;

    /// <summary>Exceptions.</summary>
    public IReadOnlyList<MrpRunException> Exceptions => _exceptions;
}

/// <summary>Netting result of one item in a run.</summary>
public sealed class MrpRunRequirement : ITenantOwned
{
    private MrpRunRequirement()
    {
    }

    internal MrpRunRequirement(MrpRequirement source)
    {
        ItemId = source.ItemId;
        Level = source.Level;
        OnHand = source.OnHand;
        SafetyStock = source.SafetyStock;
        Gross = source.Gross;
        ScheduledReceipts = source.ScheduledReceipts;
        Net = source.Net;
        Planned = source.Planned;
    }

    /// <summary>Primary key.</summary>
    public Guid Id { get; private set; } = Guid.CreateVersion7();

    /// <inheritdoc />
    public Guid TenantId { get; private set; }

    /// <summary>Owning run.</summary>
    public Guid RunId { get; private set; }

    /// <summary>Item.</summary>
    public Guid ItemId { get; private set; }

    /// <summary>Low-level code.</summary>
    public int Level { get; private set; }

    /// <summary>Available stock at the start.</summary>
    public decimal OnHand { get; private set; }

    /// <summary>Safety stock.</summary>
    public decimal SafetyStock { get; private set; }

    /// <summary>Gross requirement.</summary>
    public decimal Gross { get; private set; }

    /// <summary>Scheduled receipts.</summary>
    public decimal ScheduledReceipts { get; private set; }

    /// <summary>Net requirement.</summary>
    public decimal Net { get; private set; }

    /// <summary>Planned order quantity.</summary>
    public decimal Planned { get; private set; }
}

/// <summary>Planned order of a run.</summary>
public sealed class MrpRunPlannedOrder : ITenantOwned
{
    private MrpRunPlannedOrder()
    {
    }

    internal MrpRunPlannedOrder(MrpPlannedOrder source)
    {
        ItemId = source.ItemId;
        OrderType = source.OrderType;
        Quantity = source.Quantity;
        ReleaseDate = source.ReleaseDate;
        DueDate = source.DueDate;
        IsLate = source.IsLate;
        Pegging = source.Pegging.Length > 300 ? source.Pegging[..300] : source.Pegging;
    }

    /// <summary>Primary key.</summary>
    public Guid Id { get; private set; } = Guid.CreateVersion7();

    /// <inheritdoc />
    public Guid TenantId { get; private set; }

    /// <summary>Owning run.</summary>
    public Guid RunId { get; private set; }

    /// <summary>Item.</summary>
    public Guid ItemId { get; private set; }

    /// <summary>Buy or make.</summary>
    public PlannedOrderType OrderType { get; private set; }

    /// <summary>Quantity in stock unit.</summary>
    public decimal Quantity { get; private set; }

    /// <summary>Date to place or start the order.</summary>
    public DateOnly ReleaseDate { get; private set; }

    /// <summary>Date the quantity is needed.</summary>
    public DateOnly DueDate { get; private set; }

    /// <summary>The release date was already in the past at run time.</summary>
    public bool IsLate { get; private set; }

    /// <summary>Sources of the requirement.</summary>
    public string Pegging { get; private set; } = string.Empty;

    /// <summary>State.</summary>
    public PlannedOrderStatus Status { get; private set; } = PlannedOrderStatus.Proposed;

    /// <summary>Type key of the document the order became (<c>PR</c> or <c>WO</c>).</summary>
    public string? ConvertedDocumentType { get; private set; }

    /// <summary>Document the order became.</summary>
    public Guid? ConvertedDocumentId { get; private set; }

    /// <summary>Number of the document the order became.</summary>
    public string? ConvertedDocumentNo { get; private set; }

    /// <summary>Records the document created from the proposal.</summary>
    public void MarkConverted(string documentType, Guid documentId, string documentNo)
    {
        EnsureProposed();
        Status = PlannedOrderStatus.Converted;
        ConvertedDocumentType = documentType;
        ConvertedDocumentId = documentId;
        ConvertedDocumentNo = documentNo;
    }

    /// <summary>Rejects the proposal.</summary>
    public void Dismiss()
    {
        EnsureProposed();
        Status = PlannedOrderStatus.Dismissed;
    }

    private void EnsureProposed()
    {
        if (Status != PlannedOrderStatus.Proposed)
        {
            throw DomainException.Conflict("production.mrp.not_proposed", $"The planned order is already {Status}.");
        }
    }
}

/// <summary>Exception of a run.</summary>
public sealed class MrpRunException : ITenantOwned
{
    private MrpRunException()
    {
    }

    internal MrpRunException(MrpException source)
    {
        ItemId = source.ItemId;
        Code = source.Code;
        Message = source.Message.Length > 500 ? source.Message[..500] : source.Message;
    }

    /// <summary>Primary key.</summary>
    public Guid Id { get; private set; } = Guid.CreateVersion7();

    /// <inheritdoc />
    public Guid TenantId { get; private set; }

    /// <summary>Owning run.</summary>
    public Guid RunId { get; private set; }

    /// <summary>Item.</summary>
    public Guid ItemId { get; private set; }

    /// <summary>Exception code.</summary>
    public string Code { get; private set; } = string.Empty;

    /// <summary>English description.</summary>
    public string Message { get; private set; } = string.Empty;
}
