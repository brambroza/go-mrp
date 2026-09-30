"use client";

import { useEffect, useMemo, useState } from "react";

/** Largest page size the API accepts. */
export const MAX_PAGE_SIZE = 200;

/** Page sizes offered by the table footer. */
export const PAGE_SIZES = [20, 50, 100] as const;

/** Options of {@link useListState}. */
export interface ListStateOptions {
  /** Rows per page at the start (default 20). */
  pageSize?: number;
  /** Milliseconds to wait after the last keystroke before searching (default 300). */
  debounceMs?: number;
}

/** Paging and search state of a server-side list. */
export interface ListState {
  /** Current page, starting at 1. */
  page: number;
  /** Rows per page. */
  pageSize: number;
  /** Text in the search box. */
  searchInput: string;
  /** Debounced, trimmed search text to send to the API; `undefined` when empty. */
  search: string | undefined;
  /** Query parameters for the API: `{ search, page, pageSize }`. */
  params: { search: string | undefined; page: number; pageSize: number };
  /** Goes to a page. */
  setPage: (page: number) => void;
  /** Changes the page size and returns to page 1. */
  setPageSize: (pageSize: number) => void;
  /** Updates the search box; the list returns to page 1 when the debounced value changes. */
  setSearchInput: (value: string) => void;
}

/** Returns a value that follows `value` after it stopped changing for `delayMs`. */
export function useDebouncedValue<T>(value: T, delayMs: number): T {
  const [debounced, setDebounced] = useState(value);
  useEffect(() => {
    const timer = setTimeout(() => setDebounced(value), delayMs);
    return () => clearTimeout(timer);
  }, [value, delayMs]);
  return debounced;
}

/** State of a list with server-side paging and search; pass `params` to the query and the state to `DataTable`. */
export function useListState(options: ListStateOptions = {}): ListState {
  const [page, setPage] = useState(1);
  const [pageSize, setPageSizeState] = useState(Math.min(options.pageSize ?? 20, MAX_PAGE_SIZE));
  const [searchInput, setSearchInputState] = useState("");
  const debounced = useDebouncedValue(searchInput, options.debounceMs ?? 300).trim();
  const search = debounced.length > 0 ? debounced : undefined;

  return useMemo(
    () => ({
      page,
      pageSize,
      searchInput,
      search,
      params: { search, page, pageSize },
      setPage,
      setPageSize: (next: number) => {
        setPageSizeState(Math.min(Math.max(next, 1), MAX_PAGE_SIZE));
        setPage(1);
      },
      setSearchInput: (value: string) => {
        setSearchInputState(value);
        setPage(1);
      },
    }),
    [page, pageSize, searchInput, search],
  );
}
