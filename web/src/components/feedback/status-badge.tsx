"use client";

import { useTranslations } from "next-intl";

import { Badge } from "@/components/ui/badge";
import { cn } from "@/lib/utils";

/** Colour families of a status. */
export type StatusTone = "neutral" | "info" | "progress" | "success" | "warning" | "danger";

/**
 * Tone of every document, approval, lot and master status of the API. A status missing here is
 * shown in neutral with its raw name; add new statuses here and in `messages/<locale>/status.json`.
 */
export const statusTones = {
  Draft: "neutral",
  Submitted: "info",
  Pending: "info",
  Approved: "success",
  Rejected: "danger",
  Withdrawn: "neutral",
  Posted: "success",
  Voided: "danger",
  Cancelled: "neutral",
  Closed: "neutral",
  PartiallyOrdered: "progress",
  Ordered: "success",
  PartiallyReceived: "progress",
  Received: "success",
  Open: "info",
  Quarantine: "warning",
  Released: "success",
  OnHold: "warning",
  Active: "success",
  Inactive: "neutral",
  Trial: "info",
  Suspended: "danger",
} as const satisfies Record<string, StatusTone>;

/** A status the web app knows. */
export type KnownStatus = keyof typeof statusTones;

const toneClasses: Record<StatusTone, string> = {
  neutral: "border-border bg-muted text-muted-foreground",
  info: "border-sky-600/30 bg-sky-500/10 text-sky-800 dark:text-sky-300",
  progress: "border-violet-600/30 bg-violet-500/10 text-violet-800 dark:text-violet-300",
  success: "border-emerald-600/30 bg-emerald-500/10 text-emerald-800 dark:text-emerald-300",
  warning: "border-amber-600/40 bg-amber-500/15 text-amber-900 dark:text-amber-300",
  danger: "border-red-600/30 bg-red-500/10 text-red-800 dark:text-red-300",
};

/** Whether the text is a status with a tone and a translation. */
export function isKnownStatus(status: string): status is KnownStatus {
  return Object.hasOwn(statusTones, status);
}

/** Tone of a status; unknown statuses are neutral. */
export function statusTone(status: string): StatusTone {
  return isKnownStatus(status) ? statusTones[status] : "neutral";
}

/** Props of {@link StatusBadge}. */
export interface StatusBadgeProps {
  /** Status as the API returns it, for example `PartiallyReceived`. */
  status: string;
  className?: string;
}

/** Coloured, translated badge for the status of a document, approval request, lot or master record. */
export function StatusBadge({ status, className }: StatusBadgeProps) {
  const t = useTranslations("status");
  return (
    <Badge variant="outline" data-status={status} className={cn("whitespace-nowrap", toneClasses[statusTone(status)], className)}>
      {isKnownStatus(status) ? t(status) : status}
    </Badge>
  );
}

/** Badge for the active flag of a master record. */
export function ActiveBadge({ active, className }: { active: boolean; className?: string }) {
  return <StatusBadge status={active ? "Active" : "Inactive"} className={className} />;
}
