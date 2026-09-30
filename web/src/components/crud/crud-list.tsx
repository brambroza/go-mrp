"use client";

import { zodResolver } from "@hookform/resolvers/zod";
import type { QueryKey } from "@tanstack/react-query";
import { PencilIcon, PlusIcon } from "lucide-react";
import { useTranslations } from "next-intl";
import { useEffect, useMemo, useState, type ReactNode } from "react";
import { useForm, type DefaultValues, type FieldValues, type UseFormReturn } from "react-hook-form";
import type { ZodType } from "zod";

import { createDataTableColumns, DataTable, type DataTableColumn, type PagedData } from "@/components/data-table/data-table";
import { useListState } from "@/components/data-table/use-list-state";
import { FormDrawer, type FormDrawerProps } from "@/components/form/form-drawer";
import { Button } from "@/components/ui/button";
import { Label } from "@/components/ui/label";
import { Switch } from "@/components/ui/switch";
import type { ApiClient } from "@/lib/api/client";
import type { ApiResult } from "@/lib/api/problem";
import type { PermissionRequirement } from "@/lib/auth/permissions";
import { useCan } from "@/lib/auth/use-can";
import { useApiMutation, useApiQuery } from "@/lib/hooks/use-api";

/** Query parameters every master list of the API accepts. */
export interface CrudListParams {
  search: string | undefined;
  activeOnly: boolean;
  page: number;
  pageSize: number;
}

/** What the form of a {@link CrudList} receives to render its fields. */
export interface CrudFieldsContext<TRow, TValues extends FieldValues> {
  /** The form; pass `form.control` to the field components. */
  form: UseFormReturn<TValues, unknown, TValues>;
  /** Record being edited, or `null` while creating. */
  editing: TRow | null;
}

/** Texts of a {@link CrudList}. */
export interface CrudLabels {
  /** Accessible name of the table, for example "Units". */
  caption: string;
  /** Label of the add button and title of the drawer while creating. */
  add: string;
  /** Title of the drawer while editing. */
  edit: string;
  /** Title of the drawer for users who may only read. */
  view: string;
  /** Toast after saving. */
  saved: string;
  /** Placeholder of the search box. */
  searchPlaceholder?: string;
}

/** Props of {@link CrudList}. */
export interface CrudListProps<TRow extends { id: string }, TValues extends FieldValues, TBody> {
  /** Root of the cache key, for example `queryKeys.units`. */
  queryKey: QueryKey;
  /** Other query key roots to refresh after a save. */
  alsoInvalidate?: readonly QueryKey[];
  /** Permission needed to create and edit; without it the list is read-only. */
  managePermission: PermissionRequirement;
  /** Loads a page. */
  list: (client: ApiClient, params: CrudListParams) => Promise<ApiResult<PagedData<TRow>>>;
  /** Creates a record. */
  create: (client: ApiClient, body: TBody) => Promise<ApiResult<TRow>>;
  /** Updates a record. */
  update: (client: ApiClient, id: string, body: TBody) => Promise<ApiResult<TRow>>;
  /** Columns of the table; the edit and active columns are added automatically. */
  columns: DataTableColumn<TRow>[];
  /** Validation schema of the form; mirror the limits of the API request record. */
  schema: ZodType<TValues, TValues>;
  /** Values of an empty form. */
  defaultValues: TValues;
  /** Converts a record to form values. */
  toValues: (row: TRow) => TValues;
  /** Converts form values to the request body. */
  toBody: (values: TValues) => TBody;
  /** Renders the fields of the form. */
  fields: (context: CrudFieldsContext<TRow, TValues>) => ReactNode;
  /** Texts. */
  labels: CrudLabels;
  /** Show the search box (default `true`). */
  searchable?: boolean;
  /** Sent as `search` instead of the search box, for child lists filtered by the id of the parent. */
  fixedSearch?: string;
  /** The records have `isActive`: adds the "active only" filter and the active switch per row. */
  hasActiveFlag?: boolean;
  /** Show the "active only" filter (default: same as `hasActiveFlag`); turn off when the API has no such parameter. */
  activeFilter?: boolean;
  /** Extra buttons per row, for example "Locations". */
  rowActions?: (row: TRow) => ReactNode;
  /** Width of the drawer. */
  drawerSize?: FormDrawerProps<TValues>["size"];
  /** Rows per page (default 20). */
  pageSize?: number;
}

/**
 * List with search, paging and a drawer to create and edit — the pattern of every master data
 * screen. Supply the API calls, the columns, the zod schema and the fields.
 */
