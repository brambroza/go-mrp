using Mrp.SharedKernel.Domain;

namespace Mrp.Production.Domain;

/// <summary>A working shift of the day.</summary>
/// <param name="Start">Shift start.</param>
/// <param name="End">Shift end (after start, same day).</param>
public sealed record Shift(TimeOnly Start, TimeOnly End);

/// <summary>
/// Working time of the factory in local time: working weekdays, shifts per day and holidays.
/// Places durations onto the calendar so that nothing is scheduled outside working hours.
/// </summary>
public sealed class WorkCalendar
{
    private const int MaxLookAheadDays = 400;
    private readonly HashSet<DayOfWeek> _workDays;
    private readonly IReadOnlyList<Shift> _shifts;
    private readonly HashSet<DateOnly> _holidays;

    /// <summary>Creates a calendar.</summary>
    /// <param name="workDays">Working days of the week.</param>
    /// <param name="shifts">Shifts of a working day, non-overlapping.</param>
    /// <param name="holidays">Dates without work.</param>
    public WorkCalendar(IEnumerable<DayOfWeek> workDays, IEnumerable<Shift> shifts, IEnumerable<DateOnly> holidays)
    {
        _workDays = [.. workDays];
        _shifts = shifts.OrderBy(s => s.Start).ToList();
        _holidays = [.. holidays];
        if (_workDays.Count == 0 || _shifts.Count == 0)
        {
            throw new DomainException("production.calendar.empty", "The work calendar needs at least one working day and one shift.");
        }

        for (var i = 0; i < _shifts.Count; i++)
        {
            if (_shifts[i].End <= _shifts[i].Start || (i > 0 && _shifts[i].Start < _shifts[i - 1].End))
            {
                throw new DomainException("production.calendar.invalid_shift", "Shifts must end after they start and must not overlap.");
            }
        }
    }

    /// <summary>Working minutes of one full day.</summary>
    public decimal MinutesPerDay => _shifts.Sum(s => (decimal)(s.End - s.Start).TotalMinutes);

    /// <summary>Whether the date is a working day.</summary>
    public bool IsWorkDay(DateOnly date) => _workDays.Contains(date.DayOfWeek) && !_holidays.Contains(date);

    /// <summary>First working minute at or after <paramref name="moment"/>.</summary>
    public DateTime NextWorkingTime(DateTime moment)
    {
        var date = DateOnly.FromDateTime(moment);
        for (var day = 0; day < MaxLookAheadDays; day++)
        {
            var current = date.AddDays(day);
            if (!IsWorkDay(current))
            {
                continue;
            }

            foreach (var shift in _shifts)
            {
                var start = current.ToDateTime(shift.Start);
                var end = current.ToDateTime(shift.End);
                if (moment < end)
                {
                    return moment > start ? moment : start;
                }
            }
        }

        throw new DomainException("production.calendar.no_working_time", "No working time within 400 days.");
    }

    /// <summary>
    /// Places <paramref name="minutes"/> of work at the first working time at or after <paramref name="earliest"/>,
    /// continuing across shifts and days. Returns the actual start and end.
    /// </summary>
    public (DateTime Start, DateTime End) Place(DateTime earliest, decimal minutes)
    {
        var start = NextWorkingTime(earliest);
        if (minutes <= 0)
        {
            return (start, start);
        }

        var remaining = minutes;
        var cursor = start;
        var date = DateOnly.FromDateTime(cursor);
        for (var day = 0; day < MaxLookAheadDays; day++)
        {
            var current = date.AddDays(day);
            if (!IsWorkDay(current))
            {
                continue;
            }

            foreach (var shift in _shifts)
            {
                var shiftStart = current.ToDateTime(shift.Start);
                var shiftEnd = current.ToDateTime(shift.End);
                var from = cursor > shiftStart ? cursor : shiftStart;
                if (from >= shiftEnd)
                {
                    continue;
                }

                var available = (decimal)(shiftEnd - from).TotalMinutes;
                if (remaining <= available)
                {
                    return (start, from.AddMinutes((double)remaining));
                }

                remaining -= available;
                cursor = shiftEnd;
            }
        }

        throw new DomainException("production.calendar.no_working_time", "The work does not fit within 400 days of working time.");
    }
}

/// <summary>Operation of a routing as scheduler input.</summary>
/// <param name="Seq">Order within the routing, starting at 1.</param>
/// <param name="Name">Operation name.</param>
/// <param name="MachineGroup">Group of machines that can do it.</param>
/// <param name="SetupMinutes">Fixed time before the run.</param>
/// <param name="MinutesPerUnit">Run time per unit of the work order quantity.</param>
public sealed record ScheduleOperation(int Seq, string Name, string MachineGroup, decimal SetupMinutes, decimal MinutesPerUnit)
{
    /// <summary>Total minutes for a quantity, rounded up to whole minutes.</summary>
    public decimal DurationFor(decimal quantity) => Math.Ceiling(SetupMinutes + (MinutesPerUnit * quantity));
}

