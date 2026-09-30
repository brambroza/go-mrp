using Mrp.SharedKernel.Domain;

namespace Mrp.Production.Domain;

/// <summary>State of a work order.</summary>
public enum WorkOrderStatus
{
    /// <summary>Created; materials not yet to be issued.</summary>
    Planned,

    /// <summary>Released to the shop floor; materials may be issued.</summary>
    Released,

    /// <summary>Materials issued or output received.</summary>
    InProgress,

    /// <summary>Temporarily stopped.</summary>
    OnHold,

    /// <summary>Output complete.</summary>
    Completed,

    /// <summary>Closed for postings.</summary>
    Closed,

    /// <summary>Cancelled before any issue.</summary>
    Cancelled,
}

/// <summary>Action on a work order.</summary>
public enum WorkOrderAction
{
    /// <summary>Release to the shop floor.</summary>
    Release,

    /// <summary>First posting happened.</summary>
    Start,

    /// <summary>Stop temporarily.</summary>
    Hold,

    /// <summary>Continue after a hold.</summary>
    Resume,

    /// <summary>Output is complete.</summary>
    Complete,

    /// <summary>Output dropped below the ordered quantity again (a receipt was voided).</summary>
    Reopen,

    /// <summary>Close for postings.</summary>
    Close,

    /// <summary>Cancel.</summary>
    Cancel,
}

/// <summary>Where a work order came from.</summary>
public enum WorkOrderSource
{
    /// <summary>Entered by a planner.</summary>
    Manual,

    /// <summary>Converted from an MRP proposal.</summary>
    Mrp,

    /// <summary>Created for a demand.</summary>
    Demand,

    /// <summary>Created for a semi-finished component of another work order.</summary>
    Parent,
}

/// <summary>Work order (production order): make a quantity of an item from the materials of its BOM.</summary>
public sealed class WorkOrder : Entity
{
    /// <summary>Reference type used on warehouse documents that belong to a work order.</summary>
    public const string ReferenceType = "WO";

    private static readonly StateMachine<WorkOrderStatus, WorkOrderAction> Machine = new StateMachine<WorkOrderStatus, WorkOrderAction>()
        .Permit(WorkOrderStatus.Planned, WorkOrderAction.Release, WorkOrderStatus.Released)
        .Permit(WorkOrderStatus.Released, WorkOrderAction.Start, WorkOrderStatus.InProgress)
        .Permit(WorkOrderStatus.Released, WorkOrderAction.Hold, WorkOrderStatus.OnHold)
        .Permit(WorkOrderStatus.InProgress, WorkOrderAction.Hold, WorkOrderStatus.OnHold)
        .Permit(WorkOrderStatus.Released, WorkOrderAction.Complete, WorkOrderStatus.Completed)
        .Permit(WorkOrderStatus.InProgress, WorkOrderAction.Complete, WorkOrderStatus.Completed)
        .Permit(WorkOrderStatus.Completed, WorkOrderAction.Reopen, WorkOrderStatus.InProgress)
        .Permit(WorkOrderStatus.Completed, WorkOrderAction.Close, WorkOrderStatus.Closed)
        .Permit(WorkOrderStatus.Planned, WorkOrderAction.Cancel, WorkOrderStatus.Cancelled)
        .Permit(WorkOrderStatus.Released, WorkOrderAction.Cancel, WorkOrderStatus.Cancelled);

    private readonly List<WorkOrderMaterial> _materials = [];

    private WorkOrder()
    {
    }

    /// <summary>Creates a planned work order with the materials of a BOM explosion.</summary>
    public WorkOrder(
        string documentNo,
        Guid itemId,
        decimal quantity,
        DateOnly startDate,
        DateOnly dueDate,
        Guid bomVersionId,
        IReadOnlyList<ExplodedComponent> materials,
        WorkOrderSource source,
        string? sourceReference,
        Guid? demandId,
        Guid? parentWorkOrderId,
        Guid? warehouseId,
        string? remark)
    {
        if (quantity <= 0)
        {
            throw new DomainException("production.invalid_quantity", "Quantity must be greater than zero.", 400);
        }

        if (dueDate < startDate)
        {
            throw new DomainException("production.workorder.due_before_start", "Due date must not be before the start date.", 400);
        }

        if (materials.Count == 0)
        {
            throw new DomainException("production.workorder.no_materials", "The BOM has no components.");
        }

        DocumentNo = documentNo;
        ItemId = itemId;
        Quantity = quantity;
        StartDate = startDate;
        DueDate = dueDate;
        BomVersionId = bomVersionId;
        Source = source;
        SourceReference = sourceReference;
        DemandId = demandId;
        ParentWorkOrderId = parentWorkOrderId;
        WarehouseId = warehouseId;
        Remark = string.IsNullOrWhiteSpace(remark) ? null : remark.Trim();
        var number = 0;
        foreach (var material in materials)
        {
            _materials.Add(new WorkOrderMaterial(++number, material.ItemId, material.Quantity, material.IsMadeInHouse));
        }
    }

