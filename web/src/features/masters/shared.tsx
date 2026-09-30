"use client";

import type { Schemas } from "@mrp/api-client";
import type { QueryKey } from "@tanstack/react-query";
import { useLocale } from "next-intl";
import { useMemo } from "react";
import type { FieldValues } from "react-hook-form";

import { createDataTableColumns, type DataTableColumn } from "@/components/data-table/data-table";
import { EntityCombobox, type EntityComboboxProps } from "@/components/form/entity-combobox";
import { FieldShell, SelectField, type BaseFieldProps, type SelectOption } from "@/components/form/fields";
import type { ApiClient } from "@/lib/api/client";
import type { ApiResult } from "@/lib/api/problem";
import { queryKeys } from "@/lib/api/query-keys";
import { useApiQuery } from "@/lib/hooks/use-api";
import { msg, optionalText, requiredText, z } from "@/lib/validation/zod";

/** Record with a code and a name in two languages. */
export interface CodedRecord {
  code: string;
  name: string;
  nameEn?: string | null;
}

/** Code field of every master: 1-40 characters without spaces (stored in upper case by the API). */
export function codeText() {
  return requiredText(40).refine((value) => !/\s/.test(value), { error: msg("code") });
}

/** Schema of units and item groups; mirrors `SaveCodedRequest`. */
export const codedSchema = z.object({
  code: codeText(),
  name: requiredText(200),
  nameEn: optionalText(200),
  isActive: z.boolean(),
});

/** Form values of units and item groups. */
export type CodedValues = z.infer<typeof codedSchema>;

/** Converts what is typed into a code field to upper case without spaces. */
export function toCode(value: string): string {
  return value.toUpperCase().replace(/\s+/g, "");
}

/** Name of a record in the UI language; falls back to the Thai name. */
export function localizedName(record: CodedRecord, locale: string): string {
  return locale === "en" && record.nameEn ? record.nameEn : record.name;
}

/** `CODE — name` in the UI language. */
export function codedLabel(record: CodedRecord, locale: string): string {
  return `${record.code} — ${localizedName(record, locale)}`;
}

/** Hook returning {@link codedLabel} bound to the current UI language. */
export function useCodedLabel(): (record: CodedRecord) => string {
  const locale = useLocale();
  return (record) => codedLabel(record, locale);
}

/** Code and name columns shared by every master table. */
export function codedColumns<TRow extends CodedRecord>(labels: { code: string; name: string; nameEn: string }): DataTableColumn<TRow>[] {
  const helper = createDataTableColumns<TRow>();
  return [
    helper.accessor((row) => row.code, { id: "code", header: labels.code, meta: { className: "font-medium whitespace-nowrap" } }),
    helper.accessor((row) => row.name, { id: "name", header: labels.name }),
    helper.accessor((row) => row.nameEn ?? "", { id: "nameEn", header: labels.nameEn, meta: { hideOnMobile: true } }),
  ];
}

