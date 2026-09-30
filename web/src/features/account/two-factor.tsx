"use client";

import { zodResolver } from "@hookform/resolvers/zod";
import type { Schemas } from "@mrp/api-client";
import { CopyIcon, Loader2Icon, ShieldCheckIcon, ShieldOffIcon } from "lucide-react";
import { useTranslations } from "next-intl";
import { QRCodeSVG } from "qrcode.react";
import { useState } from "react";
import { useForm } from "react-hook-form";
import { toast } from "sonner";

import { TextField } from "@/components/form/fields";
import { useAuth, useUser } from "@/components/providers/auth-provider";
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardFooter, CardHeader, CardTitle } from "@/components/ui/card";
import { FieldGroup } from "@/components/ui/field";
import { twoFactorCodeSchema, type TwoFactorCodeValues } from "@/lib/auth/schemas";
import { useApiMutation } from "@/lib/hooks/use-api";

/** Splits an authenticator key into groups of four characters for reading it aloud or typing it. */
export function groupKey(key: string): string {
  return (key.replace(/\s+/g, "").match(/.{1,4}/g) ?? []).join(" ");
}

/** Form with one field for the 6-digit code of the authenticator app. */
function CodeForm({
  submitLabel,
  pending,
  destructive,
  onSubmit,
}: {
  submitLabel: string;
  pending: boolean;
  destructive?: boolean;
  onSubmit: (values: TwoFactorCodeValues, form: ReturnType<typeof useForm<TwoFactorCodeValues>>) => void;
}) {
  const t = useTranslations("account");
  const form = useForm<TwoFactorCodeValues>({ resolver: zodResolver(twoFactorCodeSchema), defaultValues: { code: "" } });
  return (
    <form noValidate className="flex flex-col gap-4" onSubmit={(event) => void form.handleSubmit((values) => onSubmit(values, form))(event)}>
      <FieldGroup>
        <TextField
          control={form.control}
          name="code"
          label={t("twoFactor.code")}
          description={t("twoFactor.codeHint")}
          required
          maxLength={6}
          inputMode="numeric"
          autoComplete="one-time-code"
          className="max-w-xs"
          transform={(value) => value.replace(/\D/g, "")}
        />
      </FieldGroup>
      <div>
        <Button type="submit" variant={destructive ? "destructive" : "default"} disabled={pending}>
          {pending ? <Loader2Icon data-icon="inline-start" className="animate-spin" /> : null}
          {submitLabel}
        </Button>
      </div>
    </form>
  );
}

/** Two-factor setup: shows the QR code and the key, then switches 2FA on with a code; or switches it off. */
export function TwoFactorSetup() {
  const t = useTranslations("account");
  const user = useUser();
  const { reloadProfile } = useAuth();
  const [secret, setSecret] = useState<Schemas["TwoFactorSetupResponse"] | null>(null);
  const [justEnabled, setJustEnabled] = useState(false);

  const setup = useApiMutation({
    mutationFn: (api) => api.POST("/api/v1/auth/2fa/setup"),
    onSuccess: (data) => setSecret(data),
  });
  const enable = useApiMutation({
    mutationFn: (api, values: TwoFactorCodeValues) => api.POST("/api/v1/auth/2fa/enable", { body: values }),
    successMessage: t("twoFactor.enabled"),
    onSuccess: async () => {
      setSecret(null);
      setJustEnabled(true);
      await reloadProfile();
    },
  });
  const disable = useApiMutation({
    mutationFn: (api, values: TwoFactorCodeValues) => api.POST("/api/v1/auth/2fa/disable", { body: values }),
    successMessage: t("twoFactor.disabled"),
    onSuccess: async () => {
      setJustEnabled(false);
      await reloadProfile();
    },
  });

  const reportTo = (form: ReturnType<typeof useForm<TwoFactorCodeValues>>) => ({
    onError: () => form.setFocus("code"),
  });

  if (user.twoFactorEnabled) {
    return (
      <Card className="max-w-2xl">
        <CardHeader>
          <CardTitle className="flex items-center gap-2">
            <ShieldCheckIcon className="size-5 text-emerald-600" />
            {t("twoFactor.onTitle")}
          </CardTitle>
          <CardDescription>{t("twoFactor.onDescription")}</CardDescription>
        </CardHeader>
        <CardContent className="flex flex-col gap-4">
          {justEnabled ? (
            <Alert>
              <AlertTitle>{t("twoFactor.reloginTitle")}</AlertTitle>
              <AlertDescription>{t("twoFactor.reloginDescription")}</AlertDescription>
            </Alert>
          ) : null}
          <div>
            <h2 className="flex items-center gap-2 font-medium">
              <ShieldOffIcon className="size-4" />
              {t("twoFactor.disableTitle")}
            </h2>
            <p className="mt-1 mb-3 text-sm text-muted-foreground">{t("twoFactor.disableDescription")}</p>
            <CodeForm
              submitLabel={t("twoFactor.disable")}
              destructive
              pending={disable.isPending}
              onSubmit={(values, form) => disable.mutate(values, reportTo(form))}
            />
          </div>
        </CardContent>
      </Card>
    );
  }

  return (
    <Card className="max-w-2xl">
      <CardHeader>
        <CardTitle>{t("twoFactor.offTitle")}</CardTitle>
        <CardDescription>{t("twoFactor.offDescription")}</CardDescription>
      </CardHeader>
      {secret ? (
        <CardContent className="flex flex-col gap-5">
          <ol className="flex flex-col gap-5 text-sm">
            <li className="flex flex-col gap-2">
              <span className="font-medium">{t("twoFactor.step1")}</span>
              <div className="w-fit rounded-lg border bg-white p-3" data-testid="two-factor-qr">
                <QRCodeSVG value={secret.authenticatorUri} size={176} level="M" title={t("twoFactor.qrTitle")} />
              </div>
            </li>
            <li className="flex flex-col gap-2">
              <span className="font-medium">{t("twoFactor.step2")}</span>
              <div className="flex flex-wrap items-center gap-2">
                <code className="rounded bg-muted px-2 py-1 text-base tracking-wider break-all" data-testid="two-factor-key">
                  {groupKey(secret.sharedKey)}
                </code>
                <Button
                  type="button"
                  variant="outline"
                  size="sm"
                  onClick={() =>
                    void navigator.clipboard.writeText(secret.sharedKey).then(
                      () => toast.success(t("twoFactor.copied")),
                      () => toast.error(t("twoFactor.copyFailed")),
                    )
                  }
                >
                  <CopyIcon data-icon="inline-start" />
                  {t("twoFactor.copy")}
                </Button>
              </div>
            </li>
            <li className="flex flex-col gap-2">
              <span className="font-medium">{t("twoFactor.step3")}</span>
              <CodeForm
                submitLabel={t("twoFactor.enable")}
                pending={enable.isPending}
                onSubmit={(values, form) => enable.mutate(values, reportTo(form))}
              />
            </li>
          </ol>
        </CardContent>
      ) : (
        <CardFooter>
          <Button type="button" disabled={setup.isPending} onClick={() => setup.mutate()} data-testid="two-factor-start">
            {setup.isPending ? <Loader2Icon data-icon="inline-start" className="animate-spin" /> : <ShieldCheckIcon data-icon="inline-start" />}
            {t("twoFactor.start")}
          </Button>
        </CardFooter>
      )}
    </Card>
  );
}
