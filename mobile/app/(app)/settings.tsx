import { useQuery } from "@tanstack/react-query";
import { useState } from "react";
import { useTranslation } from "react-i18next";
import { StyleSheet, View } from "react-native";

import { signOut } from "@/auth/session";
import { useCurrentUser } from "@/features/common/hooks";
import { setLanguage } from "@/i18n";
import { getQueueStore } from "@/offline/service";
import { useThemePreference, type ThemePreference } from "@/theme/ThemeProvider";
import { spacing } from "@/theme/tokens";
import { Button, Card, ConfirmDialog, Field, Screen, SectionTitle } from "@/ui";

const THEMES: ThemePreference[] = ["system", "light", "dark"];

/** Settings: profile, language, theme and sign-out. */
export default function SettingsScreen() {
  const { t, i18n } = useTranslation();
  const { user, scope } = useCurrentUser();
  const { preference, setPreference } = useThemePreference();
  const [confirming, setConfirming] = useState(false);
  const [leaving, setLeaving] = useState(false);

  const waiting = useQuery({
    queryKey: ["queue", "open", scope?.tenantId, scope?.userId],
    queryFn: async () => (scope ? (await getQueueStore()).count(scope, ["pending", "sending", "failed"]) : 0),
    enabled: scope !== null,
  });
  const openCount = waiting.data ?? 0;

  const leave = async () => {
    setLeaving(true);
    try {
      await signOut();
    } finally {
      setLeaving(false);
      setConfirming(false);
    }
  };

  return (
    <Screen>
      <SectionTitle>{t("settings.profile")}</SectionTitle>
      <Card>
        <Field label={t("settings.name")} value={user?.displayName ?? "-"} />
        <Field label={t("login.userName")} value={user?.userName ?? "-"} />
        <Field label={t("settings.company")} value={user?.tenantName ?? "-"} />
        <Field label={t("settings.roles")} value={user?.roles.join(", ") || "-"} />
      </Card>

      <SectionTitle>{t("settings.language")}</SectionTitle>
      <View style={styles.row}>
        <Button label="ไทย" accessibilityLabel="ภาษาไทย" variant={i18n.language === "th" ? "primary" : "secondary"} onPress={() => void setLanguage("th")} style={styles.grow} />
        <Button label="English" variant={i18n.language === "en" ? "primary" : "secondary"} onPress={() => void setLanguage("en")} style={styles.grow} />
      </View>

      <SectionTitle>{t("settings.theme")}</SectionTitle>
      <View style={styles.row}>
        {THEMES.map((theme) => (
          <Button key={theme} label={t(`settings.themes.${theme}`)} variant={preference === theme ? "primary" : "secondary"} onPress={() => setPreference(theme)} style={styles.grow} />
        ))}
      </View>

      <Button label={t("settings.signOut")} variant="danger" onPress={() => setConfirming(true)} style={styles.signOut} testID="sign-out" />

      <ConfirmDialog
        visible={confirming}
        title={t("settings.signOutTitle")}
        message={openCount > 0 ? t("settings.signOutWithQueue", { count: openCount }) : t("settings.signOutMessage")}
        confirmLabel={t("settings.signOut")}
        cancelLabel={t("common.cancel")}
        destructive
        busy={leaving}
        onConfirm={() => void leave()}
        onCancel={() => setConfirming(false)}
      />
    </Screen>
  );
}

const styles = StyleSheet.create({
  row: { flexDirection: "row", gap: spacing.sm },
  grow: { flex: 1 },
  signOut: { marginTop: spacing.xxl },
});
