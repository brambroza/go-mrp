"use client";

import { Loader2Icon } from "lucide-react";
import { useTranslations } from "next-intl";
import { useId, type ReactNode } from "react";
import type { FieldValues, UseFormReturn } from "react-hook-form";

import { Button } from "@/components/ui/button";
import { FieldGroup } from "@/components/ui/field";
import { Sheet, SheetContent, SheetDescription, SheetFooter, SheetHeader, SheetTitle } from "@/components/ui/sheet";
import { cn } from "@/lib/utils";

/** Props of {@link FormDrawer}. */
export interface FormDrawerProps<TValues extends FieldValues, TOutput = TValues> {
  /** Whether the drawer is open. */
  open: boolean;
  /** Called when the drawer wants to open or close. */
  onOpenChange: (open: boolean) => void;
  /** Heading, for example "Add unit". */
  title: ReactNode;
  /** Text under the heading. */
  description?: ReactNode;
  /** The react-hook-form instance (created with `zodResolver`). */
  // eslint-disable-next-line @typescript-eslint/no-explicit-any -- accepts forms with any context
  form: UseFormReturn<TValues, any, TOutput>;
  /** Called with the validated values. */
  onSubmit: (values: TOutput) => void | Promise<void>;
  /** Disables the buttons and shows a spinner while saving. */
  submitting?: boolean;
  /** Label of the submit button (default "Save"). */
  submitLabel?: string;
  /** Hides the submit button and disables every field, for users without the manage permission. */
  readOnly?: boolean;
  /** Width on desktop (default `md`). */
  size?: "md" | "lg" | "xl";
  /** Extra content under the fields that is not part of the form, for example a child table. */
  after?: ReactNode;
  /** The form fields. */
  children: ReactNode;
}

const sizes = {
  md: "data-[side=right]:sm:max-w-md",
  lg: "data-[side=right]:sm:max-w-xl",
  xl: "data-[side=right]:sm:max-w-3xl",
} as const;

/**
 * Side drawer with a form: scrollable fields, sticky footer with Cancel and Save.
 * Full width on phones. Closing is blocked while the form is being saved.
 */
export function FormDrawer<TValues extends FieldValues, TOutput = TValues>({
  open,
  onOpenChange,
  title,
  description,
  form,
  onSubmit,
  submitting = false,
  submitLabel,
  readOnly = false,
  size = "md",
  after,
  children,
}: FormDrawerProps<TValues, TOutput>) {
  const t = useTranslations("common");
  const formId = useId();
  return (
    <Sheet open={open} onOpenChange={(next) => (submitting ? undefined : onOpenChange(next))}>
      <SheetContent side="right" showCloseButton={false} className={cn("w-full gap-0 data-[side=right]:w-full", sizes[size])}>
        <SheetHeader className="border-b">
          <SheetTitle>{title}</SheetTitle>
          {description ? <SheetDescription>{description}</SheetDescription> : <SheetDescription className="sr-only">{title}</SheetDescription>}
        </SheetHeader>
        <div className="min-h-0 flex-1 overflow-y-auto p-4">
          <form
            id={formId}
            noValidate
            onSubmit={(event) => {
              // Drawers can be stacked; React lets events of a portal bubble to the form that opened it.
              event.stopPropagation();
              void form.handleSubmit(onSubmit)(event);
            }}
          >
            <fieldset disabled={readOnly || submitting} className="contents">
              <FieldGroup>{children}</FieldGroup>
            </fieldset>
          </form>
          {after ? <div className="mt-6">{after}</div> : null}
        </div>
        <SheetFooter className="flex-row justify-end border-t">
          <Button type="button" variant="outline" disabled={submitting} onClick={() => onOpenChange(false)}>
            {readOnly ? t("actions.close") : t("actions.cancel")}
          </Button>
          {readOnly ? null : (
            <Button type="submit" form={formId} disabled={submitting}>
              {submitting ? <Loader2Icon data-icon="inline-start" className="animate-spin" /> : null}
              {submitLabel ?? t("actions.save")}
            </Button>
          )}
        </SheetFooter>
      </SheetContent>
    </Sheet>
  );
}
