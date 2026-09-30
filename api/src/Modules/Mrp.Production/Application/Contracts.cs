using System.ComponentModel.DataAnnotations;
using Mrp.Production.Domain;

namespace Mrp.Production.Application;

/// <summary>Creates or replaces a draft BOM version.</summary>
public sealed record SaveBomRequest(
    Guid ItemId,
    [property: Required, StringLength(200, MinimumLength = 1)] string Name,
    [property: Range(0.000001, 999999999999.0)] decimal BatchSize,
    [property: Required, MinLength(1), MaxLength(300)] IReadOnlyList<SaveBomLine> Lines,
    DateOnly? EffectiveFrom = null,
    [property: StringLength(500)] string? Remark = null);

/// <summary>Component of a BOM request.</summary>
public sealed record SaveBomLine(
    Guid ComponentItemId,
    [property: Range(0.000001, 999999999999.0)] decimal Quantity,
    Guid? UnitId = null,
    [property: Range(0, 100)] decimal LossPercent = 0,
    [property: StringLength(300)] string? Remark = null);

/// <summary>BOM version list row.</summary>
public sealed record BomSummary(Guid Id, Guid ItemId, string ItemCode, string ItemName, int VersionNo, string Name, decimal BatchSize, BomStatus Status, DateOnly? EffectiveFrom, int LineCount);

/// <summary>BOM version with components.</summary>
public sealed record BomDto(
    Guid Id, Guid ItemId, string ItemCode, string ItemName, string UnitCode, int VersionNo, string Name, decimal BatchSize, BomStatus Status,
    DateOnly? EffectiveFrom, string? Remark, IReadOnlyList<BomLineDto> Lines);

/// <summary>Component of a BOM.</summary>
public sealed record BomLineDto(
    Guid Id, int LineNo, Guid ComponentItemId, string ComponentCode, string ComponentName, string ItemType, Guid UnitId, string UnitCode,
    decimal Quantity, decimal ConversionFactor, decimal StockQuantity, decimal LossPercent, bool IsMadeInHouse, string? Remark);

/// <summary>Row of an exploded BOM.</summary>
public sealed record ExplosionRow(
    int Level, Guid ItemId, string ItemCode, string ItemName, string ItemType, Guid ParentItemId, string ParentItemCode, decimal Quantity,
    string UnitCode, bool IsMadeInHouse, decimal Available);

/// <summary>Exploded BOM with the purchased material summary.</summary>
public sealed record ExplosionDto(Guid ItemId, decimal Quantity, IReadOnlyList<ExplosionRow> Rows, IReadOnlyList<MaterialSummaryRow> Materials);

/// <summary>Total requirement of a purchased material.</summary>
public sealed record MaterialSummaryRow(Guid ItemId, string ItemCode, string ItemName, string UnitCode, decimal Required, decimal Available, decimal Shortage);

/// <summary>BOM that uses an item.</summary>
public sealed record WhereUsedRow(Guid BomId, Guid ParentItemId, string ParentItemCode, string ParentItemName, int VersionNo, BomStatus Status, decimal StockQuantity, decimal BatchSize);

/// <summary>Creates or updates a demand.</summary>
public sealed record SaveDemandRequest(
    Guid ItemId,
    [property: Range(0.000001, 999999999999.0)] decimal Quantity,
    DateOnly DueDate,
    DemandSource Source = DemandSource.Manual,
    [property: StringLength(40), RegularExpression("^[A-Za-z0-9._/-]*$")] string? ReferenceNo = null,
    Guid? CustomerId = null,
    [property: StringLength(500)] string? Remark = null);

/// <summary>Demand with coverage.</summary>
public sealed record DemandDto(
    Guid Id, Guid ItemId, string ItemCode, string ItemName, string UnitCode, decimal Quantity, decimal CoveredQuantity, DateOnly DueDate,
    DemandSource Source, string? ReferenceNo, Guid? CustomerId, DemandStatus Status, string? Remark);

/// <summary>Starts an MRP run.</summary>
public sealed record StartMrpRunRequest([property: Range(1, 730)] int HorizonDays = 90);