/** Loads up to 200 records of a small master (units, item groups, warehouses) for drop-downs. */
function useAllCoded<TRow extends CodedRecord & { id: string; isActive: boolean }>(
  queryKey: QueryKey,
  load: (client: ApiClient) => Promise<ApiResult<{ items: TRow[] }>>,
) {
  const label = useCodedLabel();
  const query = useApiQuery({ queryKey: [...queryKey, "all"], queryFn: load, staleTime: 60_000 });
  return useMemo(() => {
    const items = query.data?.items ?? [];
    const byId = new Map(items.map((item) => [item.id, item]));
    const options: SelectOption[] = items.filter((item) => item.isActive).map((item) => ({ value: item.id, label: label(item) }));
    return { items, byId, options, isLoading: query.isLoading, error: query.error };
    // `label` only depends on the locale, which remounts the tree when it changes.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [query.data, query.isLoading, query.error]);
}

/** All units, for drop-downs and for showing unit codes of ids. */
export function useUnits() {
  return useAllCoded<Schemas["CodedDto"]>(queryKeys.units, (client) =>
    client.GET("/api/v1/masters/units", { params: { query: { page: 1, pageSize: 200 } } }),
  );
}

/** All item groups, for drop-downs. */
export function useItemGroups() {
  return useAllCoded<Schemas["CodedDto"]>(queryKeys.itemGroups, (client) =>
    client.GET("/api/v1/masters/item-groups", { params: { query: { page: 1, pageSize: 200 } } }),
  );
}

/** Props of {@link UnitSelectField}. */
export interface UnitSelectFieldProps<TValues extends FieldValues> extends BaseFieldProps<TValues> {
  /** Adds a choice that clears the field. */
  emptyLabel?: string;
  placeholder?: string;
}

/** Drop-down of the active units, bound to a form field that holds the unit id. */
export function UnitSelectField<TValues extends FieldValues>(props: UnitSelectFieldProps<TValues>) {
  const units = useUnits();
  return <SelectField {...props} options={units.options} />;
}

/** Drop-down of the active item groups, bound to a form field that holds the group id. */
export function ItemGroupSelectField<TValues extends FieldValues>(props: UnitSelectFieldProps<TValues>) {
  const groups = useItemGroups();
  return <SelectField {...props} options={groups.options} />;
}

/** Props of {@link EntityField}. */
export interface EntityFieldProps<TValues extends FieldValues, TItem>
  extends BaseFieldProps<TValues>,
    Pick<EntityComboboxProps<TItem>, "queryKey" | "search" | "load" | "toOption" | "placeholder" | "clearable"> {
  /** Called with the selected record, for example to copy its unit into another field. */
  onSelected?: (item: TItem | null) => void;
}

/** Searchable select of records, bound to a form field that holds the id. */
export function EntityField<TValues extends FieldValues, TItem>({
  queryKey,
  search,
  load,
  toOption,
  placeholder,
  clearable,
  onSelected,
  ...shell
}: EntityFieldProps<TValues, TItem>) {
  return (
    <FieldShell {...shell}>
      {(field, aria) => (
        <EntityCombobox<TItem>
          id={aria.id}
          aria-invalid={aria["aria-invalid"]}
          aria-describedby={aria["aria-describedby"]}
          value={field.value as string | null | undefined}
          onChange={(value, item) => {
            field.onChange(value);
            onSelected?.(item);
          }}
          onBlur={field.onBlur}
          disabled={shell.disabled || field.disabled}
          {...{ queryKey, search, load, toOption, placeholder, clearable }}
        />
      )}
    </FieldShell>
  );
}

/** Props of the record pickers. */
export type PickerFieldProps<TValues extends FieldValues, TItem> = BaseFieldProps<TValues> & {
  placeholder?: string;
  clearable?: boolean;
  onSelected?: (item: TItem | null) => void;
};

/** Searchable select of active items (by code, name or barcode). */
export function ItemPickerField<TValues extends FieldValues>(props: PickerFieldProps<TValues, Schemas["ItemDto"]>) {
  const label = useCodedLabel();
  return (
    <EntityField<TValues, Schemas["ItemDto"]>
      {...props}
      queryKey={queryKeys.items}
      search={(client, search) =>
        client.GET("/api/v1/masters/items", { params: { query: { search, activeOnly: true, page: 1, pageSize: 20 } } })
      }
      load={(client, id) => client.GET("/api/v1/masters/items/{id}", { params: { path: { id } } })}
      toOption={(item) => ({ value: item.id, label: label(item), description: item.stockUnitCode })}
    />
  );
}

/** Searchable select of active suppliers. */
export function SupplierPickerField<TValues extends FieldValues>(props: PickerFieldProps<TValues, Schemas["SupplierDto"]>) {
  const label = useCodedLabel();
  return (
    <EntityField<TValues, Schemas["SupplierDto"]>
      {...props}
      queryKey={queryKeys.suppliers}
      search={(client, search) =>
        client.GET("/api/v1/masters/suppliers", { params: { query: { search, activeOnly: true, page: 1, pageSize: 20 } } })
      }
      load={(client, id) => client.GET("/api/v1/masters/suppliers/{id}", { params: { path: { id } } })}
      toOption={(supplier) => ({ value: supplier.id, label: label(supplier), description: supplier.currency })}
    />
  );
}

/** Shows `CODE — name` of an item id; used in tables whose rows only carry the id. */
export function ItemLabel({ id }: { id: string }) {
  const label = useCodedLabel();
  const query = useApiQuery({
    queryKey: [...queryKeys.items, "detail", id],
    queryFn: (client) => client.GET("/api/v1/masters/items/{id}", { params: { path: { id } } }),
    staleTime: 5 * 60_000,
  });
  return <span>{query.data ? label(query.data) : "…"}</span>;
}
