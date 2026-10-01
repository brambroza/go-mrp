using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Mrp.Masters.Application;
using Mrp.Production.Domain;
using Mrp.Production.Persistence;
using Mrp.SharedKernel.Domain;
using Mrp.SharedKernel.Persistence;

namespace Mrp.Production.Application;

/// <summary>Machines, routings, holidays and the finite-capacity schedule of work orders.</summary>
public sealed class SchedulingService(ProductionDbContext db, DbSession session, MasterData masters, ITenantSettings settings, TimeProvider clock)
{
    private static readonly int[] DefaultWorkDays = [1, 2, 3, 4, 5];
    private static readonly ShiftSetting[] DefaultShifts = [new("08:00", "12:00"), new("13:00", "17:00")];

    /// <summary>Shift as stored in the tenant setting.</summary>
    public sealed record ShiftSetting(string Start, string End);

    /// <summary>All machines.</summary>
    public async Task<IReadOnlyList<MachineDto>> ListMachinesAsync(CancellationToken cancellationToken) =>
        await db.Machines.AsNoTracking().OrderBy(m => m.MachineGroup).ThenBy(m => m.Priority).ThenBy(m => m.Code)
            .Select(m => new MachineDto(m.Id, m.Code, m.Name, m.MachineGroup, m.Priority, m.IsActive))
            .ToListAsync(cancellationToken);

    /// <summary>Creates a machine.</summary>
    public async Task<MachineDto> CreateMachineAsync(SaveMachineRequest request, CancellationToken cancellationToken)
    {
        var machine = new Machine(request.Code, request.Name, request.MachineGroup, request.Priority, request.IsActive);
        db.Machines.Add(machine);
        await db.SaveChangesAsync(cancellationToken);
        return ToDto(machine);
    }

    /// <summary>Updates a machine.</summary>
    public async Task<MachineDto> UpdateMachineAsync(Guid id, SaveMachineRequest request, CancellationToken cancellationToken)
    {
        var machine = await db.Machines.FirstOrDefaultAsync(m => m.Id == id, cancellationToken) ?? throw DomainException.NotFound("Machine", id);
        machine.Update(request.Code, request.Name, request.MachineGroup, request.Priority, request.IsActive);
        await db.SaveChangesAsync(cancellationToken);
        return ToDto(machine);
    }

    /// <summary>Routing of an item; empty operations when none is defined.</summary>
    public async Task<RoutingDto> GetRoutingAsync(Guid itemId, CancellationToken cancellationToken)
    {
        var item = (await masters.GetItemsAsync([itemId], requireActive: false, cancellationToken))[itemId];
        var routing = await db.Routings.AsNoTracking().Include(r => r.Operations).FirstOrDefaultAsync(r => r.ItemId == itemId, cancellationToken);
        return new RoutingDto(routing?.Id, itemId, item.Code, item.Name, ToDto(routing));
    }

    /// <summary>Creates or replaces the routing of an item.</summary>
    public Task<RoutingDto> SaveRoutingAsync(Guid itemId, SaveRoutingRequest request, CancellationToken cancellationToken) =>
        session.ExecuteInTransactionAsync(
            async ct =>
            {
                var item = (await masters.GetItemsAsync([itemId], requireActive: true, ct))[itemId];
                var routing = await db.Routings.Include(r => r.Operations).FirstOrDefaultAsync(r => r.ItemId == itemId, ct);
                if (routing is null)
                {
                    routing = new Routing(itemId);
                    db.Routings.Add(routing);
                }

                routing.Replace(request.Operations.Select(o => new RoutingOperationData(o.Name, o.MachineGroup, o.SetupMinutes, o.MinutesPerUnit)).ToList());
                await db.SaveChangesAsync(ct);
                return new RoutingDto(routing.Id, itemId, item.Code, item.Name, ToDto(routing));
            },
            cancellationToken);

    /// <summary>Holidays from a date, ordered.</summary>
    public async Task<IReadOnlyList<HolidayDto>> ListHolidaysAsync(DateOnly? from, CancellationToken cancellationToken)
    {
        var query = db.Holidays.AsNoTracking();
        if (from is { } start)
        {
            query = query.Where(h => h.Date >= start);
        }

        return await query.OrderBy(h => h.Date).Take(500).Select(h => new HolidayDto(h.Id, h.Date, h.Name)).ToListAsync(cancellationToken);
    }

    /// <summary>Adds a holiday.</summary>
    public async Task<HolidayDto> CreateHolidayAsync(SaveHolidayRequest request, CancellationToken cancellationToken)
    {
        var holiday = new Holiday(request.Date, request.Name);
        db.Holidays.Add(holiday);
        await db.SaveChangesAsync(cancellationToken);
        return new HolidayDto(holiday.Id, holiday.Date, holiday.Name);
    }

