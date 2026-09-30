"use client";

import { createColumnHelper, tableFeatures, useTable, type ColumnDef, type RowData } from "@tanstack/react-table";
import { ChevronLeftIcon, ChevronRightIcon, ChevronsLeftIcon, ChevronsRightIcon, SearchIcon, XIcon } from "lucide-react";
import { useTranslations } from "next-intl";
import type { ReactNode } from "react";

import { EmptyState, ErrorState } from "@/components/feedback/states";
import { Button } from "@/components/ui/button";
import { InputGroup, InputGroupAddon, InputGroupButton, InputGroupInput } from "@/components/ui/input-group";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Skeleton } from "@/components/ui/skeleton";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { formatQuantity } from "@/lib/format/number";
import { cn } from "@/lib/utils";

import { PAGE_SIZES, type ListState } from "./use-list-state";

/** Display options of a column, set as `meta` of the column definition. */
export interface DataTableColumnMeta {
  /** Text alignment (numbers: `right`). */
  align?: "left" | "right" | "center";
  /** Hide the column on phones; keep only what identifies the row visible. */
  hideOnMobile?: boolean;
  /** Extra classes for header and cells, for example a width. */
  className?: string;
}

declare module "@tanstack/react-table" {
  // eslint-disable-next-line @typescript-eslint/no-unused-vars, @typescript-eslint/no-empty-object-type -- module augmentation: the parameters must match the library's declaration
  interface ColumnMeta<TFeatures, TData, TValue> extends DataTableColumnMeta {}
}

/** Table features used by {@link DataTable}: paging, search and sorting happen on the server. */
export const dataTableFeatures = tableFeatures({});

/** Column definition of a {@link DataTable}. */
// eslint-disable-next-line @typescript-eslint/no-explicit-any -- a column list mixes value types
export type DataTableColumn<TData extends RowData> = ColumnDef<typeof dataTableFeatures, TData, any>;

/** Creates the typed column helper: `const column = createDataTableColumns<Row>()`. */
export function createDataTableColumns<TData extends RowData>() {
  return createColumnHelper<typeof dataTableFeatures, TData>();
}

/** Page of rows as every list endpoint of the API returns it. */
export interface PagedData<TData> {
  items: TData[];
  total: number;
  page: number;
  pageSize: number;
}

/** Props of {@link DataTable}. */
export interface DataTableProps<TData extends RowData> {
  /** Column definitions; create them outside of render or with `useMemo`. */
  columns: DataTableColumn<TData>[];
  /** Result of the list query; a plain array shows all rows without paging. */
  data: PagedData<TData> | TData[] | undefined;
  /** First load in progress: shows skeleton rows. */
  loading?: boolean;
  /** Background refresh in progress: dims the rows. */
  fetching?: boolean;
  /** Error of the list query: replaces the rows with the error state. */
  error?: unknown;
  /** Repeats the list query; shown as retry button in the error state. */
  onRetry?: () => void;
  /** Paging and search state from `useListState`; omit for lists without paging. */
  state?: ListState;
  /** Shows the search box (needs `state`). */
  searchable?: boolean;
  /** Placeholder of the search box. */
  searchPlaceholder?: string;
  /** Filters shown next to the search box. */
  filters?: ReactNode;
  /** Buttons on the right of the toolbar, for example "Add". */
  actions?: ReactNode;
  /** Stable id of a row (default `row.id`). */
  getRowId?: (row: TData) => string;
  /** Makes rows clickable. */
  onRowClick?: (row: TData) => void;
  /** Text of the empty state. */
  emptyTitle?: ReactNode;
  /** Second line of the empty state. */
  emptyDescription?: ReactNode;
  /** Accessible name of the table. */
  caption: string;
}

const EMPTY_ROWS: never[] = [];

const alignClass = { left: "text-left", right: "text-right", center: "text-center" } as const;

function cellClass(meta: DataTableColumnMeta | undefined): string {
  return cn(meta?.align ? alignClass[meta.align] : null, meta?.hideOnMobile ? "hidden md:table-cell" : null, meta?.className);
}

/**
 * Table for server-side lists: search box, filters, loading skeleton, empty and error states and
 * a pager. It renders what the query returns; paging and search state live in `useListState`.
 */
