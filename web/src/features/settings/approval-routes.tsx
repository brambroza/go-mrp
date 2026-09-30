"use client";

import { zodResolver } from "@hookform/resolvers/zod";
import type { Schemas } from "@mrp/api-client";
import { ArrowDownIcon, ArrowUpIcon, PencilIcon, PlusIcon, Trash2Icon } from "lucide-react";
import { useLocale, useTranslations } from "next-intl";
import { useEffect, useMemo, useState } from "react";
import { useFieldArray, useForm } from "react-hook-form";

import { createDataTableColumns, DataTable } from "@/components/data-table/data-table";
import { MoneyField, SelectField, SwitchField, TextField } from "@/components/form/fields";
import { FormDrawer } from "@/components/form/form-drawer";
import { ActiveBadge } from "@/components/feedback/status-badge";
import { Alert, AlertDescription } from "@/components/ui/alert";
import { Button } from "@/components/ui/button";
import { FieldLegend, FieldSet } from "@/components/ui/field";
import { documentTypes, numberingOnlyTypes, useDocumentTypeLabel } from "@/features/approvals/document-types";
import { toLocale } from "@/i18n/config";
import { queryKeys } from "@/lib/api/query-keys";
import { Permissions } from "@/lib/auth/permissions";
import { useCan } from "@/lib/auth/use-can";
import { formatMoney } from "@/lib/format/number";
import { useApiMutation, useApiQuery } from "@/lib/hooks/use-api";
import { optionalNumber, requiredId, requiredText, z } from "@/lib/validation/zod";

import { useRoles } from "./roles-data";

/** Most steps a route may have (`SaveApprovalRouteRequest`). */
export const MAX_STEPS = 10;

/** Schema of an approval route; mirrors `SaveApprovalRouteRequest` and `ApprovalRouteStepDto`. */
export const approvalRouteSchema = z.object({
  name: requiredText(100),
  isActive: z.boolean(),
  steps: z
    .array(
      z.object({
        name: requiredText(100),
        roleId: requiredId(),
        minAmount: optionalNumber(0, 9_999_999_999_999.9999),
        requireTwoFactor: z.boolean(),
      }),
    )
    .max(MAX_STEPS),
});

/** Form values of an approval route. */
export type ApprovalRouteValues = z.infer<typeof approvalRouteSchema>;

/** Row of the routes table: a document type with its route, if one is configured. */
export interface ApprovalRouteRow {
  documentType: string;
  route: Schemas["ApprovalRouteDto"] | null;
}

/** Form values → `SaveApprovalRouteRequest`; steps are numbered in the order shown. */
export function approvalRouteToBody(values: ApprovalRouteValues): Schemas["SaveApprovalRouteRequest"] {
  return {
    name: values.name,
    isActive: values.isActive,
    steps: values.steps.map((step, index) => ({
      stepNo: index + 1,
      name: step.name,
      roleId: step.roleId,
      minAmount: step.minAmount,
      requireTwoFactor: step.requireTwoFactor,
    })),
  };
}

/**
 * One row per document type that can be approved: the types the web app knows plus any type the
 * API returned a route for.
 */
export function toRouteRows(routes: readonly Schemas["ApprovalRouteDto"][]): ApprovalRouteRow[] {
  const byType = new Map(routes.map((route) => [route.documentType, route]));
  const types = [...new Set([...documentTypes, ...byType.keys()])].filter((type) => !numberingOnlyTypes.includes(type));
  return types.map((documentType) => ({ documentType, route: byType.get(documentType) ?? null }));
}

