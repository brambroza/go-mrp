using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Mrp.Masters.Application;
using Mrp.Masters.Domain;
using Mrp.Masters.Persistence;
using Mrp.SharedKernel.Domain;
using Mrp.SharedKernel.Persistence;
using Mrp.SharedKernel.Web;

namespace Mrp.Masters;

/// <summary>Registration and endpoints of the masters module.</summary>
public static class MastersModule
{
    /// <summary>Adds the masters DbContext and services.</summary>
    public static IServiceCollection AddMastersModule(this IServiceCollection services)
    {
        services.AddModuleDbContext<MastersDbContext>(MastersDbContext.SchemaName);
        services.AddScoped<MasterData>();
        AddMaster<Unit, CodedDto, SaveCodedRequest, UnitDefinition>(services);
        AddMaster<ItemGroup, CodedDto, SaveCodedRequest, ItemGroupDefinition>(services);
        AddMaster<Item, ItemDto, SaveItemRequest, ItemDefinition>(services);
        AddMaster<UnitConversion, UnitConversionDto, SaveUnitConversionRequest, UnitConversionDefinition>(services);
        AddMaster<Warehouse, WarehouseDto, SaveWarehouseRequest, WarehouseDefinition>(services);
        AddMaster<Location, LocationDto, SaveLocationRequest, LocationDefinition>(services);
        AddMaster<Customer, CustomerDto, SaveCustomerRequest, CustomerDefinition>(services);
        AddMaster<Supplier, SupplierDto, SaveSupplierRequest, SupplierDefinition>(services);
        AddMaster<SupplierPrice, SupplierPriceDto, SaveSupplierPriceRequest, SupplierPriceDefinition>(services);
        return services;
    }

    /// <summary>Maps <c>/masters/*</c>.</summary>
    public static IEndpointRouteBuilder MapMastersEndpoints(this IEndpointRouteBuilder api)
    {
        var masters = api.MapGroup("/masters");
        MapMaster<Unit, CodedDto, SaveCodedRequest>(masters, "units", "Unit");
        MapMaster<ItemGroup, CodedDto, SaveCodedRequest>(masters, "item-groups", "ItemGroup");
        MapMaster<Item, ItemDto, SaveItemRequest>(masters, "items", "Item");
        MapMaster<UnitConversion, UnitConversionDto, SaveUnitConversionRequest>(masters, "unit-conversions", "UnitConversion");
        MapMaster<Warehouse, WarehouseDto, SaveWarehouseRequest>(masters, "warehouses", "Warehouse");
        MapMaster<Location, LocationDto, SaveLocationRequest>(masters, "locations", "Location");
        MapMaster<Customer, CustomerDto, SaveCustomerRequest>(masters, "customers", "Customer");
        MapMaster<Supplier, SupplierDto, SaveSupplierRequest>(masters, "suppliers", "Supplier");
        MapMaster<SupplierPrice, SupplierPriceDto, SaveSupplierPriceRequest>(masters, "supplier-prices", "SupplierPrice");

        masters.MapGet("/items/{id:guid}/convert", (Guid id, Guid from, Guid to, decimal qty, MasterData data, CancellationToken ct) =>
                data.ConvertAsync(id, from, to, qty, ct))
            .WithTags("Masters").RequirePermission(Permissions.MastersRead).WithName("ConvertItemQuantity");

        masters.MapGet("/supplier-prices/resolve", async (Guid supplierId, Guid itemId, Guid unitId, decimal qty, DateOnly date, MasterData data, CancellationToken ct) =>
                await data.ResolveSupplierPriceAsync(supplierId, itemId, unitId, qty, date, ct) is { } price ? Results.Ok(price) : Results.NoContent())
            .WithTags("Masters").RequirePermission(Permissions.MastersRead).WithName("ResolveSupplierPrice")
            .Produces<SupplierPriceDto>().Produces(StatusCodes.Status204NoContent);
        return api;
    }

    private static void AddMaster<TEntity, TDto, TRequest, TDefinition>(IServiceCollection services)
        where TEntity : Entity
        where TDefinition : class, IMasterDefinition<TEntity, TDto, TRequest>
    {
        services.AddSingleton<IMasterDefinition<TEntity, TDto, TRequest>, TDefinition>();
        services.AddScoped<MasterCrud<TEntity, TDto, TRequest>>();
    }

    private static void MapMaster<TEntity, TDto, TRequest>(IEndpointRouteBuilder masters, string path, string name)
        where TEntity : Entity
        where TRequest : class
    {
        var group = masters.MapGroup($"/{path}").WithTags("Masters");
        group.MapGet("/", (string? search, bool? activeOnly, int? page, int? pageSize, MasterCrud<TEntity, TDto, TRequest> crud, CancellationToken ct) =>
                crud.ListAsync(search, activeOnly, page, pageSize, ct))
            .RequirePermission(Permissions.MastersRead).WithName($"List{name}s");
        group.MapGet("/{id:guid}", (Guid id, MasterCrud<TEntity, TDto, TRequest> crud, CancellationToken ct) => crud.GetAsync(id, ct))
            .RequirePermission(Permissions.MastersRead).WithName($"Get{name}");
        group.MapPost("/", (TRequest request, MasterCrud<TEntity, TDto, TRequest> crud, CancellationToken ct) => crud.CreateAsync(request, ct))
            .Validate<TRequest>().RequirePermission(Permissions.MastersManage).WithName($"Create{name}");
        group.MapPut("/{id:guid}", (Guid id, TRequest request, MasterCrud<TEntity, TDto, TRequest> crud, CancellationToken ct) =>
                crud.UpdateAsync(id, request, ct))
            .Validate<TRequest>().RequirePermission(Permissions.MastersManage).WithName($"Update{name}");
    }
}
