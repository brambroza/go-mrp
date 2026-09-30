using Microsoft.EntityFrameworkCore;
using Mrp.SharedKernel.Persistence;
using Npgsql;

namespace Mrp.Api;

/// <summary>Applies schema migrations of every module and the Row-Level Security bootstrap.</summary>
public static class DatabaseMigrator
{
    /// <summary>Runs migrations for all registered module contexts, then enables RLS and grants.</summary>
    public static async Task RunAsync(IServiceProvider services, IConfiguration configuration, ILogger logger, CancellationToken cancellationToken = default)
    {
        var appRole = configuration["Database:AppRole"] ?? "mrp_app";
        await using var scope = services.CreateAsyncScope();
        var schemas = new List<string>();
        var appendOnly = new List<(string Schema, string Table)>();
        foreach (var context in scope.ServiceProvider.GetServices<ModuleDbContext>())
        {
            var schema = context.Model.GetDefaultSchema()
                ?? throw new InvalidOperationException($"{context.GetType().Name} has no default schema.");
            logger.LogInformation("Migrating schema {Schema}", schema);
            await context.Database.MigrateAsync(cancellationToken);
            schemas.Add(schema);
            appendOnly.AddRange(context.AppendOnlyTables.Select(table => (schema, table)));
        }

        var session = scope.ServiceProvider.GetRequiredService<DbSession>();
        var connection = await session.GetOpenConnectionAsync(cancellationToken);
        await RlsBootstrapper.ApplyAsync(connection, schemas, appRole, cancellationToken, appendOnly);
        logger.LogInformation("Row-Level Security applied to schemas {Schemas} for role {Role}", string.Join(", ", schemas), appRole);
    }

    /// <summary>
    /// Refuses to start outside Development when the API's database role could bypass RLS
    /// (superuser or BYPASSRLS), because tenant isolation would then depend on application code only.
    /// </summary>
    public static async Task EnsureSafeRoleAsync(IServiceProvider services, IHostEnvironment environment, ILogger logger, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await services.GetRequiredService<NpgsqlDataSource>().OpenConnectionAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT rolsuper OR rolbypassrls FROM pg_roles WHERE rolname = current_user";
            var unsafeRole = await command.ExecuteScalarAsync(cancellationToken) is true;
            if (!unsafeRole)
            {
                return;
            }

            const string message = "The API database role can bypass Row-Level Security. Connect with a non-superuser role without BYPASSRLS.";
            if (!environment.IsDevelopment())
            {
                throw new InvalidOperationException(message);
            }

            logger.LogWarning(message);
        }
        catch (NpgsqlException exception)
        {
            // The database may not be reachable yet (container start order); requests will fail on their own.
            logger.LogWarning(exception, "Could not verify the database role at startup");
        }
    }
}
