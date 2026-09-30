using Mrp.SharedKernel.Domain;

namespace Mrp.Masters.Domain;

/// <summary>Base of master records identified by a tenant-unique code.</summary>
public abstract class CodedMaster : Entity
{
    /// <summary>Upper-case code, unique per tenant.</summary>
    public string Code { get; private set; } = string.Empty;

    /// <summary>Name in the tenant's main language.</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>Optional English name.</summary>
    public string? NameEn { get; private set; }

    /// <summary>Inactive records cannot be used on new documents.</summary>
    public bool IsActive { get; private set; } = true;

    /// <summary>Trims and upper-cases a code.</summary>
    public static string NormalizeCode(string code) => (code ?? string.Empty).Trim().ToUpperInvariant();

    /// <summary>Sets the identifying fields.</summary>
    public void SetIdentity(string code, string name, string? nameEn, bool isActive)
    {
        var normalized = NormalizeCode(code);
        if (normalized.Length is 0 or > 40 || normalized.Any(char.IsWhiteSpace))
        {
            throw new DomainException("masters.invalid_code", "Code must be 1-40 characters without spaces.", 400);
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("masters.name_required", "Name is required.", 400);
        }

        Code = normalized;
        Name = name.Trim();
        NameEn = string.IsNullOrWhiteSpace(nameEn) ? null : nameEn.Trim();
        IsActive = isActive;
    }
}

/// <summary>Unit of measure.</summary>
public sealed class Unit : CodedMaster;

/// <summary>Reporting group of items. Does not decide the item type.</summary>
public sealed class ItemGroup : CodedMaster;

/// <summary>What an item is in the production flow.</summary>
public enum ItemType
{
    /// <summary>Raw material.</summary>
    RawMaterial,

    /// <summary>Packaging material.</summary>
    Packaging,

    /// <summary>Bulk / mixed intermediate.</summary>
    Bulk,

    /// <summary>Other semi-finished goods.</summary>
    SemiFinished,

    /// <summary>Finished goods.</summary>
    FinishedGood,

    /// <summary>Non-stock service.</summary>
    Service,
}

/// <summary>How an item is replenished.</summary>
public enum SupplyType
{
    /// <summary>Purchased; MRP proposes purchase requests.</summary>
    Buy,

    /// <summary>Manufactured; MRP proposes work orders.</summary>
    Make,
}

/// <summary>Material, packaging, intermediate or product.</summary>
public sealed class Item : CodedMaster
{
    /// <summary>Item type.</summary>
    public ItemType ItemType { get; private set; }

    /// <summary>Replenishment method.</summary>
    public SupplyType SupplyType { get; private set; }

    /// <summary>Reporting group.</summary>
    public Guid? ItemGroupId { get; private set; }

    /// <summary>Unit stock is kept in. All ledger quantities use this unit.</summary>
    public Guid StockUnitId { get; private set; }

    /// <summary>Default unit on purchase documents.</summary>
    public Guid? PurchaseUnitId { get; private set; }

    /// <summary>Product barcode (GTIN), unique per tenant when set.</summary>
    public string? Barcode { get; private set; }

    /// <summary>Whether stock is tracked per lot.</summary>
    public bool IsLotTracked { get; private set; } = true;

    /// <summary>Days from receipt/production until expiry; null = does not expire.</summary>
    public int? ShelfLifeDays { get; private set; }

    /// <summary>Purchase or production lead time in days, used by MRP.</summary>
    public int LeadTimeDays { get; private set; }

    /// <summary>Quantity MRP keeps in reserve.</summary>
    public decimal SafetyStock { get; private set; }

    /// <summary>Reorder warning level.</summary>
    public decimal MinStock { get; private set; }

    /// <summary>Overstock warning level; 0 = not checked.</summary>
    public decimal MaxStock { get; private set; }

    /// <summary>Smallest order quantity; 0 = none.</summary>
    public decimal MinOrderQty { get; private set; }

    /// <summary>Orders are rounded up to a multiple of this quantity; 0 = none.</summary>
    public decimal OrderMultiple { get; private set; }

    /// <summary>Standard cost per stock unit.</summary>
    public decimal StandardCost { get; private set; }

