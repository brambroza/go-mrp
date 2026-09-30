"use client";

import { zodResolver } from "@hookform/resolvers/zod";
import { Loader2Icon } from "lucide-react";
import { useTranslations } from "next-intl";
import { useEffect, useMemo } from "react";
import { useForm } from "react-hook-form";

import { PercentField, SelectField, SwitchField } from "@/components/form/fields";
import { ErrorState } from "@/components/feedback/states";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { FieldGroup } from "@/components/ui/field";
import { Skeleton } from "@/components/ui/skeleton";
import { queryKeys } from "@/lib/api/query-keys";
import { Permissions } from "@/lib/auth/permissions";
import { useCan } from "@/lib/auth/use-can";
import { useApiMutation, useApiQuery } from "@/lib/hooks/use-api";
import { requiredNumber, z } from "@/lib/validation/zod";

import { settingDefinitions, settingFieldName, toSettingValues, toSettingsBody, type SettingDefinition, type SettingValues } from "./setting-definitions";

/** Builds the zod schema of the settings form from the definitions. */
export function buildSettingsSchema() {
  const shape: Record<string, z.ZodType> = {};
  for (const definition of settingDefinitions) {
    const kind: SettingDefinition["kind"] = definition.kind;
    shape[settingFieldName(definition.key)] =
      kind.type === "boolean" ? z.boolean() : kind.type === "percent" ? requiredNumber(0, 100) : z.enum(kind.options as [string, ...string[]]);
  }
  return z.object(shape);
}

const groups = ["approval", "inventory", "purchasing", "production"] as const;

/** Tenant settings grouped by module. */
export function GeneralSettings() {
  const t = useTranslations("settings");
  const tc = useTranslations("common");
  const canManage = useCan(Permissions.settingsManage);
  const schema = useMemo(() => buildSettingsSchema(), []);

  const query = useApiQuery({
    queryKey: [...queryKeys.settings, "all"],
    queryFn: (api) => api.GET("/api/v1/settings"),
  });

  const form = useForm<SettingValues, unknown, SettingValues>({
    resolver: zodResolver(schema as z.ZodType<SettingValues, SettingValues>),
    defaultValues: toSettingValues([]),
  });
  useEffect(() => {
    if (query.data) {
      form.reset(toSettingValues(query.data));
    }
  }, [query.data, form]);

  const save = useApiMutation({
    mutationFn: (api, values: SettingValues) => api.PUT("/api/v1/settings", { body: toSettingsBody(values) }),
    form,
    invalidate: [queryKeys.settings],
    successMessage: t("general.saved"),
  });

  if (query.isLoading) {
    return <Skeleton className="h-64 w-full max-w-3xl" />;
  }
  if (query.error) {
    return <ErrorState error={query.error} onRetry={() => void query.refetch()} />;
  }

  return (
    <form
      noValidate
      className="flex max-w-3xl flex-col gap-4"
      onSubmit={(event) => void form.handleSubmit((values) => save.mutateAsync(values).then(() => undefined, () => undefined))(event)}
    >
      <fieldset disabled={!canManage || save.isPending} className="contents">
        {groups.map((group) => (
          <Card key={group} data-setting-group={group}>
            <CardHeader>
              <CardTitle>{t(`modules.${group === "approval" ? "platform" : group}`)}</CardTitle>
            </CardHeader>
            <CardContent>
              <FieldGroup>
                {settingDefinitions
                  .filter((definition) => definition.group === group)
                  .map((definition) => {
                    const name = settingFieldName(definition.key);
                    const label = t(`general.keys.${definition.key}.label`);
                    const description = t(`general.keys.${definition.key}.description`);
                    const kind: SettingDefinition["kind"] = definition.kind;
                    if (kind.type === "boolean") {
                      return <SwitchField key={name} control={form.control} name={name} label={label} description={description} />;
                    }
                    if (kind.type === "percent") {
                      return (
                        <PercentField key={name} control={form.control} name={name} label={label} description={description} required className="max-w-xs" />
                      );
                    }
                    return (
                      <SelectField
                        key={name}
                        control={form.control}
                        name={name}
                        label={label}
                        description={description}
                        required
                        className="max-w-xs"
                        options={kind.options.map((value) => ({ value, label: t(`general.options.${value}` as never) }))}
                      />
                    );
                  })}
              </FieldGroup>
            </CardContent>
          </Card>
        ))}
      </fieldset>
      {canManage ? (
        <div className="flex justify-end">
          <Button type="submit" disabled={save.isPending || !form.formState.isDirty} data-testid="settings-save">
            {save.isPending ? <Loader2Icon data-icon="inline-start" className="animate-spin" /> : null}
            {tc("actions.save")}
          </Button>
        </div>
      ) : null}
    </form>
  );
}
