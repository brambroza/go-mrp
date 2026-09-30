using System.ComponentModel.DataAnnotations;
using Mrp.Inventory.Domain;

namespace Mrp.Inventory.Application;

/// <summary>Creates or replaces a draft warehouse document.</summary>
public sealed record SaveStockDocumentRequest(
    StockDocumentType DocumentType,
    DateOnly DocumentDate,
    Guid WarehouseId,
    [property: Required, MinLength(1), MaxLength(500)] IReadOnlyList<SaveStockDocumentLine> Lines,
    Guid? ToWarehouseId = null,
    Guid? SupplierId = null,
    [property: StringLength(16)] string? ReferenceType = null,
    Guid? ReferenceId = null,
    [property: StringLength(40)] string? ReferenceNo = null,
    [property: StringLength(500)] string? Remark = null);

/// <summary>Line of a warehouse document request.</summary>
public sealed record SaveStockDocumentLine(
    Guid ItemId,
    [property: Range(-999999999999.0, 999999999999.0)] decimal Quantity,
    Guid? UnitId = null,
    [property: Range(0, 99999999999999.0)] decimal? UnitCost = null,
    Guid? LocationId = null,
    Guid? ToLocationId = null,
    Guid? LotId = null,
    [property: StringLength(40), RegularExpression("^[A-Za-z0-9._/-]*$")] string? LotNo = null,
    [property: StringLength(60)] string? SupplierLot = null,
    DateOnly? MfgDate = null,
    DateOnly? ExpiryDate = null,
    Guid? PoLineId = null,
    [property: StringLength(20), RegularExpression("^[A-Za-z0-9_-]*$")] string? ReasonCode = null,
    [property: StringLength(300)] string? Remark = null);

/// <summary>Creates a count sheet from the system balance.</summary>
public sealed record CreateCountSheetRequest(
    DateOnly DocumentDate,
    Guid WarehouseId,
    Guid? LocationId = null,
    [property: MaxLength(500)] IReadOnlyList<Guid>? ItemIds = null,
    [property: StringLength(500)] string? Remark = null);

/// <summary>Reason for voiding a document.</summary>
public sealed record VoidRequest([property: Required, StringLength(500, MinimumLength = 3)] string Reason);

/// <summary>Warehouse document list row.</summary>
public sealed record StockDocumentSummary(
    Guid Id, StockDocumentType DocumentType, string DocumentNo, DateOnly DocumentDate, StockDocumentStatus Status,
    Guid WarehouseId, Guid? ToWarehouseId, Guid? SupplierId, string? ReferenceNo, string? Remark, int LineCount, DateTimeOffset CreatedAt);

/// <summary>Warehouse document with lines.</summary>
public sealed record StockDocumentDto(
    Guid Id, StockDocumentType DocumentType, string DocumentNo, DateOnly DocumentDate, StockDocumentStatus Status,
    Guid WarehouseId, Guid? ToWarehouseId, Guid? SupplierId, string? ReferenceType, Guid? ReferenceId, string? ReferenceNo,
    string? Remark, string? StatusReason, DateTimeOffset? PostedAt, DateTimeOffset CreatedAt,
    IReadOnlyList<StockDocumentLineDto> Lines);

/// <summary>Line of a warehouse document.</summary>
public sealed record StockDocumentLineDto(
    Guid Id, int LineNo, Guid ItemId, string ItemCode, string ItemName, Guid UnitId, string UnitCode, decimal Quantity,
    decimal ConversionFactor, decimal StockQuantity, decimal UnitCost, Guid? LocationId, Guid? ToLocationId, Guid? LotId,
    string? LotNo, string? SupplierLot, DateOnly? MfgDate, DateOnly? ExpiryDate, Guid? PoLineId, string? ReasonCode,
    decimal? SystemQuantity, string? Remark, IReadOnlyList<AllocationDto> Allocations);

/// <summary>Lot quantity consumed by a line.</summary>
public sealed record AllocationDto(Guid LotId, string LotNo, Guid? LocationId, decimal Quantity);

