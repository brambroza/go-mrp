using Microsoft.EntityFrameworkCore;
using Mrp.Platform.Domain;
using Mrp.Platform.Persistence;
using Mrp.SharedKernel.Domain;
using Mrp.SharedKernel.Persistence;
using Mrp.SharedKernel.Tenancy;
using Npgsql;

namespace Mrp.Platform.Application;

/// <summary>
/// Issues document numbers from <c>platform.document_sequences</c> with an atomic upsert on the shared
/// connection, so the number is part of the caller's transaction and concurrent requests never collide.
/// </summary>
public sealed class DocumentNumberGenerator(PlatformDbContext db, DbSession session, ITenantContext tenant, ITenantSettings settings)
    : IDocumentNumberGenerator
{
    private const string NextSql = """
        INSERT INTO platform.document_sequences (tenant_id, document_type, period_key, last_number)
        VALUES (@tenant, @type, @period, 1)
        ON CONFLICT (tenant_id, document_type, period_key)
        DO UPDATE SET last_number = platform.document_sequences.last_number + 1
        RETURNING last_number
        """;

    /// <inheritdoc />
    public async Task<string> NextAsync(string documentType, DateTimeOffset documentDate, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentType);
        var format = await db.DocumentNumberFormats.AsNoTracking()
            .FirstOrDefaultAsync(f => f.DocumentType == documentType, cancellationToken)
            ?? DocumentNumberFormat.Default(documentType);

        var zone = await settings.GetTimeZoneAsync(cancellationToken);
        var localDate = TimeZoneInfo.ConvertTime(documentDate, zone).DateTime;

        var connection = await session.GetOpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(NextSql, connection, session.Transaction);
        command.Parameters.AddWithValue("tenant", tenant.RequireTenantId());
        command.Parameters.AddWithValue("type", documentType);
        command.Parameters.AddWithValue("period", format.PeriodKey(localDate));
        var running = (long)(await command.ExecuteScalarAsync(cancellationToken))!;
        return format.Format(localDate, running);
    }
}
