import * as Device from "expo-device";
import { useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { Platform, StyleSheet, View, type TextInput } from "react-native";

import { readRememberedLogin, signIn, useSession } from "@/auth/session";
import { getApiBaseUrl } from "@/config/env";
import { useErrorText } from "@/features/common/hooks";
import { setLanguage } from "@/i18n";
import { spacing } from "@/theme/tokens";
import { AppText, Banner, Button, Screen, TextField } from "@/ui";
import { loginSchema, twoFactorCodeSchema } from "@/validation/schemas";

/** Field errors of the login form (i18n keys). */
type FieldErrors = Partial<Record<"companyCode" | "userName" | "password" | "twoFactorCode", string>>;

/** Label sent to the API so that the user can recognise this device in the session list. */
function deviceLabel(): string {
  const name = [Device.manufacturer, Device.modelName].filter(Boolean).join(" ");
  return `${name || "Mobile"} (${Platform.OS})`.slice(0, 200);
}

/** Sign-in screen: company code, user name and password, then the TOTP code when required. */
export default function LoginScreen() {
  const { t, i18n } = useTranslation();
  const errorText = useErrorText();
  const expired = useSession((state) => state.expired);
  const config = getApiBaseUrl();

  const [companyCode, setCompanyCode] = useState("");
  const [userName, setUserName] = useState("");
  const [password, setPassword] = useState("");
  const [twoFactorCode, setTwoFactorCode] = useState("");
  const [step, setStep] = useState<"credentials" | "twoFactor">("credentials");
  const [errors, setErrors] = useState<FieldErrors>({});
  const [formError, setFormError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);
  const userNameRef = useRef<TextInput>(null);
  const passwordRef = useRef<TextInput>(null);

  useEffect(() => {
    let active = true;
    void readRememberedLogin().then((remembered) => {
      if (active) {
        setCompanyCode((current) => current || remembered.companyCode);
        setUserName((current) => current || remembered.userName);
      }
    });
    return () => {
      active = false;
    };
  }, []);

  const submit = async () => {
    if (loading) {
      return;
    }
    setFormError(null);
    const parsed = loginSchema.safeParse({ companyCode, userName, password });
    const next: FieldErrors = {};
    if (!parsed.success) {
      for (const issue of parsed.error.issues) {
        const field = issue.path[0];
        if (field === "companyCode" || field === "userName" || field === "password") {
          next[field] ??= issue.message;
        }
      }
    }
    let code: string | undefined;
    if (step === "twoFactor") {
      const parsedCode = twoFactorCodeSchema.safeParse(twoFactorCode);
      if (parsedCode.success) {
        code = parsedCode.data;
      } else {
        next.twoFactorCode = parsedCode.error.issues[0]?.message ?? "validation.invalid";
      }
    }
    setErrors(next);
    if (!parsed.success || Object.keys(next).length > 0) {
      return;
    }

    setLoading(true);
    try {
      const result = await signIn({ ...parsed.data, twoFactorCode: code, device: deviceLabel() });
      if (result.ok) {
        setPassword("");
        return;
      }
      if (result.twoFactorRequired) {
        setStep("twoFactor");
        setTwoFactorCode("");
        return;
      }
      setFormError(errorText(result.error));
      if (result.error.fieldErrors) {
        const fromApi: FieldErrors = {};
        for (const [field, messages] of Object.entries(result.error.fieldErrors)) {
          if ((field === "companyCode" || field === "userName" || field === "password" || field === "twoFactorCode") && messages[0]) {
            fromApi[field] = messages[0];
          }
        }
        setErrors(fromApi);
      }
    } finally {
      setLoading(false);
    }
  };

  const message = (key: string | undefined) => (key ? (i18n.exists(key) ? t(key) : key) : null);

  return (
    <Screen testID="login-screen">
      <View style={styles.header}>
        <AppText variant="title" accessibilityRole="header">
          {t("login.title")}
        </AppText>
        <AppText muted>{t("login.subtitle")}</AppText>
      </View>

      {!config.ok ? <Banner tone="danger" title={t("login.configTitle")} message={t(`login.config.${config.error}`)} /> : null}
      {expired ? <Banner tone="warning" title={t("login.expiredTitle")} message={t("login.expiredMessage")} /> : null}
      {formError ? <Banner tone="danger" title={t("login.failedTitle")} message={formError} testID="login-error" /> : null}

      {step === "credentials" ? (
        <>
          <TextField
            label={t("login.companyCode")}
            value={companyCode}
            onChangeText={setCompanyCode}
            error={message(errors.companyCode)}
            autoCapitalize="none"
            autoCorrect={false}
            autoComplete="organization"
            maxLength={40}
            returnKeyType="next"
            onSubmitEditing={() => userNameRef.current?.focus()}
            testID="login-company"
          />
          <TextField
            ref={userNameRef}
            label={t("login.userName")}
            value={userName}
            onChangeText={setUserName}
            error={message(errors.userName)}
            autoCapitalize="none"
            autoCorrect={false}
            autoComplete="username"
            textContentType="username"
            maxLength={200}
            returnKeyType="next"
            onSubmitEditing={() => passwordRef.current?.focus()}
            testID="login-user"
          />
          <TextField
            ref={passwordRef}
            label={t("login.password")}
            value={password}
            onChangeText={setPassword}
            error={message(errors.password)}
            secureTextEntry
            autoCapitalize="none"
            autoCorrect={false}
            autoComplete="current-password"
            textContentType="password"
            maxLength={100}
            returnKeyType="go"
            onSubmitEditing={() => void submit()}
            testID="login-password"
          />
          <Button label={loading ? t("login.signingIn") : t("login.signIn")} onPress={() => void submit()} loading={loading} disabled={!config.ok} testID="login-submit" />
        </>
      ) : (
        <>
          <Banner tone="info" title={t("login.twoFactorTitle")} message={t("login.twoFactorMessage")} />
          <TextField
            label={t("login.twoFactorCode")}
            value={twoFactorCode}
            onChangeText={setTwoFactorCode}
            error={message(errors.twoFactorCode)}
            keyboardType="number-pad"
            inputMode="numeric"
            autoComplete="one-time-code"
            textContentType="oneTimeCode"
            maxLength={10}
            autoFocus
            returnKeyType="go"
            onSubmitEditing={() => void submit()}
            testID="login-totp"
          />
          <Button label={loading ? t("login.signingIn") : t("login.verify")} onPress={() => void submit()} loading={loading} testID="login-verify" />
          <Button
            label={t("common.back")}
            variant="ghost"
            disabled={loading}
            onPress={() => {
              setStep("credentials");
              setErrors({});
              setFormError(null);
            }}
            style={styles.gap}
          />
        </>
      )}

      <View style={styles.language}>
        <Button label="ไทย" variant={i18n.language === "th" ? "primary" : "secondary"} onPress={() => void setLanguage("th")} accessibilityLabel="ภาษาไทย" style={styles.languageButton} />
        <Button label="English" variant={i18n.language === "en" ? "primary" : "secondary"} onPress={() => void setLanguage("en")} style={styles.languageButton} />
      </View>
    </Screen>
  );
}

const styles = StyleSheet.create({
  header: { marginTop: spacing.xxl, marginBottom: spacing.xl },
  gap: { marginTop: spacing.sm },
  language: { flexDirection: "row", gap: spacing.sm, marginTop: spacing.xxl },
  languageButton: { flex: 1 },
});
