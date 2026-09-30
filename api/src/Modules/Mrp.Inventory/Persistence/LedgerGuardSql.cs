namespace Mrp.Inventory.Persistence;

/// <summary>
/// Database-level protection of the stock ledger, applied by the initial migration: the ledger is
/// append-only and closed periods are locked, so no application bug or ad-hoc script can rewrite history.
/// </summary>
public static class LedgerGuardSql
{
    /// <summary>Creates the guard function and triggers.</summary>
    public const string Up = """
        CREATE FUNCTION inventory.movements_guard() RETURNS trigger
        LANGUAGE plpgsql AS $$
        BEGIN
            IF TG_OP <> 'INSERT' THEN
                RAISE EXCEPTION 'inventory.movements is append-only (% not allowed)', TG_OP
                    USING ERRCODE = 'P0001';
            END IF;

            IF EXISTS (
                SELECT 1 FROM inventory.periods p
                WHERE p.tenant_id = NEW.tenant_id
                  AND p.year = extract(year FROM NEW.posting_date)
                  AND p.month = extract(month FROM NEW.posting_date)
                  AND p.status = 'Closed'
            ) THEN
                RAISE EXCEPTION 'stock period % is closed', to_char(NEW.posting_date, 'YYYY-MM')
                    USING ERRCODE = 'P0001';
            END IF;

            RETURN NEW;
        END $$;

        CREATE TRIGGER movements_guard
            BEFORE INSERT OR UPDATE OR DELETE ON inventory.movements
            FOR EACH ROW EXECUTE FUNCTION inventory.movements_guard();

        CREATE TRIGGER movements_no_truncate
            BEFORE TRUNCATE ON inventory.movements
            FOR EACH STATEMENT EXECUTE FUNCTION inventory.movements_guard();
        """;

    /// <summary>Removes the guard function and triggers.</summary>
    public const string Down = """
        DROP TRIGGER IF EXISTS movements_no_truncate ON inventory.movements;
        DROP TRIGGER IF EXISTS movements_guard ON inventory.movements;
        DROP FUNCTION IF EXISTS inventory.movements_guard();
        """;
}
