"use client";

import type { Schemas } from "@mrp/api-client";
import { useTranslations } from "next-intl";
import { useMemo } from "react";

import { CrudList } from "@/components/crud/crud-list";
import { createDataTableColumns } from "@/components/data-table/data-table";
import { NumberField } from "@/components/form/fields";
import { Permissions } from "@/lib/auth/permissions";
import { queryKeys } from "@/lib/api/query-keys";
import { formatQuantity } from "@/lib/format/number";
import { emptyToNull, msg, optionalId, requiredId, requiredNumber, z } from "@/lib/validation/zod";

import { ItemLabel, ItemPickerField, UnitSelectField, useUnits } from "./shared";

/** Schema of a unit conversion; mirrors `SaveUnitConversionRequest`. */
export const unitConversionSchema = z
  .object({
    itemId: optionalId(),
    fromUnitId: requiredId(),
    toUnitId: requiredId(),
    factor: requiredNumber(0.000001, 999_999_999_999),
  })
  .refine((values) => values.fromUnitId === "" || values.fromUnitId !== values.toUnitId, {
    path: ["toUnitId"],
    error: msg("sameUnit"),
  });

/** Form values of a unit conversion. */
export type UnitConversionValues = z.infer<typeof unitConversionSchema>;

type Row = Schemas["UnitConversionDto"];

/** List and form of unit conversions: `1 from-unit = factor × to-unit`, for all items or one item. */
export function UnitConversionList() {
  const t = useTranslations("masters");
  const units = useUnits();
  const columns = useMemo(() => {
    const helper = createDataTableColumns<Row>();
    const unitCode = (id: string) => units.byId.get(id)?.code ?? "…";
    return [
      helper.display({
        id: "item",
        header: t("conversions.item"),
        cell: ({ row }) => (row.original.itemId ? <ItemLabel id={row.original.itemId} /> : <span className="text-muted-foreground">{t("conversions.allItems")}</span>),
      }),
      helper.display({
        id: "formula",
        header: t("conversions.formula"),
        meta: { className: "whitespace-nowrap tabular-nums" },
        cell: ({ row }) => `1 ${unitCode(row.original.fromUnitId)} = ${formatQuantity(row.original.factor)} ${unitCode(row.original.toUnitId)}`,
      }),
    ];
  }, [t, units.byId]);

  return (
    <CrudList<Row, UnitConversionValues, Schemas["SaveUnitConversionRequest"]>
      queryKey={queryKeys.unitConversions}
      managePermission={Permissions.mastersManage}
      list={(client, { page, pageSize }) => client.GET("/api/v1/masters/unit-conversions", { params: { query: { page, pageSize } } })}
      create={(client, body) => client.POST("/api/v1/masters/unit-conversions", { body })}
      update={(client, id, body) => client.PUT("/api/v1/masters/unit-conversions/{id}", { params: { path: { id } }, body })}
      columns={columns}
      schema={unitConversionSchema}
      defaultValues={{ itemId: "", fromUnitId: "", toUnitId: "", factor: null as unknown as number }}
      toValues={(row) => ({ itemId: row.itemId ?? "", fromUnitId: row.fromUnitId, toUnitId: row.toUnitId, factor: row.factor })}
      toBody={(values) => ({
        itemId: emptyToNull(values.itemId),
        fromUnitId: values.fromUnitId,
        toUnitId: values.toUnitId,
        factor: values.factor,
      })}
      searchable={false}
      labels={{
        caption: t("conversions.title"),
        add: t("conversions.add"),
        edit: t("conversions.edit"),
        view: t("conversions.view"),
        saved: t("conversions.saved"),
      }}
      fields={({ form, editing }) => (
        <>
          <ItemPickerField
            control={form.control}
            name="itemId"
            label={t("conversions.item")}
            description={editing ? t("conversions.keyFixed") : t("conversions.itemHint")}
            placeholder={t("conversions.allItems")}
            disabled={Boolean(editing)}
          />
          <UnitSelectField control={form.control} name="fromUnitId" label={t("conversions.fromUnit")} required disabled={Boolean(editing)} />
          <UnitSelectField control={form.control} name="toUnitId" label={t("conversions.toUnit")} required disabled={Boolean(editing)} />
          <NumberField
            control={form.control}
            name="factor"
            label={t("conversions.factor")}
            description={t("conversions.factorHint")}
            decimals={6}
            required
          />
        </>
      )}
    />
  );
}
