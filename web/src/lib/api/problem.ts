import { asProblem, type ApiProblem } from "@mrp/api-client";

/** Problem code used when the request never reached the server. */
export const NETWORK_ERROR_CODE = "client.network";

/** Problem code used for validation problems, which the API sends without a `code`. */
export const VALIDATION_ERROR_CODE = "client.validation";

/** Problem code the API uses when a TOTP code is needed to finish signing in. */
export const TWO_FACTOR_REQUIRED_CODE = "platform.auth.two_factor_required";

/** Error thrown by every API helper; carries the HTTP status and the RFC 7807 body. */
export class ApiError extends Error {
  /** HTTP status, or 0 when the request failed before a response arrived. */
  readonly status: number;

  /** Problem body returned by the API (may be empty for non-JSON responses). */
  readonly problem: ApiProblem;

  constructor(status: number, problem: ApiProblem) {
    super(problem.detail ?? problem.title ?? `HTTP ${status}`);
    this.name = "ApiError";
    this.status = status;
    this.problem = problem;
  }

  /** Stable error code, for example `inventory.insufficient_stock`. */
  get code(): string | undefined {
    return this.problem.code;
  }
}

/** Builds an {@link ApiError} from the `error` and `response` of an `openapi-fetch` call. */
export function toApiError(error: unknown, status: number): ApiError {
  const problem = { ...asProblem(error) };
  if (!problem.code && status === 400 && problem.errors && Object.keys(problem.errors).length > 0) {
    problem.code = VALIDATION_ERROR_CODE;
  }
  return new ApiError(status, { ...problem, status: problem.status ?? status });
}

/** Converts anything thrown while calling the API into an {@link ApiError}. */
export function normalizeError(error: unknown): ApiError {
  if (error instanceof ApiError) {
    return error;
  }
  const detail = error instanceof Error ? error.message : undefined;
  return new ApiError(0, { code: NETWORK_ERROR_CODE, detail });
}

/** Result shape of every `openapi-fetch` call. */
export interface ApiResult<T> {
  /** Parsed success body. */
  data?: T;
  /** Parsed error body. */
  error?: unknown;
  /** Raw response. */
  response: Response;
}

/** Returns the success body of an `openapi-fetch` call or throws an {@link ApiError}. */
export async function unwrap<T>(call: Promise<ApiResult<T>>): Promise<T> {
  let result: ApiResult<T>;
  try {
    result = await call;
  } catch (error) {
    throw normalizeError(error);
  }
  if (!result.response.ok) {
    throw toApiError(result.error, result.response.status);
  }
  return result.data as T;
}

/** Looks up the translation of an error code; returns `undefined` when there is none. */
export type CodeTranslator = (code: string) => string | undefined;

/**
 * Message to show for a problem: the translation of `code` when there is one, then a translation
 * for the HTTP status (`http.403`), then the API's `detail`/`title`, then the generic fallback.
 */
export function problemMessage(error: ApiError, translate: CodeTranslator, fallback: string): string {
  const { code, detail, title } = error.problem;
  const byCode = code ? translate(code) : undefined;
  if (byCode) {
    return byCode;
  }
  const byStatus = translate(`http.${error.status}`);
  if (byStatus && (error.status === 403 || error.status === 429 || error.status >= 500 || !detail)) {
    return byStatus;
  }
  return detail ?? (title && title !== code ? title : undefined) ?? byStatus ?? fallback;
}

/**
 * Converts an API field path to a react-hook-form path:
 * `steps[0].name` → `steps.0.name`, `$.Lines[2].Quantity` → `lines.2.quantity`.
 */
export function toFormPath(apiPath: string): string {
  return apiPath
    .replace(/^\$\.?/, "")
    .replace(/\[(\d+)\]/g, ".$1")
    .split(".")
    .filter((segment) => segment.length > 0)
    .map((segment) => segment.charAt(0).toLowerCase() + segment.slice(1))
    .join(".");
}

/** Field errors of a problem keyed by react-hook-form path; the first message of each field wins. */
export function problemFieldErrors(problem: ApiProblem): Record<string, string> {
  const result: Record<string, string> = {};
  for (const [path, messages] of Object.entries(problem.errors ?? {})) {
    const message = Array.isArray(messages) ? messages[0] : undefined;
    const formPath = toFormPath(path);
    if (message && formPath && !(formPath in result)) {
      result[formPath] = message;
    }
  }
  return result;
}

/** Minimal part of a react-hook-form instance needed to show server errors. */
export interface FormErrorTarget {
  /** `form.setError`. */
  setError: (name: never, error: { type: string; message: string }, options?: { shouldFocus: boolean }) => void;
  /** `form.getValues`, used to find out which fields the form has. */
  getValues: () => unknown;
}

/** Whether a dotted path exists in the form values (array items and optional leaves count). */
function hasPath(values: unknown, path: string): boolean {
  const segments = path.split(".");
  let current: unknown = values;
  for (const [index, segment] of segments.entries()) {
    if (typeof current !== "object" || current === null) {
      return false;
    }
    if (!(segment in current)) {
      return false;
    }
    current = (current as Record<string, unknown>)[segment];
    if (index === segments.length - 1) {
      return true;
    }
  }
  return false;
}

/**
 * Puts the field errors of a problem on the matching form fields.
 * Returns the messages that belong to no field of the form, so the caller can show them in a toast.
 */
export function applyProblemToForm(form: FormErrorTarget, problem: ApiProblem): string[] {
  const values = form.getValues();
  const unmatched: string[] = [];
  let focused = false;
  for (const [path, message] of Object.entries(problemFieldErrors(problem))) {
    if (hasPath(values, path)) {
      form.setError(path as never, { type: "server", message }, { shouldFocus: !focused });
      focused = true;
    } else {
      unmatched.push(message);
    }
  }
  return unmatched;
}
