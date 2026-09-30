using Npgsql;

namespace Mrp.SharedKernel.Persistence;

/// <summary>
/// Applies Row-Level Security to every table that has a <c>tenant_id</c> column and grants the
/// application role access. Idempotent; run by the migrator after schema migrations.
/// </summary>
public static class RlsBootstrapper
{
    /// <summary>Name of the policy created on every tenant table.</summary>
    public const string PolicyName = "tenant_isolation";

    private const string Script = """
        DO $$
        DECLARE
            t record;
            s text;
        BEGIN
            FOR t IN
                SELECT c.table_schema, c.table_name
                FROM information_schema.columns c
                JOIN information_schema.tables tb
                  ON tb.table_schema = c.table_schema AND tb.table_name = c.table_name AND tb.table_type = 'BASE TABLE'
                WHERE c.column_name = 'tenant_id' AND c.table_schema = ANY (@schemas)
            LOOP
                EXECUTE format('ALTER TABLE %I.%I ENABLE ROW LEVEL SECURITY', t.table_schema, t.table_name);
                EXECUTE format('ALTER TABLE %I.%I FORCE ROW LEVEL SECURITY', t.table_schema, t.table_name);
                IF NOT EXISTS (
                    SELECT 1 FROM pg_policies
                    WHERE schemaname = t.table_schema AND tablename = t.table_name AND policyname = 'tenant_isolation'
                ) THEN
                    EXECUTE format(
                        'CREATE POLICY tenant_isolation ON %I.%I '
                        || 'USING (tenant_id = nullif(current_setting(''app.tenant_id'', true), '''')::uuid) '
                        || 'WITH CHECK (tenant_id = nullif(current_setting(''app.tenant_id'', true), '''')::uuid)',
                        t.table_schema, t.table_name);
                END IF;
            END LOOP;

            FOREACH s IN ARRAY @schemas
            LOOP
                IF EXISTS (SELECT 1 FROM pg_namespace WHERE nspname = s) THEN
                    EXECUTE format('GRANT USAGE ON SCHEMA %I TO %I', s, @app_role);
                    EXECUTE format('GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA %I TO %I', s, @app_role);
                    EXECUTE format('GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA %I TO %I', s, @app_role);
                    IF to_regclass(format('%I.%I', s, '__ef_migrations_history')) IS NOT NULL THEN
                        EXECUTE format('REVOKE ALL ON TABLE %I.%I FROM %I', s, '__ef_migrations_history', @app_role);
                    END IF;
                END IF;
            END LOOP;
        END $$;
        """;

    /// <summary>Enables RLS and grants on all tenant tables of the given schemas.</summary>
    /// <param name="connection">Open connection of the schema owner.</param>
    /// <param name="schemas">Module schemas to process.</param>
    /// <param name="appRole">Role the API connects with (must not own the tables).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="appendOnlyTables">Schema-qualified tables the application role must not update or delete.</param>
    public static async Task ApplyAsync(
        NpgsqlConnection connection,
        IReadOnlyCollection<string> schemas,
        string appRole,
        CancellationToken cancellationToken = default,
        IReadOnlyCollection<(string Schema, string Table)>? appendOnlyTables = null)
    {
        ValidateIdentifier(appRole);
        foreach (var schema in schemas)
        {
            ValidateIdentifier(schema);
        }

        // DO blocks cannot take bind parameters, so the validated identifiers are embedded as SQL literals.
        var schemaArray = "ARRAY[" + string.Join(",", schemas.Select(s => $"'{s}'")) + "]::text[]";
        var sql = Script.Replace("@schemas", schemaArray, StringComparison.Ordinal)
            .Replace("@app_role", $"'{appRole}'", StringComparison.Ordinal);

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);

        foreach (var (schema, table) in appendOnlyTables ?? [])
        {
            ValidateIdentifier(schema);
            ValidateIdentifier(table);
            await using var revoke = connection.CreateCommand();
            revoke.CommandText = $"REVOKE UPDATE, DELETE, TRUNCATE ON TABLE \"{schema}\".\"{table}\" FROM \"{appRole}\"";
            await revoke.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static void ValidateIdentifier(string value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 63 || !value.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '_'))
        {
            throw new ArgumentException($"'{value}' is not a valid lowercase PostgreSQL identifier.");
        }
    }
}