    /// <summary>Removes a holiday.</summary>
    public async Task DeleteHolidayAsync(Guid id, CancellationToken cancellationToken)
    {
        var holiday = await db.Holidays.FirstOrDefaultAsync(h => h.Id == id, cancellationToken) ?? throw DomainException.NotFound("Holiday", id);
        db.Holidays.Remove(holiday);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Schedules every open work order due inside the horizon. Planned slots are replaced; locked slots
    /// stay where they are and the scheduler plans around them.
    /// </summary>
    public Task<ScheduleRunDto> RunAsync(RunScheduleRequest request, CancellationToken cancellationToken) =>
        session.ExecuteInTransactionAsync(
            async ct =>
            {
                var zone = await settings.GetTimeZoneAsync(ct);
                var utcNow = clock.GetUtcNow();
                var now = TimeZoneInfo.ConvertTime(utcNow, zone).DateTime;
                var horizon = DateOnly.FromDateTime(now).AddDays(request.HorizonDays);
                var calendar = await LoadCalendarAsync(DateOnly.FromDateTime(now), ct);
                var runNo = "SCH-" + utcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);

                var orders = await db.WorkOrders.AsNoTracking()
                    .Where(w => (w.Status == WorkOrderStatus.Planned || w.Status == WorkOrderStatus.Released || w.Status == WorkOrderStatus.InProgress)
                        && w.DueDate <= horizon)
                    .OrderBy(w => w.DueDate).ThenBy(w => w.DocumentNo)
                    .ToListAsync(ct);
                var orderIds = orders.Select(o => o.Id).ToHashSet();
                var itemIds = orders.Select(o => o.ItemId).Distinct().ToList();
                var routings = await db.Routings.AsNoTracking().Include(r => r.Operations)
                    .Where(r => itemIds.Contains(r.ItemId)).ToDictionaryAsync(r => r.ItemId, ct);
                var machines = await db.Machines.AsNoTracking().Where(m => m.IsActive).ToListAsync(ct);
                var lockedSlots = await db.ScheduleSlots.AsNoTracking()
                    .Where(s => s.Status == ScheduleSlotStatus.Locked && s.EndAt > utcNow.AddDays(-1))
                    .ToListAsync(ct);

                var jobs = orders.Select(o => new ScheduleJob(
                    o.Id, o.DocumentNo, o.Quantity, o.DueDate,
                    routings.TryGetValue(o.ItemId, out var routing) ? routing.ToScheduleOperations() : [],
                    orders.Where(c => c.ParentWorkOrderId == o.Id).Select(c => c.Id).ToList())).ToList();
                var scheduleMachines = machines.Select(m => new ScheduleMachine(m.Id, m.Code, m.MachineGroup, m.Priority)).ToList();
                var busy = lockedSlots.Select(s => new BusyInterval(s.MachineId, ToLocal(s.StartAt, zone), ToLocal(s.EndAt, zone))).ToList();

                // Orders whose slots are locked keep them; they are not re-planned, but orders depending on them wait for their end.
                var lockedOrders = lockedSlots.Select(s => s.WorkOrderId).ToHashSet();
                var fixedEnds = lockedSlots.GroupBy(s => s.WorkOrderId).ToDictionary(g => g.Key, g => ToLocal(g.Max(s => s.EndAt), zone));
                var result = Scheduler.Run(now, calendar, jobs.Where(j => !lockedOrders.Contains(j.WorkOrderId)).ToList(), scheduleMachines, busy, fixedEnds);

                await db.ScheduleSlots
                    .Where(s => s.Status == ScheduleSlotStatus.Planned && orderIds.Contains(s.WorkOrderId))
                    .ExecuteDeleteAsync(ct);
                db.ScheduleSlots.AddRange(result.Slots.Select(s => new ScheduleSlot(
                    s.WorkOrderId, s.Seq, s.OperationName, s.MachineId, ToUtc(s.Start, zone), ToUtc(s.End, zone), runNo)));
                await db.SaveChangesAsync(ct);

                var items = await masters.GetItemsAsync(itemIds, requireActive: false, ct);
                var byId = orders.ToDictionary(o => o.Id);
                return new ScheduleRunDto(
                    runNo, utcNow, result.Jobs.Count, result.Slots.Count,
                    result.Jobs.OrderBy(j => byId[j.WorkOrderId].DueDate).ThenBy(j => byId[j.WorkOrderId].DocumentNo, StringComparer.Ordinal)
                        .Select(j => new ScheduleJobDto(
                            j.WorkOrderId, byId[j.WorkOrderId].DocumentNo, items[byId[j.WorkOrderId].ItemId].Code, byId[j.WorkOrderId].DueDate,
                            j.Start is { } start ? ToUtc(start, zone) : null, j.End is { } end ? ToUtc(end, zone) : null, j.IsLate)).ToList(),
                    result.Exceptions.Select(e => new ScheduleExceptionDto(e.WorkOrderId, byId[e.WorkOrderId].DocumentNo, e.Code, e.Message)).ToList());
            },
            cancellationToken);

    /// <summary>Slots and machines for a date range (Gantt).</summary>
    public async Task<GanttDto> GetGanttAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        if (to < from || to.DayNumber - from.DayNumber > 120)
        {
            throw new DomainException("production.schedule.invalid_range", "Date range must be between 1 and 120 days.", 400);
        }

