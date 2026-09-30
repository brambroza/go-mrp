"use client";

import { queryKeys } from "@/lib/api/query-keys";

import { useApiQuery } from "./use-api";

/** Number of approval requests waiting for the signed-in user; refreshed every minute. */
export function usePendingApprovalCount(): number | undefined {
  const query = useApiQuery({
    queryKey: [...queryKeys.approvals, "inbox", "count"],
    queryFn: (api) => api.GET("/api/v1/approvals/inbox", { params: { query: { page: 1, pageSize: 1 } } }),
    refetchInterval: 60_000,
    staleTime: 30_000,
  });
  return query.data?.total;
}
