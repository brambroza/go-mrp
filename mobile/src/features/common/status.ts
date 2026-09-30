import type { Lot } from "@/api/inventory";
import type { QueueStatus } from "@/offline/types";
import type { Tone } from "@/ui";

/** Tone of a QC status of a lot. */
export const QC_TONE: Record<Lot["qcStatus"], Tone> = { Released: "success", Quarantine: "warning", OnHold: "warning", Rejected: "danger" };

/** Tone of a queue status. */
export const QUEUE_TONE: Record<QueueStatus, Tone> = { pending: "info", sending: "info", failed: "danger", sent: "success" };
