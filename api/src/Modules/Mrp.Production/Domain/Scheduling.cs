using Mrp.SharedKernel.Domain;

namespace Mrp.Production.Domain;

/// <summary>A machine or work centre that operations are scheduled on.</summary>
public sealed class Machine : Entity
{
    private Machine()
    {
    }

    /// <summary>Creates a machine.</summary>
    public Machine(string code, string name, string machineGroup, int priority, bool isActive)
    {
        Update(code, name, machineGroup, priority, isActive);
    }

    /// <summary>Upper-case code, unique per tenant.</summary>
    public string Code { get; private set; } = string.Empty;

    /// <summary>Display name.</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>Group the routing operations refer to (e.g. <c>MIX</c>, <c>FILL</c>); free text per tenant.</summary>
    public string MachineGroup { get; private set; } = string.Empty;

    /// <summary>Lower is preferred when several machines of the group are free at the same time.</summary>
    public int Priority { get; private set; }

    /// <summary>Inactive machines get no new slots.</summary>
    public bool IsActive { get; private set; } = true;

    /// <summary>Changes the machine.</summary>
    public void Update(string code, string name, string machineGroup, int priority, bool isActive)
    {
        var normalizedCode = (code ?? string.Empty).Trim().ToUpperInvariant();
        var normalizedGroup = (machineGroup ?? string.Empty).Trim().ToUpperInvariant();
        if (normalizedCode.Length is 0 or > 40 || normalizedCode.Any(char.IsWhiteSpace))
        {
            throw new DomainException("production.machine.invalid_code", "Code must be 1-40 characters without spaces.", 400);
        }

        if (string.IsNullOrWhiteSpace(name) || normalizedGroup.Length is 0 or > 40)
        {
            throw new DomainException("production.machine.invalid", "Name and machine group are required.", 400);
        }

        Code = normalizedCode;
        Name = name.Trim();
        MachineGroup = normalizedGroup;
        Priority = priority;
        IsActive = isActive;
    }
}

/// <summary>Operation values of a routing.</summary>
public sealed record RoutingOperationData(string Name, string MachineGroup, decimal SetupMinutes, decimal MinutesPerUnit);

/// <summary>The sequence of operations that makes an item. One routing per item.</summary>
public sealed class Routing : Entity
{
    private readonly List<RoutingOperation> _operations = [];

    private Routing()
    {
    }

    /// <summary>Creates a routing for an item.</summary>
    public Routing(Guid itemId)
    {
        ItemId = itemId;
    }

    /// <summary>Item that is produced.</summary>
    public Guid ItemId { get; private set; }

    /// <summary>Operations in sequence.</summary>
    public IReadOnlyList<RoutingOperation> Operations => _operations;

    /// <summary>Replaces the operations; they are renumbered 1..n in the given order.</summary>
    public void Replace(IReadOnlyList<RoutingOperationData> operations)
    {
        if (operations.Count == 0)
        {
            throw new DomainException("production.routing.no_operations", "A routing needs at least one operation.", 400);
        }

        _operations.Clear();
        var seq = 0;
        foreach (var operation in operations)
        {
            seq++;
            if (string.IsNullOrWhiteSpace(operation.Name) || string.IsNullOrWhiteSpace(operation.MachineGroup))
            {
                throw new DomainException("production.routing.invalid_operation", $"Operation {seq}: name and machine group are required.", 400);
            }

            if (operation.SetupMinutes < 0 || operation.MinutesPerUnit < 0 || (operation.SetupMinutes == 0 && operation.MinutesPerUnit == 0))
            {
                throw new DomainException("production.routing.invalid_time", $"Operation {seq}: times must not be negative and the operation must take time.", 400);
            }

            _operations.Add(new RoutingOperation(seq, operation));
        }
    }

    /// <summary>The routing as scheduler input.</summary>
    public IReadOnlyList<ScheduleOperation> ToScheduleOperations() =>
        _operations.OrderBy(o => o.Seq).Select(o => new ScheduleOperation(o.Seq, o.Name, o.MachineGroup, o.SetupMinutes, o.MinutesPerUnit)).ToList();
}

