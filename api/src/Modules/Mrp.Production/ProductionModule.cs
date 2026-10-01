using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Mrp.Production.Application;
using Mrp.Production.Domain;
using Mrp.Production.Persistence;
using Mrp.SharedKernel.Domain;
using Mrp.SharedKernel.Persistence;
using Mrp.SharedKernel.Web;

namespace Mrp.Production;

/// <summary>Registration and endpoints of the production module.</summary>
public static class ProductionModule
{
    /// <summary>Adds the production DbContext and services.</summary>
    public static IServiceCollection AddProductionModule(this IServiceCollection services)
    {
        services.AddModuleDbContext<ProductionDbContext>(ProductionDbContext.SchemaName);
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<BomService>();
        services.AddScoped<WorkOrderService>();
        services.AddScoped<MrpService>();
        services.AddScoped<CostingService>();
        services.AddScoped<SchedulingService>();
        services.AddScoped<IStockReferenceHandler, WorkOrderStockHandler>();
        return services;
    }

    /// <summary>Maps <c>/production/*</c>.</summary>
    public static IEndpointRouteBuilder MapProductionEndpoints(this IEndpointRouteBuilder api)
    {
        var production = api.MapGroup("/production").WithTags("Production");
        MapBoms(production);
        MapDemands(production);
        MapMrp(production);
        MapWorkOrders(production);
        MapCosting(production);
        MapScheduling(production);
        return api;
    }

    private static void MapScheduling(IEndpointRouteBuilder production)
    {
        var machines = production.MapGroup("/machines");
        machines.MapGet("/", (SchedulingService service, CancellationToken ct) => service.ListMachinesAsync(ct))
            .RequirePermission(Permissions.ProductionRead).WithName("ListMachines");
        machines.MapPost("/", (SaveMachineRequest request, SchedulingService service, CancellationToken ct) => service.CreateMachineAsync(request, ct))
            .Validate<SaveMachineRequest>().RequirePermission(Permissions.BomManage).WithName("CreateMachine");
        machines.MapPut("/{id:guid}", (Guid id, SaveMachineRequest request, SchedulingService service, CancellationToken ct) => service.UpdateMachineAsync(id, request, ct))
            .Validate<SaveMachineRequest>().RequirePermission(Permissions.BomManage).WithName("UpdateMachine");

        production.MapGet("/items/{itemId:guid}/routing", (Guid itemId, SchedulingService service, CancellationToken ct) => service.GetRoutingAsync(itemId, ct))
            .RequirePermission(Permissions.ProductionRead).WithName("GetRouting");
        production.MapPut("/items/{itemId:guid}/routing", (Guid itemId, SaveRoutingRequest request, SchedulingService service, CancellationToken ct) =>
                service.SaveRoutingAsync(itemId, request, ct))
            .Validate<SaveRoutingRequest>().RequirePermission(Permissions.BomManage).WithName("SaveRouting");

        var holidays = production.MapGroup("/holidays");
        holidays.MapGet("/", (DateOnly? from, SchedulingService service, CancellationToken ct) => service.ListHolidaysAsync(from, ct))
            .RequirePermission(Permissions.ProductionRead).WithName("ListHolidays");
        holidays.MapPost("/", (SaveHolidayRequest request, SchedulingService service, CancellationToken ct) => service.CreateHolidayAsync(request, ct))
            .Validate<SaveHolidayRequest>().RequirePermission(Permissions.BomManage).WithName("CreateHoliday");
        holidays.MapDelete("/{id:guid}", async (Guid id, SchedulingService service, CancellationToken ct) =>
            {
                await service.DeleteHolidayAsync(id, ct);
                return Results.NoContent();
            })
            .RequirePermission(Permissions.BomManage).WithName("DeleteHoliday");

        var schedule = production.MapGroup("/schedule");
        schedule.MapPost("/run", (RunScheduleRequest request, SchedulingService service, CancellationToken ct) => service.RunAsync(request, ct))
            .Validate<RunScheduleRequest>().RequirePermission(Permissions.WorkOrderManage).WithName("RunSchedule");
        schedule.MapGet("/", (DateOnly from, DateOnly to, SchedulingService service, CancellationToken ct) => service.GetGanttAsync(from, to, ct))
            .RequirePermission(Permissions.ProductionRead).WithName("GetSchedule");
        schedule.MapGet("/work-orders/{workOrderId:guid}", (Guid workOrderId, SchedulingService service, CancellationToken ct) => service.GetWorkOrderSlotsAsync(workOrderId, ct))
            .RequirePermission(Permissions.ProductionRead).WithName("GetWorkOrderSchedule");
        schedule.MapPost("/slots/{id:guid}/lock", (Guid id, SchedulingService service, CancellationToken ct) => service.SetLockAsync(id, true, ct))
            .RequirePermission(Permissions.WorkOrderManage).WithName("LockScheduleSlot");
        schedule.MapPost("/slots/{id:guid}/unlock", (Guid id, SchedulingService service, CancellationToken ct) => service.SetLockAsync(id, false, ct))
            .RequirePermission(Permissions.WorkOrderManage).WithName("UnlockScheduleSlot");
    }

