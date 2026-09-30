"use client";

import { zodResolver } from "@hookform/resolvers/zod";
import type { Schemas } from "@mrp/api-client";
import { LockIcon, PencilIcon, PlusIcon } from "lucide-react";
import { useTranslations } from "next-intl";
import { useEffect, useMemo, useState } from "react";
import { useController, useForm, type Control } from "react-hook-form";

import { createDataTableColumns, DataTable } from "@/components/data-table/data-table";
import { TextField } from "@/components/form/fields";
import { FormDrawer } from "@/components/form/form-drawer";
import { ErrorState } from "@/components/feedback/states";
import { Alert, AlertDescription } from "@/components/ui/alert";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Checkbox } from "@/components/ui/checkbox";
import { FieldLegend, FieldSet } from "@/components/ui/field";
import { Skeleton } from "@/components/ui/skeleton";
import { queryKeys } from "@/lib/api/query-keys";
import { ALL_PERMISSIONS, Permissions } from "@/lib/auth/permissions";
import { useCan } from "@/lib/auth/use-can";
import { useApiMutation, useApiQuery } from "@/lib/hooks/use-api";
import { requiredText, z } from "@/lib/validation/zod";

import { useRoles } from "./roles-data";

/** Schema of a role; mirrors `SaveRoleRequest`. */
export const roleSchema = z.object({
  name: requiredText(100, 2),
  permissions: z.array(z.string()).max(200),
});

/** Form values of a role. */
export type RoleValues = z.infer<typeof roleSchema>;

type Role = Schemas["RoleDto"];

/** Returns functions that give the labels of a module and of a permission code. */
export function usePermissionLabels() {
  const t = useTranslations("settings");
  return {
    module: (module: string) => (t.has(`modules.${module}` as never) ? t(`modules.${module}` as never) : module),
    permission: (code: string) => (t.has(`permissions.${code}` as never) ? t(`permissions.${code}` as never) : code),
  };
}

/** Props of {@link PermissionMatrix}. */
export interface PermissionMatrixProps {
  /** Form control; the field `permissions` holds the selected codes. */
  control: Control<RoleValues, unknown, RoleValues>;
  /** Permission codes grouped by module, from `GET /permissions`. */
  catalogue: Record<string, string[]>;
  /** Shows the matrix without allowing changes. */
  disabled?: boolean;
}

/** Permission checkboxes grouped by module, with "select all" per module. */
export function PermissionMatrix({ control, catalogue, disabled }: PermissionMatrixProps) {
  const t = useTranslations("settings");
  const labels = usePermissionLabels();
  const { field } = useController({ control, name: "permissions" });
  const selected = new Set(field.value);
  const everything = selected.has(ALL_PERMISSIONS);

  const change = (codes: string[], checked: boolean) => {
    const next = new Set(selected);
    for (const code of codes) {
      if (checked) {
        next.add(code);
      } else {
        next.delete(code);
      }
    }
    field.onChange([...next].sort());
  };

  return (
    <div className="flex flex-col gap-4" data-testid="permission-matrix">
      {Object.entries(catalogue).map(([module, codes]) => {
        const count = codes.filter((code) => everything || selected.has(code)).length;
        const allChecked = count === codes.length;
        return (
          <FieldSet key={module} className="gap-2 rounded-lg border p-3" data-module={module}>
            <FieldLegend variant="label" className="mb-0 px-1">
              {labels.module(module)}
            </FieldLegend>
            <label className="flex items-center gap-2 text-sm text-muted-foreground">
              <Checkbox
                checked={allChecked ? true : count > 0 ? "indeterminate" : false}
                disabled={disabled}
                onCheckedChange={(checked) => change(codes, checked === true)}
              />
              {t("roles.selectAll", { selected: count, total: codes.length })}
            </label>
            <div className="grid gap-2 sm:grid-cols-2">
              {codes.map((code) => (
                <label key={code} className="flex items-start gap-2 text-sm">
                  <Checkbox
                    className="mt-0.5"
                    checked={everything || selected.has(code)}
                    disabled={disabled}
                    data-permission={code}
                    onCheckedChange={(checked) => change([code], checked === true)}
                  />
                  <span className="flex min-w-0 flex-col">
                    <span>{labels.permission(code)}</span>
                    <code className="text-xs break-all text-muted-foreground">{code}</code>
                  </span>
                </label>
              ))}
            </div>
          </FieldSet>
        );
      })}
    </div>
  );
}