export function DataTable<TData extends RowData>({
  columns,
  data,
  loading = false,
  fetching = false,
  error,
  onRetry,
  state,
  searchable = false,
  searchPlaceholder,
  filters,
  actions,
  getRowId,
  onRowClick,
  emptyTitle,
  emptyDescription,
  caption,
}: DataTableProps<TData>) {
  const t = useTranslations("common");
  const rows = Array.isArray(data) ? data : (data?.items ?? (EMPTY_ROWS as TData[]));
  const total = Array.isArray(data) ? data.length : (data?.total ?? 0);

  const table = useTable({
    features: dataTableFeatures,
    columns,
    data: rows,
    getRowId: getRowId ?? ((row, index) => String((row as { id?: unknown }).id ?? index)),
  });

  const pageSize = state?.pageSize ?? Math.max(rows.length, 1);
  const page = state?.page ?? 1;
  const pageCount = Math.max(1, Math.ceil(total / pageSize));
  const from = total === 0 ? 0 : (page - 1) * pageSize + 1;
  const to = Math.min(page * pageSize, total);
  const columnCount = table.getAllLeafColumns().length;
  const showToolbar = Boolean((searchable && state) || filters || actions);

  return (
    <div className="flex flex-col gap-3">
      {showToolbar ? (
        <div className="flex flex-col gap-2 sm:flex-row sm:items-center">
          {searchable && state ? (
            <InputGroup className="sm:max-w-xs">
              <InputGroupAddon>
                <SearchIcon />
              </InputGroupAddon>
              <InputGroupInput
                type="search"
                value={state.searchInput}
                placeholder={searchPlaceholder ?? t("table.searchPlaceholder")}
                aria-label={t("actions.search")}
                maxLength={100}
                onChange={(event) => state.setSearchInput(event.target.value)}
              />
              {state.searchInput ? (
                <InputGroupAddon align="inline-end">
                  <InputGroupButton size="icon-xs" aria-label={t("table.clearSearch")} onClick={() => state.setSearchInput("")}>
                    <XIcon />
                  </InputGroupButton>
                </InputGroupAddon>
              ) : null}
            </InputGroup>
          ) : null}
          {filters ? <div className="flex flex-wrap items-center gap-3">{filters}</div> : null}
          {actions ? <div className="flex items-center gap-2 sm:ml-auto">{actions}</div> : null}
        </div>
      ) : null}

      <div className="rounded-lg border bg-card">
        {error && !loading ? (
          <ErrorState error={error} onRetry={onRetry} />
        ) : (
          <Table aria-label={caption} aria-busy={loading || fetching}>
            <TableHeader>
              {table.getHeaderGroups().map((group) => (
                <TableRow key={group.id}>
                  {group.headers.map((header) => (
                    <TableHead key={header.id} scope="col" className={cellClass(header.column.columnDef.meta)}>
                      {header.isPlaceholder ? null : <table.FlexRender header={header} />}
                    </TableHead>
                  ))}
                </TableRow>
              ))}
            </TableHeader>
            <TableBody className={cn(fetching && !loading ? "opacity-60 transition-opacity" : null)}>
              {loading ? (
                Array.from({ length: 6 }, (_, rowIndex) => (
                  <TableRow key={rowIndex} data-testid="data-table-skeleton">
                    {table.getAllLeafColumns().map((column) => (
                      <TableCell key={column.id} className={cellClass(column.columnDef.meta)}>
                        <Skeleton className="h-5 w-full max-w-40" />
                      </TableCell>
                    ))}
                  </TableRow>
                ))
              ) : rows.length === 0 ? (
                <TableRow className="hover:bg-transparent">
                  <TableCell colSpan={columnCount}>
                    <EmptyState
                      title={emptyTitle ?? (state?.search ? t("table.emptySearch") : t("table.empty"))}
                      description={emptyDescription}
                    />
                  </TableCell>
                </TableRow>
              ) : (
                table.getRowModel().rows.map((row) => (
                  <TableRow
                    key={row.id}
                    data-row-id={row.id}
                    className={onRowClick ? "cursor-pointer" : undefined}
                    onClick={onRowClick ? () => onRowClick(row.original) : undefined}
                  >
                    {row.getAllCells().map((cell) => (
                      <TableCell key={cell.id} className={cellClass(cell.column.columnDef.meta)}>
                        <table.FlexRender cell={cell} />
                      </TableCell>
                    ))}
                  </TableRow>
                ))
              )}
            </TableBody>
          </Table>
        )}
      </div>

      {state && !error ? (
        <div className="flex flex-col gap-2 text-sm text-muted-foreground sm:flex-row sm:items-center sm:justify-between">
          <p aria-live="polite" data-testid="data-table-total">
            {t("table.range", { from: formatQuantity(from), to: formatQuantity(to), total: formatQuantity(total) })}
          </p>
          <div className="flex items-center gap-2">
            <Select value={String(state.pageSize)} onValueChange={(value) => state.setPageSize(Number(value))}>
              <SelectTrigger size="sm" aria-label={t("table.rowsPerPage")}>
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                {PAGE_SIZES.map((size) => (
                  <SelectItem key={size} value={String(size)}>
                    {t("table.perPage", { count: size })}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
            <span className="tabular-nums">{t("table.page", { page, pages: pageCount })}</span>
            <Button variant="outline" size="icon-sm" aria-label={t("table.first")} disabled={page <= 1} onClick={() => state.setPage(1)}>
              <ChevronsLeftIcon />
            </Button>
            <Button variant="outline" size="icon-sm" aria-label={t("table.previous")} disabled={page <= 1} onClick={() => state.setPage(page - 1)}>
              <ChevronLeftIcon />
            </Button>
            <Button variant="outline" size="icon-sm" aria-label={t("table.next")} disabled={page >= pageCount} onClick={() => state.setPage(page + 1)}>
              <ChevronRightIcon />
            </Button>
            <Button variant="outline" size="icon-sm" aria-label={t("table.last")} disabled={page >= pageCount} onClick={() => state.setPage(pageCount)}>
              <ChevronsRightIcon />
            </Button>
          </div>
        </div>
      ) : null}
    </div>
  );
}
