"use client";

import type { Schemas } from "@mrp/api-client";
import { useLocale, useTranslations } from "next-intl";
import { useMemo } from "react";

import { CrudList } from "@/components/crud/crud-list";
import { createDataTableColumns } from "@/components/data-table/data-table";
import { DateField, MoneyField, QuantityField, TextField } from "@/components/form/fields";
import { useUser } from "@/components/providers/auth-provider";
import { toLocale } from "@/i18n/config";
import { Permissions } from "@/lib/auth/permissions";
import { queryKeys } from "@/lib/api/query-keys";
import { formatDate, todayIso } from "@/lib/format/date";
import { formatMoney, formatQuantity } from "@/lib/format/number";
import { emptyToNull, msg, optionalDate, requiredDate, requiredId, requiredNumber, z } from "@/lib/validation/zod";

import { ItemLabel, ItemPickerField, UnitSelectField, useUnits } from "./shared";

/** Three-letter currency code, stored in upper case. */
export function currencyCode() {
  return z
    .string()
    .trim()
    .regex(/^[A-Za-z]{3}$/, { error: msg("currency") });
}

/** Schema of a supplier price tier; mirrors `SaveSupplierPriceRequest`. */
export const supplierPriceSchema = z
  .object({
    itemId: requiredId(),
    unitId: requiredId(),
    minQty: requiredNumber(0, 999_999_999_999),
    unitPrice: requiredNumber(0, 99_999_999_999_999),
    currency: currencyCode(),
    validFrom: requiredDate(),
    validTo: optionalDate(),
  })
  .refine((values) => values.validTo === "" || values.validFrom === "" || values.validTo >= values.validFrom, {
    path: ["validTo"],
    error: msg("dateOrder"),
  });

/** Form values of a supplier price tier. */
export type SupplierPriceValues = z.infer<typeof supplierPriceSchema>;

type Row = Schemas["SupplierPriceDto"];

/** Props of {@link SupplierPriceList}. */
export interface SupplierPriceListProps {
  /** Supplier whose price tiers are shown. */
  supplier: Pick<Schemas["SupplierDto"], "id" | "currency">;
}

/** Price tiers of one supplier: price per item and unit from a minimum quantity, valid in a date range. */
export function SupplierPriceList({ supplier }: SupplierPriceListProps) {
  const t = useTranslations("masters");
  const locale = toLocale(useLocale());
  const user = useUser();
  const units = useUnits();

  const columns = useMemo(() => {
    const helper = createDataTableColumns<Row>();
    return [
      helper.display({ id: "item", header: t("prices.item"), cell: ({ row }) => <ItemLabel id={row.original.itemId} /> }),
      helper.accessor((row) => row.minQty, {
        id: "minQty",
        header: t("prices.minQty"),
        meta: { align: "right", className: "tabular-nums whitespace-nowrap" },
        cell: ({ row }) => formatQuantity(row.original.minQty, { unit: units.byId.get(row.original.unitId)?.code }),
      }),
      helper.accessor((row) => row.unitPrice, {
        id: "unitPrice",
        header: t("prices.unitPrice"),
        meta: { align: "right", className: "tabular-nums whitespace-nowrap" },
        cell: ({ row }) => formatMoney(row.original.unitPrice, { currency: row.original.currency }),
      }),
      helper.display({
        id: "validity",
        header: t("prices.validity"),
        meta: { hideOnMobile: true, className: "whitespace-nowrap" },
        cell: ({ row }) =>
          `${formatDate(row.original.validFrom, { locale })} – ${row.original.validTo ? formatDate(row.original.validTo, { locale }) : t("prices.noEnd")}`,
      }),
    ];
  }, [t, locale, units.byId]);

  return (
    <CrudList<Row, SupplierPriceValues, Schemas["SaveSupplierPriceRequest"]>
      queryKey={[...queryKeys.supplierPrices, supplier.id]}
      managePermission={Permissions.mastersManage}
      fixedSearch={supplier.id}
      list={(client, { search, page, pageSize }) => client.GET("/api/v1/masters/supplier-prices", { params: { query: { search, page, pageSize } } })}
      create={(client, body) => client.POST("/api/v1/masters/supplier-prices", { body })}
      update={(client, id, body) => client.PUT("/api/v1/masters/supplier-prices/{id}", { params: { path: { id } }, body })}
      columns={columns}
      schema={supplierPriceSchema}
      defaultValues={{
        itemId: "",
        unitId: "",
        minQty: 0,
        unitPrice: null as unknown as number,
        currency: supplier.currency,
        validFrom: todayIso(user.timeZone),
        validTo: "",
      }}
      toValues={(row) => ({
        itemId: row.itemId,
        unitId: row.unitId,
        minQty: row.minQty,
        unitPrice: row.unitPrice,
        currency: row.currency,
        validFrom: row.validFrom,
        validTo: row.validTo ?? "",
      })}
      toBody={(values) => ({
        supplierId: supplier.id,
        itemId: values.itemId,
        unitId: values.unitId,
        minQty: values.minQty,
        unitPrice: values.unitPrice,
        currency: values.currency.toUpperCase(),
        validFrom: values.validFrom,
        validTo: emptyToNull(values.validTo),
      })}
      labels={{
        caption: t("prices.title"),
        add: t("prices.add"),
        edit: t("prices.edit"),
        view: t("prices.view"),
        saved: t("prices.saved"),
      }}
      fields={({ form, editing }) => (
        <>
          <ItemPickerField
            control={form.control}
            name="itemId"
            label={t("prices.item")}
            description={editing ? t("prices.keyFixed") : undefined}
            required
            clearable={false}
            disabled={Boolean(editing)}
            onSelected={(item) => {
              if (item && !form.getValues("unitId")) {
                form.setValue("unitId", item.purchaseUnitId ?? item.stockUnitId, { shouldValidate: true });
              }
            }}
          />
          <UnitSelectField control={form.control} name="unitId" label={t("prices.unit")} required />
          <div className="grid gap-5 sm:grid-cols-2">
            <QuantityField control={form.control} name="minQty" label={t("prices.minQty")} description={t("prices.minQtyHint")} required />
            <MoneyField control={form.control} name="unitPrice" label={t("prices.unitPrice")} prefix="" required />
            <TextField
              control={form.control}
              name="currency"
              label={t("suppliers.currency")}
              required
              maxLength={3}
              transform={(value) => value.toUpperCase()}
            />
          </div>
          <div className="grid gap-5 sm:grid-cols-2">
            <DateField control={form.control} name="validFrom" label={t("prices.validFrom")} required clearable={false} />
            <DateField control={form.control} name="validTo" label={t("prices.validTo")} description={t("prices.validToHint")} />
          </div>
        </>
      )}
    />
  );
}
