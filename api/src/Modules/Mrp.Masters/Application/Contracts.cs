using System.ComponentModel.DataAnnotations;
using Mrp.Masters.Domain;

namespace Mrp.Masters.Application;

/// <summary>Unit or item group.</summary>
public sealed record CodedDto(Guid Id, string Code, string Name, string? NameEn, bool IsActive);

/// <summary>Creates or updates a unit or item group.</summary>
public sealed record SaveCodedRequest(
    [property: Required, StringLength(40, MinimumLength = 1)] string Code,
    [property: Required, StringLength(200, MinimumLength = 1)] string Name,
    [property: StringLength(200)] string? NameEn = null,
    bool IsActive = true);

/// <summary>Item.</summary>
public sealed record ItemDto(
    Guid Id, string Code, string Name, string? NameEn, bool IsActive,
    ItemType ItemType, SupplyType SupplyType, Guid? ItemGroupId, Guid StockUnitId, string StockUnitCode, Guid? PurchaseUnitId,
    string? Barcode, bool IsLotTracked, int? ShelfLifeDays, int LeadTimeDays,
    decimal SafetyStock, decimal MinStock, decimal MaxStock, decimal MinOrderQty, decimal OrderMultiple, decimal StandardCost, decimal SalesPrice);

/// <summary>Creates or updates an item.</summary>
public sealed record SaveItemRequest(
    [property: Required, StringLength(40, MinimumLength = 1)] string Code,
    [property: Required, StringLength(200, MinimumLength = 1)] string Name,
    ItemType ItemType,
    SupplyType SupplyType,
    Guid StockUnitId,
    [property: StringLength(200)] string? NameEn = null,
    bool IsActive = true,
    Guid? ItemGroupId = null,
    Guid? PurchaseUnitId = null,
    [property: StringLength(50)] string? Barcode = null,
    bool IsLotTracked = true,
    [property: Range(1, 36500)] int? ShelfLifeDays = null,
    [property: Range(0, 3650)] int LeadTimeDays = 0,
    [property: Range(0, 999999999999.0)] decimal SafetyStock = 0,
    [property: Range(0, 999999999999.0)] decimal MinStock = 0,
    [property: Range(0, 999999999999.0)] decimal MaxStock = 0,
    [property: Range(0, 999999999999.0)] decimal MinOrderQty = 0,
    [property: Range(0, 999999999999.0)] decimal OrderMultiple = 0,
    [property: Range(0, 99999999999999.0)] decimal StandardCost = 0,
    [property: Range(0, 99999999999999.0)] decimal SalesPrice = 0);

/// <summary>Unit conversion.</summary>
public sealed record UnitConversionDto(Guid Id, Guid? ItemId, Guid FromUnitId, Guid ToUnitId, decimal Factor);

/// <summary>Creates or updates a unit conversion.</summary>
public sealed record SaveUnitConversionRequest(
    Guid? ItemId,
    Guid FromUnitId,
    Guid ToUnitId,
    [property: Range(0.000001, 999999999999.0)] decimal Factor);

/// <summary>Warehouse.</summary>
public sealed record WarehouseDto(Guid Id, string Code, string Name, string? NameEn, bool IsActive, WarehouseType WarehouseType);

/// <summary>Creates or updates a warehouse.</summary>
public sealed record SaveWarehouseRequest(
    [property: Required, StringLength(40, MinimumLength = 1)] string Code,
    [property: Required, StringLength(200, MinimumLength = 1)] string Name,
    WarehouseType WarehouseType = WarehouseType.General,
    [property: StringLength(200)] string? NameEn = null,
    bool IsActive = true);

/// <summary>Location.</summary>
public sealed record LocationDto(Guid Id, Guid WarehouseId, string Code, string Name, string? NameEn, bool IsActive);

/// <summary>Creates or updates a location.</summary>
public sealed record SaveLocationRequest(
    Guid WarehouseId,
    [property: Required, StringLength(40, MinimumLength = 1)] string Code,
    [property: Required, StringLength(200, MinimumLength = 1)] string Name,
    [property: StringLength(200)] string? NameEn = null,
    bool IsActive = true);

/// <summary>Customer.</summary>
public sealed record CustomerDto(
    Guid Id, string Code, string Name, string? NameEn, bool IsActive, string? TaxId, string? BranchNo, string? Address,
    string? Phone, string? Email, string? ContactName, int CreditDays, decimal CreditLimit, bool CheckCredit);

/// <summary>Creates or updates a customer.</summary>
public sealed record SaveCustomerRequest(
    [property: Required, StringLength(40, MinimumLength = 1)] string Code,
    [property: Required, StringLength(200, MinimumLength = 1)] string Name,
    [property: StringLength(200)] string? NameEn = null,
    bool IsActive = true,
    [property: StringLength(20), RegularExpression("^[0-9A-Za-z-]*$")] string? TaxId = null,
    [property: StringLength(10)] string? BranchNo = null,
    [property: StringLength(500)] string? Address = null,
    [property: StringLength(50)] string? Phone = null,
    [property: EmailAddress, StringLength(200)] string? Email = null,
    [property: StringLength(100)] string? ContactName = null,
    [property: Range(0, 365)] int CreditDays = 0,
    [property: Range(0, 99999999999999.0)] decimal CreditLimit = 0,
    bool CheckCredit = false);

/// <summary>Supplier.</summary>
public sealed record SupplierDto(
    Guid Id, string Code, string Name, string? NameEn, bool IsActive, string? TaxId, string? BranchNo, string? Address,
    string? Phone, string? Email, string? ContactName, int CreditDays, string Currency, decimal? VatPercent);

/// <summary>Creates or updates a supplier.</summary>
public sealed record SaveSupplierRequest(
    [property: Required, StringLength(40, MinimumLength = 1)] string Code,
    [property: Required, StringLength(200, MinimumLength = 1)] string Name,
    [property: StringLength(200)] string? NameEn = null,
    bool IsActive = true,
    [property: StringLength(20), RegularExpression("^[0-9A-Za-z-]*$")] string? TaxId = null,
    [property: StringLength(10)] string? BranchNo = null,
    [property: StringLength(500)] string? Address = null,
    [property: StringLength(50)] string? Phone = null,
    [property: EmailAddress, StringLength(200)] string? Email = null,
    [property: StringLength(100)] string? ContactName = null,
    [property: Range(0, 365)] int CreditDays = 0,
    [property: Required, RegularExpression("^[A-Za-z]{3}$")] string Currency = "THB",
    [property: Range(0, 100)] decimal? VatPercent = null);

/// <summary>Supplier price tier.</summary>
public sealed record SupplierPriceDto(Guid Id, Guid SupplierId, Guid ItemId, Guid UnitId, decimal MinQty, decimal UnitPrice, string Currency, DateOnly ValidFrom, DateOnly? ValidTo);

/// <summary>Creates or updates a supplier price tier.</summary>
public sealed record SaveSupplierPriceRequest(
    Guid SupplierId,
    Guid ItemId,
    Guid UnitId,
    [property: Range(0, 999999999999.0)] decimal MinQty,
    [property: Range(0, 99999999999999.0)] decimal UnitPrice,
    DateOnly ValidFrom,
    DateOnly? ValidTo = null,
    [property: Required, RegularExpression("^[A-Za-z]{3}$")] string Currency = "THB");

/// <summary>Result of a quantity conversion.</summary>
public sealed record ConvertedQuantity(decimal Quantity, decimal Factor);
