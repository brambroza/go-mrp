using Mrp.Production.Domain;
using Mrp.SharedKernel.Domain;

namespace Mrp.Domain.Tests.Production;

/// <summary>Working time: Monday–Friday, 08:00–12:00 and 13:00–17:00 (two shifts of 240 minutes), 2026-10-13 is a holiday.</summary>
public sealed class WorkCalendarTests
{
    private static readonly WorkCalendar Calendar = new(
        [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday],
        [new Shift(new TimeOnly(8, 0), new TimeOnly(12, 0)), new Shift(new TimeOnly(13, 0), new TimeOnly(17, 0))],
        [new DateOnly(2026, 10, 13)]);

    private static DateTime At(int day, int hour, int minute = 0) => new(2026, 10, day, hour, minute, 0);

    [Fact]
    public void Working_minutes_per_day()
    {
        Assert.Equal(480m, Calendar.MinutesPerDay);
    }

    [Theory]
    [InlineData(5, 7, 30, 5, 8, 0)]   // before the first shift → shift start
    [InlineData(5, 12, 30, 5, 13, 0)] // lunch → second shift
    [InlineData(5, 17, 30, 6, 8, 0)]  // after work → next day
    [InlineData(10, 9, 0, 12, 8, 0)]  // Saturday → Monday
    [InlineData(13, 9, 0, 14, 8, 0)]  // holiday → next working day
    [InlineData(5, 9, 15, 5, 9, 15)]  // inside a shift → unchanged
    public void Next_working_time(int day, int hour, int minute, int expectedDay, int expectedHour, int expectedMinute)
    {
        Assert.Equal(At(expectedDay, expectedHour, expectedMinute), Calendar.NextWorkingTime(At(day, hour, minute)));
    }

    [Fact]
    public void Work_spans_lunch_days_and_weekends()
    {
        // Friday 16:00 + 120 min: 60 min on Friday, 60 min on Monday morning.
        Assert.Equal((At(9, 16, 0), At(12, 9, 0)), Calendar.Place(At(9, 16, 0), 120m));
        // Monday 11:00 + 90 min: 60 min before lunch, 30 min after.
        Assert.Equal((At(5, 11, 0), At(5, 13, 30)), Calendar.Place(At(5, 11, 0), 90m));
        // A full day exactly fills the two shifts.
        Assert.Equal((At(5, 8, 0), At(5, 17, 0)), Calendar.Place(At(5, 8, 0), 480m));
        // Zero minutes snaps to working time and takes no time.
        Assert.Equal((At(5, 8, 0), At(5, 8, 0)), Calendar.Place(At(5, 6, 0), 0m));
    }

    [Fact]
    public void Shifts_must_not_overlap()
    {
        Assert.Throws<DomainException>(() => new WorkCalendar(
            [DayOfWeek.Monday],
            [new Shift(new TimeOnly(8, 0), new TimeOnly(13, 0)), new Shift(new TimeOnly(12, 0), new TimeOnly(17, 0))],
            []));
    }
}

/// <summary>Golden cases for the finite-capacity scheduler on the calendar of <see cref="WorkCalendarTests"/>.</summary>
public sealed class SchedulerTests
{
    private static readonly WorkCalendar Calendar = new(
        [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday],
        [new Shift(new TimeOnly(8, 0), new TimeOnly(12, 0)), new Shift(new TimeOnly(13, 0), new TimeOnly(17, 0))],
        []);

    private static readonly DateTime Now = new(2026, 10, 5, 8, 0, 0); // Monday
    private static readonly Guid Mix1 = Guid.NewGuid();
    private static readonly Guid Mix2 = Guid.NewGuid();
    private static readonly Guid Fill1 = Guid.NewGuid();

    private static readonly ScheduleMachine[] Machines =
    [
        new(Mix1, "MIX-01", "MIX", 1),
        new(Mix2, "MIX-02", "MIX", 2),
        new(Fill1, "FILL-01", "FILL", 1),
    ];

