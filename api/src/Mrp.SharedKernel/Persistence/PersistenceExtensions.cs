using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Mrp.SharedKernel.Tenancy;
using Npgsql;

namespace Mrp.SharedKernel.Persistence;

/// <summary>Registration helpers for the shared database session and module contexts.</summary>
public static class PersistenceExtensions
{
    /// <summary>Name of the EF migrations history table created in each module schema.</summary>
    public const string MigrationsTable = "__ef_migrations_history";

    /// <summary>Registers the data source, tenant context, and scoped database session.</summary>
    public static IServiceCollection AddMrpPersistence(this IServiceCollection services, string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        services.TryAddSingleton(_ => new NpgsqlDataSourceBuilder(connectionString).Build());
        services.TryAddScoped<TenantContext>();
        services.TryAddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());
        services.TryAddScoped<DbSession>();
        services.TryAddScoped<TenantSessionInterceptor>();
        services.TryAddScoped<TenantCommandInterceptor>();
        services.AddHttpContextAccessor();
        services.TryAddScoped<Web.ICurrentUser, Web.HttpCurrentUser>();
        return services;
    }

    /// <summary>Registers a module DbContext on the shared connection with snake_case names.</summary>
    public static IServiceCollection AddModuleDbContext<TContext>(this IServiceCollection services, string schema)
        where TContext : ModuleDbContext
    {
        services.AddDbContext<TContext>((sp, options) => options
            .UseNpgsql(
                sp.GetRequiredService<DbSession>().Connection,
                npgsql => npgsql.MigrationsHistoryTable(MigrationsTable, schema))
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(
                sp.GetRequiredService<TenantSessionInterceptor>(),
                sp.GetRequiredService<TenantCommandInterceptor>()));
        services.AddScoped<ModuleDbContext>(sp => sp.GetRequiredService<TContext>());
        return services;
    }
}
