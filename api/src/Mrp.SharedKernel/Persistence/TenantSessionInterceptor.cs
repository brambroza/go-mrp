using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Mrp.SharedKernel.Persistence;

/// <summary>
/// Keeps <c>app.tenant_id</c> on the shared connection in sync with the tenant context:
/// when EF opens the connection and before any command after the tenant changed.
/// </summary>
public sealed class TenantSessionInterceptor(DbSession session) : DbConnectionInterceptor
{
    /// <inheritdoc />
    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        session.OnConnectionOpened();
        session.ApplyTenant();
    }

    /// <inheritdoc />
    public override async Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        session.OnConnectionOpened();
        await session.ApplyTenantAsync(cancellationToken);
    }
}

/// <summary>Re-applies the tenant before a command when it changed while the connection stayed open.</summary>
public sealed class TenantCommandInterceptor(DbSession session) : DbCommandInterceptor
{
    /// <inheritdoc />
    public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        Apply();
        return result;
    }

    /// <inheritdoc />
    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        await ApplyAsync(cancellationToken);
        return result;
    }

    /// <inheritdoc />
    public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
    {
        Apply();
        return result;
    }

    /// <inheritdoc />
    public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        await ApplyAsync(cancellationToken);
        return result;
    }

    /// <inheritdoc />
    public override InterceptionResult<object> ScalarExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
    {
        Apply();
        return result;
    }

    /// <inheritdoc />
    public override async ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<object> result, CancellationToken cancellationToken = default)
    {
        await ApplyAsync(cancellationToken);
        return result;
    }

    private void Apply()
    {
        if (session.TenantDirty)
        {
            session.ApplyTenant();
        }
    }

    private Task ApplyAsync(CancellationToken cancellationToken) =>
        session.TenantDirty ? session.ApplyTenantAsync(cancellationToken) : Task.CompletedTask;
}
