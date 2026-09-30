using System.ComponentModel.DataAnnotations;
using Mrp.Purchasing.Domain;

namespace Mrp.Purchasing.Application;

/// <summary>Creates or replaces a purchase request.</summary>
public sealed record SavePurchaseRequest(
    DateOnly DocumentDate,
    [property: Required, MinLength(1), MaxLength(300)] IReadOnlyList<SavePurchaseRequestLine> Lines,
    DateOnly? RequiredDate = null,
    [property: StringLength(500)] string? Remark = null);

/// <summary>Line of a purchase request.</summary>
public sealed record SavePurchaseRequestLine(
    Guid ItemId,
    [property: Range(0.000001, 999999999999.0)] decimal Quantity,
    Guid? UnitId = null,
    DateOnly? RequiredDate = null,
    Guid? SuggestedSupplierId = null,
    [property: StringLength(300)] string? Remark = null);

/// <summary>Creates or replaces a purchase order.</summary>
public sealed record SavePurchaseOrder(
    DateOnly DocumentDate,
    Guid SupplierId,
    [property: Required, MinLength(1), MaxLength(300)] IReadOnlyList<SavePurchaseOrderLine> Lines,
    [property: Range(0.000001, 999999.0)] decimal? ExchangeRate = null,
    [property: Range(0, 100)] decimal? VatPercent = null,
    [property: Range(0, 99999999999999.0)] decimal DiscountAmount = 0,
    [property: Range(0, 365)] int? CreditDays = null,
    DateOnly? DeliveryDate = null,
    [property: StringLength(500)] string? Remark = null);

/// <summary>Line of a purchase order.</summary>
public sealed record SavePurchaseOrderLine(
    Guid ItemId,
    [property: Range(0.000001, 999999999999.0)] decimal Quantity,
    Guid? UnitId = null,
    [property: Range(0, 99999999999999.0)] decimal? UnitPrice = null,
    [property: Range(0, 100)] decimal DiscountPercent = 0,
    DateOnly? DeliveryDate = null,
    Guid? PrLineId = null,
    [property: StringLength(300)] string? Remark = null);

/// <summary>Reason for cancelling or closing.</summary>
public sealed record ReasonRequest([property: Required, StringLength(500, MinimumLength = 3)] string Reason);

/// <summary>Purchase request list row.</summary>
public sealed record PurchaseRequestSummary(
    Guid Id, string DocumentNo, DateOnly DocumentDate, PurchaseRequestStatus Status, PurchaseRequestSource Source,
    DateOnly? RequiredDate, string? Remark, int LineCount, Guid RequestedBy);

/// <summary>Purchase request.</summary>
public sealed record PurchaseRequestDto(
    Guid Id, string DocumentNo, DateOnly DocumentDate, PurchaseRequestStatus Status, PurchaseRequestSource Source, string? SourceReference,
    DateOnly? RequiredDate, string? Remark, string? StatusReason, Guid RequestedBy, IReadOnlyList<PurchaseRequestLineDto> Lines);

/// <summary>Purchase request line with ordered quantity.</summary>
public sealed record PurchaseRequestLineDto(
    Guid Id, int LineNo, Guid ItemId, string ItemCode, string ItemName, Guid UnitId, string UnitCode, decimal Quantity,
    decimal ConversionFactor, decimal StockQuantity, decimal OrderedStockQuantity, decimal OutstandingStockQuantity,
    DateOnly? RequiredDate, Guid? SuggestedSupplierId, string? Remark);

/// <summary>Approved request line that still has quantity to order.</summary>
public sealed record OpenRequestLineDto(
    Guid LineId, Guid RequestId, string RequestNo, Guid ItemId, string ItemCode, string ItemName, Guid UnitId, string UnitCode,
    decimal OutstandingQuantity, decimal OutstandingStockQuantity, DateOnly? RequiredDate, Guid? SuggestedSupplierId);

/// <summary>Purchase order list row.</summary>
public sealed record PurchaseOrderSummary(
    Guid Id, string DocumentNo, DateOnly DocumentDate, Guid SupplierId, string SupplierName, PurchaseOrderStatus Status,
    string Currency, decimal Total, DateOnly? DeliveryDate, int LineCount);

/// <summary>Purchase order.</summary>
public sealed record PurchaseOrderDto(
    Guid Id, string DocumentNo, DateOnly DocumentDate, Guid SupplierId, string SupplierName, PurchaseOrderStatus Status, string Currency,
    decimal ExchangeRate, decimal VatPercent, decimal DiscountAmount, decimal Subtotal, decimal VatAmount, decimal Total, int CreditDays,
    DateOnly? DeliveryDate, string? Remark, string? StatusReason, IReadOnlyList<PurchaseOrderLineDto> Lines);

/// <summary>Purchase order line with receiving progress.</summary>
public sealed record PurchaseOrderLineDto(
    Guid Id, int LineNo, Guid ItemId, string ItemCode, string ItemName, Guid UnitId, string UnitCode, decimal Quantity,
    decimal ConversionFactor, decimal StockQuantity, decimal UnitPrice, decimal DiscountPercent, decimal NetAmount,
    DateOnly? DeliveryDate, Guid? PrLineId, decimal ReceivedStockQuantity, decimal OutstandingStockQuantity, bool IsClosed, string? Remark);
