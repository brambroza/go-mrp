"use client";

import type { Schemas } from "@mrp/api-client";
import { MapPinIcon } from "lucide-react";
import { useTranslations } from "next-intl";
import { useMemo, useState } from "react";

import { CrudList } from "@/components/crud/crud-list";
import { createDataTableColumns } from "@/components/data-table/data-table";
import { SelectField, SwitchField, TextField } from "@/components/form/fields";
import { DetailSheet } from "@/components/layout/detail-sheet";
import { Button } from "@/components/ui/button";
import { Permissions } from "@/lib/auth/permissions";
import { queryKeys } from "@/lib/api/query-keys";
import { emptyToNull, optionalText, requiredText, z } from "@/lib/validation/zod";

import { codeText, codedColumns, toCode, useCodedLabel } from "./shared";

/** Warehouse types of the API. */
export const warehouseTypes = ["General", "Quarantine", "Production", "Scrap"] as const;

/** Schema of a warehouse; mirrors `SaveWarehouseRequest`. */
export const warehouseSchema = z.object({
  code: codeText(),
  name: requiredText(200),
  nameEn: optionalText(200),
  warehouseType: z.enum(warehouseTypes),
  isActive: z.boolean(),
});

/** Form values of a warehouse. */
export type WarehouseValues = z.infer<typeof warehouseSchema>;

/** Schema of a location; mirrors `SaveLocationRequest` (the warehouse comes from the parent). */
export const locationSchema = z.object({
  code: codeText(),
  name: requiredText(200),
  nameEn: optionalText(200),
  isActive: z.boolean(),
});

/** Form values of a location. */
export type LocationValues = z.infer<typeof locationSchema>;

type Warehouse = Schemas["WarehouseDto"];
type Location = Schemas["LocationDto"];

/** Locations (shelves, zones) of one warehouse. */
export function LocationList({ warehouseId }: { warehouseId: string }) {
  const t = useTranslations("masters");
  const tc = useTranslations("common");
  const columns = useMemo(
    () => codedColumns<Location>({ code: tc("fields.code"), name: tc("fields.name"), nameEn: tc("fields.nameEn") }),
    [tc],
  );

  return (
    <CrudList<Location, LocationValues, Schemas["SaveLocationRequest"]>
      queryKey={[...queryKeys.locations, warehouseId]}
      managePermission={Permissions.mastersManage}
      fixedSearch={warehouseId}
      list={(client, query) => client.GET("/api/v1/masters/locations", { params: { query } })}
      create={(client, body) => client.POST("/api/v1/masters/locations", { body })}
      update={(client, id, body) => client.PUT("/api/v1/masters/locations/{id}", { params: { path: { id } }, body })}
      columns={columns}
      schema={locationSchema}
      defaultValues={{ code: "", name: "", nameEn: "", isActive: true }}
      toValues={(row) => ({ code: row.code, name: row.name, nameEn: row.nameEn ?? "", isActive: row.isActive })}
      toBody={(values) => ({
        warehouseId,
        code: values.code,
        name: values.name,
        nameEn: emptyToNull(values.nameEn),
        isActive: values.isActive,
      })}
      hasActiveFlag
      labels={{
        caption: t("locations.title"),
        add: t("locations.add"),
        edit: t("locations.edit"),
        view: t("locations.view"),
        saved: t("locations.saved"),
      }}
      fields={({ form }) => (
        <>
          <TextField control={form.control} name="code" label={tc("fields.code")} required maxLength={40} transform={toCode} autoFocus />
          <TextField control={form.control} name="name" label={tc("fields.name")} required maxLength={200} />
          <TextField control={form.control} name="nameEn" label={tc("fields.nameEn")} maxLength={200} />
          <SwitchField control={form.control} name="isActive" label={tc("fields.isActive")} />
        </>
      )}
    />
  );
}

/** List and form of warehouses, with the locations of each warehouse in a side sheet. */
export function WarehouseList() {
  const t = useTranslations("masters");
  const tc = useTranslations("common");
  const label = useCodedLabel();
  const [locationsOf, setLocationsOf] = useState<Warehouse | null>(null);

  const columns = useMemo(() => {
    const helper = createDataTableColumns<Warehouse>();
    return [
      ...codedColumns<Warehouse>({ code: tc("fields.code"), name: tc("fields.name"), nameEn: tc("fields.nameEn") }),
      helper.accessor((row) => row.warehouseType, {
        id: "warehouseType",
        header: t("warehouses.type"),
        meta: { hideOnMobile: true },
        cell: ({ row }) => t(`warehouseTypes.${row.original.warehouseType}`),
      }),
    ];
  }, [t, tc]);

  return (
    <>
      <CrudList<Warehouse, WarehouseValues, Schemas["SaveWarehouseRequest"]>
        queryKey={queryKeys.warehouses}
        managePermission={Permissions.mastersManage}
        list={(client, query) => client.GET("/api/v1/masters/warehouses", { params: { query } })}
        create={(client, body) => client.POST("/api/v1/masters/warehouses", { body })}
        update={(client, id, body) => client.PUT("/api/v1/masters/warehouses/{id}", { params: { path: { id } }, body })}
        columns={columns}
        schema={warehouseSchema}
        defaultValues={{ code: "", name: "", nameEn: "", warehouseType: "General", isActive: true }}
        toValues={(row) => ({
          code: row.code,
          name: row.name,
          nameEn: row.nameEn ?? "",
          warehouseType: row.warehouseType,
          isActive: row.isActive,
        })}
        toBody={(values) => ({ ...values, nameEn: emptyToNull(values.nameEn) })}
        hasActiveFlag
        labels={{
          caption: t("warehouses.title"),
          add: t("warehouses.add"),
          edit: t("warehouses.edit"),
          view: t("warehouses.view"),
          saved: t("warehouses.saved"),
        }}
        rowActions={(row) => (
          <Button variant="ghost" size="sm" onClick={() => setLocationsOf(row)} data-testid="warehouse-locations">
            <MapPinIcon data-icon="inline-start" />
            <span className="hidden lg:inline">{t("locations.title")}</span>
            <span className="sr-only lg:hidden">{t("locations.title")}</span>
          </Button>
        )}
        fields={({ form }) => (
          <>
            <TextField control={form.control} name="code" label={tc("fields.code")} required maxLength={40} transform={toCode} autoFocus />
            <TextField control={form.control} name="name" label={tc("fields.name")} required maxLength={200} />
            <TextField control={form.control} name="nameEn" label={tc("fields.nameEn")} maxLength={200} />
            <SelectField
              control={form.control}
              name="warehouseType"
              label={t("warehouses.type")}
              description={t("warehouses.typeHint")}
              required
              options={warehouseTypes.map((value) => ({ value, label: t(`warehouseTypes.${value}`) }))}
            />
            <SwitchField control={form.control} name="isActive" label={tc("fields.isActive")} />
          </>
        )}
      />
      <DetailSheet
        open={locationsOf !== null}
        onOpenChange={(open) => (open ? undefined : setLocationsOf(null))}
        title={t("locations.title")}
        description={locationsOf ? label(locationsOf) : undefined}
      >
        {locationsOf ? <LocationList warehouseId={locationsOf.id} /> : null}
      </DetailSheet>
    </>
  );
}