    /// <summary>Document number.</summary>
    public string DocumentNo { get; private set; } = string.Empty;

    /// <summary>Item to produce.</summary>
    public Guid ItemId { get; private set; }

    /// <summary>Ordered quantity in stock unit.</summary>
    public decimal Quantity { get; private set; }

    /// <summary>Quantity received into stock.</summary>
    public decimal ProducedQuantity { get; private set; }

    /// <summary>Planned start; materials are needed on this date.</summary>
    public DateOnly StartDate { get; private set; }

    /// <summary>Date the output is needed.</summary>
    public DateOnly DueDate { get; private set; }

    /// <summary>State.</summary>
    public WorkOrderStatus Status { get; private set; } = WorkOrderStatus.Planned;

    /// <summary>State before the order was put on hold.</summary>
    public WorkOrderStatus? StatusBeforeHold { get; private set; }

    /// <summary>BOM version the materials were taken from.</summary>
    public Guid BomVersionId { get; private set; }

    /// <summary>Work order that consumes the output (for semi-finished goods).</summary>
    public Guid? ParentWorkOrderId { get; private set; }

    /// <summary>Demand the order covers.</summary>
    public Guid? DemandId { get; private set; }

    /// <summary>Origin.</summary>
    public WorkOrderSource Source { get; private set; }

    /// <summary>Number of the MRP run or other origin.</summary>
    public string? SourceReference { get; private set; }

    /// <summary>Warehouse the output goes to.</summary>
    public Guid? WarehouseId { get; private set; }

    /// <summary>Remark.</summary>
    public string? Remark { get; private set; }

    /// <summary>Reason of cancellation or hold.</summary>
    public string? StatusReason { get; private set; }

    /// <summary>Optimistic concurrency token.</summary>
    public uint Version { get; private set; }

    /// <summary>Materials, a snapshot of the BOM at creation.</summary>
    public IReadOnlyList<WorkOrderMaterial> Materials => _materials;

    /// <summary>Whether any material was issued or output received.</summary>
    public bool HasPostings => ProducedQuantity != 0 || _materials.Any(m => m.IssuedQuantity != 0);

    /// <summary>Whether the order still expects output (counts as supply for MRP).</summary>
    public bool IsOpen => Status is WorkOrderStatus.Planned or WorkOrderStatus.Released or WorkOrderStatus.InProgress or WorkOrderStatus.OnHold;

    /// <summary>Output still expected.</summary>
    public decimal OutstandingQuantity => IsOpen ? Math.Max(Quantity - ProducedQuantity, 0m) : 0m;

    /// <summary>Applies a user action.</summary>
    public void Apply(WorkOrderAction action, string? reason = null)
    {
        switch (action)
        {
            case WorkOrderAction.Hold:
                StatusBeforeHold = Status;
                Status = Machine.Fire(Status, action);
                StatusReason = Clean(reason);
                break;
            case WorkOrderAction.Resume:
                if (Status != WorkOrderStatus.OnHold || StatusBeforeHold is not { } previous)
                {
                    throw DomainException.Conflict("common.invalid_transition", $"Action '{action}' is not allowed while the document is '{Status}'.");
                }

                Status = previous;
                StatusBeforeHold = null;
                StatusReason = null;
                break;
            case WorkOrderAction.Cancel:
                if (HasPostings)
                {
                    throw DomainException.Conflict("production.workorder.has_postings", "A work order with issued materials or output cannot be cancelled.");
                }

                if (string.IsNullOrWhiteSpace(reason))
                {
                    throw new DomainException("production.workorder.reason_required", "A reason is required.", 400);
                }

                Status = Machine.Fire(Status, action);
                StatusReason = Clean(reason);
                break;
            default:
                Status = Machine.Fire(Status, action);
                break;
        }
    }

    /// <summary>Links a material line to the work order that produces the component.</summary>
    public void LinkChild(Guid componentItemId, Guid childWorkOrderId) =>
        _materials.First(m => m.ComponentItemId == componentItemId).SetChild(childWorkOrderId);