/// <summary>Lot.</summary>
public sealed record LotDto(
    Guid Id, Guid ItemId, string ItemCode, string ItemName, string LotNo, string? SupplierLot, DateOnly? MfgDate, DateOnly? ExpiryDate,
    DateTimeOffset ReceivedAt, LotQcStatus QcStatus, string? QcRemark, decimal UnitCost, decimal OnHand);

/// <summary>QC decision on a lot.</summary>
public sealed record LotQcRequest(
    LotQcStatus Status,
    [property: StringLength(500)] string? Remark = null,
    DateOnly? ExpiryDate = null);

/// <summary>On-hand row.</summary>
public sealed class OnHandRow
{
    /// <summary>Item.</summary>
    public Guid ItemId { get; init; }

    /// <summary>Item code.</summary>
    public string ItemCode { get; init; } = string.Empty;

    /// <summary>Item name.</summary>
    public string ItemName { get; init; } = string.Empty;

    /// <summary>Stock unit code.</summary>
    public string UnitCode { get; init; } = string.Empty;

    /// <summary>Warehouse.</summary>
    public Guid WarehouseId { get; init; }

    /// <summary>Warehouse code.</summary>
    public string WarehouseCode { get; init; } = string.Empty;

    /// <summary>Location.</summary>
    public Guid? LocationId { get; init; }

    /// <summary>Location code.</summary>
    public string? LocationCode { get; init; }

    /// <summary>Lot.</summary>
    public Guid LotId { get; init; }

    /// <summary>Lot number.</summary>
    public string LotNo { get; init; } = string.Empty;

    /// <summary>Expiry date.</summary>
    public DateOnly? ExpiryDate { get; init; }

    /// <summary>QC status.</summary>
    public string QcStatus { get; init; } = string.Empty;

    /// <summary>On-hand quantity in stock unit.</summary>
    public decimal Quantity { get; init; }

    /// <summary>Quantity × lot cost.</summary>
    public decimal Value { get; init; }
}

/// <summary>Stock card row.</summary>
public sealed class StockCardRow
{
    /// <summary>Ledger sequence.</summary>
    public long Seq { get; init; }

    /// <summary>Posting date.</summary>
    public DateOnly PostingDate { get; init; }

    /// <summary>Movement type.</summary>
    public string MovementType { get; init; } = string.Empty;

    /// <summary>Document number.</summary>
    public string DocumentNo { get; init; } = string.Empty;

    /// <summary>Warehouse code.</summary>
    public string WarehouseCode { get; init; } = string.Empty;

    /// <summary>Lot number.</summary>
    public string LotNo { get; init; } = string.Empty;

    /// <summary>Quantity in.</summary>
    public decimal QuantityIn { get; init; }

    /// <summary>Quantity out.</summary>
    public decimal QuantityOut { get; init; }

    /// <summary>Running balance after the movement.</summary>
    public decimal Balance { get; init; }

    /// <summary>Cost per stock unit.</summary>
    public decimal UnitCost { get; init; }
}

/// <summary>Stock card of an item.</summary>
public sealed record StockCardDto(Guid ItemId, DateOnly From, DateOnly To, decimal OpeningBalance, decimal ClosingBalance, IReadOnlyList<StockCardRow> Rows);

/// <summary>Lot the system would consume next.</summary>
public sealed record AllocationPreviewRow(Guid LotId, string LotNo, Guid? LocationId, string? LocationCode, DateOnly? ExpiryDate, DateTimeOffset ReceivedAt, decimal Available, decimal Allocated);

/// <summary>Allocation preview for a quantity.</summary>
public sealed record AllocationPreviewDto(decimal Required, decimal Available, bool Sufficient, IReadOnlyList<AllocationPreviewRow> Lots);

/// <summary>Stock period.</summary>
public sealed record PeriodDto(int Year, int Month, PeriodStatus Status, DateTimeOffset? ClosedAt, DateTimeOffset? ReopenedAt, string? ReopenReason);

/// <summary>Reason for reopening a period.</summary>
public sealed record ReopenPeriodRequest([property: Required, StringLength(500, MinimumLength = 3)] string Reason);
