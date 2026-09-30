"use client";

import { CalendarIcon, XIcon } from "lucide-react";
import { useLocale, useTranslations } from "next-intl";
import { useState } from "react";
import { enGB, th } from "react-day-picker/locale";

import { Button } from "@/components/ui/button";
import { Calendar } from "@/components/ui/calendar";
import { Popover, PopoverContent, PopoverTrigger } from "@/components/ui/popover";
import { toLocale } from "@/i18n/config";
import { formatDate, isoToLocalDate, localDateToIso } from "@/lib/format/date";
import { cn } from "@/lib/utils";

/** Props of {@link DateInput}. */
export interface DateInputProps {
  /** Selected date as `YYYY-MM-DD` (Gregorian), or empty. */
  value: string | null | undefined;
  /** Called with `YYYY-MM-DD`, or an empty string when cleared. */
  onChange: (value: string) => void;
  /** Called when the picker closes; used by forms to mark the field as touched. */
  onBlur?: () => void;
  /** Earliest selectable date as `YYYY-MM-DD`. */
  min?: string;
  /** Latest selectable date as `YYYY-MM-DD`. */
  max?: string;
  /** Whether the date can be removed (default `true`). */
  clearable?: boolean;
  /** Text when nothing is selected. */
  placeholder?: string;
  disabled?: boolean;
  id?: string;
  "aria-invalid"?: boolean;
  "aria-describedby"?: string;
}

/**
 * Date picker. Shows the Buddhist-era year in the Thai UI and the Gregorian year in the English
 * UI, while the value is always an ISO Gregorian `YYYY-MM-DD` string for the API.
 */
export function DateInput({ value, onChange, onBlur, min, max, clearable = true, placeholder, disabled, ...aria }: DateInputProps) {
  const locale = toLocale(useLocale());
  const t = useTranslations("common");
  const [open, setOpen] = useState(false);
  const selected = isoToLocalDate(value);
  const minDate = isoToLocalDate(min);
  const maxDate = isoToLocalDate(max);
  const yearFormat = new Intl.DateTimeFormat(locale === "en" ? "en-GB-u-ca-gregory-nu-latn" : "th-TH-u-ca-buddhist-nu-latn", {
    year: "numeric",
  });
  const captionFormat = new Intl.DateTimeFormat(locale === "en" ? "en-GB-u-ca-gregory-nu-latn" : "th-TH-u-ca-buddhist-nu-latn", {
    month: "long",
    year: "numeric",
  });

  return (
    <div className="flex items-center gap-1">
      <Popover
        open={open}
        onOpenChange={(next) => {
          setOpen(next);
          if (!next) {
            onBlur?.();
          }
        }}
      >
        <PopoverTrigger asChild>
          <Button
            type="button"
            variant="outline"
            disabled={disabled}
            className={cn("h-9 flex-1 justify-start font-normal", !selected && "text-muted-foreground")}
            {...aria}
          >
            <CalendarIcon data-icon="inline-start" />
            {selected ? formatDate(value, { locale }) : (placeholder ?? t("date.placeholder"))}
          </Button>
        </PopoverTrigger>
        <PopoverContent className="w-auto p-0" align="start">
          <Calendar
            mode="single"
            locale={locale === "en" ? enGB : th}
            captionLayout="dropdown"
            selected={selected}
            defaultMonth={selected ?? maxDate ?? undefined}
            startMonth={minDate ?? new Date(2000, 0, 1)}
            endMonth={maxDate ?? new Date(new Date().getFullYear() + 10, 11, 1)}
            disabled={[...(minDate ? [{ before: minDate }] : []), ...(maxDate ? [{ after: maxDate }] : [])]}
            formatters={{
              formatCaption: (date) => captionFormat.format(date),
              formatYearDropdown: (date) => yearFormat.format(date).replace(/[^\d]/g, ""),
            }}
            onSelect={(date) => {
              if (date) {
                onChange(localDateToIso(date));
                setOpen(false);
                onBlur?.();
              }
            }}
          />
        </PopoverContent>
      </Popover>
      {clearable && selected && !disabled ? (
        <Button type="button" variant="ghost" size="icon" aria-label={t("date.clear")} onClick={() => onChange("")}>
          <XIcon />
        </Button>
      ) : null}
    </div>
  );
}
