"use client";

import { useTranslations } from "next-intl";
import type { ReactNode } from "react";

import { Button } from "@/components/ui/button";
import { Sheet, SheetContent, SheetDescription, SheetFooter, SheetHeader, SheetTitle } from "@/components/ui/sheet";
import { cn } from "@/lib/utils";

/** Props of {@link DetailSheet}. */
export interface DetailSheetProps {
  /** Whether the sheet is open. */
  open: boolean;
  /** Called when the sheet wants to open or close. */
  onOpenChange: (open: boolean) => void;
  /** Heading. */
  title: ReactNode;
  /** Text under the heading. */
  description?: ReactNode;
  /** Width on desktop (default `lg`). */
  size?: "md" | "lg" | "xl";
  /** Buttons in the footer, left of "Close". */
  footer?: ReactNode;
  children: ReactNode;
}

const sizes = {
  md: "data-[side=right]:sm:max-w-md",
  lg: "data-[side=right]:sm:max-w-2xl",
  xl: "data-[side=right]:sm:max-w-4xl",
} as const;

/** Side sheet for details and child lists of a record (locations of a warehouse, prices of a supplier). */
export function DetailSheet({ open, onOpenChange, title, description, size = "lg", footer, children }: DetailSheetProps) {
  const t = useTranslations("common");
  return (
    <Sheet open={open} onOpenChange={onOpenChange}>
      <SheetContent side="right" showCloseButton={false} className={cn("w-full gap-0 data-[side=right]:w-full", sizes[size])}>
        <SheetHeader className="border-b">
          <SheetTitle>{title}</SheetTitle>
          {description ? <SheetDescription>{description}</SheetDescription> : <SheetDescription className="sr-only">{title}</SheetDescription>}
        </SheetHeader>
        <div className="min-h-0 flex-1 overflow-y-auto p-4">{children}</div>
        <SheetFooter className="flex-row justify-end border-t">
          {footer}
          <Button type="button" variant="outline" onClick={() => onOpenChange(false)}>
            {t("actions.close")}
          </Button>
        </SheetFooter>
      </SheetContent>
    </Sheet>
  );
}
