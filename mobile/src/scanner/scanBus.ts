/**
 * Hands the text read by the scanner screen back to the screen that asked for it.
 * Only one scan can be open at a time; opening a new one cancels the previous request.
 */

type Resolver = (text: string | null) => void;

let pending: Resolver | null = null;

/** Starts waiting for a scan. Resolves with the text, or `null` when the user closed the scanner. */
export function beginScan(): Promise<string | null> {
  pending?.(null);
  return new Promise<string | null>((resolve) => {
    pending = resolve;
  });
}

/** Delivers the result to the waiting screen. Later calls for the same scan are ignored. */
export function completeScan(text: string | null): void {
  const resolve = pending;
  pending = null;
  resolve?.(text);
}

/** Returns whether a screen is waiting for a scan. */
export function isScanPending(): boolean {
  return pending !== null;
}
