"use client";

import type { Schemas } from "@mrp/api-client";
import type { QueryKey } from "@tanstack/react-query";
import { useTranslations } from "next-intl";
import { useMemo } from "react";

import { CrudList, type CrudLabels } from "@/components/crud/crud-list";
import { SwitchField, TextField } from "@/components/form/fields";
import { Permissions } from "@/lib/auth/permissions";
import { emptyToNull } from "@/lib/validation/zod";

import { codedColumns, codedSchema, toCode, type CodedValues } from "./shared";

/** Props of {@link CodedMasterList}. */
export interface CodedMasterListProps {
  /** Which resource: both share `SaveCodedRequest`. */
  resource: "units" | "item-groups";
  /** Root of the cache key. */
  queryKey: QueryKey;
  /** Texts. */
  labels: CrudLabels;
}

/** Form values → `SaveCodedRequest`. */
export function codedToBody(values: CodedValues): Schemas["SaveCodedRequest"] {
  return { code: values.code, name: values.name, nameEn: emptyToNull(values.nameEn), isActive: values.isActive };
}

/** List and form of a master that only has code, name and English name (units, item groups). */
export function CodedMasterList({ resource, queryKey, labels }: CodedMasterListProps) {
  const t = useTranslations("common");
  const path = `/api/v1/masters/${resource}` as const;
  const columns = useMemo(
    () => codedColumns<Schemas["CodedDto"]>({ code: t("fields.code"), name: t("fields.name"), nameEn: t("fields.nameEn") }),
    [t],
  );

  return (
    <CrudList<Schemas["CodedDto"], CodedValues, Schemas["SaveCodedRequest"]>
      queryKey={queryKey}
      managePermission={Permissions.mastersManage}
      list={(client, query) => client.GET(path, { params: { query } })}
      create={(client, body) => client.POST(path, { body })}
      update={(client, id, body) => client.PUT(`${path}/{id}`, { params: { path: { id } }, body })}
      columns={columns}
      schema={codedSchema}
      defaultValues={{ code: "", name: "", nameEn: "", isActive: true }}
      toValues={(row) => ({ code: row.code, name: row.name, nameEn: row.nameEn ?? "", isActive: row.isActive })}
      toBody={codedToBody}
      labels={labels}
      hasActiveFlag
      fields={({ form }) => (
        <>
          <TextField control={form.control} name="code" label={t("fields.code")} required maxLength={40} transform={toCode} autoFocus />
          <TextField control={form.control} name="name" label={t("fields.name")} required maxLength={200} />
          <TextField control={form.control} name="nameEn" label={t("fields.nameEn")} maxLength={200} />
          <SwitchField control={form.control} name="isActive" label={t("fields.isActive")} />
        </>
      )}
    />
  );
}
