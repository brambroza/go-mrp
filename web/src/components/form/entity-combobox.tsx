"use client";

import { CheckIcon, ChevronsUpDownIcon, Loader2Icon, XIcon } from "lucide-react";
import { useTranslations } from "next-intl";
import { useState } from "react";
import type { QueryKey } from "@tanstack/react-query";

import { useDebouncedValue } from "@/components/data-table/use-list-state";
import { Button } from "@/components/ui/button";
import { Command, CommandEmpty, CommandGroup, CommandInput, CommandItem, CommandList } from "@/components/ui/command";
import { Popover, PopoverContent, PopoverTrigger } from "@/components/ui/popover";
import type { ApiClient } from "@/lib/api/client";
import type { ApiResult } from "@/lib/api/problem";
import { useApiQuery } from "@/lib/hooks/use-api";
import { cn } from "@/lib/utils";

/** One choice of an {@link EntityCombobox}. */
export interface EntityOption {
  /** Id of the record. */
  value: string;
  /** Main text, usually `CODE — name`. */
  label: string;
  /** Second line, for example the unit. */
  description?: string;
}

/** Props of {@link EntityCombobox}. */
export interface EntityComboboxProps<TItem> {
  /** Selected id, or empty. */
  value: string | null | undefined;
  /** Called with the selected id (empty string when cleared) and the record. */
  onChange: (value: string, item: TItem | null) => void;
  onBlur?: () => void;
  /** Root of the cache key, for example `queryKeys.items`. */
  queryKey: QueryKey;
  /** Searches the records: `(api, search) => api.GET(path, { params: { query: { search, activeOnly: true, pageSize: 20 } } })`. */
  search: (client: ApiClient, search: string | undefined) => Promise<ApiResult<{ items: TItem[] }>>;
  /** Loads the selected record to show its label: `(api, id) => api.GET(path + "/{id}", ...)`. */
  load: (client: ApiClient, id: string) => Promise<ApiResult<TItem>>;
  /** Converts a record to a choice. */
  toOption: (item: TItem) => EntityOption;
  /** Text when nothing is selected. */
  placeholder?: string;
  /** Whether the selection can be removed (default `true`). */
  clearable?: boolean;
  disabled?: boolean;
  id?: string;
  "aria-invalid"?: boolean;
  "aria-describedby"?: string;
}

/** Select with server-side search for records that are too many for a drop-down (items, suppliers, customers). */
export function EntityCombobox<TItem>({
  value,
  onChange,
  onBlur,
  queryKey,
  search,
  load,
  toOption,
  placeholder,
  clearable = true,
  disabled,
  ...aria
}: EntityComboboxProps<TItem>) {
  const t = useTranslations("common");
  const [open, setOpen] = useState(false);
  const [input, setInput] = useState("");
  const term = useDebouncedValue(input, 250).trim();

  const results = useApiQuery({
    queryKey: [...queryKey, "combobox", term],
    queryFn: (client) => search(client, term.length > 0 ? term : undefined),
    enabled: open,
    keepPrevious: true,
  });
  const selected = useApiQuery({
    queryKey: [...queryKey, "detail", value],
    queryFn: (client) => load(client, value ?? ""),
    enabled: Boolean(value),
    staleTime: 5 * 60_000,
  });

  const items = results.data?.items ?? [];
  const selectedOption = selected.data ? toOption(selected.data) : null;

  return (
    <div className="flex items-center gap-1">
      <Popover
        open={open}
        onOpenChange={(next) => {
          setOpen(next);
          if (!next) {
            setInput("");
            onBlur?.();
          }
        }}
      >
        <PopoverTrigger asChild>
          <Button
            type="button"
            variant="outline"
            role="combobox"
            aria-expanded={open}
            disabled={disabled}
            className={cn("h-9 min-w-0 flex-1 justify-between font-normal", !value && "text-muted-foreground")}
            {...aria}
          >
            <span className="truncate">
              {value ? (selectedOption?.label ?? (selected.isLoading ? t("table.loading") : value)) : (placeholder ?? t("select.placeholder"))}
            </span>
            <ChevronsUpDownIcon data-icon="inline-end" className="opacity-50" />
          </Button>
        </PopoverTrigger>
        <PopoverContent className="w-(--radix-popover-trigger-width) min-w-64 p-0" align="start">
          <Command shouldFilter={false}>
            <CommandInput value={input} onValueChange={setInput} placeholder={t("table.searchPlaceholder")} maxLength={100} />
            <CommandList>
              {results.isLoading ? (
                <div className="flex items-center justify-center gap-2 p-4 text-sm text-muted-foreground">
                  <Loader2Icon className="size-4 animate-spin" />
                  {t("table.loading")}
                </div>
              ) : (
                <>
                  <CommandEmpty>{results.error ? t("state.errorTitle") : t("table.emptySearch")}</CommandEmpty>
                  <CommandGroup>
                    {items.map((item) => {
                      const option = toOption(item);
                      return (
                        <CommandItem
                          key={option.value}
                          value={option.value}
                          onSelect={() => {
                            onChange(option.value, item);
                            setOpen(false);
                            setInput("");
                            onBlur?.();
                          }}
                        >
                          <CheckIcon className={cn("size-4", option.value === value ? "opacity-100" : "opacity-0")} />
                          <span className="flex min-w-0 flex-col">
                            <span className="truncate">{option.label}</span>
                            {option.description ? <span className="truncate text-xs text-muted-foreground">{option.description}</span> : null}
                          </span>
                        </CommandItem>
                      );
                    })}
                  </CommandGroup>
                </>
              )}
            </CommandList>
          </Command>
        </PopoverContent>
      </Popover>
      {clearable && value && !disabled ? (
        <Button type="button" variant="ghost" size="icon" aria-label={t("select.clear")} onClick={() => onChange("", null)}>
          <XIcon />
        </Button>
      ) : null}
    </div>
  );
}
