using Mrp.SharedKernel.Domain;

namespace Mrp.Production.Domain;

/// <summary>State of a BOM version.</summary>
public enum BomStatus
{
    /// <summary>Editable.</summary>
    Draft,

    /// <summary>Reviewed; may be activated.</summary>
    Approved,

    /// <summary>The version used for planning and new work orders. One per item.</summary>
    Active,

    /// <summary>Replaced by a newer active version.</summary>
    Superseded,

    /// <summary>Abandoned before activation.</summary>
    Cancelled,
}

/// <summary>Action on a BOM version.</summary>
public enum BomAction
{
    /// <summary>Approve a draft.</summary>
    Approve,

    /// <summary>Make the version the active one.</summary>
    Activate,

    /// <summary>Replace by a newer version.</summary>
    Supersede,

    /// <summary>Abandon.</summary>
    Cancel,
}

/// <summary>Line values of a BOM.</summary>
public sealed record BomLineData(Guid ComponentItemId, Guid UnitId, decimal Quantity, decimal ConversionFactor, decimal StockQuantity, decimal LossPercent, string? Remark);

/// <summary>One version of the bill of materials of an item.</summary>
public sealed class BomVersion : Entity
{
    private static readonly StateMachine<BomStatus, BomAction> Machine = new StateMachine<BomStatus, BomAction>()
        .Permit(BomStatus.Draft, BomAction.Approve, BomStatus.Approved)
        .Permit(BomStatus.Approved, BomAction.Activate, BomStatus.Active)
        .Permit(BomStatus.Active, BomAction.Supersede, BomStatus.Superseded)
        .Permit(BomStatus.Draft, BomAction.Cancel, BomStatus.Cancelled)
        .Permit(BomStatus.Approved, BomAction.Cancel, BomStatus.Cancelled);

    private readonly List<BomLine> _lines = [];

    private BomVersion()
    {
    }

    /// <summary>Creates a draft version.</summary>
    public BomVersion(Guid itemId, int versionNo)
    {
        ItemId = itemId;
        VersionNo = versionNo;
    }

    /// <summary>Item that is produced.</summary>
    public Guid ItemId { get; private set; }

    /// <summary>Version number per item, starting at 1.</summary>
    public int VersionNo { get; private set; }

    /// <summary>Name of the formula.</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>Output quantity the line quantities refer to, in the item's stock unit.</summary>
    public decimal BatchSize { get; private set; }

    /// <summary>State.</summary>
    public BomStatus Status { get; private set; } = BomStatus.Draft;

    /// <summary>First day the version may be used.</summary>
    public DateOnly? EffectiveFrom { get; private set; }

    /// <summary>User that approved.</summary>
    public Guid? ApprovedBy { get; private set; }

    /// <summary>Time of activation.</summary>
    public DateTimeOffset? ActivatedAt { get; private set; }

    /// <summary>Remark.</summary>
    public string? Remark { get; private set; }

    /// <summary>Optimistic concurrency token.</summary>
    public uint Version { get; private set; }

    /// <summary>Components.</summary>
    public IReadOnlyList<BomLine> Lines => _lines;

    /// <summary>Replaces the content of a draft.</summary>
    public void Update(string name, decimal batchSize, DateOnly? effectiveFrom, string? remark, IReadOnlyList<BomLineData> lines)
    {
        if (Status != BomStatus.Draft)
        {
            throw DomainException.Conflict("production.bom.not_editable", $"A BOM that is {Status} cannot be edited; copy it to a new version.");
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("production.bom.name_required", "Name is required.", 400);
        }

        if (batchSize <= 0)
        {
            throw new DomainException("production.bom.invalid_batch_size", "Batch size must be greater than zero.", 400);
        }

        if (lines.Count == 0)
        {
            throw new DomainException("production.bom.no_lines", "A BOM needs at least one component.", 400);
        }

        _lines.Clear();
        var number = 0;
        foreach (var line in lines)
        {
            number++;
            if (line.ComponentItemId == ItemId)
            {
                throw new DomainException("production.bom.cycle", $"Line {number}: an item cannot be a component of itself.");
            }

            if (line.Quantity <= 0 || line.StockQuantity <= 0 || line.ConversionFactor <= 0)
            {
                throw new DomainException("production.bom.invalid_quantity", $"Line {number}: quantity must be greater than zero.", 400);
            }

            if (line.LossPercent is < 0 or > 100)
            {
                throw new DomainException("production.bom.invalid_loss", $"Line {number}: loss must be between 0 and 100 percent.", 400);
            }

            _lines.Add(new BomLine(number, line));
        }

        if (_lines.GroupBy(l => l.ComponentItemId).Any(g => g.Count() > 1))
        {
            throw new DomainException("production.bom.duplicate_component", "A component may appear only once in a BOM.", 400);
        }

        Name = name.Trim();
        BatchSize = batchSize;
        EffectiveFrom = effectiveFrom;
        Remark = string.IsNullOrWhiteSpace(remark) ? null : remark.Trim();
    }

    /// <summary>Applies an action of the state machine.</summary>
    public void Apply(BomAction action, Guid? userId = null)
    {
        Status = Machine.Fire(Status, action);
        if (action == BomAction.Approve)
        {
            ApprovedBy = userId;
        }

        if (action == BomAction.Activate)
        {
            ActivatedAt = DateTimeOffset.UtcNow;
        }
    }

