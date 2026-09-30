"use client";

import type { Schemas } from "@mrp/api-client";
import { useTranslations } from "next-intl";
import { useMemo } from "react";

import { CrudList } from "@/components/crud/crud-list";
import { createDataTableColumns } from "@/components/data-table/data-table";
import { IntegerField, MoneyField, QuantityField, SelectField, SwitchField, TextField } from "@/components/form/fields";
import { FieldLegend, FieldSet } from "@/components/ui/field";
import { Permissions } from "@/lib/auth/permissions";
import { queryKeys } from "@/lib/api/query-keys";
import { formatMoney } from "@/lib/format/number";
import { emptyToNull, msg, optionalId, optionalInteger, optionalText, requiredId, requiredInteger, requiredNumber, requiredText, z } from "@/lib/validation/zod";

import { codeText, codedColumns, ItemGroupSelectField, toCode, UnitSelectField } from "./shared";

/** Item types of the API. */
export const itemTypes = ["RawMaterial", "Packaging", "Bulk", "SemiFinished", "FinishedGood", "Service"] as const;

/** Supply types of the API. */
export const supplyTypes = ["Buy", "Make"] as const;

const MAX_QUANTITY = 999_999_999_999;

/** Schema of an item; mirrors `SaveItemRequest` and the rules of the `Item` entity. */
export const itemSchema = z
  .object({
    code: codeText(),
    name: requiredText(200),
    nameEn: optionalText(200),
    itemType: z.enum(itemTypes),
    supplyType: z.enum(supplyTypes),
    itemGroupId: optionalId(),
    stockUnitId: requiredId(),
    purchaseUnitId: optionalId(),
    barcode: optionalText(50),
    isLotTracked: z.boolean(),
    shelfLifeDays: optionalInteger(1, 36_500),
    leadTimeDays: requiredInteger(0, 3_650),
    safetyStock: requiredNumber(0, MAX_QUANTITY),
    minStock: requiredNumber(0, MAX_QUANTITY),
    maxStock: requiredNumber(0, MAX_QUANTITY),
    minOrderQty: requiredNumber(0, MAX_QUANTITY),
    orderMultiple: requiredNumber(0, MAX_QUANTITY),
    standardCost: requiredNumber(0, 99_999_999_999_999),
    isActive: z.boolean(),
  })
  .refine((values) => !(values.maxStock > 0 && values.maxStock < values.minStock), { path: ["maxStock"], error: msg("maxBelowMin") });

/** Form values of an item. */
export type ItemValues = z.infer<typeof itemSchema>;

type Row = Schemas["ItemDto"];

const defaults: ItemValues = {
  code: "",
  name: "",
  nameEn: "",
  itemType: "RawMaterial",
  supplyType: "Buy",
  itemGroupId: "",
  stockUnitId: "",
  purchaseUnitId: "",
  barcode: "",
  isLotTracked: true,
  shelfLifeDays: null,
  leadTimeDays: 0,
  safetyStock: 0,
  minStock: 0,
  maxStock: 0,
  minOrderQty: 0,
  orderMultiple: 0,
  standardCost: 0,
  isActive: true,
};

/** Item → form values. */
export function itemToValues(row: Row): ItemValues {
  return {
    code: row.code,
    name: row.name,
    nameEn: row.nameEn ?? "",
    itemType: row.itemType,
    supplyType: row.supplyType,
    itemGroupId: row.itemGroupId ?? "",
    stockUnitId: row.stockUnitId,
    purchaseUnitId: row.purchaseUnitId ?? "",
    barcode: row.barcode ?? "",
    isLotTracked: row.isLotTracked,
    shelfLifeDays: row.shelfLifeDays ?? null,
    leadTimeDays: row.leadTimeDays,
    safetyStock: row.safetyStock,
    minStock: row.minStock,
    maxStock: row.maxStock,
    minOrderQty: row.minOrderQty,
    orderMultiple: row.orderMultiple,
    standardCost: row.standardCost,
    isActive: row.isActive,
  };
}

/** Form values → `SaveItemRequest`. */
export function itemToBody(values: ItemValues): Schemas["SaveItemRequest"] {
  return {
    ...values,
    nameEn: emptyToNull(values.nameEn),
    itemGroupId: emptyToNull(values.itemGroupId),
    purchaseUnitId: emptyToNull(values.purchaseUnitId),
    barcode: emptyToNull(values.barcode),
  };
}