    private static readonly ScheduleOperation[] MixThenFill =
    [
        new(1, "ผสม", "MIX", SetupMinutes: 30, MinutesPerUnit: 1),
        new(2, "บรรจุ", "FILL", SetupMinutes: 15, MinutesPerUnit: 0.5m),
    ];

    private static ScheduleJob Job(string no, decimal quantity, DateOnly due, IReadOnlyList<ScheduleOperation>? operations = null, params Guid[] dependsOn) =>
        new(Guid.NewGuid(), no, quantity, due, operations ?? MixThenFill, dependsOn);

    private static DateTime At(int day, int hour, int minute = 0) => new(2026, 10, day, hour, minute, 0);

    [Fact]
    public void Operations_follow_each_other_and_span_lunch()
    {
        // Mix 30 + 200 = 230 min: 08:00–11:50. Fill 15 + 100 = 115 min: 11:50–12:00 (10) + 13:00–14:45 (105).
        var job = Job("WO-1", 200m, new DateOnly(2026, 10, 9));

        var result = Scheduler.Run(Now, Calendar, [job], Machines, []);

        Assert.Equal(
        [
            (1, Mix1, At(5, 8, 0), At(5, 11, 50)),
            (2, Fill1, At(5, 11, 50), At(5, 14, 45)),
        ],
        result.Slots.Select(s => (s.Seq, s.MachineId, s.Start, s.End)));
        var outcome = Assert.Single(result.Jobs);
        Assert.Equal((At(5, 8, 0), At(5, 14, 45), false), (outcome.Start, outcome.End, outcome.IsLate));
        Assert.Empty(result.Exceptions);
    }

    [Fact]
    public void Second_order_takes_the_free_machine_of_the_group()
    {
        var first = Job("WO-1", 200m, new DateOnly(2026, 10, 9));
        var second = Job("WO-2", 100m, new DateOnly(2026, 10, 9));

        var result = Scheduler.Run(Now, Calendar, [first, second], Machines, []);

        var mixOfSecond = result.Slots.Single(s => s.WorkOrderId == second.WorkOrderId && s.Seq == 1);
        Assert.Equal((Mix2, At(5, 8, 0), At(5, 10, 10)), (mixOfSecond.MachineId, mixOfSecond.Start, mixOfSecond.End));
        // The single filling line is free until the first order's fill starts at 11:50, so the second order's fill (65 min) slips in before it.
        var fillOfSecond = result.Slots.Single(s => s.WorkOrderId == second.WorkOrderId && s.Seq == 2);
        Assert.Equal((Fill1, At(5, 10, 10), At(5, 11, 15)), (fillOfSecond.MachineId, fillOfSecond.Start, fillOfSecond.End));
    }

    [Fact]
    public void Earlier_due_date_is_scheduled_first_regardless_of_input_order()
    {
        var later = Job("WO-1", 200m, new DateOnly(2026, 10, 20));
        var urgent = Job("WO-2", 200m, new DateOnly(2026, 10, 6));

        var result = Scheduler.Run(Now, Calendar, [later, urgent], [Machines[0], Machines[2]], []);

        Assert.Equal(urgent.WorkOrderId, result.Slots.OrderBy(s => s.Start).First().WorkOrderId);
    }

    [Fact]
    public void Child_order_finishes_before_the_parent_starts()
    {
        var bulk = Job("WO-BULK", 100m, new DateOnly(2026, 10, 9), [new ScheduleOperation(1, "ผสม", "MIX", 30, 1)]);
        var jar = Job("WO-JAR", 100m, new DateOnly(2026, 10, 9), [new ScheduleOperation(1, "บรรจุ", "FILL", 15, 0.5m)], bulk.WorkOrderId);

        var result = Scheduler.Run(Now, Calendar, [jar, bulk], Machines, []);

        var bulkEnd = result.Jobs.Single(j => j.WorkOrderId == bulk.WorkOrderId).End;
        var jarStart = result.Jobs.Single(j => j.WorkOrderId == jar.WorkOrderId).Start;
        Assert.Equal(At(5, 10, 10), bulkEnd);
        Assert.Equal(bulkEnd, jarStart);
    }

