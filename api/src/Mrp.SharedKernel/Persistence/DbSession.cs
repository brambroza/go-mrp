using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Mrp.SharedKernel.Tenancy;
using Npgsql;

namespace Mrp.SharedKernel.Persistence;

/// <summary>
/// One PostgreSQL connection per scope, shared by every module DbContext so that a single transaction
/// can span modules (document + ledger + numbering). Applies <c>app.tenant_id</c> for Row-Level Security.
/// </summary>
public sealed class DbSession : IAsyncDisposable, IDisposable
{
    private readonly ITenantContext _tenant;
    private readonly List<DbContext> _contexts = [];
    private int _appliedVersion = -1;

    /// <summary>Creates the session; the connection is opened lazily.</summary>
    public DbSession(NpgsqlDataSource dataSource, ITenantContext tenant)
    {
        _tenant = tenant;
        Connection = dataSource.CreateConnection();
    }

    /// <summary>The shared connection.</summary>
    public NpgsqlConnection Connection { get; }

    /// <summary>The transaction opened by <see cref="ExecuteInTransactionAsync{T}"/>, if any.</summary>
    public NpgsqlTransaction? Transaction { get; private set; }

    /// <summary>True when the tenant changed since it was last applied to the open connection.</summary>
    public bool TenantDirty => Connection.State == ConnectionState.Open && _appliedVersion != _tenant.Version;

    /// <summary>Registers a context so it takes part in the shared transaction.</summary>
    public void Register(DbContext context)
    {
        _contexts.Add(context);
        if (Transaction is not null)
        {
            context.Database.UseTransaction(Transaction);
        }
    }

    /// <summary>Returns the connection opened with the tenant applied; for Dapper queries.</summary>
    public async Task<NpgsqlConnection> GetOpenConnectionAsync(CancellationToken cancellationToken = default)
    {
        if (Connection.State != ConnectionState.Open)
        {
            await Connection.OpenAsync(cancellationToken);
            _appliedVersion = -1;
        }

        await ApplyTenantAsync(cancellationToken);
        return Connection;
    }

    /// <summary>Marks the connection as freshly opened so the tenant is applied again.</summary>
    public void OnConnectionOpened() => _appliedVersion = -1;

    /// <summary>Sets <c>app.tenant_id</c> on the open connection when it is stale.</summary>
    public async Task ApplyTenantAsync(CancellationToken cancellationToken = default)
    {
        if (Connection.State != ConnectionState.Open || _appliedVersion == _tenant.Version)
        {
            return;
        }

        await using var command = CreateTenantCommand();
        await command.ExecuteNonQueryAsync(cancellationToken);
        _appliedVersion = _tenant.Version;
    }

    /// <summary>Synchronous variant of <see cref="ApplyTenantAsync"/>.</summary>
    public void ApplyTenant()
    {
        if (Connection.State != ConnectionState.Open || _appliedVersion == _tenant.Version)
        {
            return;
        }

        using var command = CreateTenantCommand();
        command.ExecuteNonQuery();
        _appliedVersion = _tenant.Version;
    }

    /// <summary>
    /// Runs <paramref name="work"/> in one database transaction shared by all module contexts.
    /// Nested calls join the outer transaction.
    /// </summary>
    public async Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken cancellationToken = default)
    {
        if (Transaction is not null)
        {
            return await work(cancellationToken);
        }

        await GetOpenConnectionAsync(cancellationToken);
        Transaction = await Connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        foreach (var context in _contexts)
        {
            await context.Database.UseTransactionAsync(Transaction, cancellationToken);
        }

        try
        {
            var result = await work(cancellationToken);
            await Transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch
        {
            await Transaction.RollbackAsync(CancellationToken.None);
            foreach (var context in _contexts)
            {
                context.ChangeTracker.Clear();
            }

            throw;
        }
        finally
        {
            foreach (var context in _contexts)
            {
                await context.Database.UseTransactionAsync(null, CancellationToken.None);
            }

            await Transaction.DisposeAsync();
            Transaction = null;
        }
    }

    /// <summary>Runs <paramref name="work"/> in one database transaction shared by all module contexts.</summary>
    public Task ExecuteInTransactionAsync(Func<CancellationToken, Task> work, CancellationToken cancellationToken = default) =>
        ExecuteInTransactionAsync(
            async ct =>
            {
                await work(ct);
                return true;
            },
            cancellationToken);

    /// <inheritdoc />
    public ValueTask DisposeAsync() => Connection.DisposeAsync();

    /// <inheritdoc />
    public void Dispose() => Connection.Dispose();

    private NpgsqlCommand CreateTenantCommand()
    {
        var command = Connection.CreateCommand();
        command.Transaction = Transaction;
        command.CommandText = "SELECT set_config('app.tenant_id', @tenant, false)";
        command.Parameters.AddWithValue("tenant", _tenant.TenantId?.ToString() ?? string.Empty);
        return command;
    }
}