    private static void MapCosting(IEndpointRouteBuilder production)
    {
        var costing = production.MapGroup("/costing");
        costing.MapGet("/", (CostingService service, CancellationToken ct) => service.ListAsync(ct))
            .RequirePermission(Permissions.ProductionRead).WithName("ListItemCosts");
        costing.MapGet("/items/{itemId:guid}", (Guid itemId, decimal? quantity, CostingService service, CancellationToken ct) =>
                service.GetAsync(itemId, quantity ?? 1m, ct))
            .RequirePermission(Permissions.ProductionRead).WithName("GetItemCostBreakdown");
        costing.MapPost("/apply", (ApplyCostsRequest request, CostingService service, CancellationToken ct) => service.ApplyAsync(request, ct))
            .Validate<ApplyCostsRequest>().RequirePermission(Permissions.BomManage).WithName("ApplyRolledUpCosts");
    }

    private static void MapBoms(IEndpointRouteBuilder production)
    {
        var boms = production.MapGroup("/boms");
        boms.MapGet("/", (Guid? itemId, BomStatus? status, int? page, int? pageSize, BomService service, CancellationToken ct) =>
                service.ListAsync(itemId, status, page, pageSize, ct))
            .RequirePermission(Permissions.ProductionRead).WithName("ListBoms");
        boms.MapGet("/{id:guid}", (Guid id, BomService service, CancellationToken ct) => service.GetAsync(id, ct))
            .RequirePermission(Permissions.ProductionRead).WithName("GetBom");
        boms.MapGet("/{id:guid}/explode", (Guid id, decimal quantity, BomService service, CancellationToken ct) => service.ExplodeAsync(id, quantity, ct))
            .RequirePermission(Permissions.ProductionRead).WithName("ExplodeBom");
        boms.MapPost("/", (SaveBomRequest request, BomService service, CancellationToken ct) => service.CreateAsync(request, ct))
            .Validate<SaveBomRequest>().RequirePermission(Permissions.BomManage).WithName("CreateBom");
        boms.MapPut("/{id:guid}", (Guid id, SaveBomRequest request, BomService service, CancellationToken ct) => service.UpdateAsync(id, request, ct))
            .Validate<SaveBomRequest>().RequirePermission(Permissions.BomManage).WithName("UpdateBom");
        boms.MapPost("/{id:guid}/copy", (Guid id, BomService service, CancellationToken ct) => service.CopyAsync(id, ct))
            .RequirePermission(Permissions.BomManage).WithName("CopyBom");
        boms.MapPost("/{id:guid}/approve", (Guid id, BomService service, CancellationToken ct) => service.ApplyAsync(id, BomAction.Approve, ct))
            .RequirePermission(Permissions.BomManage).WithName("ApproveBom");
        boms.MapPost("/{id:guid}/activate", (Guid id, BomService service, CancellationToken ct) => service.ApplyAsync(id, BomAction.Activate, ct))
            .RequirePermission(Permissions.BomManage).WithName("ActivateBom");
        boms.MapPost("/{id:guid}/cancel", (Guid id, BomService service, CancellationToken ct) => service.ApplyAsync(id, BomAction.Cancel, ct))
            .RequirePermission(Permissions.BomManage).WithName("CancelBom");

        production.MapGet("/items/{itemId:guid}/where-used", (Guid itemId, bool? activeOnly, BomService service, CancellationToken ct) =>
                service.WhereUsedAsync(itemId, activeOnly ?? true, ct))
            .RequirePermission(Permissions.ProductionRead).WithName("GetWhereUsed");
    }

