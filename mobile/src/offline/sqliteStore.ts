import type { QueueDisplay, QueueItem, QueueKind, QueueScope, QueueStatus, QueueStore } from "./types";

/** Value that can be bound to a SQL parameter. */
export type SqlValue = string | number | null;

/** The part of `expo-sqlite`'s `SQLiteDatabase` used by the queue (also implemented in tests). */
export interface SqlDatabase {
  execAsync(sql: string): Promise<void>;
  runAsync(sql: string, params: SqlValue[]): Promise<{ changes: number }>;
  getAllAsync<T>(sql: string, params: SqlValue[]): Promise<T[]>;
  getFirstAsync<T>(sql: string, params: SqlValue[]): Promise<T | null>;
}

/** Row of the table `offline_queue`. */
interface QueueRow {
  id: string;
  tenant_id: string;
  user_id: string;
  kind: string;
  status: string;
  payload: string;
  display: string;
  server_document_id: string | null;
  server_document_no: string | null;
  server_status: string | null;
  create_attempted: number;
  draft_synced: number;
  attempts: number;
  last_error_code: string | null;
  last_error_message: string | null;
  last_error_status: number | null;
  created_at: string;
  updated_at: string;
  sent_at: string | null;
}

const SCHEMA = `
CREATE TABLE IF NOT EXISTS offline_queue (
  id TEXT PRIMARY KEY NOT NULL,
  tenant_id TEXT NOT NULL,
  user_id TEXT NOT NULL,
  kind TEXT NOT NULL,
  status TEXT NOT NULL CHECK (status IN ('pending', 'sending', 'failed', 'sent')),
  payload TEXT NOT NULL,
  display TEXT NOT NULL,
  server_document_id TEXT,
  server_document_no TEXT,
  server_status TEXT,
  create_attempted INTEGER NOT NULL DEFAULT 0,
  draft_synced INTEGER NOT NULL DEFAULT 0,
  attempts INTEGER NOT NULL DEFAULT 0,
  last_error_code TEXT,
  last_error_message TEXT,
  last_error_status INTEGER,
  created_at TEXT NOT NULL,
  updated_at TEXT NOT NULL,
  sent_at TEXT
);
CREATE INDEX IF NOT EXISTS ix_offline_queue_scope ON offline_queue (tenant_id, user_id, status, created_at);
`;

const COLUMNS =
  "id, tenant_id, user_id, kind, status, payload, display, server_document_id, server_document_no, server_status, " +
  "create_attempted, draft_synced, attempts, last_error_code, last_error_message, last_error_status, created_at, updated_at, sent_at";

/** Creates the queue table when it does not exist. */
export async function migrateQueue(db: SqlDatabase): Promise<void> {
  await db.execAsync(SCHEMA);
}

/** Converts a row to an item. */
function toItem(row: QueueRow): QueueItem {
  return {
    id: row.id,
    tenantId: row.tenant_id,
    userId: row.user_id,
    kind: row.kind as QueueKind,
    status: row.status as QueueStatus,
    payload: JSON.parse(row.payload) as QueueItem["payload"],
    display: JSON.parse(row.display) as QueueDisplay,
    serverDocumentId: row.server_document_id,
    serverDocumentNo: row.server_document_no,
    serverStatus: row.server_status as QueueItem["serverStatus"],
    createAttempted: row.create_attempted === 1,
    draftSynced: row.draft_synced === 1,
    attempts: row.attempts,
    lastErrorCode: row.last_error_code,
    lastErrorMessage: row.last_error_message,
    lastErrorStatus: row.last_error_status,
    createdAt: row.created_at,
    updatedAt: row.updated_at,
    sentAt: row.sent_at,
  };
}

/** SQL placeholders for a list. */
function placeholders(count: number): string {
  return Array.from({ length: count }, () => "?").join(", ");
}

/** Refuses an item that does not belong to the scope. */
function assertScope(scope: QueueScope, item: QueueItem): void {
  if (!scope.tenantId || !scope.userId) {
    throw new Error("Queue scope requires a tenant and a user.");
  }
  if (item.tenantId !== scope.tenantId || item.userId !== scope.userId) {
    throw new Error("Queue item belongs to another tenant or user.");
  }
}