        var zone = await settings.GetTimeZoneAsync(cancellationToken);
        var start = ToUtc(from.ToDateTime(TimeOnly.MinValue), zone);
        var end = ToUtc(to.AddDays(1).ToDateTime(TimeOnly.MinValue), zone);
        var machines = await ListMachinesAsync(cancellationToken);
        var slots = await db.ScheduleSlots.AsNoTracking()
            .Where(s => s.StartAt < end && s.EndAt > start)
            .OrderBy(s => s.StartAt)
            .ToListAsync(cancellationToken);
        return new GanttDto(from, to, machines, await ToDtoAsync(slots, machines, cancellationToken));
    }

    /// <summary>Slots of one work order.</summary>
    public async Task<IReadOnlyList<ScheduleSlotDto>> GetWorkOrderSlotsAsync(Guid workOrderId, CancellationToken cancellationToken)
    {
        var slots = await db.ScheduleSlots.AsNoTracking().Where(s => s.WorkOrderId == workOrderId).OrderBy(s => s.Seq).ToListAsync(cancellationToken);
        return await ToDtoAsync(slots, await ListMachinesAsync(cancellationToken), cancellationToken);
    }

    /// <summary>Locks or unlocks a slot.</summary>
    public async Task<ScheduleSlotDto> SetLockAsync(Guid slotId, bool locked, CancellationToken cancellationToken)
    {
        var slot = await db.ScheduleSlots.FirstOrDefaultAsync(s => s.Id == slotId, cancellationToken) ?? throw DomainException.NotFound("Schedule slot", slotId);
        if (locked)
        {
            slot.Lock();
        }
        else
        {
            slot.Unlock();
        }

        await db.SaveChangesAsync(cancellationToken);
        return (await ToDtoAsync([slot], await ListMachinesAsync(cancellationToken), cancellationToken))[0];
    }

    /// <summary>Builds the work calendar from tenant settings and holidays.</summary>
    public async Task<WorkCalendar> LoadCalendarAsync(DateOnly from, CancellationToken cancellationToken)
    {
        var workDays = await settings.GetAsync(SettingKeys.WorkDays, DefaultWorkDays, cancellationToken);
        var shifts = await settings.GetAsync(SettingKeys.Shifts, DefaultShifts, cancellationToken);
        var holidays = await db.Holidays.AsNoTracking().Where(h => h.Date >= from.AddDays(-7)).Select(h => h.Date).ToListAsync(cancellationToken);
        return new WorkCalendar(
            workDays.Select(d => d == 7 ? DayOfWeek.Sunday : (DayOfWeek)d),
            shifts.Select(s => new Shift(TimeOnly.ParseExact(s.Start, "HH:mm", CultureInfo.InvariantCulture), TimeOnly.ParseExact(s.End, "HH:mm", CultureInfo.InvariantCulture))),
            holidays);
    }

    private static DateTimeOffset ToUtc(DateTime local, TimeZoneInfo zone) =>
        new DateTimeOffset(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), zone.GetUtcOffset(local)).ToUniversalTime();

    private static DateTime ToLocal(DateTimeOffset utc, TimeZoneInfo zone) => TimeZoneInfo.ConvertTime(utc, zone).DateTime;

    private static MachineDto ToDto(Machine m) => new(m.Id, m.Code, m.Name, m.MachineGroup, m.Priority, m.IsActive);

    private static IReadOnlyList<RoutingOperationDto> ToDto(Routing? routing) =>
        routing is null
            ? []
            : routing.Operations.OrderBy(o => o.Seq).Select(o => new RoutingOperationDto(o.Seq, o.Name, o.MachineGroup, o.SetupMinutes, o.MinutesPerUnit)).ToList();

    private async Task<IReadOnlyList<ScheduleSlotDto>> ToDtoAsync(IReadOnlyList<ScheduleSlot> slots, IReadOnlyList<MachineDto> machines, CancellationToken cancellationToken)
    {
        var orderIds = slots.Select(s => s.WorkOrderId).Distinct().ToList();
        var orders = await db.WorkOrders.AsNoTracking().Where(w => orderIds.Contains(w.Id)).ToDictionaryAsync(w => w.Id, cancellationToken);
        var items = await masters.GetItemsAsync(orders.Values.Select(o => o.ItemId), requireActive: false, cancellationToken);
        var machineById = machines.ToDictionary(m => m.Id);
        return slots.Select(s =>
        {
            var order = orders[s.WorkOrderId];
            var item = items[order.ItemId];
            var machine = machineById.GetValueOrDefault(s.MachineId);
            return new ScheduleSlotDto(
                s.Id, s.WorkOrderId, order.DocumentNo, order.ItemId, item.Code, item.Name, order.Quantity, order.DueDate, s.Seq, s.OperationName,
                s.MachineId, machine?.Code ?? string.Empty, machine?.MachineGroup ?? string.Empty, s.StartAt, s.EndAt, s.Status, s.RunNo);
        }).ToList();
    }
}