    [Fact]
    public void Locked_slot_is_planned_around()
    {
        var job = Job("WO-1", 200m, new DateOnly(2026, 10, 9), [new ScheduleOperation(1, "ผสม", "MIX", 30, 1)]);
        BusyInterval[] busy = [new(Mix1, At(5, 8, 0), At(5, 12, 0)), new(Mix2, At(5, 8, 0), At(5, 17, 0))];

        var result = Scheduler.Run(Now, Calendar, [job], Machines, busy);

        var slot = Assert.Single(result.Slots);
        // MIX-01 frees at 12:00 (lunch) → 13:00–16:50; MIX-02 only frees Tuesday, so MIX-01 wins.
        Assert.Equal((Mix1, At(5, 13, 0), At(5, 16, 50)), (slot.MachineId, slot.Start, slot.End));
    }

    [Fact]
    public void Order_that_ends_after_its_due_date_is_late()
    {
        var job = Job("WO-1", 2000m, new DateOnly(2026, 10, 5), [new ScheduleOperation(1, "ผสม", "MIX", 0, 1)]);

        var result = Scheduler.Run(Now, Calendar, [job], Machines, []);

        // 2,000 minutes = 4 days + 80 minutes → ends Friday 09:20.
        Assert.Equal(At(9, 9, 20), result.Jobs.Single().End);
        Assert.True(result.Jobs.Single().IsLate);
        Assert.Contains(result.Exceptions, e => e.Code == Scheduler.Late);
    }

    [Fact]
    public void Missing_routing_or_machine_is_reported_and_other_orders_still_run()
    {
        var noRouting = Job("WO-1", 10m, new DateOnly(2026, 10, 9), []);
        var noMachine = Job("WO-2", 10m, new DateOnly(2026, 10, 9), [new ScheduleOperation(1, "ตัด", "CUT", 10, 1)]);
        var fine = Job("WO-3", 10m, new DateOnly(2026, 10, 9));

        var result = Scheduler.Run(Now, Calendar, [noRouting, noMachine, fine], Machines, []);

        Assert.Contains(result.Exceptions, e => e.WorkOrderId == noRouting.WorkOrderId && e.Code == Scheduler.NoRouting);
        Assert.Contains(result.Exceptions, e => e.WorkOrderId == noMachine.WorkOrderId && e.Code == Scheduler.NoMachine);
        Assert.Equal(2, result.Slots.Count(s => s.WorkOrderId == fine.WorkOrderId));
        Assert.Equal(3, result.Jobs.Count);
    }

    [Fact]
    public void Dependency_cycle_is_reported()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        ScheduleJob[] jobs =
        [
            new(a, "WO-A", 1m, new DateOnly(2026, 10, 9), MixThenFill, [b]),
            new(b, "WO-B", 1m, new DateOnly(2026, 10, 9), MixThenFill, [a]),
        ];

        var result = Scheduler.Run(Now, Calendar, jobs, Machines, []);

        Assert.Empty(result.Slots);
        Assert.Equal(2, result.Exceptions.Count(e => e.Code == Scheduler.Cycle));
    }

    [Fact]
    public void Result_is_deterministic()
    {
        ScheduleJob[] jobs = [Job("WO-1", 120m, new DateOnly(2026, 10, 9)), Job("WO-2", 80m, new DateOnly(2026, 10, 9)), Job("WO-3", 300m, new DateOnly(2026, 10, 8))];

        var first = Scheduler.Run(Now, Calendar, jobs, Machines, []);
        var second = Scheduler.Run(Now, Calendar, jobs.Reverse().ToArray(), Machines.Reverse().ToArray(), []);

        Assert.Equal(first.Slots, second.Slots);
    }

    [Fact]
    public void Duration_rounds_up_to_whole_minutes()
    {
        Assert.Equal(16m, new ScheduleOperation(1, "x", "MIX", 15m, 0.5m).DurationFor(1m));
        Assert.Equal(115m, new ScheduleOperation(1, "x", "FILL", 15m, 0.5m).DurationFor(200m));
    }
}