export function CrudList<TRow extends { id: string }, TValues extends FieldValues, TBody>({
  queryKey,
  alsoInvalidate,
  managePermission,
  list,
  create,
  update,
  columns,
  schema,
  defaultValues,
  toValues,
  toBody,
  fields,
  labels,
  searchable = true,
  fixedSearch,
  hasActiveFlag = false,
  activeFilter = hasActiveFlag,
  rowActions,
  drawerSize,
  pageSize,
}: CrudListProps<TRow, TValues, TBody>) {
  const t = useTranslations("common");
  const canManage = useCan(managePermission);
  const state = useListState({ pageSize });
  const [activeOnly, setActiveOnly] = useState(false);
  const [drawer, setDrawer] = useState<{ open: boolean; editing: TRow | null }>({ open: false, editing: null });

  const params: CrudListParams = { ...state.params, search: fixedSearch ?? state.params.search, activeOnly };
  const query = useApiQuery({
    queryKey: [...queryKey, "list", params],
    queryFn: (client) => list(client, params),
    keepPrevious: true,
  });

  const form = useForm<TValues, unknown, TValues>({
    resolver: zodResolver(schema),
    defaultValues: defaultValues as DefaultValues<TValues>,
  });

  useEffect(() => {
    if (drawer.open) {
      form.reset(drawer.editing ? toValues(drawer.editing) : defaultValues);
    }
    // Reset only when the drawer opens for another record, not when the converters change identity.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [drawer.open, drawer.editing]);

  const invalidate = [queryKey, ...(alsoInvalidate ?? [])];
  const save = useApiMutation({
    mutationFn: (client, values: TValues) =>
      drawer.editing ? update(client, drawer.editing.id, toBody(values)) : create(client, toBody(values)),
    form,
    invalidate,
    successMessage: labels.saved,
    onSuccess: () => setDrawer({ open: false, editing: null }),
  });
  const toggle = useApiMutation({
    mutationFn: (client, input: { row: TRow; isActive: boolean }) =>
      update(client, input.row.id, toBody({ ...toValues(input.row), isActive: input.isActive })),
    invalidate,
    successMessage: labels.saved,
  });

  const allColumns = useMemo(() => {
    const helper = createDataTableColumns<TRow>();
    const extra: DataTableColumn<TRow>[] = [];
    if (hasActiveFlag) {
      extra.push(
        helper.display({
          id: "isActive",
          header: () => t("fields.isActive"),
          meta: { className: "w-24" },
          cell: ({ row }) => {
            const active = Boolean((row.original as { isActive?: boolean }).isActive);
            return (
              <span onClick={(event) => event.stopPropagation()}>
                <Switch
                  checked={active}
                  disabled={!canManage || toggle.isPending}
                  aria-label={active ? t("actions.deactivate") : t("actions.activate")}
                  onCheckedChange={(next) => toggle.mutate({ row: row.original, isActive: next })}
                />
              </span>
            );
          },
        }),
      );
    }
    extra.push(
      helper.display({
        id: "actions",
        header: () => <span className="sr-only">{t("table.actions")}</span>,
        meta: { align: "right", className: "w-px whitespace-nowrap" },
        cell: ({ row }) => (
          <span className="flex items-center justify-end gap-1" onClick={(event) => event.stopPropagation()}>
            {rowActions?.(row.original)}
            <Button
              variant="ghost"
              size="icon-sm"
              aria-label={canManage ? t("actions.edit") : t("actions.view")}
              onClick={() => setDrawer({ open: true, editing: row.original })}
            >
              <PencilIcon />
            </Button>
          </span>
        ),
      }),
    );
    return [...columns, ...extra];
  }, [columns, hasActiveFlag, canManage, rowActions, t, toggle]);

  return (
    <>
      <DataTable
        caption={labels.caption}
        columns={allColumns}
        data={query.data}
        loading={query.isLoading}
        fetching={query.isFetching}
        error={query.error}
        onRetry={() => void query.refetch()}
        state={state}
        searchable={searchable && fixedSearch === undefined}
        searchPlaceholder={labels.searchPlaceholder}
        onRowClick={(row) => setDrawer({ open: true, editing: row })}
        filters={
          activeFilter ? (
            <Label className="flex items-center gap-2 text-sm font-normal">
              <Switch
                checked={activeOnly}
                onCheckedChange={(next) => {
                  setActiveOnly(next);
                  state.setPage(1);
                }}
              />
              {t("filters.activeOnly")}
            </Label>
          ) : null
        }
        actions={
          canManage ? (
            <Button type="button" onClick={() => setDrawer({ open: true, editing: null })} data-testid="crud-add">
              <PlusIcon data-icon="inline-start" />
              {labels.add}
            </Button>
          ) : null
        }
      />
      <FormDrawer
        open={drawer.open}
        onOpenChange={(open) => setDrawer((current) => ({ ...current, open }))}
        title={drawer.editing ? (canManage ? labels.edit : labels.view) : labels.add}
        form={form}
        onSubmit={(values) => save.mutateAsync(values).then(() => undefined, () => undefined)}
        submitting={save.isPending}
        readOnly={!canManage}
        size={drawerSize}
      >
        {fields({ form, editing: drawer.editing })}
      </FormDrawer>
    </>
  );
}
