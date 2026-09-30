# @mrp/web

Web app of the MRP SaaS (office work: master data, purchasing, inventory, approvals, settings).
Next.js (App Router) + TypeScript (strict) + Tailwind CSS + shadcn/ui + TanStack Query/Table,
Thai first, English second.

Read `../CLAUDE.md` and `../docs/specs/*.md` before changing behaviour.

## Run

All commands run from the repository root (pnpm workspace).

```sh
pnpm install
cp web/.env.example web/.env.local     # API_BASE_URL=http://localhost:5080
pnpm --filter @mrp/web dev             # http://localhost:3000
```

| Command | What it does |
|---|---|
| `pnpm --filter @mrp/web dev` | development server |
| `pnpm --filter @mrp/web typecheck` | `tsc --noEmit` |
| `pnpm --filter @mrp/web lint` | ESLint (Next rules, JSDoc on every export, no literal UI text) |
| `pnpm --filter @mrp/web test` | unit tests (Vitest) |
| `pnpm --filter @mrp/web build` | production build, `output: "standalone"` |
| `pnpm --filter @mrp/web start:standalone` | runs the standalone build like the container does (`PORT`, `HOSTNAME`) |
| `pnpm --filter @mrp/web e2e` | Playwright against the real API |

### Environment

| Variable | Read by | Meaning |
|---|---|---|
| `API_BASE_URL` | server, at runtime | URL of the .NET API for the route handlers. Default `http://localhost:5080`. |
| `NEXT_PUBLIC_API_BASE_URL` | browser, at build time | Leave empty. When set, the browser calls the API directly instead of the proxy (needs CORS on the API; not covered by the e2e tests). |
| `APP_ORIGIN` | server | Extra origins accepted by the CSRF check, comma separated. Only needed behind a proxy that rewrites `Host`. |
| `COOKIE_SECURE` | server | Forces the `Secure` cookie attribute on/off. Default: on in production. |

No secrets belong in this package: the web app has none. It only forwards the user's tokens.

## Security design

### Tokens

| Token | Lives in | Lifetime |
|---|---|---|
| refresh token | cookie `mrp_rt`: `HttpOnly; Secure (production); SameSite=Lax; Path=/` | 30 days, rotates on every use |
| access token (JWT) | memory of the tab (`src/lib/auth/token-manager.ts`) | 15 minutes |

Nothing is written to `localStorage`/`sessionStorage`. After a reload the tab has no access
token and gets one from `POST /api/auth/refresh`.

The browser never sees the refresh token. The route handlers under `src/app/api/auth/*`
(`login`, `signup`, `refresh`, `logout`) call the API, move the refresh token into the cookie and
return `{ accessToken, expiresIn, user }`.

**Single flight.** Refresh tokens rotate and reusing an old one revokes every session of the
user, so two refreshes must never run with the same token:

- inside a tab, `tokenManager.refresh()` shares one request between all callers
  (ten requests failing with 401 → one refresh);
- between tabs, the refresh runs inside a Web Lock (`navigator.locks`), so tabs take turns and
  each one sends the cookie the previous one received.

A refresh that fails for a temporary reason (API down, HTTP 429/5xx) keeps the session; only
400/401/403 sign the user out.

### Data calls: same-origin proxy (decision)

Data calls go to `/api/v1/*` of this app (`src/app/api/v1/[...path]/route.ts`), which forwards
them to the API with the `Authorization: Bearer` header the browser sent.

Why the proxy and not direct calls from the browser:

- the API needs no CORS configuration and no public hostname of its own; the web app works on
  any origin and port (the API's development CORS list only allows `localhost:3000`, which is
  often taken on developer machines);
- the Content-Security-Policy can stay at `connect-src 'self'`;
- the API URL is runtime configuration of the server (`API_BASE_URL`), not baked into the bundle.

Cost: one extra hop inside the same region. If that ever matters, set
`NEXT_PUBLIC_API_BASE_URL` to switch the browser to direct calls; nothing else changes.

The proxy forwards method, path, query, body (max 2 MB), `Authorization`, `Content-Type`,
`Accept-Language` and `X-Forwarded-For`. It never forwards cookies and never turns a cookie into
credentials. `auth/login`, `auth/signup`, `auth/refresh` and `auth/logout` are not reachable
through it, so refresh tokens cannot leak into the browser that way.

### CSRF