/// <summary>MRP run list row.</summary>
public sealed record MrpRunSummary(Guid Id, string DocumentNo, DateOnly RunDate, int HorizonDays, int ItemCount, int PlannedOrderCount, int ExceptionCount, DateTimeOffset CreatedAt);

/// <summary>MRP run with results.</summary>
public sealed record MrpRunDto(
    Guid Id, string DocumentNo, DateOnly RunDate, int HorizonDays, DateTimeOffset CreatedAt,
    IReadOnlyList<MrpRequirementDto> Requirements, IReadOnlyList<PlannedOrderDto> PlannedOrders, IReadOnlyList<MrpExceptionDto> Exceptions);

/// <summary>Netting result of an item.</summary>
public sealed record MrpRequirementDto(
    Guid ItemId, string ItemCode, string ItemName, string UnitCode, int Level, decimal OnHand, decimal SafetyStock, decimal Gross,
    decimal ScheduledReceipts, decimal Net, decimal Planned);

/// <summary>Planned order.</summary>
public sealed record PlannedOrderDto(
    Guid Id, Guid ItemId, string ItemCode, string ItemName, string UnitCode, PlannedOrderType OrderType, decimal Quantity, DateOnly ReleaseDate,
    DateOnly DueDate, bool IsLate, string Pegging, PlannedOrderStatus Status, string? ConvertedDocumentType, Guid? ConvertedDocumentId, string? ConvertedDocumentNo);

/// <summary>Exception of a run.</summary>
public sealed record MrpExceptionDto(Guid ItemId, string ItemCode, string Code, string Message);

/// <summary>Planned orders to turn into documents.</summary>
public sealed record ConvertPlannedOrdersRequest([property: Required, MinLength(1), MaxLength(300)] IReadOnlyList<Guid> PlannedOrderIds);

/// <summary>Documents created from planned orders.</summary>
public sealed record ConversionResult(IReadOnlyList<CreatedDocument> Documents);

/// <summary>Document created from planned orders.</summary>
public sealed record CreatedDocument(string DocumentType, Guid Id, string DocumentNo, int PlannedOrderCount);

/// <summary>Creates a work order.</summary>
public sealed record CreateWorkOrderRequest(
    Guid ItemId,
    [property: Range(0.000001, 999999999999.0)] decimal Quantity,
    DateOnly StartDate,
    DateOnly DueDate,
    Guid? DemandId = null,
    Guid? WarehouseId = null,
    bool CreateChildOrders = false,
    [property: StringLength(500)] string? Remark = null);

/// <summary>Reason for a work order action.</summary>
public sealed record WorkOrderReasonRequest([property: StringLength(500)] string? Reason = null);

/// <summary>Work order list row.</summary>
public sealed record WorkOrderSummary(
    Guid Id, string DocumentNo, Guid ItemId, string ItemCode, string ItemName, decimal Quantity, decimal ProducedQuantity, DateOnly StartDate,
    DateOnly DueDate, WorkOrderStatus Status, Guid? ParentWorkOrderId, WorkOrderSource Source);

/// <summary>Work order with materials.</summary>
public sealed record WorkOrderDto(
    Guid Id, string DocumentNo, Guid ItemId, string ItemCode, string ItemName, string UnitCode, decimal Quantity, decimal ProducedQuantity,
    DateOnly StartDate, DateOnly DueDate, WorkOrderStatus Status, Guid BomVersionId, Guid? ParentWorkOrderId, Guid? DemandId,
    WorkOrderSource Source, string? SourceReference, Guid? WarehouseId, string? Remark, string? StatusReason,
    IReadOnlyList<WorkOrderMaterialDto> Materials, IReadOnlyList<WorkOrderSummary> ChildOrders);

/// <summary>Material of a work order.</summary>
public sealed record WorkOrderMaterialDto(
    Guid Id, int LineNo, Guid ComponentItemId, string ComponentCode, string ComponentName, string UnitCode, decimal RequiredQuantity,
    decimal IssuedQuantity, decimal OutstandingQuantity, bool IsMadeInHouse, Guid? ChildWorkOrderId);
