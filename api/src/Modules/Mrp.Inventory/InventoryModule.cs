using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Mrp.Inventory.Application;
using Mrp.Inventory.Domain;
using Mrp.Inventory.Persistence;
using Mrp.SharedKernel.Domain;
using Mrp.SharedKernel.Persistence;
using Mrp.SharedKernel.Web;

namespace Mrp.Inventory;

/// <summary>Registration and endpoints of the inventory module.</summary>
public static class InventoryModule
{
    /// <summary>Adds the inventory DbContext and services.</summary>
    public static IServiceCollection AddInventoryModule(this IServiceCollection services)
    {
        services.AddModuleDbContext<InventoryDbContext>(InventoryDbContext.SchemaName);
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddScoped<IPurchaseOrderGateway, NoPurchaseOrders>();
        services.AddScoped<StockLedger>();
        services.AddScoped<StockDocumentService>();
        services.AddScoped<InventoryQueries>();
        services.AddScoped<PeriodService>();
        foreach (var type in Enum.GetValues<StockDocumentType>())
        {
            services.AddScoped<IApprovalSubscriber>(sp =>
                new StockDocumentApprovalSubscriber(StockDocument.KeyOf(type), sp));
        }

        return services;
    }

    /// <summary>Maps <c>/inventory/*</c>.</summary>
    public static IEndpointRouteBuilder MapInventoryEndpoints(this IEndpointRouteBuilder api)
    {
        var inventory = api.MapGroup("/inventory").WithTags("Inventory");

        var documents = inventory.MapGroup("/documents");
        documents.MapGet("/", (StockDocumentType? type, StockDocumentStatus? status, string? search, DateOnly? from, DateOnly? to, Guid? referenceId,
                int? page, int? pageSize, StockDocumentService service, CancellationToken ct) =>
                service.ListAsync(type, status, search, from, to, referenceId, page, pageSize, ct))
            .RequirePermission(Permissions.InventoryRead).WithName("ListStockDocuments");
        documents.MapGet("/{id:guid}", (Guid id, StockDocumentService service, CancellationToken ct) => service.GetAsync(id, ct))
            .RequirePermission(Permissions.InventoryRead).WithName("GetStockDocument");
        documents.MapPost("/", (SaveStockDocumentRequest request, StockDocumentService service, CancellationToken ct) => service.CreateAsync(request, ct))
            .Validate<SaveStockDocumentRequest>().RequireAuthorization().WithName("CreateStockDocument");
        documents.MapPut("/{id:guid}", (Guid id, SaveStockDocumentRequest request, StockDocumentService service, CancellationToken ct) =>
                service.UpdateAsync(id, request, ct))
            .Validate<SaveStockDocumentRequest>().RequireAuthorization().WithName("UpdateStockDocument");
        documents.MapPost("/count-sheet", (CreateCountSheetRequest request, StockDocumentService service, CancellationToken ct) =>
                service.CreateCountSheetAsync(request, ct))
            .Validate<CreateCountSheetRequest>().RequireAuthorization().WithName("CreateCountSheet");
        documents.MapPost("/{id:guid}/submit", (Guid id, StockDocumentService service, CancellationToken ct) => service.SubmitAsync(id, ct))
            .RequireAuthorization().WithName("SubmitStockDocument");
        documents.MapPost("/{id:guid}/post", (Guid id, StockDocumentService service, CancellationToken ct) => service.PostAsync(id, ct))
            .RequireAuthorization().WithName("PostStockDocument");
        documents.MapPost("/{id:guid}/void", (Guid id, VoidRequest request, StockDocumentService service, CancellationToken ct) =>
                service.VoidAsync(id, request.Reason, ct))
            .Validate<VoidRequest>().RequireAuthorization().WithName("VoidStockDocument");

        var lots = inventory.MapGroup("/lots");
        lots.MapGet("/", (Guid? itemId, LotQcStatus? status, string? search, int? page, int? pageSize, InventoryQueries queries, CancellationToken ct) =>
                queries.ListLotsAsync(itemId, status, search, page, pageSize, ct))
            .RequirePermission(Permissions.InventoryRead).WithName("ListLots");
        lots.MapGet("/by-number/{lotNo}", (string lotNo, InventoryQueries queries, CancellationToken ct) => queries.GetLotByNumberAsync(lotNo, ct))
            .RequirePermission(Permissions.InventoryRead).WithName("GetLotByNumber");
        lots.MapPost("/{id:guid}/qc", (Guid id, LotQcRequest request, InventoryQueries queries, CancellationToken ct) =>
                queries.SetQcStatusAsync(id, request, ct))
            .Validate<LotQcRequest>().RequirePermission(Permissions.InventoryQc).WithName("SetLotQcStatus");

        inventory.MapGet("/on-hand", (Guid? itemId, Guid? warehouseId, Guid? lotId, bool? availableOnly, int? page, int? pageSize,
                InventoryQueries queries, CancellationToken ct) =>
                queries.OnHandAsync(itemId, warehouseId, lotId, availableOnly ?? false, page, pageSize, ct))
            .RequirePermission(Permissions.InventoryRead).WithName("GetOnHand");
        inventory.MapGet("/stock-card", (Guid itemId, Guid? warehouseId, DateOnly from, DateOnly to, InventoryQueries queries, CancellationToken ct) =>
                queries.StockCardAsync(itemId, warehouseId, from, to, ct))
            .RequirePermission(Permissions.InventoryRead).WithName("GetStockCard");
        inventory.MapGet("/allocation-preview", (Guid itemId, Guid warehouseId, decimal quantity, InventoryQueries queries, CancellationToken ct) =>
                queries.PreviewAllocationAsync(itemId, warehouseId, quantity, ct))
            .RequirePermission(Permissions.InventoryRead).WithName("PreviewAllocation");

        var periods = inventory.MapGroup("/periods");
        periods.MapGet("/", (PeriodService service, CancellationToken ct) => service.ListAsync(ct))
            .RequirePermission(Permissions.InventoryRead).WithName("ListStockPeriods");
        periods.MapPost("/{year:int}/{month:int}/close", (int year, int month, PeriodService service, CancellationToken ct) =>
                service.CloseAsync(year, month, ct))
            .RequirePermission(Permissions.InventoryClosePeriod).WithName("CloseStockPeriod");
        periods.MapPost("/{year:int}/{month:int}/reopen", (int year, int month, ReopenPeriodRequest request, PeriodService service, CancellationToken ct) =>
                service.ReopenAsync(year, month, request.Reason, ct))
            .Validate<ReopenPeriodRequest>().RequirePermission(Permissions.InventoryClosePeriod).WithName("ReopenStockPeriod");
        return api;
    }

    /// <summary>
    /// Forwards approval results of one document type to the document service. The service is resolved
    /// on use because it depends on the approval engine itself.
    /// </summary>
    private sealed class StockDocumentApprovalSubscriber(string documentType, IServiceProvider services) : IApprovalSubscriber
    {
        public string DocumentType => documentType;

        public Task OnCompletedAsync(Guid documentId, ApprovalOutcome outcome, Guid actedBy, string? comment, CancellationToken cancellationToken) =>
            services.GetRequiredService<StockDocumentService>().OnApprovalCompletedAsync(documentId, outcome, comment, cancellationToken);
    }

    /// <summary>Used when the purchasing module is not loaded: no purchase order can be referenced.</summary>
    private sealed class NoPurchaseOrders : IPurchaseOrderGateway
    {
        public Task<IReadOnlyDictionary<Guid, PurchaseOrderLineInfo>> GetLinesAsync(IReadOnlyCollection<Guid> lineIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<Guid, PurchaseOrderLineInfo>>(new Dictionary<Guid, PurchaseOrderLineInfo>());

        public Task ApplyReceiptAsync(IReadOnlyDictionary<Guid, decimal> stockQuantityByLine, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