/// <summary>Work order as scheduler input.</summary>
/// <param name="WorkOrderId">Work order.</param>
/// <param name="DocumentNo">Number, used for deterministic ordering.</param>
/// <param name="Quantity">Quantity to produce.</param>
/// <param name="DueDate">Day the output is needed; the order is late when it ends after this day.</param>
/// <param name="Operations">Operations in sequence; empty when the item has no routing.</param>
/// <param name="DependsOn">Work orders that must finish before this one starts (child orders).</param>
public sealed record ScheduleJob(Guid WorkOrderId, string DocumentNo, decimal Quantity, DateOnly DueDate, IReadOnlyList<ScheduleOperation> Operations, IReadOnlyList<Guid> DependsOn);

/// <summary>Machine as scheduler input.</summary>
/// <param name="MachineId">Machine.</param>
/// <param name="Code">Code, used for deterministic ordering.</param>
/// <param name="Group">Machine group.</param>
/// <param name="Priority">Lower is preferred when two machines are free at the same time.</param>
public sealed record ScheduleMachine(Guid MachineId, string Code, string Group, int Priority);

/// <summary>Time a machine is already taken (locked or started slots).</summary>
public sealed record BusyInterval(Guid MachineId, DateTime Start, DateTime End);

/// <summary>Planned slot of one operation on one machine.</summary>
public sealed record PlannedSlot(Guid WorkOrderId, int Seq, string OperationName, Guid MachineId, DateTime Start, DateTime End);

/// <summary>Schedule outcome of one work order.</summary>
public sealed record JobOutcome(Guid WorkOrderId, DateTime? Start, DateTime? End, bool IsLate);

/// <summary>Something the planner must look at.</summary>
/// <param name="WorkOrderId">Work order.</param>
/// <param name="Code"><c>NO_ROUTING</c>, <c>NO_MACHINE</c>, <c>LATE</c> or <c>CYCLE</c>.</param>
/// <param name="Message">English description.</param>
public sealed record ScheduleException(Guid WorkOrderId, string Code, string Message);

/// <summary>Output of a scheduling run.</summary>
public sealed record ScheduleResult(IReadOnlyList<PlannedSlot> Slots, IReadOnlyList<JobOutcome> Jobs, IReadOnlyList<ScheduleException> Exceptions);

/// <summary>
/// Forward, finite-capacity scheduler: work orders are taken child-before-parent and then by due date;
/// every operation is placed after the previous one on the machine of its group that is free first,
/// inside working time, never overlapping locked or already planned slots. Deterministic.
/// </summary>
public static class Scheduler
{
    /// <summary>Exception code: the item has no routing, so the order could not be placed.</summary>
    public const string NoRouting = "NO_ROUTING";

    /// <summary>Exception code: an operation's machine group has no active machine.</summary>
    public const string NoMachine = "NO_MACHINE";

    /// <summary>Exception code: the order ends after its due date.</summary>
    public const string Late = "LATE";

    /// <summary>Exception code: dependencies form a cycle.</summary>
    public const string Cycle = "CYCLE";

