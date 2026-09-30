"use client";

import { zodResolver } from "@hookform/resolvers/zod";
import type { Schemas } from "@mrp/api-client";
import { PencilIcon } from "lucide-react";
import { useTranslations } from "next-intl";
import { useEffect, useMemo, useState } from "react";
import { useForm, useWatch } from "react-hook-form";

import { createDataTableColumns, DataTable } from "@/components/data-table/data-table";
import { IntegerField, SelectField, TextField } from "@/components/form/fields";
import { FormDrawer } from "@/components/form/form-drawer";
import { useUser } from "@/components/providers/auth-provider";
import { Button } from "@/components/ui/button";
import { useDocumentTypeLabel } from "@/features/approvals/document-types";
import { queryKeys } from "@/lib/api/query-keys";
import { Permissions } from "@/lib/auth/permissions";
import { useCan } from "@/lib/auth/use-can";
import { todayIso } from "@/lib/format/date";
import { useApiMutation, useApiQuery } from "@/lib/hooks/use-api";
import { msg, requiredInteger, z } from "@/lib/validation/zod";

import { formatDocumentNumber, numberingPeriods, separators } from "./numbering-format";

/** Schema of a document number format; mirrors `SaveDocumentNumberFormatRequest`. */
export const numberingSchema = z.object({
  prefix: z
    .string()
    .trim()
    .min(1)
    .max(10)
    .regex(/^[a-zA-Z0-9]+$/, { error: msg("prefix") }),
  period: z.enum(numberingPeriods),
  digits: requiredInteger(3, 10),
  separator: z.enum(separators),
});

/** Form values of a document number format. */
export type NumberingValues = z.infer<typeof numberingSchema>;

type Row = Schemas["DocumentNumberFormatDto"];

/** Document number formats per document type, with a live example while editing. */
export function NumberingList() {
  const t = useTranslations("settings");
  const tc = useTranslations("common");
  const user = useUser();
  const canManage = useCan(Permissions.settingsManage);
  const documentType = useDocumentTypeLabel();
  const [editing, setEditing] = useState<Row | null>(null);

  const query = useApiQuery({
    queryKey: [...queryKeys.numbering, "all"],
    queryFn: (api) => api.GET("/api/v1/document-number-formats"),
  });

  const form = useForm<NumberingValues, unknown, NumberingValues>({
    resolver: zodResolver(numberingSchema),
    defaultValues: { prefix: "", period: "Month", digits: 4, separator: "-" },
  });
  useEffect(() => {
    if (editing) {
      form.reset({
        prefix: editing.prefix,
        period: editing.period,
        digits: editing.digits,
        separator: separators.find((value) => value === editing.separator) ?? "-",
      });
    }
  }, [editing, form]);

  const values = useWatch({ control: form.control });
  const example = formatDocumentNumber(
    { prefix: values.prefix ?? "", period: values.period ?? "None", digits: values.digits ?? 0, separator: values.separator ?? "" },
    todayIso(user.timeZone),
    1,
  );

  const save = useApiMutation({
    mutationFn: (api, body: NumberingValues) =>
      api.PUT("/api/v1/document-number-formats/{documentType}", {
        params: { path: { documentType: editing?.documentType ?? "" } },
        body: { ...body, prefix: body.prefix.toUpperCase() },
      }),
    form,
    invalidate: [queryKeys.numbering],
    successMessage: t("numbering.saved"),
    onSuccess: () => setEditing(null),
  });

  const columns = useMemo(() => {
    const helper = createDataTableColumns<Row>();
    return [
      helper.display({
        id: "documentType",
        header: t("numbering.documentType"),
        cell: ({ row }) => (
          <span className="flex flex-col">
            <span className="font-medium">{documentType(row.original.documentType)}</span>
            <code className="text-xs text-muted-foreground">{row.original.documentType}</code>
          </span>
        ),
      }),
      helper.accessor((row) => row.period, {
        id: "period",
        header: t("numbering.period"),
        meta: { hideOnMobile: true },
        cell: ({ row }) => t(`numbering.periods.${row.original.period}`),
      }),
      helper.accessor((row) => row.digits, { id: "digits", header: t("numbering.digits"), meta: { align: "right", hideOnMobile: true } }),
      helper.accessor((row) => row.example, {
        id: "example",
        header: t("numbering.example"),
        cell: ({ row }) => <code className="rounded bg-muted px-1.5 py-0.5">{row.original.example}</code>,
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
  }, [t, tc, canManage, documentType]);

  return (
    <>
      <DataTable
        caption={t("numbering.title")}
        columns={columns}
        data={query.data}
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
        title={t("numbering.edit", { type: editing ? documentType(editing.documentType) : "" })}
        form={form}
        onSubmit={(body) => save.mutateAsync(body).then(() => undefined, () => undefined)}
        submitting={save.isPending}
        readOnly={!canManage}
        after={
          <div className="rounded-lg border bg-muted/50 p-3" aria-live="polite">
            <p className="text-xs text-muted-foreground">{t("numbering.liveExample")}</p>
            <p className="mt-1 font-mono text-lg" data-testid="numbering-example">
              {example}
            </p>
          </div>
        }
      >
        <TextField
          control={form.control}
          name="prefix"
          label={t("numbering.prefix")}
          description={t("numbering.prefixRule")}
          required
          maxLength={10}
          transform={(value) => value.toUpperCase()}
          autoFocus
        />
        <SelectField
          control={form.control}
          name="period"
          label={t("numbering.period")}
          description={t("numbering.periodHint")}
          required
          options={numberingPeriods.map((value) => ({ value, label: t(`numbering.periods.${value}`) }))}
        />
        <IntegerField control={form.control} name="digits" label={t("numbering.digits")} description={t("numbering.digitsRule")} required />
        <SeparatorField control={form.control} />
      </FormDrawer>
    </>
  );
}

/** Separator select: maps the empty separator to a selectable value. */
function SeparatorField({ control }: { control: ReturnType<typeof useForm<NumberingValues, unknown, NumberingValues>>["control"] }) {
  const t = useTranslations("settings");
  return (
    <SelectField
      control={control}
      name="separator"
      label={t("numbering.separator")}
      emptyLabel={t("numbering.separators.none")}
      options={separators
        .filter((value) => value !== "")
        .map((value) => ({ value, label: t(`numbering.separators.${value === "-" ? "dash" : "slash"}`) }))}
    />
  );
}