- The data proxy authenticates with a bearer header, which a cross-site page cannot set: there
  is nothing to forge.
- The cookie-backed handlers (`/api/auth/*`) are all `POST` and protected twice: the cookie is
  `SameSite=Lax`, and `src/server/origin.ts` rejects every request whose `Origin` is not this
  site (or whose `Sec-Fetch-Site` is not `same-origin`). Requests without `Origin` are rejected
  unless the browser marks them `same-origin`.
- The language cookie is set by a Server Action, which Next.js protects with its own origin check.

### Headers

| Header | Where | Value |
|---|---|---|
| `Content-Security-Policy` (pages) | `src/proxy.ts` | per-request nonce, `script-src 'self' 'nonce-…' 'strict-dynamic'`, `connect-src 'self'`, `frame-ancestors 'none'`, `object-src 'none'`, `base-uri 'self'`, `form-action 'self'` |
| `Content-Security-Policy` (`/api/*`) | `next.config.ts` | `default-src 'none'; frame-ancestors 'none'` |
| `X-Content-Type-Options`, `Referrer-Policy`, `X-Frame-Options`, `Permissions-Policy`, `Cross-Origin-Opener-Policy`, HSTS (production) | `next.config.ts` | see file |

`style-src` allows `'unsafe-inline'` because Radix UI and `next/font` set style attributes at
runtime. The nonce requires dynamic rendering; every page is dynamic anyway because the language
comes from a cookie.

### Route protection and permissions

1. `src/proxy.ts` redirects visitors without a refresh cookie to `/login?next=…` (optimistic check).
2. `AppShell` restores the session and redirects when the refresh fails.
3. The sidebar hides entries the user has no permission for; pages wrap their content in
   `<RequirePermission>`; buttons use `useCan` / `<Can>`.
4. Hiding is for usability only. The API checks every call; a 403 is shown as the "no access"
   state (lists) or as a toast (actions).

`?next=` is validated by `safeNextPath` (same-site paths only).

## Structure

```
web/
  e2e/                       Playwright tests against the real API
  scripts/start-standalone.mjs
  src/
    proxy.ts                 auth redirect + CSP nonce (Next.js 16 name of "middleware")
    app/
      (auth)/login, signup   public pages
      (app)/…                signed-in pages inside the shell
      api/auth/*             BFF: sign-in, sign-up, refresh, sign-out
      api/v1/[...path]       data proxy
    components/
      ui/                    shadcn/ui primitives (generated, do not restyle per screen)
      data-table/            DataTable, useListState
      form/                  fields, inputs, FormDrawer, EntityCombobox
      feedback/              StatusBadge, ConfirmDialog, empty/error/forbidden states
      crud/                  CrudList: list + drawer in one component
      layout/                PageHeader, RequirePermission, DetailSheet, ComingSoon
      shell/                 sidebar, top bar, user menu, language and theme switch
      providers/             QueryProvider, AuthProvider
    features/<module>/       screens of a module: schema + list + form per resource
    i18n/                    locale cookie, message loading, message types
    lib/
      api/                   typed client, ApiError/problem mapping, query keys
      auth/                  token manager, session calls, permissions, useCan
      format/                money, quantity, date
      hooks/                 useApiQuery, useApiMutation, useProblemMessage
      validation/zod.ts      zod with i18n messages + helpers
      navigation.ts          the sidebar registry
    messages/<locale>/<namespace>.json
    server/                  server-only helpers of the route handlers
```

### Adding a module (for example production)

1. Regenerate the API client (`pnpm api:openapi` at the root) — types come from there, never by hand.
2. Add the permission codes to `Permissions` (`src/lib/auth/permissions.ts`). A unit test compares
   the list with `Permissions.cs`.
3. Add query key roots to `src/lib/api/query-keys.ts`.
4. Add or enable the group in `src/lib/navigation.ts` (remove `comingSoon`), labels in `nav.json`.
5. Add a namespace to `src/i18n/messages.ts` and `src/i18n/types.ts`, with
   `src/messages/th/<module>.json` and `src/messages/en/<module>.json`.
6. Add error codes of the module to `errors.json`, statuses to `StatusBadge` + `status.json`,
   document types to `features/approvals/document-types.ts` + `common.json`.
7. Build the screens in `src/features/<module>/`, pages in `src/app/(app)/<module>/…/page.tsx`
   (replace the `ComingSoon` placeholders).

## Building blocks

Every example below is taken from a screen in this repository.