    /// <summary>Runs the schedule from <paramref name="now"/> (local time).</summary>
    /// <param name="now">Earliest moment anything may start.</param>
    /// <param name="calendar">Working time.</param>
    /// <param name="jobs">Work orders to place.</param>
    /// <param name="machines">Active machines.</param>
    /// <param name="busy">Time already taken on machines (locked slots).</param>
    /// <param name="fixedEnds">End times of work orders that are not re-planned (locked); dependants wait for them.</param>
    public static ScheduleResult Run(
        DateTime now,
        WorkCalendar calendar,
        IReadOnlyList<ScheduleJob> jobs,
        IReadOnlyList<ScheduleMachine> machines,
        IReadOnlyList<BusyInterval> busy,
        IReadOnlyDictionary<Guid, DateTime>? fixedEnds = null)
    {
        var slots = new List<PlannedSlot>();
        var outcomes = new List<JobOutcome>();
        var exceptions = new List<ScheduleException>();
        var busyByMachine = machines.ToDictionary(
            m => m.MachineId,
            m => busy.Where(b => b.MachineId == m.MachineId).Select(b => (b.Start, b.End)).OrderBy(b => b.Start).ToList());
        var machinesByGroup = machines.GroupBy(m => m.Group, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.OrderBy(m => m.Priority).ThenBy(m => m.Code, StringComparer.Ordinal).ToList(), StringComparer.OrdinalIgnoreCase);
        var finished = (fixedEnds ?? new Dictionary<Guid, DateTime>()).ToDictionary(f => f.Key, f => (DateTime?)f.Value);
        var pending = jobs.ToDictionary(j => j.WorkOrderId);

        while (pending.Count > 0)
        {
            var ready = pending.Values
                .Where(j => j.DependsOn.All(d => !pending.ContainsKey(d)))
                .OrderBy(j => j.DueDate)
                .ThenBy(j => j.DocumentNo, StringComparer.Ordinal)
                .FirstOrDefault();
            if (ready is null)
            {
                foreach (var job in pending.Values.OrderBy(j => j.DocumentNo, StringComparer.Ordinal))
                {
                    exceptions.Add(new ScheduleException(job.WorkOrderId, Cycle, $"Work order {job.DocumentNo} waits for an order that waits for it."));
                    outcomes.Add(new JobOutcome(job.WorkOrderId, null, null, true));
                }

                break;
            }

            pending.Remove(ready.WorkOrderId);
            var earliest = now;
            foreach (var dependency in ready.DependsOn)
            {
                if (finished.TryGetValue(dependency, out var end) && end is { } dependencyEnd && dependencyEnd > earliest)
                {
                    earliest = dependencyEnd;
                }
            }

            if (ready.Operations.Count == 0)
            {
                exceptions.Add(new ScheduleException(ready.WorkOrderId, NoRouting, $"Work order {ready.DocumentNo}: the item has no routing."));
                outcomes.Add(new JobOutcome(ready.WorkOrderId, null, null, true));
                finished[ready.WorkOrderId] = null;
                continue;
            }

            DateTime? jobStart = null;
            var cursor = earliest;
            var placed = true;
            foreach (var operation in ready.Operations.OrderBy(o => o.Seq))
            {
                if (!machinesByGroup.TryGetValue(operation.MachineGroup, out var candidates) || candidates.Count == 0)
                {
                    exceptions.Add(new ScheduleException(ready.WorkOrderId, NoMachine, $"Work order {ready.DocumentNo}: no active machine in group '{operation.MachineGroup}' for '{operation.Name}'."));
                    placed = false;
                    break;
                }

                var duration = operation.DurationFor(ready.Quantity);
                (DateTime Start, DateTime End, ScheduleMachine Machine)? best = null;
                foreach (var machine in candidates)
                {
                    var (start, end) = PlaceOnMachine(calendar, busyByMachine[machine.MachineId], cursor, duration);
                    if (best is null || end < best.Value.End || (end == best.Value.End && start < best.Value.Start))
                    {
                        best = (start, end, machine);
                    }
                }

                var chosen = best!.Value;
                var intervals = busyByMachine[chosen.Machine.MachineId];
                intervals.Add((chosen.Start, chosen.End));
                intervals.Sort((a, b) => a.Start.CompareTo(b.Start));
                slots.Add(new PlannedSlot(ready.WorkOrderId, operation.Seq, operation.Name, chosen.Machine.MachineId, chosen.Start, chosen.End));
                jobStart ??= chosen.Start;
                cursor = chosen.End;
            }

            if (!placed)
            {
                outcomes.Add(new JobOutcome(ready.WorkOrderId, null, null, true));
                finished[ready.WorkOrderId] = null;
                continue;
            }

            var dueEnd = ready.DueDate.AddDays(1).ToDateTime(TimeOnly.MinValue);
            var late = cursor > dueEnd;
            if (late)
            {
                exceptions.Add(new ScheduleException(ready.WorkOrderId, Late, $"Work order {ready.DocumentNo} ends {Iso(cursor)}, after its due date {Iso(ready.DueDate)}."));
            }

            outcomes.Add(new JobOutcome(ready.WorkOrderId, jobStart, cursor, late));
            finished[ready.WorkOrderId] = cursor;
        }

        return new ScheduleResult(slots, outcomes, exceptions);
    }

    /// <summary>Earliest working-time placement of a duration on a machine that does not overlap its busy intervals.</summary>
    private static (DateTime Start, DateTime End) PlaceOnMachine(WorkCalendar calendar, List<(DateTime Start, DateTime End)> busy, DateTime earliest, decimal minutes)
    {
        var candidate = earliest;
        for (var attempt = 0; attempt < 10_000; attempt++)
        {
            var (start, end) = calendar.Place(candidate, minutes);
            var conflict = busy.FirstOrDefault(b => b.Start < end && start < b.End);
            if (conflict == default)
            {
                return (start, end);
            }

            candidate = conflict.End;
        }

        throw new DomainException("production.schedule.no_capacity", "No free capacity was found for the operation.");
    }

    private static string Iso(DateTime moment) => moment.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture);

    private static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
}
