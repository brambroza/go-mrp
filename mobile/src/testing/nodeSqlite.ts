import type { SqlDatabase, SqlValue } from "@/offline/sqliteStore";

/** Shape of the `node:sqlite` module used by the adapter. */
interface NodeSqliteModule {
  DatabaseSync: new (path: string) => {
    exec(sql: string): void;
    prepare(sql: string): {
      run(...params: SqlValue[]): { changes: number | bigint };
      all(...params: SqlValue[]): unknown[];
      get(...params: SqlValue[]): unknown;
    };
    close(): void;
  };
}

/**
 * Test helper: an in-memory SQLite database of Node.js behind the interface of `expo-sqlite`,
 * so that unit tests run the real SQL of the queue store.
 */
export function createNodeSqliteDatabase(): SqlDatabase & { close(): void } {
  const loader = (process as unknown as { getBuiltinModule(name: string): unknown }).getBuiltinModule;
  const sqlite = loader.call(process, "node:sqlite") as NodeSqliteModule;
  const db = new sqlite.DatabaseSync(":memory:");
  return {
    execAsync: async (sql) => {
      db.exec(sql);
    },
    runAsync: async (sql, params) => ({ changes: Number(db.prepare(sql).run(...params).changes) }),
    getAllAsync: async <T>(sql: string, params: SqlValue[]) => db.prepare(sql).all(...params).map((row) => ({ ...(row as object) }) as T),
    getFirstAsync: async <T>(sql: string, params: SqlValue[]) => {
      const row = db.prepare(sql).get(...params);
      return row ? ({ ...(row as object) } as T) : null;
    },
    close: () => db.close(),
  };
}
