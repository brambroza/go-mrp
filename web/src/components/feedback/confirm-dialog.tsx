"use client";

import { Loader2Icon } from "lucide-react";
import { useTranslations } from "next-intl";
import { useId, useState, type ReactNode } from "react";

import {
  AlertDialog,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from "@/components/ui/alert-dialog";
import { Button } from "@/components/ui/button";
import { Field, FieldDescription, FieldError, FieldLabel } from "@/components/ui/field";
import { Textarea } from "@/components/ui/textarea";

/** How the reason input of a {@link ConfirmDialog} behaves. */
export type ReasonMode = "none" | "optional" | "required";

/** Props of {@link ConfirmDialog}. */
export interface ConfirmDialogProps {
  /** Whether the dialog is open. */
  open: boolean;
  /** Called when the dialog wants to open or close. */
  onOpenChange: (open: boolean) => void;
  /** Question, for example "Reject this request?". */
  title: ReactNode;
  /** What will happen. */
  description?: ReactNode;
  /** Label of the confirm button (default "Confirm"). */
  confirmLabel?: string;
  /** Styles the confirm button as destructive. */
  destructive?: boolean;
  /** Whether a reason is asked for; `required` blocks confirming without one (default `none`). */
  reason?: ReasonMode;
  /** Label of the reason input (default "Reason"). */
  reasonLabel?: string;
  /** Longest reason the API accepts (default 500). */
  reasonMaxLength?: number;
  /**
   * Runs the action with the trimmed reason (`null` when empty). The dialog closes when the
   * promise resolves and stays open when it rejects, so the user can try again.
   */
  onConfirm: (reason: string | null) => void | Promise<unknown>;
}

/** Confirmation dialog for actions that cannot be undone, with an optional or mandatory reason. */
export function ConfirmDialog({
  open,
  onOpenChange,
  title,
  description,
  confirmLabel,
  destructive = false,
  reason = "none",
  reasonLabel,
  reasonMaxLength = 500,
  onConfirm,
}: ConfirmDialogProps) {
  const t = useTranslations("common");
  const tv = useTranslations("validation");
  const id = useId();
  const [text, setText] = useState("");
  const [touched, setTouched] = useState(false);
  const [pending, setPending] = useState(false);

  const trimmed = text.trim();
  const missing = reason === "required" && trimmed.length === 0;
  const error = touched && missing ? tv("reasonRequired") : undefined;

  const close = (next: boolean) => {
    if (pending) {
      return;
    }
    if (!next) {
      setText("");
      setTouched(false);
    }
    onOpenChange(next);
  };

  const confirm = async () => {
    setTouched(true);
    if (missing) {
      return;
    }
    setPending(true);
    try {
      await onConfirm(trimmed.length > 0 ? trimmed : null);
      setText("");
      setTouched(false);
      onOpenChange(false);
    } catch {
      // The caller reports the failure (toast); keep the dialog open for another try.
    } finally {
      setPending(false);
    }
  };

  return (
    <AlertDialog open={open} onOpenChange={close}>
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>{title}</AlertDialogTitle>
          {description ? (
            <AlertDialogDescription>{description}</AlertDialogDescription>
          ) : (
            <AlertDialogDescription className="sr-only">{title}</AlertDialogDescription>
          )}
        </AlertDialogHeader>
        {reason === "none" ? null : (
          <Field data-invalid={Boolean(error)}>
            <FieldLabel htmlFor={id}>
              {reasonLabel ?? t("confirm.reason")}
              {reason === "required" ? (
                <span aria-hidden="true" className="text-destructive">
                  *
                </span>
              ) : null}
            </FieldLabel>
            <Textarea
              id={id}
              rows={3}
              value={text}
              maxLength={reasonMaxLength}
              disabled={pending}
              aria-invalid={Boolean(error)}
              aria-required={reason === "required"}
              aria-describedby={error ? `${id}-error` : `${id}-hint`}
              onChange={(event) => setText(event.target.value)}
              onBlur={() => setTouched(true)}
            />
            {error ? (
              <FieldError id={`${id}-error`}>{error}</FieldError>
            ) : (
              <FieldDescription id={`${id}-hint`}>{t("confirm.reasonHint", { count: text.length, max: reasonMaxLength })}</FieldDescription>
            )}
          </Field>
        )}
        <AlertDialogFooter>
          <AlertDialogCancel disabled={pending}>{t("actions.cancel")}</AlertDialogCancel>
          <Button type="button" variant={destructive ? "destructive" : "default"} disabled={pending} onClick={() => void confirm()}>
            {pending ? <Loader2Icon data-icon="inline-start" className="animate-spin" /> : null}
            {confirmLabel ?? t("actions.confirm")}
          </Button>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  );
}