    /// <summary>Adds issued quantity of a material (negative when an issue is reversed).</summary>
    /// <param name="componentItemId">Issued item.</param>
    /// <param name="stockQuantity">Quantity in stock unit.</param>
    /// <param name="overIssuePercent">Tolerance above the requirement in percent.</param>
    /// <param name="itemCode">Item code for messages.</param>
    public void ApplyIssue(Guid componentItemId, decimal stockQuantity, decimal overIssuePercent, string itemCode)
    {
        var material = _materials.FirstOrDefault(m => m.ComponentItemId == componentItemId)
            ?? throw new DomainException("production.workorder.not_a_material", $"Item {itemCode} is not a material of work order {DocumentNo}.");
        if (stockQuantity > 0)
        {
            EnsureOpenForPosting();
        }

        material.AddIssued(stockQuantity, overIssuePercent, itemCode, DocumentNo);
        if (Status == WorkOrderStatus.Released && stockQuantity > 0)
        {
            Status = Machine.Fire(Status, WorkOrderAction.Start);
        }
    }

    /// <summary>Adds output received into stock (negative when a receipt is reversed).</summary>
    public void ApplyOutput(Guid itemId, decimal stockQuantity, string itemCode)
    {
        if (itemId != ItemId)
        {
            throw new DomainException("production.workorder.not_the_output", $"Item {itemCode} is not the output of work order {DocumentNo}.");
        }

        if (stockQuantity > 0)
        {
            EnsureOpenForPosting();
        }

        var result = ProducedQuantity + stockQuantity;
        if (result < 0)
        {
            throw DomainException.Conflict("production.workorder.negative_output", "Reversed output exceeds the received output.");
        }

        ProducedQuantity = result;
        if (Status == WorkOrderStatus.Released && stockQuantity > 0)
        {
            Status = Machine.Fire(Status, WorkOrderAction.Start);
        }

        if (Status == WorkOrderStatus.InProgress && ProducedQuantity >= Quantity)
        {
            Status = Machine.Fire(Status, WorkOrderAction.Complete);
        }
        else if (Status == WorkOrderStatus.Completed && ProducedQuantity < Quantity)
        {
            Status = Machine.Fire(Status, WorkOrderAction.Reopen);
        }
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim() is { Length: > 500 } text ? text[..500] : value.Trim();

    private void EnsureOpenForPosting()
    {
        if (Status is not (WorkOrderStatus.Released or WorkOrderStatus.InProgress))
        {
            throw DomainException.Conflict(
                "production.workorder.not_released",
                $"Work order {DocumentNo} is {Status}; it must be released before stock can be posted.");
        }
    }
}

/// <summary>Material requirement of a work order.</summary>
public sealed class WorkOrderMaterial : ITenantOwned
{
    private WorkOrderMaterial()
    {
    }

    internal WorkOrderMaterial(int lineNo, Guid componentItemId, decimal requiredQuantity, bool isMadeInHouse)
    {
        LineNo = lineNo;
        ComponentItemId = componentItemId;
        RequiredQuantity = requiredQuantity;
        IsMadeInHouse = isMadeInHouse;
    }

    /// <summary>Primary key.</summary>
    public Guid Id { get; private set; } = Guid.CreateVersion7();

    /// <inheritdoc />
    public Guid TenantId { get; private set; }

    /// <summary>Owning work order.</summary>
    public Guid WorkOrderId { get; private set; }

    /// <summary>Order within the work order.</summary>
    public int LineNo { get; private set; }

    /// <summary>Component item.</summary>
    public Guid ComponentItemId { get; private set; }

    /// <summary>Required quantity including loss, in stock unit.</summary>
    public decimal RequiredQuantity { get; private set; }

    /// <summary>Net issued quantity.</summary>
    public decimal IssuedQuantity { get; private set; }

    /// <summary>The component is produced in house (semi-finished / bulk).</summary>
    public bool IsMadeInHouse { get; private set; }

    /// <summary>Work order that produces the component.</summary>
    public Guid? ChildWorkOrderId { get; private set; }

    /// <summary>Quantity still to issue.</summary>
    public decimal OutstandingQuantity => Math.Max(RequiredQuantity - IssuedQuantity, 0m);

    internal void SetChild(Guid childWorkOrderId) => ChildWorkOrderId = childWorkOrderId;

    internal void AddIssued(decimal stockQuantity, decimal overIssuePercent, string itemCode, string documentNo)
    {
        var result = IssuedQuantity + stockQuantity;
        if (result < 0)
        {
            throw DomainException.Conflict("production.workorder.negative_issue", "Reversed quantity exceeds the issued quantity.");
        }

        var limit = Math.Round(RequiredQuantity * (1m + (overIssuePercent / 100m)), 6, MidpointRounding.AwayFromZero);
        if (stockQuantity > 0 && result > limit)
        {
            throw new DomainException(
                "production.workorder.over_issue",
                $"Issuing {result:0.######} of {itemCode} exceeds the requirement {limit:0.######} of work order {documentNo}.");
        }

        IssuedQuantity = result;
    }
}