    /// <summary>The version as plain data for calculations.</summary>
    public BomDefinition ToDefinition() =>
        new(ItemId, BatchSize, _lines.OrderBy(l => l.LineNo).Select(l => new BomComponent(l.ComponentItemId, l.StockQuantity, l.LossPercent)).ToList());
}

/// <summary>Component of a BOM version.</summary>
public sealed class BomLine : ITenantOwned
{
    private BomLine()
    {
    }

    internal BomLine(int lineNo, BomLineData data)
    {
        LineNo = lineNo;
        ComponentItemId = data.ComponentItemId;
        UnitId = data.UnitId;
        Quantity = data.Quantity;
        ConversionFactor = data.ConversionFactor;
        StockQuantity = data.StockQuantity;
        LossPercent = data.LossPercent;
        Remark = string.IsNullOrWhiteSpace(data.Remark) ? null : data.Remark.Trim();
    }

    /// <summary>Primary key.</summary>
    public Guid Id { get; private set; } = Guid.CreateVersion7();

    /// <inheritdoc />
    public Guid TenantId { get; private set; }

    /// <summary>Owning version.</summary>
    public Guid BomVersionId { get; private set; }

    /// <summary>Order within the BOM.</summary>
    public int LineNo { get; private set; }

    /// <summary>Component item.</summary>
    public Guid ComponentItemId { get; private set; }

    /// <summary>Unit of the quantity.</summary>
    public Guid UnitId { get; private set; }

    /// <summary>Quantity per batch in the entered unit.</summary>
    public decimal Quantity { get; private set; }

    /// <summary>Multiplier to the component's stock unit.</summary>
    public decimal ConversionFactor { get; private set; }

    /// <summary>Quantity per batch in the component's stock unit.</summary>
    public decimal StockQuantity { get; private set; }

    /// <summary>Expected loss in percent.</summary>
    public decimal LossPercent { get; private set; }

    /// <summary>Remark.</summary>
    public string? Remark { get; private set; }
}

/// <summary>Where an independent demand came from.</summary>
public enum DemandSource
{
    /// <summary>Entered by a planner.</summary>
    Manual,

    /// <summary>Sales forecast.</summary>
    Forecast,

    /// <summary>Sales order line.</summary>
    SalesOrder,
}

/// <summary>State of a demand.</summary>
public enum DemandStatus
{
    /// <summary>Not yet covered by work orders; planned by MRP.</summary>
    Open,

    /// <summary>Covered by work orders.</summary>
    Planned,

    /// <summary>Fulfilled.</summary>
    Closed,

    /// <summary>Cancelled.</summary>
    Cancelled,
}

/// <summary>Independent demand: a quantity of an item needed on a date.</summary>
public sealed class Demand : Entity
{
    private Demand()
    {
    }

    /// <summary>Creates an open demand.</summary>
    public Demand(Guid itemId, decimal quantity, DateOnly dueDate, DemandSource source, string? referenceNo, Guid? customerId, string? remark)
    {
        ItemId = itemId;
        Source = source;
        Update(quantity, dueDate, referenceNo, customerId, remark);
    }

    /// <summary>Item.</summary>
    public Guid ItemId { get; private set; }

    /// <summary>Quantity in stock unit.</summary>
    public decimal Quantity { get; private set; }

    /// <summary>Date the quantity is needed.</summary>
    public DateOnly DueDate { get; private set; }

    /// <summary>Origin.</summary>
    public DemandSource Source { get; private set; }

    /// <summary>Number of the originating document.</summary>
    public string? ReferenceNo { get; private set; }

    /// <summary>Customer, if known.</summary>
    public Guid? CustomerId { get; private set; }

    /// <summary>State.</summary>
    public DemandStatus Status { get; private set; } = DemandStatus.Open;

    /// <summary>Remark.</summary>
    public string? Remark { get; private set; }

    /// <summary>Changes an open demand.</summary>
    public void Update(decimal quantity, DateOnly dueDate, string? referenceNo, Guid? customerId, string? remark)
    {
        if (Status != DemandStatus.Open)
        {
            throw DomainException.Conflict("production.demand.not_editable", $"A demand that is {Status} cannot be edited.");
        }

        if (quantity <= 0)
        {
            throw new DomainException("production.invalid_quantity", "Quantity must be greater than zero.", 400);
        }

        Quantity = quantity;
        DueDate = dueDate;
        ReferenceNo = string.IsNullOrWhiteSpace(referenceNo) ? null : referenceNo.Trim().ToUpperInvariant();
        CustomerId = customerId;
        Remark = string.IsNullOrWhiteSpace(remark) ? null : remark.Trim();
    }

    /// <summary>Sets the status from the quantity covered by work orders.</summary>
    public void ApplyCoverage(decimal coveredQuantity)
    {
        if (Status is DemandStatus.Open or DemandStatus.Planned)
        {
            Status = coveredQuantity >= Quantity ? DemandStatus.Planned : DemandStatus.Open;
        }
    }

    /// <summary>Cancels an open demand.</summary>
    public void Cancel()
    {
        if (Status != DemandStatus.Open)
        {
            throw DomainException.Conflict("production.demand.not_open", $"A demand that is {Status} cannot be cancelled.");
        }

        Status = DemandStatus.Cancelled;
    }

    /// <summary>Marks a demand fulfilled.</summary>
    public void Close()
    {
        if (Status is DemandStatus.Cancelled or DemandStatus.Closed)
        {
            throw DomainException.Conflict("production.demand.not_open", $"A demand that is {Status} cannot be closed.");
        }

        Status = DemandStatus.Closed;
    }
}
