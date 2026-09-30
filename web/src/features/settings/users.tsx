"use client";

import type { Schemas } from "@mrp/api-client";
import { ShieldCheckIcon } from "lucide-react";
import { useTranslations } from "next-intl";
import { useMemo } from "react";

import { CrudList } from "@/components/crud/crud-list";
import { createDataTableColumns } from "@/components/data-table/data-table";
import { CheckboxGroupField, PasswordField, SelectField, SwitchField, TextField } from "@/components/form/fields";
import { Badge } from "@/components/ui/badge";
import { locales } from "@/i18n/config";
import { Permissions } from "@/lib/auth/permissions";
import { queryKeys } from "@/lib/api/query-keys";
import { emptyToNull, msg, requiredEmail, requiredText, z } from "@/lib/validation/zod";

import { useRoles } from "./roles-data";

const PASSWORD_RULE = /^(?=.*[a-z])(?=.*\d).{8,100}$/;

/**
 * Schema of the user form; mirrors `CreateUserRequest` and `UpdateUserRequest`.
 * `id` is empty while creating: then user name and password are mandatory.
 */
export const userSchema = z
  .object({
    id: z.string(),
    userName: z.string().trim().max(100),
    displayName: requiredText(100),
    email: requiredEmail(200),
    language: z.enum(locales),
    roleIds: z.array(z.string()).min(1).max(20),
    isActive: z.boolean(),
    password: z.string().max(100),
  })
  .superRefine((values, context) => {
    const creating = values.id === "";
    if (creating && values.userName.length < 3) {
      context.addIssue({ code: "custom", path: ["userName"], message: msg("tooShort", { min: 3 }) });
    } else if (creating && !/^[a-zA-Z0-9._@-]+$/.test(values.userName)) {
      context.addIssue({ code: "custom", path: ["userName"], message: msg("userName") });
    }
    if (creating && values.password === "") {
      context.addIssue({ code: "custom", path: ["password"], message: msg("required") });
    } else if (values.password !== "" && !PASSWORD_RULE.test(values.password)) {
      context.addIssue({ code: "custom", path: ["password"], message: msg("password") });
    }
  });

/** Form values of a user. */
export type UserValues = z.infer<typeof userSchema>;

type Row = Schemas["UserDto"];

/** Form values → `CreateUserRequest`. */
export function userToCreateBody(values: UserValues): Schemas["CreateUserRequest"] {
  return {
    userName: values.userName,
    displayName: values.displayName,
    email: values.email,
    password: values.password,
    roleIds: values.roleIds,
    language: values.language,
  };
}

/** Form values → `UpdateUserRequest`; the password changes only when one was typed. */
export function userToUpdateBody(values: UserValues): Schemas["UpdateUserRequest"] {
  return {
    displayName: values.displayName,
    email: values.email,
    roleIds: values.roleIds,
    isActive: values.isActive,
    language: values.language,
    newPassword: emptyToNull(values.password),
  };
}

/** List and form of the users of the company. */
export function UserList() {
  const t = useTranslations("settings");
  const tc = useTranslations("common");
  const roles = useRoles();

  const columns = useMemo(() => {
    const helper = createDataTableColumns<Row>();
    return [
      helper.accessor((row) => row.userName, { id: "userName", header: t("users.userName"), meta: { className: "font-medium" } }),
      helper.accessor((row) => row.displayName, { id: "displayName", header: t("users.displayName") }),
      helper.accessor((row) => row.email ?? "", { id: "email", header: t("users.email"), meta: { hideOnMobile: true } }),
      helper.display({
        id: "roles",
        header: t("users.roles"),
        meta: { hideOnMobile: true },
        cell: ({ row }) => (
          <span className="flex flex-wrap gap-1">
            {row.original.roleIds.map((id) => (
              <Badge key={id} variant="secondary">
                {roles.byId.get(id)?.name ?? "…"}
              </Badge>
            ))}
          </span>
        ),
      }),
      helper.display({
        id: "twoFactor",
        header: t("users.twoFactor"),
        meta: { hideOnMobile: true },
        cell: ({ row }) =>
          row.original.twoFactorEnabled ? (
            <Badge variant="outline">
              <ShieldCheckIcon />
              {tc("on")}
            </Badge>
          ) : (
            <span className="text-muted-foreground">{tc("off")}</span>
          ),
      }),
    ];
  }, [t, tc, roles.byId]);

  return (
    <CrudList<Row, UserValues, UserValues>
      queryKey={queryKeys.users}
      managePermission={Permissions.usersManage}
      list={(client, { search, page, pageSize }) => client.GET("/api/v1/users", { params: { query: { search, page, pageSize } } })}
      create={(client, values) => client.POST("/api/v1/users", { body: userToCreateBody(values) })}
      update={(client, id, values) => client.PUT("/api/v1/users/{id}", { params: { path: { id } }, body: userToUpdateBody(values) })}
      columns={columns}
      schema={userSchema}
      defaultValues={{ id: "", userName: "", displayName: "", email: "", language: "th", roleIds: [], isActive: true, password: "" }}
      toValues={(row) => ({
        id: row.id,
        userName: row.userName,
        displayName: row.displayName,
        email: row.email ?? "",
        language: row.language === "en" ? "en" : "th",
        roleIds: row.roleIds,
        isActive: row.isActive,
        password: "",
      })}
      toBody={(values) => values}
      hasActiveFlag
      activeFilter={false}
      labels={{
        caption: t("users.title"),
        add: t("users.add"),
        edit: t("users.edit"),
        view: t("users.view"),
        saved: t("users.saved"),
        searchPlaceholder: t("users.searchPlaceholder"),
      }}
      fields={({ form, editing }) => (
        <>
          <TextField
            control={form.control}
            name="userName"
            label={t("users.userName")}
            description={editing ? t("users.userNameFixed") : t("users.userNameRule")}
            required
            maxLength={100}
            disabled={Boolean(editing)}
            autoComplete="off"
            autoFocus={!editing}
          />
          <TextField control={form.control} name="displayName" label={t("users.displayName")} required maxLength={100} />
          <TextField control={form.control} name="email" label={t("users.email")} required maxLength={200} type="email" inputMode="email" />
          <PasswordField
            control={form.control}
            name="password"
            label={editing ? t("users.newPassword") : t("users.password")}
            description={editing ? t("users.newPasswordHint") : t("users.passwordRule")}
            required={!editing}
            autoComplete="new-password"
          />
          <SelectField
            control={form.control}
            name="language"
            label={t("users.language")}
            required
            options={locales.map((code) => ({ value: code, label: tc(`language.${code}`) }))}
          />
          <CheckboxGroupField control={form.control} name="roleIds" label={t("users.roles")} required options={roles.options} />
          <SwitchField control={form.control} name="isActive" label={tc("fields.isActive")} description={t("users.inactiveHint")} />
        </>
      )}
    />
  );
}
