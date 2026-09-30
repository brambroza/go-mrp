"use client";

import { zodResolver } from "@hookform/resolvers/zod";
import { Loader2Icon } from "lucide-react";
import { useTranslations } from "next-intl";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { useState } from "react";
import { useController, useForm } from "react-hook-form";

import { PasswordField, TextField } from "@/components/form/fields";
import { useAuth } from "@/components/providers/auth-provider";
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardFooter, CardHeader, CardTitle } from "@/components/ui/card";
import { FieldGroup, FieldLegend, FieldSet } from "@/components/ui/field";
import { RadioGroup, RadioGroupItem } from "@/components/ui/radio-group";
import { applyProblemToForm, normalizeError } from "@/lib/api/problem";
import { signupSchema, tenantPlans, type SignupValues } from "@/lib/auth/schemas";
import { useProblemMessage } from "@/lib/hooks/use-problem-message";
import { cn } from "@/lib/utils";

/** Sign-up form: creates a company (tenant) with its owner and a package, then opens the dashboard. */
export function SignupForm() {
  const t = useTranslations("auth");
  const router = useRouter();
  const { signUp } = useAuth();
  const messageOf = useProblemMessage();
  const [failure, setFailure] = useState<string | null>(null);

  const form = useForm<SignupValues>({
    resolver: zodResolver(signupSchema),
    defaultValues: { companyCode: "", companyName: "", displayName: "", email: "", password: "", plan: "Starter" },
  });
  const plan = useController({ control: form.control, name: "plan" });
  const submitting = form.formState.isSubmitting;

  const submit = async (values: SignupValues) => {
    setFailure(null);
    try {
      await signUp(values);
      router.replace("/");
    } catch (error) {
      const apiError = normalizeError(error);
      const unmatched = applyProblemToForm(form, apiError.problem);
      if (apiError.code === "platform.tenant.slug_taken" || apiError.code === "platform.tenant.invalid_slug") {
        form.setError("companyCode", { type: "server", message: messageOf(apiError) }, { shouldFocus: true });
        return;
      }
      setFailure([messageOf(apiError), ...unmatched].join(" "));
    }
  };

  return (
    <Card className="w-full max-w-xl">
      <CardHeader>
        <CardTitle className="text-xl">{t("signup.title")}</CardTitle>
        <CardDescription>{t("signup.description")}</CardDescription>
      </CardHeader>
      <form noValidate onSubmit={(event) => void form.handleSubmit(submit)(event)}>
        <CardContent>
          <FieldGroup>
            {failure ? (
              <Alert variant="destructive" data-testid="signup-error">
                <AlertTitle>{t("signup.failed")}</AlertTitle>
                <AlertDescription>{failure}</AlertDescription>
              </Alert>
            ) : null}
            <div className="grid gap-5 sm:grid-cols-2">
              <TextField
                control={form.control}
                name="companyCode"
                label={t("fields.companyCode")}
                description={t("fields.companyCodeRule")}
                required
                maxLength={40}
                autoComplete="off"
                autoFocus
                transform={(value) => value.toLowerCase().replace(/\s+/g, "-")}
              />
              <TextField control={form.control} name="companyName" label={t("fields.companyName")} required maxLength={200} autoComplete="organization" />
              <TextField control={form.control} name="displayName" label={t("fields.displayName")} required maxLength={100} autoComplete="name" />
              <TextField control={form.control} name="email" label={t("fields.email")} description={t("fields.emailHint")} required maxLength={200} type="email" inputMode="email" autoComplete="email" />
            </div>
            <PasswordField
              control={form.control}
              name="password"
              label={t("fields.password")}
              description={t("fields.passwordRule")}
              required
              autoComplete="new-password"
            />
            <FieldSet>
              <FieldLegend variant="label">{t("fields.plan")}</FieldLegend>
              <RadioGroup
                value={plan.field.value}
                onValueChange={plan.field.onChange}
                className="grid gap-3 sm:grid-cols-3"
                aria-label={t("fields.plan")}
              >
                {tenantPlans.map((code) => (
                  <label
                    key={code}
                    htmlFor={`plan-${code}`}
                    className={cn(
                      "flex cursor-pointer flex-col gap-1 rounded-lg border p-3 text-sm transition-colors",
                      plan.field.value === code ? "border-primary bg-primary/5" : "hover:bg-muted",
                    )}
                  >
                    <span className="flex items-center gap-2 font-medium">
                      <RadioGroupItem id={`plan-${code}`} value={code} />
                      {code}
                    </span>
                    <span className="text-xs text-muted-foreground">{t(`plans.${code}.modules`)}</span>
                    <span className="text-xs text-muted-foreground">{t(`plans.${code}.users`)}</span>
                  </label>
                ))}
              </RadioGroup>
            </FieldSet>
          </FieldGroup>
        </CardContent>
        <CardFooter className="mt-6 flex-col gap-3">
          <Button type="submit" className="w-full" size="lg" disabled={submitting}>
            {submitting ? <Loader2Icon data-icon="inline-start" className="animate-spin" /> : null}
            {t("signup.submit")}
          </Button>
          <p className="text-sm text-muted-foreground">
            {t("signup.haveAccount")}{" "}
            <Link href="/login" className="font-medium text-foreground underline underline-offset-4">
              {t("signup.loginLink")}
            </Link>
          </p>
        </CardFooter>
      </form>
    </Card>
  );
}
