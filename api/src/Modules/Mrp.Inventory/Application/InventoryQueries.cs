using Dapper;
using Microsoft.EntityFrameworkCore;
using Mrp.Inventory.Domain;
using Mrp.Inventory.Persistence;
using Mrp.Masters.Application;
using Mrp.SharedKernel.Domain;
using Mrp.SharedKernel.Persistence;
using Mrp.SharedKernel.Tenancy;
using Mrp.SharedKernel.Web;

namespace Mrp.Inventory.Application;

/// <summary>Lots, QC decisions and stock reports.</summary>
public sealed class InventoryQueries(
    InventoryDbContext db,
    DbSession session,
    ITenantContext tenant,
    ITenantSettings settings,
    MasterData masters,
    TimeProvider clock)
{
    private const string OnHandSql = """
        SELECT b.item_id AS ItemId, i.code AS ItemCode, i.name AS ItemName, u.code AS UnitCode,
               b.warehouse_id AS WarehouseId, w.code AS WarehouseCode,
               b.location_id AS LocationId, loc.code AS LocationCode,
               b.lot_id AS LotId, l.lot_no AS LotNo, l.expiry_date AS ExpiryDate, l.qc_status AS QcStatus,
               b.quantity AS Quantity, round(b.quantity * l.unit_cost, 4) AS Value
        FROM inventory.balances b
        JOIN inventory.lots l ON l.id = b.lot_id AND l.tenant_id = b.tenant_id
        JOIN masters.items i ON i.id = b.item_id AND i.tenant_id = b.tenant_id
        JOIN masters.units u ON u.id = i.stock_unit_id
        JOIN masters.warehouses w ON w.id = b.warehouse_id AND w.tenant_id = b.tenant_id
        LEFT JOIN masters.locations loc ON loc.id = b.location_id
        WHERE b.tenant_id = @TenantId
          AND b.quantity <> 0
          AND (@ItemId IS NULL OR b.item_id = @ItemId)
          AND (@WarehouseId IS NULL OR b.warehouse_id = @WarehouseId)
          AND (@LotId IS NULL OR b.lot_id = @LotId)
          AND (NOT @AvailableOnly OR (
                l.qc_status = 'Released'
                AND (l.expiry_date IS NULL OR l.expiry_date >= @Today)
                AND w.warehouse_type IN ('General', 'Production')))
        ORDER BY i.code, w.code, l.received_at, l.lot_no, loc.code
        LIMIT @Limit OFFSET @Offset
        """;

    private const string OpeningSql = """
        SELECT coalesce(sum(m.quantity), 0)
        FROM inventory.movements m
        WHERE m.tenant_id = @TenantId AND m.item_id = @ItemId
          AND (@WarehouseId IS NULL OR m.warehouse_id = @WarehouseId)
          AND m.posting_date < @From
        """;

    private const string StockCardSql = """
        SELECT m.seq AS Seq, m.posting_date AS PostingDate, m.movement_type AS MovementType, m.document_no AS DocumentNo,
               w.code AS WarehouseCode, l.lot_no AS LotNo,
               CASE WHEN m.quantity > 0 THEN m.quantity ELSE 0 END AS QuantityIn,
               CASE WHEN m.quantity < 0 THEN -m.quantity ELSE 0 END AS QuantityOut,
               @Opening + sum(m.quantity) OVER (ORDER BY m.posting_date, m.seq) AS Balance,
               m.unit_cost AS UnitCost
        FROM inventory.movements m
        JOIN inventory.lots l ON l.id = m.lot_id
        JOIN masters.warehouses w ON w.id = m.warehouse_id
        WHERE m.tenant_id = @TenantId AND m.item_id = @ItemId
          AND (@WarehouseId IS NULL OR m.warehouse_id = @WarehouseId)
          AND m.posting_date >= @From AND m.posting_date <= @To
        ORDER BY m.posting_date, m.seq
        LIMIT 5000
        """;

    static InventoryQueries()
    {
        SqlMapper.AddTypeHandler(new DateOnlyHandler());
        SqlMapper.AddTypeHandler(new NullableDateOnlyHandler());
    }

    /// <summary>Lots of an item or matching a lot number prefix.</summary>
    public async Task<PagedResult<LotDto>> ListLotsAsync(Guid? itemId, LotQcStatus? status, string? search, int? page, int? pageSize, CancellationToken cancellationToken)
    {
        var query = db.Lots.AsNoTracking();
        if (itemId is { } item)
        {
            query = query.Where(l => l.ItemId == item);
        }

        if (status is { } qc)
        {
            query = query.Where(l => l.QcStatus == qc);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var text = search.Trim().ToUpperInvariant();
            query = query.Where(l => l.LotNo.StartsWith(text) || (l.SupplierLot != null && l.SupplierLot.StartsWith(search.Trim())));
        }

        var result = await query.OrderByDescending(l => l.ReceivedAt).ThenBy(l => l.LotNo).ToPagedAsync(page, pageSize, cancellationToken);
        return new PagedResult<LotDto>(await ToDtoAsync(result.Items, cancellationToken), result.Total, result.Page, result.PageSize);
    }

    /// <summary>Lot by its number, as scanned from a barcode.</summary>
    public async Task<LotDto> GetLotByNumberAsync(string lotNo, CancellationToken cancellationToken)
    {
        var number = (lotNo ?? string.Empty).Trim().ToUpperInvariant();
        var lot = await db.Lots.AsNoTracking().FirstOrDefaultAsync(l => l.LotNo == number, cancellationToken)
            ?? throw DomainException.NotFound("Lot", number);
        return (await ToDtoAsync([lot], cancellationToken))[0];
    }

    /// <summary>Records a QC decision on a lot.</summary>
    public async Task<LotDto> SetQcStatusAsync(Guid lotId, LotQcRequest request, CancellationToken cancellationToken)
    {
        var lot = await db.Lots.FirstOrDefaultAsync(l => l.Id == lotId, cancellationToken)
            ?? throw DomainException.NotFound("Lot", lotId);
        lot.SetQcStatus(request.Status, request.Remark, request.ExpiryDate);
        await db.SaveChangesAsync(cancellationToken);
        return (await ToDtoAsync([lot], cancellationToken))[0];
    }

    /// <summary>On-hand per item, warehouse, location and lot.</summary>
    public async Task<IReadOnlyList<OnHandRow>> OnHandAsync(Guid? itemId, Guid? warehouseId, Guid? lotId, bool availableOnly, int? page, int? pageSize, CancellationToken cancellationToken)
    {
        var size = Math.Clamp(pageSize ?? 100, 1, Paging.MaxPageSize);
        var connection = await session.GetOpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<OnHandRow>(new CommandDefinition(
            OnHandSql,
            new
            {
                TenantId = tenant.RequireTenantId(),
                ItemId = itemId,
                WarehouseId = warehouseId,
                LotId = lotId,
                AvailableOnly = availableOnly,
                Today = await TodayAsync(cancellationToken),
                Limit = size,
                Offset = (Math.Max(page ?? 1, 1) - 1) * size,
            },
            session.Transaction,
            cancellationToken: cancellationToken));
        return rows.AsList();
    }

    /// <summary>Available quantity per item in usable warehouses (released, unexpired lots). Used by MRP.</summary>
    public async Task<IReadOnlyDictionary<Guid, decimal>> AvailableByItemAsync(CancellationToken cancellationToken)
    {
        var today = await TodayAsync(cancellationToken);
        var warehouses = await masters.GetAvailableWarehouseIdsAsync(cancellationToken);
        return await (
            from balance in db.Balances.AsNoTracking()
            join lot in db.Lots.AsNoTracking() on balance.LotId equals lot.Id
            where warehouses.Contains(balance.WarehouseId)
                && lot.QcStatus == LotQcStatus.Released
                && (lot.ExpiryDate == null || lot.ExpiryDate >= today)
            group balance by balance.ItemId into g
            select new { ItemId = g.Key, Quantity = g.Sum(b => b.Quantity) })
            .ToDictionaryAsync(x => x.ItemId, x => x.Quantity, cancellationToken);
    }

    /// <summary>Quantity per item waiting for QC (quarantine or on hold).</summary>
    public async Task<IReadOnlyDictionary<Guid, decimal>> InQualityHoldByItemAsync(CancellationToken cancellationToken) =>
        await (
            from balance in db.Balances.AsNoTracking()
            join lot in db.Lots.AsNoTracking() on balance.LotId equals lot.Id
            where lot.QcStatus == LotQcStatus.Quarantine || lot.QcStatus == LotQcStatus.OnHold
            group balance by balance.ItemId into g
            select new { ItemId = g.Key, Quantity = g.Sum(b => b.Quantity) })
            .ToDictionaryAsync(x => x.ItemId, x => x.Quantity, cancellationToken);

    /// <summary>Movements of an item with opening and running balance.</summary>
    public async Task<StockCardDto> StockCardAsync(Guid itemId, Guid? warehouseId, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        if (to < from || to.DayNumber - from.DayNumber > 366)
        {
            throw new DomainException("inventory.report.invalid_range", "Date range must be between 1 and 366 days.", 400);
        }

        var connection = await session.GetOpenConnectionAsync(cancellationToken);
        var parameters = new { TenantId = tenant.RequireTenantId(), ItemId = itemId, WarehouseId = warehouseId, From = from, To = to };
        var opening = await connection.ExecuteScalarAsync<decimal>(new CommandDefinition(OpeningSql, parameters, session.Transaction, cancellationToken: cancellationToken));
        var rows = (await connection.QueryAsync<StockCardRow>(new CommandDefinition(
            StockCardSql,
            new { parameters.TenantId, parameters.ItemId, parameters.WarehouseId, parameters.From, parameters.To, Opening = opening },
            session.Transaction,
            cancellationToken: cancellationToken))).AsList();
        return new StockCardDto(itemId, from, to, opening, rows.Count > 0 ? rows[^1].Balance : opening, rows);
    }

    /// <summary>Lots the system would consume for a quantity, in allocation order.</summary>
    public async Task<AllocationPreviewDto> PreviewAllocationAsync(Guid itemId, Guid warehouseId, decimal quantity, CancellationToken cancellationToken)
    {
        if (quantity <= 0)
        {
            throw new DomainException("inventory.invalid_quantity", "Quantity must be greater than zero.", 400);
        }

        var strategy = Enum.TryParse<IssueStrategy>(
            await settings.GetAsync(SettingKeys.IssueStrategy, nameof(IssueStrategy.Fifo), cancellationToken), out var parsed)
            ? parsed
            : IssueStrategy.Fifo;
        var rows = await (
            from balance in db.Balances.AsNoTracking()
            join lot in db.Lots.AsNoTracking() on balance.LotId equals lot.Id
            where balance.ItemId == itemId && balance.WarehouseId == warehouseId && balance.Quantity > 0
            select new LotStock(lot.Id, lot.LotNo, balance.LocationId, null, lot.ReceivedAt, lot.ExpiryDate, lot.QcStatus, balance.Quantity))
            .ToListAsync(cancellationToken);
        var today = await TodayAsync(cancellationToken);
        var ordered = LotAllocator.Order(rows, strategy, today);
        var available = ordered.Sum(r => r.Quantity);
        var remaining = quantity;
        var lots = new List<AllocationPreviewRow>();
        foreach (var lot in ordered)
        {
            var take = Math.Min(lot.Quantity, Math.Max(remaining, 0));
            remaining -= take;
            lots.Add(new AllocationPreviewRow(lot.LotId, lot.LotNo, lot.LocationId, lot.LocationCode, lot.ExpiryDate, lot.ReceivedAt, lot.Quantity, take));
        }

        return new AllocationPreviewDto(quantity, available, available >= quantity, lots);
    }

    private async Task<DateOnly> TodayAsync(CancellationToken cancellationToken)
    {
        var zone = await settings.GetTimeZoneAsync(cancellationToken);
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), zone).DateTime);
    }

    private async Task<IReadOnlyList<LotDto>> ToDtoAsync(IReadOnlyList<Lot> lots, CancellationToken cancellationToken)
    {
        var ids = lots.Select(l => l.Id).ToList();
        var items = await masters.GetItemsAsync(lots.Select(l => l.ItemId), requireActive: false, cancellationToken);
        var onHand = await db.Balances.AsNoTracking().Where(b => ids.Contains(b.LotId))
            .GroupBy(b => b.LotId)
            .Select(g => new { LotId = g.Key, Quantity = g.Sum(b => b.Quantity) })
            .ToDictionaryAsync(x => x.LotId, x => x.Quantity, cancellationToken);
        return lots.Select(l => new LotDto(
            l.Id, l.ItemId, items[l.ItemId].Code, items[l.ItemId].Name, l.LotNo, l.SupplierLot, l.MfgDate, l.ExpiryDate, l.ReceivedAt,
            l.QcStatus, l.QcRemark, l.UnitCost, onHand.GetValueOrDefault(l.Id))).ToList();
    }

    private sealed class DateOnlyHandler : SqlMapper.TypeHandler<DateOnly>
    {
        public override DateOnly Parse(object value) => value is DateOnly date ? date : DateOnly.FromDateTime((DateTime)value);

        public override void SetValue(System.Data.IDbDataParameter parameter, DateOnly value)
        {
            parameter.DbType = System.Data.DbType.Date;
            parameter.Value = value;
        }
    }

    private sealed class NullableDateOnlyHandler : SqlMapper.TypeHandler<DateOnly?>
    {
        public override DateOnly? Parse(object value) => value is null or DBNull ? null : value is DateOnly date ? date : DateOnly.FromDateTime((DateTime)value);

        public override void SetValue(System.Data.IDbDataParameter parameter, DateOnly? value)
        {
            parameter.DbType = System.Data.DbType.Date;
            parameter.Value = (object?)value ?? DBNull.Value;
        }
    }
}