/** List and form of items (raw materials, packaging, bulk, semi-finished and finished goods, services). */
export function ItemList() {
  const t = useTranslations("masters");
  const tc = useTranslations("common");
  const columns = useMemo(() => {
    const helper = createDataTableColumns<Row>();
    return [
      ...codedColumns<Row>({ code: tc("fields.code"), name: tc("fields.name"), nameEn: tc("fields.nameEn") }),
      helper.accessor((row) => row.itemType, {
        id: "itemType",
        header: t("items.itemType"),
        meta: { hideOnMobile: true },
        cell: ({ row }) => t(`itemTypes.${row.original.itemType}`),
      }),
      helper.accessor((row) => row.stockUnitCode, { id: "stockUnit", header: t("items.stockUnit"), meta: { hideOnMobile: true } }),
      helper.accessor((row) => row.standardCost, {
        id: "standardCost",
        header: t("items.standardCost"),
        meta: { align: "right", hideOnMobile: true, className: "tabular-nums" },
        cell: ({ row }) => formatMoney(row.original.standardCost),
      }),
    ];
  }, [t, tc]);

  return (
    <CrudList<Row, ItemValues, Schemas["SaveItemRequest"]>
      queryKey={queryKeys.items}
      managePermission={Permissions.mastersManage}
      list={(client, query) => client.GET("/api/v1/masters/items", { params: { query } })}
      create={(client, body) => client.POST("/api/v1/masters/items", { body })}
      update={(client, id, body) => client.PUT("/api/v1/masters/items/{id}", { params: { path: { id } }, body })}
      columns={columns}
      schema={itemSchema}
      defaultValues={defaults}
      toValues={itemToValues}
      toBody={itemToBody}
      hasActiveFlag
      drawerSize="lg"
      labels={{
        caption: t("items.title"),
        add: t("items.add"),
        edit: t("items.edit"),
        view: t("items.view"),
        saved: t("items.saved"),
        searchPlaceholder: t("items.searchPlaceholder"),
      }}
      fields={({ form, editing }) => (
        <>
          <div className="grid gap-5 sm:grid-cols-2">
            <TextField control={form.control} name="code" label={tc("fields.code")} required maxLength={40} transform={toCode} autoFocus />
            <TextField control={form.control} name="barcode" label={t("items.barcode")} maxLength={50} />
          </div>
          <TextField control={form.control} name="name" label={tc("fields.name")} required maxLength={200} />
          <TextField control={form.control} name="nameEn" label={tc("fields.nameEn")} maxLength={200} />
          <div className="grid gap-5 sm:grid-cols-2">
            <SelectField
              control={form.control}
              name="itemType"
              label={t("items.itemType")}
              required
              options={itemTypes.map((value) => ({ value, label: t(`itemTypes.${value}`) }))}
            />
            <SelectField
              control={form.control}
              name="supplyType"
              label={t("items.supplyType")}
              required
              options={supplyTypes.map((value) => ({ value, label: t(`supplyTypes.${value}`) }))}
            />
            <ItemGroupSelectField control={form.control} name="itemGroupId" label={t("items.itemGroup")} emptyLabel={tc("select.none")} />
            <UnitSelectField
              control={form.control}
              name="stockUnitId"
              label={t("items.stockUnit")}
              description={editing ? t("items.stockUnitFixed") : undefined}
              required
              disabled={Boolean(editing)}
            />
            <UnitSelectField control={form.control} name="purchaseUnitId" label={t("items.purchaseUnit")} emptyLabel={t("items.sameAsStockUnit")} />
            <MoneyField control={form.control} name="standardCost" label={t("items.standardCost")} required />
          </div>
          <FieldSet>
            <FieldLegend variant="label">{t("items.lotSection")}</FieldLegend>
            <SwitchField control={form.control} name="isLotTracked" label={t("items.isLotTracked")} />
            <IntegerField control={form.control} name="shelfLifeDays" label={t("items.shelfLifeDays")} suffix={tc("units.days")} />
          </FieldSet>
          <FieldSet>
            <FieldLegend variant="label">{t("items.planningSection")}</FieldLegend>
            <div className="grid gap-5 sm:grid-cols-2">
              <IntegerField control={form.control} name="leadTimeDays" label={t("items.leadTimeDays")} suffix={tc("units.days")} required />
              <QuantityField control={form.control} name="safetyStock" label={t("items.safetyStock")} required />
              <QuantityField control={form.control} name="minStock" label={t("items.minStock")} required />
              <QuantityField control={form.control} name="maxStock" label={t("items.maxStock")} description={t("items.zeroMeansNone")} required />
              <QuantityField control={form.control} name="minOrderQty" label={t("items.minOrderQty")} required />
              <QuantityField control={form.control} name="orderMultiple" label={t("items.orderMultiple")} description={t("items.zeroMeansNone")} required />
            </div>
          </FieldSet>
          <SwitchField control={form.control} name="isActive" label={tc("fields.isActive")} />
        </>
      )}
    />
  );
}
