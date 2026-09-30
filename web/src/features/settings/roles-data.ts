"use client";

import { useMemo } from "react";

import type { SelectOption } from "@/components/form/fields";
import { queryKeys } from "@/lib/api/query-keys";
import { useApiQuery } from "@/lib/hooks/use-api";

/** All roles of the tenant, as list, by id and as options for selects. */
export function useRoles() {
  const query = useApiQuery({
    queryKey: [...queryKeys.roles, "all"],
    queryFn: (api) => api.GET("/api/v1/roles"),
    staleTime: 60_000,
  });
  return useMemo(() => {
    const items = query.data ?? [];
    const options: SelectOption[] = items.map((role) => ({ value: role.id, label: role.name }));
    return { query, items, options, byId: new Map(items.map((role) => [role.id, role])) };
  }, [query]);
}