/**
 * Queue storage on SQLite. Every statement filters by `tenant_id` and `user_id`, and all values
 * are bound as parameters. The table holds document data only, never tokens.
 */
export function createSqliteQueueStore(db: SqlDatabase): QueueStore {
  return {
    async insert(scope, item) {
      assertScope(scope, item);
      await db.runAsync(`INSERT INTO offline_queue (${COLUMNS}) VALUES (${placeholders(19)})`, [
        item.id,
        item.tenantId,
        item.userId,
        item.kind,
        item.status,
        JSON.stringify(item.payload),
        JSON.stringify(item.display),
        item.serverDocumentId,
        item.serverDocumentNo,
        item.serverStatus,
        item.createAttempted ? 1 : 0,
        item.draftSynced ? 1 : 0,
        item.attempts,
        item.lastErrorCode,
        item.lastErrorMessage,
        item.lastErrorStatus,
        item.createdAt,
        item.updatedAt,
        item.sentAt,
      ]);
    },

    async update(scope, item) {
      assertScope(scope, item);
      await db.runAsync(
        `UPDATE offline_queue SET kind = ?, status = ?, payload = ?, display = ?, server_document_id = ?, server_document_no = ?,
           server_status = ?, create_attempted = ?, draft_synced = ?, attempts = ?, last_error_code = ?, last_error_message = ?,
           last_error_status = ?, updated_at = ?, sent_at = ?
         WHERE id = ? AND tenant_id = ? AND user_id = ?`,
        [
          item.kind,
          item.status,
          JSON.stringify(item.payload),
          JSON.stringify(item.display),
          item.serverDocumentId,
          item.serverDocumentNo,
          item.serverStatus,
          item.createAttempted ? 1 : 0,
          item.draftSynced ? 1 : 0,
          item.attempts,
          item.lastErrorCode,
          item.lastErrorMessage,
          item.lastErrorStatus,
          item.updatedAt,
          item.sentAt,
          item.id,
          scope.tenantId,
          scope.userId,
        ],
      );
    },

    async get(scope, id) {
      const row = await db.getFirstAsync<QueueRow>(
        `SELECT ${COLUMNS} FROM offline_queue WHERE id = ? AND tenant_id = ? AND user_id = ?`,
        [id, scope.tenantId, scope.userId],
      );
      return row ? toItem(row) : null;
    },

    async list(scope, statuses) {
      const filter = statuses && statuses.length > 0 ? ` AND status IN (${placeholders(statuses.length)})` : "";
      const rows = await db.getAllAsync<QueueRow>(
        `SELECT ${COLUMNS} FROM offline_queue WHERE tenant_id = ? AND user_id = ?${filter} ORDER BY created_at, id`,
        [scope.tenantId, scope.userId, ...(statuses ?? [])],
      );
      return rows.map(toItem);
    },

    async count(scope, statuses) {
      if (statuses.length === 0) {
        return 0;
      }
      const row = await db.getFirstAsync<{ total: number }>(
        `SELECT COUNT(*) AS total FROM offline_queue WHERE tenant_id = ? AND user_id = ? AND status IN (${placeholders(statuses.length)})`,
        [scope.tenantId, scope.userId, ...statuses],
      );
      return row?.total ?? 0;
    },

    async remove(scope, id) {
      await db.runAsync("DELETE FROM offline_queue WHERE id = ? AND tenant_id = ? AND user_id = ?", [id, scope.tenantId, scope.userId]);
    },

    async removeSentBefore(scope, isoTimestamp) {
      const result = await db.runAsync(
        "DELETE FROM offline_queue WHERE tenant_id = ? AND user_id = ? AND status = 'sent' AND sent_at IS NOT NULL AND sent_at < ?",
        [scope.tenantId, scope.userId, isoTimestamp],
      );
      return result.changes;
    },

    async recoverInterrupted(scope, nowIso) {
      const result = await db.runAsync(
        "UPDATE offline_queue SET status = 'pending', updated_at = ? WHERE tenant_id = ? AND user_id = ? AND status = 'sending'",
        [nowIso, scope.tenantId, scope.userId],
      );
      return result.changes;
    },
  };
}