/** Approval routes per document type: steps with role, minimum amount and the 2FA requirement. */
export function ApprovalRouteList() {
  const t = useTranslations("settings");
  const tc = useTranslations("common");
  const locale = toLocale(useLocale());
  const canManage = useCan(Permissions.approvalsConfigure);
  const documentType = useDocumentTypeLabel();
  const roles = useRoles();
  const [editing, setEditing] = useState<ApprovalRouteRow | null>(null);

  const query = useApiQuery({
    queryKey: [...queryKeys.approvalRoutes, "all"],
    queryFn: (api) => api.GET("/api/v1/approval-routes"),
  });
  const rows = useMemo(() => (query.data ? toRouteRows(query.data) : undefined), [query.data]);

  const form = useForm<ApprovalRouteValues, unknown, ApprovalRouteValues>({
    resolver: zodResolver(approvalRouteSchema),
    defaultValues: { name: "", isActive: true, steps: [] },
  });
  const steps = useFieldArray({ control: form.control, name: "steps" });

  useEffect(() => {
    if (!editing) {
      return;
    }
    form.reset(
      editing.route
        ? {
            name: editing.route.name,
            isActive: editing.route.isActive,
            steps: [...editing.route.steps]
              .sort((a, b) => a.stepNo - b.stepNo)
              .map((step) => ({
                name: step.name,
                roleId: step.roleId,
                minAmount: step.minAmount ?? null,
                requireTwoFactor: step.requireTwoFactor,
              })),
          }
        : { name: documentType(editing.documentType), isActive: true, steps: [] },
    );
    // `documentType` only changes with the locale.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [editing, form]);

  const save = useApiMutation({
    mutationFn: (api, values: ApprovalRouteValues) =>
      api.PUT("/api/v1/approval-routes/{documentType}", {
        params: { path: { documentType: editing?.documentType ?? "" } },
        body: approvalRouteToBody(values),
      }),
    form,
    invalidate: [queryKeys.approvalRoutes],
    successMessage: t("routes.saved"),
    onSuccess: () => setEditing(null),
  });

  const columns = useMemo(() => {
    const helper = createDataTableColumns<ApprovalRouteRow>();
    return [
      helper.display({
        id: "documentType",
        header: t("routes.documentType"),
        cell: ({ row }) => (
          <span className="flex flex-col">
            <span className="font-medium">{documentType(row.original.documentType)}</span>
            <code className="text-xs text-muted-foreground">{row.original.documentType}</code>
          </span>
        ),
      }),
      helper.display({
        id: "steps",
        header: t("routes.steps"),
        cell: ({ row }) => {
          const route = row.original.route;
          if (!route || !route.isActive || route.steps.length === 0) {
            return <span className="text-muted-foreground">{t("routes.autoApprove")}</span>;
          }
          return (
            <ol className="flex flex-col gap-0.5 text-sm">
              {route.steps.map((step) => (
                <li key={step.stepNo}>
                  {t("routes.stepLine", { number: step.stepNo, name: step.name })}
                  <span className="text-muted-foreground">
                    {" "}
                    · {roles.byId.get(step.roleId)?.name ?? "…"}
                    {step.minAmount ? ` · ≥ ${formatMoney(step.minAmount, { locale, currency: "THB" })}` : ""}
                    {step.requireTwoFactor ? ` · ${t("routes.twoFactorShort")}` : ""}
                  </span>
                </li>
              ))}
            </ol>
          );
        },
      }),
      helper.display({
        id: "status",
        header: tc("fields.status"),
        meta: { hideOnMobile: true },
        cell: ({ row }) => (row.original.route ? <ActiveBadge active={row.original.route.isActive} /> : null),
      }),
      helper.display({
        id: "actions",
        header: () => <span className="sr-only">{tc("table.actions")}</span>,
        meta: { align: "right", className: "w-px" },
        cell: ({ row }) => (
          <Button
            variant="ghost"
            size="icon-sm"
            aria-label={canManage ? tc("actions.edit") : tc("actions.view")}
            onClick={(event) => {
              event.stopPropagation();
              setEditing(row.original);
            }}
          >
            <PencilIcon />
          </Button>
        ),
      }),
    ];
  }, [t, tc, locale, canManage, documentType, roles.byId]);

  return (
    <>
      <DataTable
        caption={t("routes.title")}
        columns={columns}
        data={rows}
        loading={query.isLoading}
        fetching={query.isFetching}
        error={query.error}
        onRetry={() => void query.refetch()}
        getRowId={(row) => row.documentType}
        onRowClick={setEditing}
      />
      <FormDrawer
        open={editing !== null}
        onOpenChange={(open) => (open ? undefined : setEditing(null))}
        title={t("routes.edit", { type: editing ? documentType(editing.documentType) : "" })}
        description={t("routes.editDescription")}
        form={form}
        onSubmit={(values) => save.mutateAsync(values).then(() => undefined, () => undefined)}
        submitting={save.isPending}
        readOnly={!canManage}
        size="lg"
      >
        <TextField control={form.control} name="name" label={t("routes.name")} required maxLength={100} />
        <SwitchField control={form.control} name="isActive" label={t("routes.isActive")} description={t("routes.isActiveHint")} />
        {steps.fields.length === 0 ? (
          <Alert>
            <AlertDescription>{t("routes.noSteps")}</AlertDescription>
          </Alert>
        ) : null}
        {steps.fields.map((step, index) => (
          <FieldSet key={step.id} className="rounded-lg border p-3" data-testid={`route-step-${index}`}>
            <div className="flex items-center justify-between gap-2">
              <FieldLegend variant="label" className="mb-0">
                {t("routes.step", { number: index + 1 })}
              </FieldLegend>
              <div className="flex items-center gap-1">
                <Button
                  type="button"
                  variant="ghost"
                  size="icon-sm"
                  aria-label={t("routes.moveUp")}
                  disabled={index === 0}
                  onClick={() => steps.move(index, index - 1)}
                >
                  <ArrowUpIcon />
                </Button>
                <Button
                  type="button"
                  variant="ghost"
                  size="icon-sm"
                  aria-label={t("routes.moveDown")}
                  disabled={index === steps.fields.length - 1}
                  onClick={() => steps.move(index, index + 1)}
                >
                  <ArrowDownIcon />
                </Button>
                <Button type="button" variant="ghost" size="icon-sm" aria-label={t("routes.removeStep")} onClick={() => steps.remove(index)}>
                  <Trash2Icon />
                </Button>
              </div>
            </div>
            <div className="grid gap-5 sm:grid-cols-2">
              <TextField control={form.control} name={`steps.${index}.name`} label={t("routes.stepName")} required maxLength={100} />
              <SelectField
                control={form.control}
                name={`steps.${index}.roleId`}
                label={t("routes.role")}
                description={t("routes.roleHint")}
                required
                options={roles.options}
              />
              <MoneyField
                control={form.control}
                name={`steps.${index}.minAmount`}
                label={t("routes.minAmount")}
                description={t("routes.minAmountHint")}
              />
              <SwitchField
                control={form.control}
                name={`steps.${index}.requireTwoFactor`}
                label={t("routes.requireTwoFactor")}
                description={t("routes.requireTwoFactorHint")}
              />
            </div>
          </FieldSet>
        ))}
        {canManage ? (
          <Button
            type="button"
            variant="outline"
            disabled={steps.fields.length >= MAX_STEPS}
            onClick={() => steps.append({ name: "", roleId: "", minAmount: null, requireTwoFactor: false })}
            data-testid="route-add-step"
          >
            <PlusIcon data-icon="inline-start" />
            {t("routes.addStep")}
          </Button>
        ) : null}
      </FormDrawer>
    </>
  );
}
