"use client";

import {
  keepPreviousData,
  useMutation,
  useQuery,
  useQueryClient,
  type QueryKey,
  type UseMutationResult,
  type UseQueryResult,
} from "@tanstack/react-query";
import { useTranslations } from "next-intl";
import { toast } from "sonner";

import { api, type ApiClient } from "@/lib/api/client";
import { ApiError, applyProblemToForm, problemFieldErrors, unwrap, type ApiResult, type FormErrorTarget } from "@/lib/api/problem";

import { useProblemMessage } from "./use-problem-message";

/** Options of {@link useApiQuery}. */
export interface ApiQueryOptions<TData> {
  /** Cache key; start with a root from `queryKeys` and include every parameter of the request. */
  queryKey: QueryKey;
  /** The request, for example `(api) => api.GET("/api/v1/masters/units", { params: { query } })`. */
  queryFn: (client: ApiClient, context: { signal: AbortSignal }) => Promise<ApiResult<TData>>;
  /** Set to `false` to wait, for example until an id is known. */
  enabled?: boolean;
  /** Keep showing the previous page while the next one loads (default `false`). */
  keepPrevious?: boolean;
  /** Milliseconds the result counts as fresh. */
  staleTime?: number;
  /** Refetch on this interval in milliseconds. */
  refetchInterval?: number | false;
}

/**
 * `useQuery` for the typed API client. Unwraps the `{ data, error, response }` result of
 * `openapi-fetch`, so `data` is the response body and `error` is always an `ApiError`.
 */
export function useApiQuery<TData>(options: ApiQueryOptions<TData>): UseQueryResult<TData, ApiError> {
  return useQuery<TData, ApiError>({
    queryKey: options.queryKey,
    queryFn: ({ signal }) => unwrap(options.queryFn(api, { signal })),
    enabled: options.enabled,
    placeholderData: options.keepPrevious ? keepPreviousData : undefined,
    staleTime: options.staleTime,
    refetchInterval: options.refetchInterval,
  });
}

/** Options of {@link useApiMutation}. */
export interface ApiMutationOptions<TData, TVariables> {
  /** The request, for example `(api, body) => api.POST("/api/v1/masters/units", { body })`. */
  mutationFn: (client: ApiClient, variables: TVariables) => Promise<ApiResult<TData>>;
  /** Form whose fields receive the server-side field errors (`form` from `useForm`). */
  form?: FormErrorTarget;
  /** Toast shown after success. */
  successMessage?: string | ((data: TData, variables: TVariables) => string);
  /** Query key roots to refresh after success. */
  invalidate?: readonly QueryKey[];
  /** Runs after success, after the caches were invalidated. */
  onSuccess?: (data: TData, variables: TVariables) => void | Promise<void>;
  /** Runs on failure before the default handling; return `true` to suppress the error toast. */
  onError?: (error: ApiError, variables: TVariables) => boolean | void;
}

/**
 * `useMutation` for the typed API client with the standard error handling:
 * field errors of a validation problem go to the form fields, everything else becomes a toast
 * with the translated message of the problem `code`.
 */
export function useApiMutation<TData, TVariables = void>(
  options: ApiMutationOptions<TData, TVariables>,
): UseMutationResult<TData, ApiError, TVariables> {
  const queryClient = useQueryClient();
  const messageOf = useProblemMessage();
  const t = useTranslations("errors");

  return useMutation<TData, ApiError, TVariables>({
    mutationFn: (variables) => unwrap(options.mutationFn(api, variables)),
    onSuccess: async (data, variables) => {
      await Promise.all((options.invalidate ?? []).map((queryKey) => queryClient.invalidateQueries({ queryKey })));
      const message = typeof options.successMessage === "function" ? options.successMessage(data, variables) : options.successMessage;
      if (message) {
        toast.success(message);
      }
      await options.onSuccess?.(data, variables);
    },
    onError: (error, variables) => {
      if (options.onError?.(error, variables) === true) {
        return;
      }
      const fieldErrors = problemFieldErrors(error.problem);
      const unmatched = options.form ? applyProblemToForm(options.form, error.problem) : Object.values(fieldErrors);
      if (Object.keys(fieldErrors).length > 0 && unmatched.length === 0) {
        toast.error(t("client.validation"));
        return;
      }
      toast.error(messageOf(error), unmatched.length > 0 ? { description: unmatched.join("\n") } : undefined);
    },
  });
}
