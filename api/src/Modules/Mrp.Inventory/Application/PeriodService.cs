using Microsoft.EntityFrameworkCore;
using Mrp.Inventory.Domain;
using Mrp.Inventory.Persistence;
using Mrp.SharedKernel.Domain;
using Mrp.SharedKernel.Persistence;
using Mrp.SharedKernel.Tenancy;
using Npgsql;

namespace Mrp.Inventory.Application;

/// <summary>Monthly stock close: pre-checks, balance snapshot from the ledger, and lock.</summary>
public sealed class PeriodService(InventoryDbContext db, DbSession session, ITenantContext tenant, ITenantSettings settings, TimeProvider clock)
{
    private const string SnapshotSql = """
        INSERT INTO inventory.period_balances (tenant_id, year, month, item_id, lot_id, warehouse_id, location_id, quantity, value)
        SELECT m.tenant_id, @year, @month, m.item_id, m.lot_id, m.warehouse_id, m.location_id,
               sum(m.quantity), round(sum(m.quantity) * l.unit_cost, 4)
        FROM inventory.movements m
        JOIN inventory.lots l ON l.id = m.lot_id
        WHERE m.tenant_id = @tenant AND m.posting_date <= @last_day
        GROUP BY m.tenant_id, m.item_id, m.lot_id, m.warehouse_id, m.location_id, l.unit_cost
        HAVING sum(m.quantity) <> 0
        """;

    /// <summary>Periods that were closed at least once, newest first.</summary>
    public async Task<IReadOnlyList<PeriodDto>> ListAsync(CancellationToken cancellationToken) =>
        await db.Periods.AsNoTracking()
            .OrderByDescending(p => p.Year).ThenByDescending(p => p.Month)
            .Select(p => new PeriodDto(p.Year, p.Month, p.Status, p.ClosedAt, p.ReopenedAt, p.ReopenReason))
            .ToListAsync(cancellationToken);

    /// <summary>Closes a month. Months must be closed in order and must be over.</summary>
    public Task<PeriodDto> CloseAsync(int year, int month, CancellationToken cancellationToken) =>
        session.ExecuteInTransactionAsync(
            async ct =>
            {
                if (year is < 2000 or > 2100 || month is < 1 or > 12)
                {
                    throw new DomainException("inventory.period.invalid", "Period is not valid.", 400);
                }

                var firstDay = new DateOnly(year, month, 1);
                var lastDay = firstDay.AddMonths(1).AddDays(-1);
                var zone = await settings.GetTimeZoneAsync(ct);
                var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), zone).DateTime);
                if (lastDay >= today)
                {
                    throw DomainException.Conflict("inventory.period.not_over", "A period can be closed only after its last day.");
                }

                var previous = firstDay.AddMonths(-1);
                var hasEarlierMovements = await db.Movements.AnyAsync(m => m.PostingDate < firstDay, ct);
                var previousClosed = await db.Periods.AnyAsync(
                    p => p.Year == previous.Year && p.Month == previous.Month && p.Status == PeriodStatus.Closed, ct);
                if (hasEarlierMovements && !previousClosed)
                {
                    throw DomainException.Conflict("inventory.period.previous_open", $"Close period {previous.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture)} first.");
                }

                var pending = await db.Documents.CountAsync(
                    d => d.DocumentDate >= firstDay && d.DocumentDate <= lastDay
                        && (d.Status == StockDocumentStatus.Draft || d.Status == StockDocumentStatus.Submitted
                            || d.Status == StockDocumentStatus.Approved || d.Status == StockDocumentStatus.Rejected),
                    ct);
                if (pending > 0)
                {
                    throw DomainException.Conflict(
                        "inventory.period.pending_documents",
                        $"{pending} document(s) dated in {firstDay.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture)} are not posted. Post or re-date them first.");
                }

                var userId = tenant.RequireUserId();
                var period = await db.Periods.FirstOrDefaultAsync(p => p.Year == year && p.Month == month, ct);
                if (period is null)
                {
                    period = new Period(year, month, userId);
                    db.Periods.Add(period);
                }
                else
                {
                    period.Close(userId);
                }

                await db.SaveChangesAsync(ct);
                await db.PeriodBalances.Where(b => b.Year == year && b.Month == month).ExecuteDeleteAsync(ct);

                var connection = await session.GetOpenConnectionAsync(ct);
                await using var command = new NpgsqlCommand(SnapshotSql, connection, session.Transaction);
                command.Parameters.AddWithValue("tenant", tenant.RequireTenantId());
                command.Parameters.AddWithValue("year", year);
                command.Parameters.AddWithValue("month", month);
                command.Parameters.AddWithValue("last_day", lastDay);
                await command.ExecuteNonQueryAsync(ct);

                return new PeriodDto(period.Year, period.Month, period.Status, period.ClosedAt, period.ReopenedAt, period.ReopenReason);
            },
            cancellationToken);

    /// <summary>Reopens the most recently closed month and discards its snapshot.</summary>
    public Task<PeriodDto> ReopenAsync(int year, int month, string reason, CancellationToken cancellationToken) =>
        session.ExecuteInTransactionAsync(
            async ct =>
            {
                var period = await db.Periods.FirstOrDefaultAsync(p => p.Year == year && p.Month == month, ct)
                    ?? throw DomainException.NotFound("Period", $"{year}-{month:00}");
                var laterClosed = await db.Periods.AnyAsync(
                    p => p.Status == PeriodStatus.Closed && (p.Year > year || (p.Year == year && p.Month > month)), ct);
                if (laterClosed)
                {
                    throw DomainException.Conflict("inventory.period.later_closed", "Reopen later periods first.");
                }

                period.Reopen(tenant.RequireUserId(), reason);
                await db.SaveChangesAsync(ct);
                await db.PeriodBalances.Where(b => b.Year == year && b.Month == month).ExecuteDeleteAsync(ct);
                return new PeriodDto(period.Year, period.Month, period.Status, period.ClosedAt, period.ReopenedAt, period.ReopenReason);
            },
            cancellationToken);
}
