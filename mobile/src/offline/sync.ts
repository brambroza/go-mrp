import type { AppError } from "@/lib/errors";

import { stopsSync } from "./machine";
import { sendItem, type DocumentApi, type SendContext } from "./sender";
import type { QueueItem, QueueScope, QueueStore } from "./types";

/** Sent items are kept this long as a history, then removed from the device. */
export const SENT_RETENTION_MS = 7 * 24 * 60 * 60 * 1000;

/** Summary of one sync run. */
export interface SyncSummary {
  sent: number;
  failed: number;
  /** Items still waiting (offline or session expired). */
  waiting: number;
  /** Reason the run stopped early. */
  stoppedBy?: AppError;
}

/** Result of sending one item immediately. */
export interface SendNowResult {
  /** The item after the attempt; `null` when it does not exist in the scope. */
  item: QueueItem | null;
  /** Present when the send did not succeed. */
  error?: AppError;
}

/** Dependencies of {@link QueueSync}. */
export interface QueueSyncOptions {
  store: QueueStore;
  api: DocumentApi;
  /** Clock in milliseconds. */
  now?: () => number;
  /** Called after every change so that screens can reload. */
  onChange?: () => void;
}

/**
 * Sends pending items of one scope, one at a time and oldest first. All work goes through one
 * chain, so two sends never run at the same time and an item is never sent twice concurrently.
 * Items in `failed` are never picked up.
 */
export class QueueSync {
  private tail: Promise<void> = Promise.resolve();
  private waitingRun: { key: string; promise: Promise<SyncSummary> } | null = null;
  private readonly store: QueueStore;
  private readonly api: DocumentApi;
  private readonly now: () => number;
  private readonly onChange: (() => void) | undefined;

  constructor(options: QueueSyncOptions) {
    this.store = options.store;
    this.api = options.api;
    this.now = options.now ?? Date.now;
    this.onChange = options.onChange;
  }

  /** Runs a sync for the scope. A call made while one is waiting to start joins that one. */
  run(scope: QueueScope): Promise<SyncSummary> {
    const key = `${scope.tenantId}/${scope.userId}`;
    if (this.waitingRun?.key === key) {
      return this.waitingRun.promise;
    }
    const promise = this.exclusive(() => {
      if (this.waitingRun?.promise === promise) {
        this.waitingRun = null;
      }
      return this.execute(scope);
    });
    this.waitingRun = { key, promise };
    return promise;
  }

  /** Sends one pending item as soon as the running work is done (after "submit" or "send again"). */
  sendNow(scope: QueueScope, id: string): Promise<SendNowResult> {
    return this.exclusive(async () => {
      const item = await this.store.get(scope, id);
      if (!item || item.status !== "pending") {
        return { item };
      }
      const result = await sendItem(this.context(scope), item);
      this.onChange?.();
      return result;
    });
  }

  /** Waits until all queued work is done. */
  async idle(): Promise<void> {
    await this.tail;
  }

  private exclusive<T>(task: () => Promise<T>): Promise<T> {
    const result = this.tail.then(task);
    this.tail = result.then(
      () => undefined,
      () => undefined,
    );
    return result;
  }

  private context(scope: QueueScope): SendContext {
    return { store: this.store, api: this.api, scope, now: () => new Date(this.now()).toISOString() };
  }

  private async execute(scope: QueueScope): Promise<SyncSummary> {
    const summary: SyncSummary = { sent: 0, failed: 0, waiting: 0 };
    const nowIso = new Date(this.now()).toISOString();
    await this.store.recoverInterrupted(scope, nowIso);
    await this.store.removeSentBefore(scope, new Date(this.now() - SENT_RETENTION_MS).toISOString());

    const pending = await this.store.list(scope, ["pending"]);
    for (let index = 0; index < pending.length; index += 1) {
      const item = pending[index];
      if (!item) {
        continue;
      }
      const result = await sendItem(this.context(scope), item);
      this.onChange?.();
      if (!result.error) {
        summary.sent += 1;
        continue;
      }
      if (result.item.status === "failed") {
        summary.failed += 1;
      } else {
        summary.waiting += 1;
      }
      if (stopsSync(result.error)) {
        summary.waiting += pending.length - index - 1;
        summary.stoppedBy = result.error;
        break;
      }
    }
    return summary;
  }
}