    /// <summary>Sets the item attributes.</summary>
    public void SetDetails(ItemDetails details)
    {
        if (details.StockUnitId == Guid.Empty)
        {
            throw new DomainException("masters.item.stock_unit_required", "Stock unit is required.", 400);
        }

        if (details.ShelfLifeDays is <= 0 || details.LeadTimeDays < 0 || details.SafetyStock < 0 || details.MinStock < 0
            || details.MaxStock < 0 || details.MinOrderQty < 0 || details.OrderMultiple < 0 || details.StandardCost < 0)
        {
            throw new DomainException("masters.item.negative_value", "Quantities, days and cost must not be negative.", 400);
        }

        if (details.MaxStock > 0 && details.MaxStock < details.MinStock)
        {
            throw new DomainException("masters.item.max_below_min", "Maximum stock must not be below minimum stock.", 400);
        }

        ItemType = details.ItemType;
        SupplyType = details.SupplyType;
        ItemGroupId = details.ItemGroupId;
        StockUnitId = details.StockUnitId;
        PurchaseUnitId = details.PurchaseUnitId;
        Barcode = string.IsNullOrWhiteSpace(details.Barcode) ? null : details.Barcode.Trim();
        IsLotTracked = details.IsLotTracked;
        ShelfLifeDays = details.ShelfLifeDays;
        LeadTimeDays = details.LeadTimeDays;
        SafetyStock = details.SafetyStock;
        MinStock = details.MinStock;
        MaxStock = details.MaxStock;
        MinOrderQty = details.MinOrderQty;
        OrderMultiple = details.OrderMultiple;
        StandardCost = details.StandardCost;
    }
}

/// <summary>Attributes of an item.</summary>
public sealed record ItemDetails(
    ItemType ItemType,
    SupplyType SupplyType,
    Guid? ItemGroupId,
    Guid StockUnitId,
    Guid? PurchaseUnitId,
    string? Barcode,
    bool IsLotTracked,
    int? ShelfLifeDays,
    int LeadTimeDays,
    decimal SafetyStock,
    decimal MinStock,
    decimal MaxStock,
    decimal MinOrderQty,
    decimal OrderMultiple,
    decimal StandardCost);

/// <summary><c>qty_to = qty_from × factor</c>, for one item or (when <see cref="ItemId"/> is null) for all items.</summary>
public sealed class UnitConversion : Entity
{
    private UnitConversion()
    {
    }

    /// <summary>Creates a conversion.</summary>
    public UnitConversion(Guid? itemId, Guid fromUnitId, Guid toUnitId, decimal factor)
    {
        ItemId = itemId;
        FromUnitId = fromUnitId;
        ToUnitId = toUnitId;
        SetFactor(factor);
        if (fromUnitId == toUnitId)
        {
            throw new DomainException("masters.uom.same_unit", "From and to unit must differ.", 400);
        }
    }

    /// <summary>Item the conversion applies to; null = every item.</summary>
    public Guid? ItemId { get; private set; }

    /// <summary>Source unit.</summary>
    public Guid FromUnitId { get; private set; }

    /// <summary>Target unit.</summary>
    public Guid ToUnitId { get; private set; }

    /// <summary>Multiplier from source to target.</summary>
    public decimal Factor { get; private set; }

    /// <summary>Changes the multiplier.</summary>
    public void SetFactor(decimal factor)
    {
        if (factor <= 0)
        {
            throw new DomainException("masters.uom.invalid_factor", "Factor must be greater than zero.", 400);
        }

        Factor = factor;
    }
}

/// <summary>Purpose of a warehouse.</summary>
public enum WarehouseType
{
    /// <summary>Normal usable stock.</summary>
    General,

    /// <summary>Stock waiting for QC; not available to issue or MRP.</summary>
    Quarantine,

    /// <summary>Line-side / production stock.</summary>
    Production,

    /// <summary>Rejected or scrap stock; not available.</summary>
    Scrap,
}

/// <summary>Warehouse.</summary>
public sealed class Warehouse : CodedMaster
{
    /// <summary>Purpose of the warehouse.</summary>
    public WarehouseType WarehouseType { get; private set; }

    /// <summary>Sets the purpose.</summary>
    public void SetType(WarehouseType type) => WarehouseType = type;
}

/// <summary>Storage location inside a warehouse.</summary>
public sealed class Location : CodedMaster
{
    /// <summary>Owning warehouse.</summary>
    public Guid WarehouseId { get; private set; }

    /// <summary>Assigns the warehouse; it cannot change once stock may exist.</summary>
    public void SetWarehouse(Guid warehouseId)
    {
        if (WarehouseId != Guid.Empty && WarehouseId != warehouseId)
        {
            throw DomainException.Conflict("masters.location.warehouse_fixed", "A location cannot be moved to another warehouse.");
        }

        WarehouseId = warehouseId;
    }
}

/// <summary>Contact and tax data shared by customers and suppliers.</summary>
public abstract class BusinessPartner : CodedMaster
{
    /// <summary>Tax id (13 digits for Thai companies).</summary>
    public string? TaxId { get; private set; }

    /// <summary>Branch number on tax invoices ("00000" = head office).</summary>
    public string? BranchNo { get; private set; }

    /// <summary>Address.</summary>
    public string? Address { get; private set; }

    /// <summary>Phone.</summary>
    public string? Phone { get; private set; }

