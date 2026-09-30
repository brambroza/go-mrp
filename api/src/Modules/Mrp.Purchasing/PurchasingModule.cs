using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Mrp.Purchasing.Application;
using Mrp.Purchasing.Domain;
using Mrp.Purchasing.Persistence;
using Mrp.SharedKernel.Domain;
using Mrp.SharedKernel.Persistence;
using Mrp.SharedKernel.Web;

namespace Mrp.Purchasing;

/// <summary>Registration and endpoints of the purchasing module.</summary>
public static class PurchasingModule
{
    /// <summary>Adds the purchasing DbContext and services.</summary>
    public static IServiceCollection AddPurchasingModule(this IServiceCollection services)
    {
        services.AddModuleDbContext<PurchasingDbContext>(PurchasingDbContext.SchemaName);
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<PurchasingService>();
        services.AddScoped<IPurchaseOrderGateway, PurchaseOrderGateway>();
        services.AddScoped<IApprovalSubscriber>(sp => new PurchasingApprovalSubscriber(DocumentTypes.PurchaseRequest, sp));
        services.AddScoped<IApprovalSubscriber>(sp => new PurchasingApprovalSubscriber(DocumentTypes.PurchaseOrder, sp));
        return services;
    }

    /// <summary>Maps <c>/purchasing/*</c>.</summary>
    public static IEndpointRouteBuilder MapPurchasingEndpoints(this IEndpointRouteBuilder api)
    {
        var purchasing = api.MapGroup("/purchasing").WithTags("Purchasing");

        var requests = purchasing.MapGroup("/requests");
        requests.MapGet("/", (PurchaseRequestStatus? status, string? search, int? page, int? pageSize, PurchasingService service, CancellationToken ct) =>
                service.ListRequestsAsync(status, search, page, pageSize, ct))
            .RequirePermission(Permissions.PurchasingRead).WithName("ListPurchaseRequests");
        requests.MapGet("/open-lines", (Guid? supplierId, PurchasingService service, CancellationToken ct) => service.OpenRequestLinesAsync(supplierId, ct))
            .RequirePermission(Permissions.PurchasingRead).WithName("ListOpenPurchaseRequestLines");
        requests.MapGet("/{id:guid}", (Guid id, PurchasingService service, CancellationToken ct) => service.GetRequestAsync(id, ct))
            .RequirePermission(Permissions.PurchasingRead).WithName("GetPurchaseRequest");
        requests.MapPost("/", (SavePurchaseRequest request, PurchasingService service, CancellationToken ct) => service.CreateRequestAsync(request, ct))
            .Validate<SavePurchaseRequest>().RequirePermission(Permissions.PurchaseRequestManage).WithName("CreatePurchaseRequest");
        requests.MapPut("/{id:guid}", (Guid id, SavePurchaseRequest request, PurchasingService service, CancellationToken ct) =>
                service.UpdateRequestAsync(id, request, ct))
            .Validate<SavePurchaseRequest>().RequirePermission(Permissions.PurchaseRequestManage).WithName("UpdatePurchaseRequest");
        requests.MapPost("/{id:guid}/submit", (Guid id, PurchasingService service, CancellationToken ct) => service.SubmitRequestAsync(id, ct))
            .RequirePermission(Permissions.PurchaseRequestManage).WithName("SubmitPurchaseRequest");
        requests.MapPost("/{id:guid}/cancel", (Guid id, ReasonRequest request, PurchasingService service, CancellationToken ct) =>
                service.EndRequestAsync(id, PurchaseAction.Cancel, request.Reason, ct))
            .Validate<ReasonRequest>().RequirePermission(Permissions.PurchaseRequestManage).WithName("CancelPurchaseRequest");
        requests.MapPost("/{id:guid}/close", (Guid id, ReasonRequest request, PurchasingService service, CancellationToken ct) =>
                service.EndRequestAsync(id, PurchaseAction.Close, request.Reason, ct))
            .Validate<ReasonRequest>().RequirePermission(Permissions.PurchaseRequestManage).WithName("ClosePurchaseRequest");

        var orders = purchasing.MapGroup("/orders");
        orders.MapGet("/", (PurchaseOrderStatus? status, Guid? supplierId, bool? receivableOnly, string? search, int? page, int? pageSize,
                PurchasingService service, CancellationToken ct) =>
                service.ListOrdersAsync(status, supplierId, receivableOnly, search, page, pageSize, ct))
            .RequirePermission(Permissions.PurchasingRead).WithName("ListPurchaseOrders");
        orders.MapGet("/{id:guid}", (Guid id, PurchasingService service, CancellationToken ct) => service.GetOrderAsync(id, ct))
            .RequirePermission(Permissions.PurchasingRead).WithName("GetPurchaseOrder");
        orders.MapPost("/", (SavePurchaseOrder request, PurchasingService service, CancellationToken ct) => service.CreateOrderAsync(request, ct))
            .Validate<SavePurchaseOrder>().RequirePermission(Permissions.PurchaseOrderManage).WithName("CreatePurchaseOrder");
        orders.MapPut("/{id:guid}", (Guid id, SavePurchaseOrder request, PurchasingService service, CancellationToken ct) =>
                service.UpdateOrderAsync(id, request, ct))
            .Validate<SavePurchaseOrder>().RequirePermission(Permissions.PurchaseOrderManage).WithName("UpdatePurchaseOrder");
        orders.MapPost("/{id:guid}/submit", (Guid id, PurchasingService service, CancellationToken ct) => service.SubmitOrderAsync(id, ct))
            .RequirePermission(Permissions.PurchaseOrderManage).WithName("SubmitPurchaseOrder");
        orders.MapPost("/{id:guid}/cancel", (Guid id, ReasonRequest request, PurchasingService service, CancellationToken ct) =>
                service.EndOrderAsync(id, PurchaseAction.Cancel, request.Reason, ct))
            .Validate<ReasonRequest>().RequirePermission(Permissions.PurchaseOrderManage).WithName("CancelPurchaseOrder");
        orders.MapPost("/{id:guid}/close", (Guid id, ReasonRequest request, PurchasingService service, CancellationToken ct) =>
                service.EndOrderAsync(id, PurchaseAction.Close, request.Reason, ct))
            .Validate<ReasonRequest>().RequirePermission(Permissions.PurchaseOrderManage).WithName("ClosePurchaseOrder");
        orders.MapPost("/{id:guid}/lines/{lineId:guid}/close", (Guid id, Guid lineId, PurchasingService service, CancellationToken ct) =>
                service.CloseOrderLineAsync(id, lineId, ct))
            .RequirePermission(Permissions.PurchaseOrderManage).WithName("ClosePurchaseOrderLine");
        return api;
    }

    /// <summary>Forwards approval results to the purchasing service, resolved on use to avoid a dependency cycle.</summary>
    private sealed class PurchasingApprovalSubscriber(string documentType, IServiceProvider services) : IApprovalSubscriber
    {
        public string DocumentType => documentType;

        public Task OnCompletedAsync(Guid documentId, ApprovalOutcome outcome, Guid actedBy, string? comment, CancellationToken cancellationToken) =>
            services.GetRequiredService<PurchasingService>().OnApprovalCompletedAsync(documentType, documentId, outcome, comment, cancellationToken);
    }
}
