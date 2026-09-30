using Mrp.Inventory;
using Mrp.Masters;
using Mrp.Platform;
using Mrp.Purchasing;
using Mrp.Platform.Endpoints;
using Mrp.Production;
using Mrp.SharedKernel.Persistence;

namespace Mrp.Api;

/// <summary>Single place that lists the modules of the monolith.</summary>
public static class ModuleRegistration
{
    /// <summary>Registers persistence and every module.</summary>
    public static IServiceCollection AddMrpModules(this IServiceCollection services, IConfiguration configuration, string connectionString)
    {
        services.AddMrpPersistence(connectionString);
        services.AddPlatformModule(configuration);
        services.AddMastersModule();
        services.AddInventoryModule();
        services.AddPurchasingModule();
        services.AddProductionModule();
        return services;
    }

    /// <summary>Maps the endpoints of every module under the given group.</summary>
    public static IEndpointRouteBuilder MapMrpModules(this IEndpointRouteBuilder api)
    {
        api.MapPlatformEndpoints();
        api.MapMastersEndpoints();
        api.MapInventoryEndpoints();
        api.MapPurchasingEndpoints();
        api.MapProductionEndpoints();
        return api;
    }
}