/** List of roles with the permission matrix in a drawer. */
export function RoleList() {
  const t = useTranslations("settings");
  const tc = useTranslations("common");
  const canManage = useCan(Permissions.rolesManage);
  const roles = useRoles();
  const catalogue = useApiQuery({
    queryKey: queryKeys.permissions,
    queryFn: (api) => api.GET("/api/v1/permissions"),
    staleTime: 10 * 60_000,
  });
  const [drawer, setDrawer] = useState<{ open: boolean; editing: Role | null }>({ open: false, editing: null });

  const form = useForm<RoleValues, unknown, RoleValues>({
    resolver: zodResolver(roleSchema),
    defaultValues: { name: "", permissions: [] },
  });
  useEffect(() => {
    if (drawer.open) {
      form.reset(drawer.editing ? { name: drawer.editing.name, permissions: drawer.editing.permissions } : { name: "", permissions: [] });
    }
  }, [drawer.open, drawer.editing, form]);

  const save = useApiMutation({
    mutationFn: (api, values: RoleValues) =>
      drawer.editing
        ? api.PUT("/api/v1/roles/{id}", { params: { path: { id: drawer.editing.id } }, body: values })
        : api.POST("/api/v1/roles", { body: values }),
    form,
    invalidate: [queryKeys.roles],
    successMessage: t("roles.saved"),
    onSuccess: () => setDrawer({ open: false, editing: null }),
  });

  const columns = useMemo(() => {
    const helper = createDataTableColumns<Role>();
    return [
      helper.display({
        id: "name",
        header: t("roles.name"),
        cell: ({ row }) => (
          <span className="flex items-center gap-2 font-medium">
            {row.original.name}
            {row.original.isSystem ? (
              <Badge variant="outline">
                <LockIcon />
                {t("roles.system")}
              </Badge>
            ) : null}
          </span>
        ),
      }),
      helper.display({
        id: "permissions",
        header: t("roles.permissionCount"),
        meta: { align: "right", className: "tabular-nums" },
        cell: ({ row }) => (row.original.permissions.includes(ALL_PERMISSIONS) ? t("roles.allPermissions") : row.original.permissions.length),
      }),
      helper.display({
        id: "actions",
        header: () => <span className="sr-only">{tc("table.actions")}</span>,
        meta: { align: "right", className: "w-px" },
        cell: ({ row }) => (
          <Button
            variant="ghost"
            size="icon-sm"
            aria-label={canManage && !row.original.isSystem ? tc("actions.edit") : tc("actions.view")}
            onClick={(event) => {
              event.stopPropagation();
              setDrawer({ open: true, editing: row.original });
            }}
          >
            <PencilIcon />
          </Button>
        ),
      }),
    ];
  }, [t, tc, canManage]);

  const readOnly = !canManage || Boolean(drawer.editing?.isSystem);

  return (
    <>
      <DataTable
        caption={t("roles.title")}
        columns={columns}
        data={roles.query.data}
        loading={roles.query.isLoading}
        fetching={roles.query.isFetching}
        error={roles.query.error}
        onRetry={() => void roles.query.refetch()}
        onRowClick={(row) => setDrawer({ open: true, editing: row })}
        actions={
          canManage ? (
            <Button type="button" onClick={() => setDrawer({ open: true, editing: null })} data-testid="crud-add">
              <PlusIcon data-icon="inline-start" />
              {t("roles.add")}
            </Button>
          ) : null
        }
      />
      <FormDrawer
        open={drawer.open}
        onOpenChange={(open) => setDrawer((current) => ({ ...current, open }))}
        title={drawer.editing ? (readOnly ? t("roles.view") : t("roles.edit")) : t("roles.add")}
        form={form}
        onSubmit={(values) => save.mutateAsync(values).then(() => undefined, () => undefined)}
        submitting={save.isPending}
        readOnly={readOnly}
        size="lg"
      >
        {drawer.editing?.isSystem ? (
          <Alert>
            <LockIcon />
            <AlertDescription>{t("roles.systemHint")}</AlertDescription>
          </Alert>
        ) : null}
        <TextField control={form.control} name="name" label={t("roles.name")} required maxLength={100} autoFocus />
        {catalogue.isLoading ? (
          <Skeleton className="h-40 w-full" />
        ) : catalogue.error || !catalogue.data ? (
          <ErrorState error={catalogue.error} onRetry={() => void catalogue.refetch()} />
        ) : (
          <PermissionMatrix control={form.control} catalogue={catalogue.data} disabled={readOnly} />
        )}
      </FormDrawer>
    </>
  );
}