/// <summary>Operation of a routing.</summary>
public sealed class RoutingOperation : ITenantOwned
{
    private RoutingOperation()
    {
    }

    internal RoutingOperation(int seq, RoutingOperationData data)
    {
        Seq = seq;
        Name = data.Name.Trim();
        MachineGroup = data.MachineGroup.Trim().ToUpperInvariant();
        SetupMinutes = data.SetupMinutes;
        MinutesPerUnit = data.MinutesPerUnit;
    }

    /// <summary>Primary key.</summary>
    public Guid Id { get; private set; } = Guid.CreateVersion7();

    /// <inheritdoc />
    public Guid TenantId { get; private set; }

    /// <summary>Owning routing.</summary>
    public Guid RoutingId { get; private set; }

    /// <summary>Order, starting at 1.</summary>
    public int Seq { get; private set; }

    /// <summary>Operation name (e.g. ผสม, บรรจุ).</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>Machine group that can do the operation.</summary>
    public string MachineGroup { get; private set; } = string.Empty;

    /// <summary>Fixed time before the run, in minutes.</summary>
    public decimal SetupMinutes { get; private set; }

    /// <summary>Run time per unit of the work order quantity, in minutes.</summary>
    public decimal MinutesPerUnit { get; private set; }
}

/// <summary>A day without production.</summary>
public sealed class Holiday : Entity
{
    private Holiday()
    {
    }

    /// <summary>Creates a holiday.</summary>
    public Holiday(DateOnly date, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("production.holiday.name_required", "Name is required.", 400);
        }

        Date = date;
        Name = name.Trim();
    }

    /// <summary>Date.</summary>
    public DateOnly Date { get; private set; }

    /// <summary>Name.</summary>
    public string Name { get; private set; } = string.Empty;
}

/// <summary>State of a schedule slot.</summary>
public enum ScheduleSlotStatus
{
    /// <summary>Produced by the scheduler; replaced on the next run.</summary>
    Planned,

    /// <summary>Fixed by a planner; the scheduler plans around it.</summary>
    Locked,
}

/// <summary>One operation of a work order placed on a machine.</summary>
public sealed class ScheduleSlot : Entity
{
    private ScheduleSlot()
    {
    }

    /// <summary>Creates a planned slot.</summary>
    public ScheduleSlot(Guid workOrderId, int seq, string operationName, Guid machineId, DateTimeOffset startAt, DateTimeOffset endAt, string runNo)
    {
        if (endAt < startAt)
        {
            throw new DomainException("production.schedule.invalid_slot", "A slot must end after it starts.", 400);
        }

        WorkOrderId = workOrderId;
        Seq = seq;
        OperationName = operationName;
        MachineId = machineId;
        StartAt = startAt;
        EndAt = endAt;
        RunNo = runNo;
    }

    /// <summary>Work order.</summary>
    public Guid WorkOrderId { get; private set; }

    /// <summary>Operation sequence within the routing.</summary>
    public int Seq { get; private set; }

    /// <summary>Operation name at planning time.</summary>
    public string OperationName { get; private set; } = string.Empty;

    /// <summary>Machine.</summary>
    public Guid MachineId { get; private set; }

    /// <summary>Start (UTC).</summary>
    public DateTimeOffset StartAt { get; private set; }

    /// <summary>End (UTC).</summary>
    public DateTimeOffset EndAt { get; private set; }

    /// <summary>State.</summary>
    public ScheduleSlotStatus Status { get; private set; } = ScheduleSlotStatus.Planned;

    /// <summary>Identifier of the scheduling run that produced the slot.</summary>
    public string RunNo { get; private set; } = string.Empty;

    /// <summary>Fixes the slot so later runs plan around it.</summary>
    public void Lock() => Status = ScheduleSlotStatus.Locked;

    /// <summary>Lets later runs move the slot again.</summary>
    public void Unlock() => Status = ScheduleSlotStatus.Planned;
}
