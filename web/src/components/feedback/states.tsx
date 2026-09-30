"use client";

import { AlertTriangleIcon, InboxIcon, LockIcon, RefreshCwIcon, WifiOffIcon } from "lucide-react";
import { useTranslations } from "next-intl";
import type { ReactNode } from "react";

import { Button } from "@/components/ui/button";
import { normalizeError } from "@/lib/api/problem";
import { useProblemMessage } from "@/lib/hooks/use-problem-message";
import { cn } from "@/lib/utils";

/** Props of {@link StateMessage}. */
export interface StateMessageProps {
  /** Icon above the title. */
  icon?: ReactNode;
  /** Short heading. */
  title: ReactNode;
  /** Explanation under the heading. */
  description?: ReactNode;
  /** Buttons under the text. */
  action?: ReactNode;
  className?: string;
}

/** Centered icon, title and text; the base of the empty, error and forbidden states. */
export function StateMessage({ icon, title, description, action, className }: StateMessageProps) {
  return (
    <div className={cn("flex flex-col items-center justify-center gap-2 px-4 py-10 text-center", className)}>
      {icon ? <div className="text-muted-foreground [&_svg]:size-8">{icon}</div> : null}
      <p className="font-medium">{title}</p>
      {description ? <p className="max-w-md text-sm text-muted-foreground">{description}</p> : null}
      {action ? <div className="mt-2 flex gap-2">{action}</div> : null}
    </div>
  );
}

/** Shown when a list has no rows. */
export function EmptyState({ title, description, action }: Partial<Pick<StateMessageProps, "title" | "description" | "action">>) {
  const t = useTranslations("common");
  return <StateMessage icon={<InboxIcon />} title={title ?? t("table.empty")} description={description} action={action} />;
}

/** Shown when the user lacks the permission for a page or the API answered 403. */
export function ForbiddenState({ description }: { description?: ReactNode }) {
  const t = useTranslations("common");
  return <StateMessage icon={<LockIcon />} title={t("state.forbiddenTitle")} description={description ?? t("state.forbiddenDescription")} />;
}

/** Props of {@link ErrorState}. */
export interface ErrorStateProps {
  /** Error of the failed request. */
  error: unknown;
  /** Repeats the request; shows a retry button when given. */
  onRetry?: () => void;
}

/** Shown when loading failed: forbidden for 403, offline for network failures, the API message otherwise. */
export function ErrorState({ error, onRetry }: ErrorStateProps) {
  const t = useTranslations("common");
  const messageOf = useProblemMessage();
  const apiError = normalizeError(error);
  if (apiError.status === 403) {
    return <ForbiddenState description={messageOf(apiError)} />;
  }
  return (
    <StateMessage
      icon={apiError.status === 0 ? <WifiOffIcon /> : <AlertTriangleIcon />}
      title={t("state.errorTitle")}
      description={messageOf(apiError)}
      action={
        onRetry ? (
          <Button type="button" variant="outline" onClick={onRetry}>
            <RefreshCwIcon data-icon="inline-start" />
            {t("actions.retry")}
          </Button>
        ) : null
      }
    />
  );
}
