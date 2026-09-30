"use client";

import { zodResolver } from "@hookform/resolvers/zod";
import { Loader2Icon, ShieldCheckIcon } from "lucide-react";
import { useTranslations } from "next-intl";
import Link from "next/link";
import { useRouter, useSearchParams } from "next/navigation";
import { useState } from "react";
import { useForm } from "react-hook-form";

import { PasswordField, TextField } from "@/components/form/fields";
import { useAuth } from "@/components/providers/auth-provider";
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardFooter, CardHeader, CardTitle } from "@/components/ui/card";
import { FieldGroup } from "@/components/ui/field";
import { TWO_FACTOR_REQUIRED_CODE, applyProblemToForm, normalizeError } from "@/lib/api/problem";
import { safeNextPath } from "@/lib/auth/safe-redirect";
import { loginSchema, type LoginValues } from "@/lib/auth/schemas";
import { useProblemMessage } from "@/lib/hooks/use-problem-message";

/** Sign-in form: company code, user and password, then the authenticator code when 2FA is on. */
export function LoginForm() {
  const t = useTranslations("auth");
  const router = useRouter();
  const searchParams = useSearchParams();
  const { signIn } = useAuth();
  const messageOf = useProblemMessage();
  const [step, setStep] = useState<"credentials" | "twoFactor">("credentials");
  const [failure, setFailure] = useState<string | null>(null);

  const form = useForm<LoginValues>({
    resolver: zodResolver(loginSchema),
    defaultValues: { companyCode: "", userName: "", password: "", twoFactorCode: "" },
  });
  const submitting = form.formState.isSubmitting;

  const submit = async (values: LoginValues) => {
    setFailure(null);
    if (step === "twoFactor" && values.twoFactorCode.trim() === "") {
      form.setError("twoFactorCode", { type: "required", message: "@required" });
      return;
    }
    try {
      await signIn(step === "twoFactor" ? values : { ...values, twoFactorCode: "" });
      router.replace(safeNextPath(searchParams.get("next")));
    } catch (error) {
      const apiError = normalizeError(error);
      if (apiError.code === TWO_FACTOR_REQUIRED_CODE) {
        setStep("twoFactor");
        form.setValue("twoFactorCode", "");
        setTimeout(() => form.setFocus("twoFactorCode"), 0);
        return;
      }
      applyProblemToForm(form, apiError.problem);
      setFailure(messageOf(apiError));
    }
  };

  return (
    <Card className="w-full max-w-md">
      <CardHeader>
        <CardTitle className="text-xl">{step === "twoFactor" ? t("login.twoFactorTitle") : t("login.title")}</CardTitle>
        <CardDescription>{step === "twoFactor" ? t("login.twoFactorDescription") : t("login.description")}</CardDescription>
      </CardHeader>
      <form noValidate onSubmit={(event) => void form.handleSubmit(submit)(event)}>
        <CardContent>
          <FieldGroup>
            {failure ? (
              <Alert variant="destructive" data-testid="login-error">
                <AlertTitle>{t("login.failed")}</AlertTitle>
                <AlertDescription>{failure}</AlertDescription>
              </Alert>
            ) : null}
            {step === "credentials" ? (
              <>
                <TextField
                  control={form.control}
                  name="companyCode"
                  label={t("fields.companyCode")}
                  description={t("fields.companyCodeHint")}
                  required
                  maxLength={40}
                  autoComplete="organization"
                  autoFocus
                />
                <TextField control={form.control} name="userName" label={t("fields.userName")} description={t("fields.userNameHint")} required maxLength={200} autoComplete="username" />
                <PasswordField control={form.control} name="password" label={t("fields.password")} required autoComplete="current-password" />
              </>
            ) : (
              <>
                <Alert>
                  <ShieldCheckIcon />
                  <AlertDescription>{t("login.twoFactorHint")}</AlertDescription>
                </Alert>
                <TextField
                  control={form.control}
                  name="twoFactorCode"
                  label={t("fields.twoFactorCode")}
                  required
                  maxLength={10}
                  inputMode="numeric"
                  autoComplete="one-time-code"
                />
              </>
            )}
          </FieldGroup>
        </CardContent>
        <CardFooter className="mt-6 flex-col gap-3">
          <Button type="submit" className="w-full" size="lg" disabled={submitting}>
            {submitting ? <Loader2Icon data-icon="inline-start" className="animate-spin" /> : null}
            {step === "twoFactor" ? t("login.verify") : t("login.submit")}
          </Button>
          {step === "twoFactor" ? (
            <Button
              type="button"
              variant="ghost"
              className="w-full"
              disabled={submitting}
              onClick={() => {
                setStep("credentials");
                setFailure(null);
              }}
            >
              {t("login.back")}
            </Button>
          ) : (
            <p className="text-sm text-muted-foreground">
              {t("login.noAccount")}{" "}
              <Link href="/signup" className="font-medium text-foreground underline underline-offset-4">
                {t("login.signupLink")}
              </Link>
            </p>
          )}
        </CardFooter>
      </form>
    </Card>
  );
}