### `useApiQuery` / `useApiMutation`

Wrap TanStack Query around the typed client. `data` is the response body; `error` is always an
`ApiError` (`status`, `code`, `problem`). Mutations put server-side field errors on the form
fields and show everything else as a toast, translated by the problem `code`
(`messages/*/errors.json`) with the API's `detail` as fallback.

```tsx
const query = useApiQuery({
  queryKey: [...queryKeys.units, "list", params],
  queryFn: (api) => api.GET("/api/v1/masters/units", { params: { query: params } }),
  keepPrevious: true,
});

const save = useApiMutation({
  mutationFn: (api, body: Schemas["SaveCodedRequest"]) => api.POST("/api/v1/masters/units", { body }),
  form,                                  // server field errors → form fields
  invalidate: [queryKeys.units],         // refetch lists afterwards
  successMessage: t("units.saved"),
  onError: (error) => error.code === "common.duplicate" && handleItMyself(), // return true to skip the toast
});
```

`useProblemMessage()` returns `(error) => string` for places that show the message themselves.

### `DataTable` + `useListState`

Server-side paging and search, loading skeleton, empty/error/forbidden states, pager.
Columns are TanStack Table v9 column definitions; `meta` controls alignment and
`hideOnMobile` (keep only what identifies the row visible on phones).

```tsx
const state = useListState();                       // page, pageSize, debounced search
const query = useApiQuery({ queryKey: [...queryKeys.items, "list", state.params], queryFn: … });

const column = createDataTableColumns<Schemas["ItemDto"]>();
const columns = useMemo(() => [
  column.accessor((row) => row.code, { id: "code", header: t("fields.code") }),
  column.accessor((row) => row.standardCost, {
    id: "cost", header: t("items.standardCost"),
    meta: { align: "right", hideOnMobile: true },
    cell: ({ row }) => formatMoney(row.original.standardCost),
  }),
], [t]);

<DataTable caption={t("items.title")} columns={columns} data={query.data}
  loading={query.isLoading} fetching={query.isFetching} error={query.error}
  onRetry={() => void query.refetch()} state={state} searchable
  onRowClick={(row) => open(row)} actions={<Button>…</Button>} />
```

`data` may also be a plain array (lists the API does not page, e.g. roles).

### Form fields, `FormDrawer`

Fields are bound to react-hook-form through `control` + `name` and render label, required marker,
help text and the error. Validation messages are i18n keys (`validation.json`); messages coming
from the API are shown as they are.

`TextField`, `PasswordField`, `TextareaField`, `NumberField`, `MoneyField` (4 decimals),
`QuantityField` (6 decimals), `PercentField`, `IntegerField`, `SelectField`, `SwitchField`,
`CheckboxField`, `CheckboxGroupField`, `DateField`, and in `features/masters/shared.tsx`
`UnitSelectField`, `ItemGroupSelectField`, `ItemPickerField`, `SupplierPickerField`
(`EntityField`/`EntityCombobox` for other records with server-side search).

```tsx
const schema = z.object({                 // import { z, requiredText, … } from "@/lib/validation/zod"
  code: requiredText(40),                 // same limits as the API request record
  name: requiredText(200),
  standardCost: requiredNumber(0, 99_999_999_999_999),
  validFrom: requiredDate(),              // "YYYY-MM-DD"
});
const form = useForm({ resolver: zodResolver(schema), defaultValues });

<FormDrawer open={open} onOpenChange={setOpen} title={t("items.add")} form={form}
  onSubmit={(values) => save.mutateAsync(toBody(values)).then(() => undefined, () => undefined)}
  submitting={save.isPending} readOnly={!canManage} size="lg">
  <TextField control={form.control} name="code" label={t("fields.code")} required maxLength={40} />
  <MoneyField control={form.control} name="standardCost" label={t("items.standardCost")} required />
  <DateField control={form.control} name="validFrom" label={t("prices.validFrom")} required />
</FormDrawer>
```

Form conventions: text fields hold `""` for "not set" and numbers hold `null`; convert with
`emptyToNull` when building the request body. Custom messages: `msg("dateOrder")`.
Every field wrapper carries `data-field="<name>"` for tests.

### `CrudList`

The whole "list + search + active filter + drawer" pattern of a master in one component.
Give it the API calls, columns, schema, converters and fields. See
`features/masters/warehouses.tsx` (with a child list in a `DetailSheet`) and
`features/settings/users.tsx` (different bodies for create and update).