    /// <summary>Email.</summary>
    public string? Email { get; private set; }

    /// <summary>Contact person.</summary>
    public string? ContactName { get; private set; }

    /// <summary>Payment term in days.</summary>
    public int CreditDays { get; private set; }

    /// <summary>Sets contact and tax data.</summary>
    public void SetContact(string? taxId, string? branchNo, string? address, string? phone, string? email, string? contactName, int creditDays)
    {
        if (creditDays is < 0 or > 365)
        {
            throw new DomainException("masters.partner.invalid_credit_days", "Credit days must be between 0 and 365.", 400);
        }

        TaxId = Clean(taxId);
        BranchNo = Clean(branchNo);
        Address = Clean(address);
        Phone = Clean(phone);
        Email = Clean(email);
        ContactName = Clean(contactName);
        CreditDays = creditDays;
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>Customer.</summary>
public sealed class Customer : BusinessPartner
{
    /// <summary>Credit limit in the tenant currency; 0 = unlimited.</summary>
    public decimal CreditLimit { get; private set; }

    /// <summary>Whether sales orders are checked against the credit limit.</summary>
    public bool CheckCredit { get; private set; }

    /// <summary>Sets the credit policy.</summary>
    public void SetCredit(decimal creditLimit, bool checkCredit)
    {
        if (creditLimit < 0)
        {
            throw new DomainException("masters.customer.invalid_credit_limit", "Credit limit must not be negative.", 400);
        }

        CreditLimit = creditLimit;
        CheckCredit = checkCredit;
    }
}

/// <summary>Supplier.</summary>
public sealed class Supplier : BusinessPartner
{
    /// <summary>ISO 4217 currency of purchase documents.</summary>
    public string Currency { get; private set; } = "THB";

    /// <summary>VAT percent applied to purchases from this supplier; null = tenant default.</summary>
    public decimal? VatPercent { get; private set; }

    /// <summary>Sets purchase terms.</summary>
    public void SetTerms(string currency, decimal? vatPercent)
    {
        var code = (currency ?? string.Empty).Trim().ToUpperInvariant();
        if (code.Length != 3 || !code.All(char.IsAsciiLetterUpper))
        {
            throw new DomainException("masters.supplier.invalid_currency", "Currency must be a 3-letter ISO code.", 400);
        }

        if (vatPercent is < 0 or > 100)
        {
            throw new DomainException("masters.supplier.invalid_vat", "VAT percent must be between 0 and 100.", 400);
        }

        Currency = code;
        VatPercent = vatPercent;
    }
}

/// <summary>Quantity-tier purchase price of an item from a supplier.</summary>
public sealed class SupplierPrice : Entity
{
    private SupplierPrice()
    {
    }

    /// <summary>Creates a price tier.</summary>
    public SupplierPrice(Guid supplierId, Guid itemId, Guid unitId, decimal minQty, decimal unitPrice, string currency, DateOnly validFrom, DateOnly? validTo)
    {
        SupplierId = supplierId;
        ItemId = itemId;
        Update(unitId, minQty, unitPrice, currency, validFrom, validTo);
    }

    /// <summary>Supplier.</summary>
    public Guid SupplierId { get; private set; }

    /// <summary>Item.</summary>
    public Guid ItemId { get; private set; }

    /// <summary>Unit the price and minimum quantity are expressed in.</summary>
    public Guid UnitId { get; private set; }

    /// <summary>Tier applies from this quantity.</summary>
    public decimal MinQty { get; private set; }

    /// <summary>Price per unit.</summary>
    public decimal UnitPrice { get; private set; }

    /// <summary>ISO 4217 currency.</summary>
    public string Currency { get; private set; } = "THB";

    /// <summary>First valid day.</summary>
    public DateOnly ValidFrom { get; private set; }

    /// <summary>Last valid day; null = open ended.</summary>
    public DateOnly? ValidTo { get; private set; }

    /// <summary>Changes the tier.</summary>
    public void Update(Guid unitId, decimal minQty, decimal unitPrice, string currency, DateOnly validFrom, DateOnly? validTo)
    {
        if (minQty < 0 || unitPrice < 0)
        {
            throw new DomainException("masters.price.negative", "Quantity and price must not be negative.", 400);
        }

        if (validTo is { } to && to < validFrom)
        {
            throw new DomainException("masters.price.invalid_period", "Valid-to must not be before valid-from.", 400);
        }

        UnitId = unitId;
        MinQty = minQty;
        UnitPrice = unitPrice;
        Currency = (currency ?? "THB").Trim().ToUpperInvariant();
        ValidFrom = validFrom;
        ValidTo = validTo;
    }

    /// <summary>Whether the tier is valid on the given day.</summary>
    public bool IsValidOn(DateOnly date) => ValidFrom <= date && (ValidTo is null || date <= ValidTo);
}
