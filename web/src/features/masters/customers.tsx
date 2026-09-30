"use client";

import type { Schemas } from "@mrp/api-client";
import { useTranslations } from "next-intl";
import { useMemo } from "react";

import { CrudList } from "@/components/crud/crud-list";
import { createDataTableColumns } from "@/components/data-table/data-table";
import { MoneyField, SwitchField } from "@/components/form/fields";
import { Permissions } from "@/lib/auth/permissions";
import { queryKeys } from "@/lib/api/query-keys";
import { formatMoney } from "@/lib/format/number";
import { emptyToNull, requiredNumber, z } from "@/lib/validation/zod";

import { PartnerFields, partnerDefaults, partnerShape } from "./partner-fields";
import { codedColumns } from "./shared";

/** Schema of a customer; mirrors `SaveCustomerRequest`. */
export const customerSchema = z.object({
  ...partnerShape,
  creditLimit: requiredNumber(0, 99_999_999_999_999),
  checkCredit: z.boolean(),
});

/** Form values of a customer. */
export type CustomerValues = z.infer<typeof customerSchema>;

type Row = Schemas["CustomerDto"];

/** Form values → `SaveCustomerRequest`. */
export function customerToBody(values: CustomerValues): Schemas["SaveCustomerRequest"] {
  return {
    ...values,
    nameEn: emptyToNull(values.nameEn),
    taxId: emptyToNull(values.taxId),
    branchNo: emptyToNull(values.branchNo),
    address: emptyToNull(values.address),
    phone: emptyToNull(values.phone),
    email: emptyToNull(values.email),
    contactName: emptyToNull(values.contactName),
  };
}

/** List and form of customers. */
export function CustomerList() {
  const t = useTranslations("masters");
  const tc = useTranslations("common");
  const columns = useMemo(() => {
    const helper = createDataTableColumns<Row>();
    return [
      ...codedColumns<Row>({ code: tc("fields.code"), name: tc("fields.name"), nameEn: tc("fields.nameEn") }),
      helper.accessor((row) => row.phone ?? "", { id: "phone", header: t("partners.phone"), meta: { hideOnMobile: true, className: "whitespace-nowrap" } }),
      helper.accessor((row) => row.creditDays, {
        id: "creditDays",
        header: t("partners.creditDays"),
        meta: { align: "right", hideOnMobile: true, className: "tabular-nums" },
      }),
      helper.accessor((row) => row.creditLimit, {
        id: "creditLimit",
        header: t("customers.creditLimit"),
        meta: { align: "right", hideOnMobile: true, className: "tabular-nums" },
        cell: ({ row }) => formatMoney(row.original.creditLimit),
      }),
    ];
  }, [t, tc]);

  return (
    <CrudList<Row, CustomerValues, Schemas["SaveCustomerRequest"]>
      queryKey={queryKeys.customers}
      managePermission={Permissions.mastersManage}
      list={(client, query) => client.GET("/api/v1/masters/customers", { params: { query } })}
      create={(client, body) => client.POST("/api/v1/masters/customers", { body })}
      update={(client, id, body) => client.PUT("/api/v1/masters/customers/{id}", { params: { path: { id } }, body })}
      columns={columns}
      schema={customerSchema}
      defaultValues={{ ...partnerDefaults, creditLimit: 0, checkCredit: false }}
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
        creditLimit: row.creditLimit,
        checkCredit: row.checkCredit,
        isActive: row.isActive,
      })}
      toBody={customerToBody}
      hasActiveFlag
      drawerSize="lg"
      labels={{
        caption: t("customers.title"),
        add: t("customers.add"),
        edit: t("customers.edit"),
        view: t("customers.view"),
        saved: t("customers.saved"),
      }}
      fields={({ form }) => (
        <>
          <PartnerFields control={form.control} />
          <MoneyField control={form.control} name="creditLimit" label={t("customers.creditLimit")} required />
          <SwitchField control={form.control} name="checkCredit" label={t("customers.checkCredit")} description={t("customers.checkCreditHint")} />
          <SwitchField control={form.control} name="isActive" label={tc("fields.isActive")} />
        </>
      )}
    />
  );
}