```tsx
<CrudList<Row, Values, Schemas["SaveWarehouseRequest"]>
  queryKey={queryKeys.warehouses} managePermission={Permissions.mastersManage}
  list={(api, query) => api.GET("/api/v1/masters/warehouses", { params: { query } })}
  create={(api, body) => api.POST("/api/v1/masters/warehouses", { body })}
  update={(api, id, body) => api.PUT("/api/v1/masters/warehouses/{id}", { params: { path: { id } }, body })}
  columns={columns} schema={warehouseSchema} defaultValues={defaults}
  toValues={toValues} toBody={toBody} hasActiveFlag labels={labels}
  fields={({ form, editing }) => <>…</>} />
```

### `StatusBadge`

```tsx
<StatusBadge status={document.status} />     // "PartiallyReceived" → translated, coloured
<ActiveBadge active={row.isActive} />
```

New statuses: add the tone to `statusTones` and the label to `status.json` (a unit test checks both).

### `ConfirmDialog`

For actions that cannot be undone. `reason`: `"none"`, `"optional"` or `"required"` (blocks the
confirm button's action until a reason is typed). The dialog closes when `onConfirm` resolves and
stays open when it rejects.

```tsx
<ConfirmDialog open={open} onOpenChange={setOpen} destructive reason="required"
  title={t("confirm.reject.title", { documentNo })} reasonMaxLength={1000}
  onConfirm={(reason) => reject.mutateAsync({ comment: reason })} />
```

### Permissions

```tsx
const canManage = useCan(Permissions.mastersManage);          // "*" grants everything
<Can permission={[Permissions.purchaseOrderManage, Permissions.purchaseRequestManage]}>…</Can>  // any of
<RequirePermission permission={Permissions.mastersRead}>page</RequirePermission>
```

### Formatters and inputs

```ts
formatMoney(1234.5)                          // "1,234.50"
formatMoney(1234.5, { currency: "THB" })     // "฿1,234.50"
formatQuantity(1250.5, { unit: "KG" })       // "1,250.5 KG"   (up to 6 decimals, no trailing zeros)
formatPercent(7)                             // "7%"
formatDate("2026-09-29", { locale: "th" })   // "29 ก.ย. 2569"
formatDate("2026-09-29", { locale: "en" })   // "29 Sep 2026"
formatDateTime(utcTimestamp, { locale, timeZone: user.timeZone })
todayIso(user.timeZone)                      // "2026-09-29" for the API, always Gregorian
```

Dates sent to the API are built by `toIsoDate`/`todayIso`/`localDateToIso` only — explicit
Gregorian calendar and Latin digits, independent of the locale of the OS or browser (a Thai
locale would otherwise produce year 2569). Never use `toLocaleDateString` for API values.

Unbound inputs for tables and custom forms: `NumberInput`, `MoneyInput`, `QuantityInput`,
`PercentInput`, `IntegerInput`, `DateInput`.

### i18n

Texts live in `src/messages/<locale>/<namespace>.json`; Thai is the reference, a unit test keeps
English in sync (same keys, same placeholders). ESLint rejects literal text in JSX. The language
choice is stored in the cookie `mrp_locale`; the first sign-in sets it from the user's profile.

## Tests

- **Unit** (`src/**/*.test.ts`): formatters, permission helper, problem → form error mapping,
  token refresh single flight, origin check, redirect validation, document number example,
  navigation filtering, message files.
- **E2E** (`e2e/`): Playwright against the real API. Each run signs up a fresh company with a
  random code. `E2E_API_URL` (default `http://localhost:5080`) is the API, `E2E_WEB_PORT`
  (default 3100) the port of the web app, `E2E_BASE_URL` tests a deployed web app instead.
  The run fails at once with an explanation when the API or its database is not usable.

```sh
pnpm --filter @mrp/web exec playwright install chromium   # once
pnpm --filter @mrp/web e2e
```

The tests share one signed-in page and navigate through the sidebar, because the API rate-limits
its sign-in endpoints (including refresh) per client address.

## Known limitations

- The cross-tab refresh lock needs Web Locks (all current browsers). Without it, two tabs
  refreshing in the same instant could present the same refresh token.
- Export to Excel is not built yet; `DataTable` is the place to add it.
- The permission matrix shows the raw code for permissions without a translation, so new API
  modules work before their labels are added.
