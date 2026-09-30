using Microsoft.EntityFrameworkCore;
using Mrp.Inventory.Domain;
using Mrp.Inventory.Persistence;
using Mrp.SharedKernel.Domain;
using Mrp.SharedKernel.Persistence;
using Mrp.SharedKernel.Tenancy;
using Npgsql;

namespace Mrp.Inventory.Application;

/// <summary>
/// The only writer of the stock ledger. Appends movements and updates the balance cache in the caller's
/// transaction; refuses postings into closed periods and (unless the tenant allows it) negative balances.
/// </summary>
public sealed class StockLedger(InventoryDbContext db, DbSession session, ITenantContext tenant, ITenantSettings settings)
{
    private const string UpsertBalanceSql = """
        INSERT INTO inventory.balances (tenant_id, item_id, lot_id, warehouse_id, location_id, quantity, updated_at)
        VALUES (@tenant, @item, @lot, @warehouse, @location, @quantity, now())
        ON CONFLICT (tenant_id, item_id, lot_id, warehouse_id, location_id)
        DO UPDATE SET quantity = inventory.balances.quantity + EXCLUDED.quantity, updated_at = now()
        RETURNING quantity
        """;

    /// <summary>Throws when the date falls into a closed stock period.</summary>
    public async Task EnsurePeriodOpenAsync(DateOnly postingDate, CancellationToken cancellationToken)
    {
        var closed = await db.Periods.AnyAsync(
            p => p.Year == postingDate.Year && p.Month == postingDate.Month && p.Status == PeriodStatus.Closed,
            cancellationToken);
        if (closed)
        {
            throw DomainException.Conflict(
                "inventory.period.closed",
                $"Stock period {postingDate.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture)} is closed. Reopen it or use a date in an open period.");
        }
    }

    /// <summary>Appends the movements and applies them to the balance cache. Must run inside a transaction.</summary>
    public async Task PostAsync(IReadOnlyCollection<Movement> movements, CancellationToken cancellationToken)
    {
        if (session.Transaction is null)
        {
            throw new InvalidOperationException("Stock must be posted inside a database transaction.");
        }

        if (movements.Count == 0)
        {
            return;
        }

        foreach (var date in movements.Select(m => m.PostingDate).Distinct())
        {
            await EnsurePeriodOpenAsync(date, cancellationToken);
        }

        db.Movements.AddRange(movements);
        await db.SaveChangesAsync(cancellationToken);

        var allowNegative = await settings.GetAsync(SettingKeys.AllowNegativeStock, false, cancellationToken);
        var connection = await session.GetOpenConnectionAsync(cancellationToken);
        var tenantId = tenant.RequireTenantId();

        // Apply in a fixed key order so concurrent postings lock balance rows in the same order (no deadlocks).
        var deltas = movements
            .GroupBy(m => (m.ItemId, m.LotId, m.WarehouseId, m.LocationId))
            .Select(g => (g.Key, Quantity: g.Sum(m => m.Quantity)))
            .Where(d => d.Quantity != 0)
            .OrderBy(d => d.Key.ItemId).ThenBy(d => d.Key.LotId).ThenBy(d => d.Key.WarehouseId).ThenBy(d => d.Key.LocationId);

        foreach (var (key, quantity) in deltas)
        {
            await using var command = new NpgsqlCommand(UpsertBalanceSql, connection, session.Transaction);
            command.Parameters.AddWithValue("tenant", tenantId);
            command.Parameters.AddWithValue("item", key.ItemId);
            command.Parameters.AddWithValue("lot", key.LotId);
            command.Parameters.AddWithValue("warehouse", key.WarehouseId);
            command.Parameters.Add(new NpgsqlParameter("location", NpgsqlTypes.NpgsqlDbType.Uuid) { Value = (object?)key.LocationId ?? DBNull.Value });
            command.Parameters.AddWithValue("quantity", quantity);
            var balance = (decimal)(await command.ExecuteScalarAsync(cancellationToken))!;
            if (balance < 0 && quantity < 0 && !allowNegative)
            {
                throw new DomainException(
                    "inventory.insufficient_stock",
                    $"Not enough stock: the balance of the lot would become {balance:0.######}.");
            }
        }
    }
}