    private static void MapDemands(IEndpointRouteBuilder production)
    {
        var demands = production.MapGroup("/demands");
        demands.MapGet("/", (DemandStatus? status, Guid? itemId, int? page, int? pageSize, WorkOrderService service, CancellationToken ct) =>
                service.ListDemandsAsync(status, itemId, page, pageSize, ct))
            .RequirePermission(Permissions.ProductionRead).WithName("ListDemands");
        demands.MapPost("/", (SaveDemandRequest request, WorkOrderService service, CancellationToken ct) => service.CreateDemandAsync(request, ct))
            .Validate<SaveDemandRequest>().RequirePermission(Permissions.MrpRun).WithName("CreateDemand");
        demands.MapPut("/{id:guid}", (Guid id, SaveDemandRequest request, WorkOrderService service, CancellationToken ct) =>
                service.UpdateDemandAsync(id, request, ct))
            .Validate<SaveDemandRequest>().RequirePermission(Permissions.MrpRun).WithName("UpdateDemand");
        demands.MapPost("/{id:guid}/cancel", (Guid id, WorkOrderService service, CancellationToken ct) => service.EndDemandAsync(id, cancel: true, ct))
            .RequirePermission(Permissions.MrpRun).WithName("CancelDemand");
        demands.MapPost("/{id:guid}/close", (Guid id, WorkOrderService service, CancellationToken ct) => service.EndDemandAsync(id, cancel: false, ct))
            .RequirePermission(Permissions.MrpRun).WithName("CloseDemand");
    }

    private static void MapMrp(IEndpointRouteBuilder production)
    {
        var runs = production.MapGroup("/mrp-runs");
        runs.MapGet("/", (int? page, int? pageSize, MrpService service, CancellationToken ct) => service.ListAsync(page, pageSize, ct))
            .RequirePermission(Permissions.ProductionRead).WithName("ListMrpRuns");
        runs.MapGet("/{id:guid}", (Guid id, MrpService service, CancellationToken ct) => service.GetAsync(id, ct))
            .RequirePermission(Permissions.ProductionRead).WithName("GetMrpRun");
        runs.MapPost("/", (StartMrpRunRequest request, MrpService service, CancellationToken ct) => service.RunAsync(request, ct))
            .Validate<StartMrpRunRequest>().RequirePermission(Permissions.MrpRun).WithName("StartMrpRun");
        runs.MapPost("/{id:guid}/convert", (Guid id, ConvertPlannedOrdersRequest request, MrpService service, CancellationToken ct) =>
                service.ConvertAsync(id, request, ct))
            .Validate<ConvertPlannedOrdersRequest>().RequirePermission(Permissions.MrpRun).WithName("ConvertPlannedOrders");
        production.MapPost("/planned-orders/{id:guid}/dismiss", async (Guid id, MrpService service, CancellationToken ct) =>
            {
                await service.DismissAsync(id, ct);
                return Results.NoContent();
            })
            .RequirePermission(Permissions.MrpRun).WithName("DismissPlannedOrder");
    }

    private static void MapWorkOrders(IEndpointRouteBuilder production)
    {
        var orders = production.MapGroup("/work-orders");
        orders.MapGet("/", (WorkOrderStatus? status, Guid? itemId, bool? openOnly, string? search, int? page, int? pageSize, WorkOrderService service, CancellationToken ct) =>
                service.ListAsync(status, itemId, openOnly, search, page, pageSize, ct))
            .RequirePermission(Permissions.ProductionRead).WithName("ListWorkOrders");
        orders.MapGet("/{id:guid}", (Guid id, WorkOrderService service, CancellationToken ct) => service.GetAsync(id, ct))
            .RequirePermission(Permissions.ProductionRead).WithName("GetWorkOrder");
        orders.MapPost("/", (CreateWorkOrderRequest request, WorkOrderService service, CancellationToken ct) => service.CreateAsync(request, ct))
            .Validate<CreateWorkOrderRequest>().RequirePermission(Permissions.WorkOrderManage).WithName("CreateWorkOrder");

        foreach (var (path, action, name) in new[]
                 {
                     ("release", WorkOrderAction.Release, "ReleaseWorkOrder"),
                     ("hold", WorkOrderAction.Hold, "HoldWorkOrder"),
                     ("resume", WorkOrderAction.Resume, "ResumeWorkOrder"),
                     ("complete", WorkOrderAction.Complete, "CompleteWorkOrder"),
                     ("close", WorkOrderAction.Close, "CloseWorkOrder"),
                     ("cancel", WorkOrderAction.Cancel, "CancelWorkOrder"),
                 })
        {
            orders.MapPost($"/{{id:guid}}/{path}", (Guid id, WorkOrderReasonRequest request, WorkOrderService service, CancellationToken ct) =>
                    service.ApplyAsync(id, action, request.Reason, ct))
                .Validate<WorkOrderReasonRequest>().RequirePermission(Permissions.WorkOrderManage).WithName(name);
        }
    }
}
