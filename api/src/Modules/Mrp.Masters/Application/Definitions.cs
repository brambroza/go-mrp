using Microsoft.EntityFrameworkCore;
using Mrp.Masters.Domain;
using Mrp.Masters.Persistence;
using Mrp.SharedKernel.Domain;

namespace Mrp.Masters.Application;

internal abstract class CodedDefinition<T>(string name) : IMasterDefinition<T, CodedDto, SaveCodedRequest>
    where T : CodedMaster, new()
{
    public string Name => name;

    public IQueryable<T> Filter(IQueryable<T> query, string? search, bool activeOnly) => MasterQueries.FilterCoded(query, search, activeOnly);

    public IQueryable<T> Order(IQueryable<T> query) => query.OrderBy(e => e.Code);

    public T Create(SaveCodedRequest request) => new();

    public Task ApplyAsync(T entity, SaveCodedRequest request, MastersDbContext db, CancellationToken cancellationToken)
    {
        entity.SetIdentity(request.Code, request.Name, request.NameEn, request.IsActive);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<CodedDto>> ToDtoAsync(IReadOnlyList<T> entities, MastersDbContext db, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<CodedDto>>(entities.Select(e => new CodedDto(e.Id, e.Code, e.Name, e.NameEn, e.IsActive)).ToList());
}

internal sealed class UnitDefinition() : CodedDefinition<Unit>("Unit");

internal sealed class ItemGroupDefinition() : CodedDefinition<ItemGroup>("Item group");

internal sealed class ItemDefinition : IMasterDefinition<Item, ItemDto, SaveItemRequest>
{
    public string Name => "Item";

    public IQueryable<Item> Filter(IQueryable<Item> query, string? search, bool activeOnly)
    {
        if (search is not null)
        {
            var barcode = search;
            var pattern = $"%{MasterQueries.EscapeLike(search)}%";
            query = query.Where(e => e.Barcode == barcode || EF.Functions.ILike(e.Code, pattern, "\\") || EF.Functions.ILike(e.Name, pattern, "\\")
                || (e.NameEn != null && EF.Functions.ILike(e.NameEn, pattern, "\\")));
        }

        return activeOnly ? query.Where(e => e.IsActive) : query;
    }

    public IQueryable<Item> Order(IQueryable<Item> query) => query.OrderBy(e => e.Code);

    public Item Create(SaveItemRequest request) => new();

    public async Task ApplyAsync(Item entity, SaveItemRequest request, MastersDbContext db, CancellationToken cancellationToken)
    {
        await MasterQueries.RequireAsync<Unit>(db, request.StockUnitId, "Unit", cancellationToken);
        if (request.PurchaseUnitId is { } purchaseUnit)
        {
            await MasterQueries.RequireAsync<Unit>(db, purchaseUnit, "Unit", cancellationToken);
        }

        if (request.ItemGroupId is { } group)
        {
            await MasterQueries.RequireAsync<ItemGroup>(db, group, "Item group", cancellationToken);
        }

        if (entity.StockUnitId != Guid.Empty && entity.StockUnitId != request.StockUnitId)
        {
            // Ledger quantities are stored in the stock unit; changing it would silently rescale all history.
            throw DomainException.Conflict("masters.item.stock_unit_fixed", "The stock unit of an item cannot be changed.");
        }

        entity.SetIdentity(request.Code, request.Name, request.NameEn, request.IsActive);
        entity.SetDetails(new ItemDetails(
            request.ItemType, request.SupplyType, request.ItemGroupId, request.StockUnitId, request.PurchaseUnitId, request.Barcode,
            request.IsLotTracked, request.ShelfLifeDays, request.LeadTimeDays, request.SafetyStock, request.MinStock, request.MaxStock,
            request.MinOrderQty, request.OrderMultiple, request.StandardCost, request.SalesPrice));
    }

    public async Task<IReadOnlyList<ItemDto>> ToDtoAsync(IReadOnlyList<Item> entities, MastersDbContext db, CancellationToken cancellationToken)
    {
        var unitIds = entities.Select(e => e.StockUnitId).Distinct().ToList();
        var units = await db.Units.AsNoTracking().Where(u => unitIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Code, cancellationToken);
        return entities.Select(e => new ItemDto(
            e.Id, e.Code, e.Name, e.NameEn, e.IsActive, e.ItemType, e.SupplyType, e.ItemGroupId, e.StockUnitId,
            units.GetValueOrDefault(e.StockUnitId, string.Empty), e.PurchaseUnitId, e.Barcode, e.IsLotTracked, e.ShelfLifeDays, e.LeadTimeDays,
            e.SafetyStock, e.MinStock, e.MaxStock, e.MinOrderQty, e.OrderMultiple, e.StandardCost, e.SalesPrice)).ToList();
    }
}

internal sealed class UnitConversionDefinition : IMasterDefinition<UnitConversion, UnitConversionDto, SaveUnitConversionRequest>
{
    public string Name => "Unit conversion";

    public IQueryable<UnitConversion> Filter(IQueryable<UnitConversion> query, string? search, bool activeOnly) =>
        Guid.TryParse(search, out var itemId) ? query.Where(c => c.ItemId == itemId) : query;

    public IQueryable<UnitConversion> Order(IQueryable<UnitConversion> query) => query.OrderBy(c => c.ItemId).ThenBy(c => c.Id);

    public UnitConversion Create(SaveUnitConversionRequest request) => new(request.ItemId, request.FromUnitId, request.ToUnitId, request.Factor);

    public async Task ApplyAsync(UnitConversion entity, SaveUnitConversionRequest request, MastersDbContext db, CancellationToken cancellationToken)
    {
        if (entity.ItemId != request.ItemId || entity.FromUnitId != request.FromUnitId || entity.ToUnitId != request.ToUnitId)
        {
            throw DomainException.Conflict("masters.uom.key_fixed", "Item and units of a conversion cannot be changed; create a new one.");
        }

        await MasterQueries.RequireAsync<Unit>(db, request.FromUnitId, "Unit", cancellationToken);
        await MasterQueries.RequireAsync<Unit>(db, request.ToUnitId, "Unit", cancellationToken);
        if (request.ItemId is { } itemId)
        {
            await MasterQueries.RequireAsync<Item>(db, itemId, "Item", cancellationToken);
        }

        var reverseExists = await db.UnitConversions.AnyAsync(
            c => c.Id != entity.Id && c.ItemId == request.ItemId && c.FromUnitId == request.ToUnitId && c.ToUnitId == request.FromUnitId,
            cancellationToken);
        if (reverseExists)
        {
            throw DomainException.Conflict("masters.uom.reverse_exists", "The reverse conversion already exists; edit that one instead.");
        }

        entity.SetFactor(request.Factor);
    }

    public Task<IReadOnlyList<UnitConversionDto>> ToDtoAsync(IReadOnlyList<UnitConversion> entities, MastersDbContext db, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<UnitConversionDto>>(
            entities.Select(c => new UnitConversionDto(c.Id, c.ItemId, c.FromUnitId, c.ToUnitId, c.Factor)).ToList());
}

internal sealed class WarehouseDefinition : IMasterDefinition<Warehouse, WarehouseDto, SaveWarehouseRequest>
{
    public string Name => "Warehouse";

    public IQueryable<Warehouse> Filter(IQueryable<Warehouse> query, string? search, bool activeOnly) => MasterQueries.FilterCoded(query, search, activeOnly);

    public IQueryable<Warehouse> Order(IQueryable<Warehouse> query) => query.OrderBy(e => e.Code);

    public Warehouse Create(SaveWarehouseRequest request) => new();

    public Task ApplyAsync(Warehouse entity, SaveWarehouseRequest request, MastersDbContext db, CancellationToken cancellationToken)
    {
        entity.SetIdentity(request.Code, request.Name, request.NameEn, request.IsActive);
        entity.SetType(request.WarehouseType);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<WarehouseDto>> ToDtoAsync(IReadOnlyList<Warehouse> entities, MastersDbContext db, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<WarehouseDto>>(
            entities.Select(e => new WarehouseDto(e.Id, e.Code, e.Name, e.NameEn, e.IsActive, e.WarehouseType)).ToList());
}

internal sealed class LocationDefinition : IMasterDefinition<Location, LocationDto, SaveLocationRequest>
{
    public string Name => "Location";

    public IQueryable<Location> Filter(IQueryable<Location> query, string? search, bool activeOnly) =>
        Guid.TryParse(search, out var warehouseId)
            ? (activeOnly ? query.Where(l => l.IsActive) : query).Where(l => l.WarehouseId == warehouseId)
            : MasterQueries.FilterCoded(query, search, activeOnly);

    public IQueryable<Location> Order(IQueryable<Location> query) => query.OrderBy(e => e.WarehouseId).ThenBy(e => e.Code);

    public Location Create(SaveLocationRequest request) => new();

    public async Task ApplyAsync(Location entity, SaveLocationRequest request, MastersDbContext db, CancellationToken cancellationToken)
    {
        await MasterQueries.RequireAsync<Warehouse>(db, request.WarehouseId, "Warehouse", cancellationToken);
        entity.SetWarehouse(request.WarehouseId);
        entity.SetIdentity(request.Code, request.Name, request.NameEn, request.IsActive);
    }

    public Task<IReadOnlyList<LocationDto>> ToDtoAsync(IReadOnlyList<Location> entities, MastersDbContext db, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<LocationDto>>(
            entities.Select(e => new LocationDto(e.Id, e.WarehouseId, e.Code, e.Name, e.NameEn, e.IsActive)).ToList());
}

internal sealed class CustomerDefinition : IMasterDefinition<Customer, CustomerDto, SaveCustomerRequest>
{
    public string Name => "Customer";

    public IQueryable<Customer> Filter(IQueryable<Customer> query, string? search, bool activeOnly) => MasterQueries.FilterCoded(query, search, activeOnly);

    public IQueryable<Customer> Order(IQueryable<Customer> query) => query.OrderBy(e => e.Code);

    public Customer Create(SaveCustomerRequest request) => new();

    public Task ApplyAsync(Customer entity, SaveCustomerRequest request, MastersDbContext db, CancellationToken cancellationToken)
    {
        entity.SetIdentity(request.Code, request.Name, request.NameEn, request.IsActive);
        entity.SetContact(request.TaxId, request.BranchNo, request.Address, request.Phone, request.Email, request.ContactName, request.CreditDays);
        entity.SetCredit(request.CreditLimit, request.CheckCredit);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<CustomerDto>> ToDtoAsync(IReadOnlyList<Customer> entities, MastersDbContext db, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<CustomerDto>>(entities.Select(e => new CustomerDto(
            e.Id, e.Code, e.Name, e.NameEn, e.IsActive, e.TaxId, e.BranchNo, e.Address, e.Phone, e.Email, e.ContactName,
            e.CreditDays, e.CreditLimit, e.CheckCredit)).ToList());
}

internal sealed class SupplierDefinition : IMasterDefinition<Supplier, SupplierDto, SaveSupplierRequest>
{
    public string Name => "Supplier";

    public IQueryable<Supplier> Filter(IQueryable<Supplier> query, string? search, bool activeOnly) => MasterQueries.FilterCoded(query, search, activeOnly);

    public IQueryable<Supplier> Order(IQueryable<Supplier> query) => query.OrderBy(e => e.Code);

    public Supplier Create(SaveSupplierRequest request) => new();

    public Task ApplyAsync(Supplier entity, SaveSupplierRequest request, MastersDbContext db, CancellationToken cancellationToken)
    {
        entity.SetIdentity(request.Code, request.Name, request.NameEn, request.IsActive);
        entity.SetContact(request.TaxId, request.BranchNo, request.Address, request.Phone, request.Email, request.ContactName, request.CreditDays);
        entity.SetTerms(request.Currency, request.VatPercent);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<SupplierDto>> ToDtoAsync(IReadOnlyList<Supplier> entities, MastersDbContext db, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<SupplierDto>>(entities.Select(e => new SupplierDto(
            e.Id, e.Code, e.Name, e.NameEn, e.IsActive, e.TaxId, e.BranchNo, e.Address, e.Phone, e.Email, e.ContactName,
            e.CreditDays, e.Currency, e.VatPercent)).ToList());
}

internal sealed class SupplierPriceDefinition : IMasterDefinition<SupplierPrice, SupplierPriceDto, SaveSupplierPriceRequest>
{
    public string Name => "Supplier price";

    public IQueryable<SupplierPrice> Filter(IQueryable<SupplierPrice> query, string? search, bool activeOnly) =>
        Guid.TryParse(search, out var id) ? query.Where(p => p.SupplierId == id || p.ItemId == id) : query;

    public IQueryable<SupplierPrice> Order(IQueryable<SupplierPrice> query) =>
        query.OrderBy(p => p.SupplierId).ThenBy(p => p.ItemId).ThenBy(p => p.MinQty).ThenBy(p => p.ValidFrom);

    public SupplierPrice Create(SaveSupplierPriceRequest request) =>
        new(request.SupplierId, request.ItemId, request.UnitId, request.MinQty, request.UnitPrice, request.Currency, request.ValidFrom, request.ValidTo);

    public async Task ApplyAsync(SupplierPrice entity, SaveSupplierPriceRequest request, MastersDbContext db, CancellationToken cancellationToken)
    {
        if (entity.SupplierId != request.SupplierId || entity.ItemId != request.ItemId)
        {
            throw DomainException.Conflict("masters.price.key_fixed", "Supplier and item of a price cannot be changed; create a new one.");
        }

        await MasterQueries.RequireAsync<Supplier>(db, request.SupplierId, "Supplier", cancellationToken);
        await MasterQueries.RequireAsync<Item>(db, request.ItemId, "Item", cancellationToken);
        await MasterQueries.RequireAsync<Unit>(db, request.UnitId, "Unit", cancellationToken);
        entity.Update(request.UnitId, request.MinQty, request.UnitPrice, request.Currency, request.ValidFrom, request.ValidTo);
    }

    public Task<IReadOnlyList<SupplierPriceDto>> ToDtoAsync(IReadOnlyList<SupplierPrice> entities, MastersDbContext db, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<SupplierPriceDto>>(entities.Select(p => new SupplierPriceDto(
            p.Id, p.SupplierId, p.ItemId, p.UnitId, p.MinQty, p.UnitPrice, p.Currency, p.ValidFrom, p.ValidTo)).ToList());
}
