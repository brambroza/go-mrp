"use client";

import type { Schemas } from "@mrp/api-client";
import { TagsIcon } from "lucide-react";
import { useTranslations } from "next-intl";
import { useMemo, useState } from "react";

import { CrudList } from "@/components/crud/crud-list";
import { createDataTableColumns } from "@/components/data-table/data-table";
import { PercentField, SwitchField, TextField } from "@/components/form/fields";
import { DetailSheet } from "@/components/layout/detail-sheet";
import { Button } from "@/components/ui/button";
import { Permissions } from "@/lib/auth/permissions";
import { queryKeys } from "@/lib/api/query-keys";
import { formatPercent } from "@/lib/format/number";
import { emptyToNull, optionalNumber, z } from "@/lib/validation/zod";

import { PartnerFields, partnerDefaults, partnerShape } from "./partner-fields";
import { codedColumns, useCodedLabel } from "./shared";
import { SupplierPriceList, currencyCode } from "./supplier-prices";

/** Schema of a supplier; mirrors `SaveSupplierRequest`. */
export const supplierSchema = z.object({
  ...partnerShape,
  currency: currencyCode(),
  vatPercent: optionalNumber(0, 100),
});

/** Form values of a supplier. */
export type SupplierValues = z.infer<typeof supplierSchema>;

type Row = Schemas["SupplierDto"];

/** Form values → `SaveSupplierRequest`. */
export function supplierToBody(values: SupplierValues): Schemas["SaveSupplierRequest"] {
  return {
    ...values,
    currency: values.currency.toUpperCase(),
    nameEn: emptyToNull(values.nameEn),
    taxId: emptyToNull(values.taxId),
    branchNo: emptyToNull(values.branchNo),
    address: emptyToNull(values.address),
    phone: emptyToNull(values.phone),
    email: emptyToNull(values.email),
    contactName: emptyToNull(values.contactName),
  };
}

/** List and form of suppliers, with the price tiers of each supplier in a side sheet. */
export function SupplierList() {
  const t = useTranslations("masters");
  const tc = useTranslations("common");
  const label = useCodedLabel();
  const [pricesOf, setPricesOf] = useState<Row | null>(null);

  const columns = useMemo(() => {
    const helper = createDataTableColumns<Row>();
    return [
      ...codedColumns<Row>({ code: tc("fields.code"), name: tc("fields.name"), nameEn: tc("fields.nameEn") }),
      helper.accessor((row) => row.phone ?? "", { id: "phone", header: t("partners.phone"), meta: { hideOnMobile: true, className: "whitespace-nowrap" } }),
      helper.accessor((row) => row.currency, { id: "currency", header: t("suppliers.currency"), meta: { hideOnMobile: true } }),
      helper.accessor((row) => row.vatPercent, {
        id: "vatPercent",
        header: t("suppliers.vatPercent"),
        meta: { align: "right", hideOnMobile: true, className: "tabular-nums" },
        cell: ({ row }) => formatPercent(row.original.vatPercent, { empty: t("suppliers.vatDefault") }),
      }),
    ];
  }, [t, tc]);

  return (
    <>
      <CrudList<Row, SupplierValues, Schemas["SaveSupplierRequest"]>
        queryKey={queryKeys.suppliers}
        managePermission={Permissions.mastersManage}
        list={(client, query) => client.GET("/api/v1/masters/suppliers", { params: { query } })}
        create={(client, body) => client.POST("/api/v1/masters/suppliers", { body })}
        update={(client, id, body) => client.PUT("/api/v1/masters/suppliers/{id}", { params: { path: { id } }, body })}
        columns={columns}
        schema={supplierSchema}
        defaultValues={{ ...partnerDefaults, currency: "THB", vatPercent: null }}
        toValues={(row) => ({
          code: row.code,
          name: row.name,
          nameEn: row.nameEn ?? "",
          taxId: row.taxId ?? "",
          branchNo: row.branchNo ?? "",
          address: row.address ?? "",
          phone: row.phone ?? "",
          email: row.email ?? "",
          contactName: row.contactName ?? "",
          creditDays: row.creditDays,
          currency: row.currency,
          vatPercent: row.vatPercent ?? null,
          isActive: row.isActive,
        })}
        toBody={supplierToBody}
        hasActiveFlag
        drawerSize="lg"
        labels={{
          caption: t("suppliers.title"),
          add: t("suppliers.add"),
          edit: t("suppliers.edit"),
          view: t("suppliers.view"),
          saved: t("suppliers.saved"),
        }}
        rowActions={(row) => (
          <Button variant="ghost" size="sm" onClick={() => setPricesOf(row)} data-testid="supplier-prices">
            <TagsIcon data-icon="inline-start" />
            <span className="hidden lg:inline">{t("prices.title")}</span>
            <span className="sr-only lg:hidden">{t("prices.title")}</span>
          </Button>
        )}
        fields={({ form }) => (
          <>
            <PartnerFields control={form.control} />
            <div className="grid gap-5 sm:grid-cols-2">
              <TextField
                control={form.control}
                name="currency"
                label={t("suppliers.currency")}
                description={t("suppliers.currencyHint")}
                required
                maxLength={3}
                transform={(value) => value.toUpperCase()}
              />
              <PercentField control={form.control} name="vatPercent" label={t("suppliers.vatPercent")} description={t("suppliers.vatHint")} />
            </div>
            <SwitchField control={form.control} name="isActive" label={tc("fields.isActive")} />
          </>
        )}
      />
      <DetailSheet
        open={pricesOf !== null}
        onOpenChange={(open) => (open ? undefined : setPricesOf(null))}
        title={t("prices.title")}
        description={pricesOf ? label(pricesOf) : undefined}
        size="xl"
      >
        {pricesOf ? <SupplierPriceList supplier={pricesOf} /> : null}
      </DetailSheet>
    </>
  );
}
