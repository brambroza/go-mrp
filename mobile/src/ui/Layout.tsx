import type { ReactNode } from "react";
import { ActivityIndicator, KeyboardAvoidingView, Platform, Pressable, ScrollView, StyleSheet, View, type StyleProp, type ViewStyle } from "react-native";
import { useSafeAreaInsets } from "react-native-safe-area-context";

import { useTheme } from "@/theme/ThemeProvider";
import { radius, spacing, TOUCH_TARGET } from "@/theme/tokens";

import { AppText } from "./AppText";
import { Button } from "./Button";

/** Props of {@link Screen}. */
export interface ScreenProps {
  children: ReactNode;
  /** Scroll the content. Default `true`; use `false` when the screen contains a list. */
  scroll?: boolean;
  /** Buttons pinned to the bottom, above the keyboard. */
  footer?: ReactNode;
  /** Element pinned to the top, e.g. the offline banner. */
  header?: ReactNode;
  testID?: string;
}

/** Page container with the background colour, safe-area padding and keyboard handling. */
export function Screen({ children, scroll = true, footer, header, testID }: ScreenProps) {
  const { colors } = useTheme();
  const insets = useSafeAreaInsets();
  return (
    <KeyboardAvoidingView behavior={Platform.OS === "ios" ? "padding" : undefined} keyboardVerticalOffset={Platform.OS === "ios" ? 64 : 0} style={[styles.flex, { backgroundColor: colors.background }]} testID={testID}>
      {header}
      {scroll ? (
        <ScrollView keyboardShouldPersistTaps="handled" contentContainerStyle={[styles.content, { paddingBottom: spacing.xl + (footer ? 0 : insets.bottom) }]}>
          {children}
        </ScrollView>
      ) : (
        <View style={[styles.flex, styles.content]}>{children}</View>
      )}
      {footer ? <View style={[styles.footer, { backgroundColor: colors.surface, borderTopColor: colors.border, paddingBottom: spacing.md + insets.bottom }]}>{footer}</View> : null}
    </KeyboardAvoidingView>
  );
}

/** Props of {@link Card}. */
export interface CardProps {
  children: ReactNode;
  /** Makes the whole card pressable. */
  onPress?: () => void;
  accessibilityLabel?: string;
  style?: StyleProp<ViewStyle>;
  testID?: string;
}

/** Surface with a border that groups related content; pressable when `onPress` is given. */
export function Card({ children, onPress, accessibilityLabel, style, testID }: CardProps) {
  const { colors } = useTheme();
  const base = [styles.card, { backgroundColor: colors.surface, borderColor: colors.border }, style];
  if (!onPress) {
    return (
      <View style={base} testID={testID}>
        {children}
      </View>
    );
  }
  return (
    <Pressable accessibilityRole="button" accessibilityLabel={accessibilityLabel} onPress={onPress} testID={testID} style={({ pressed }) => [base, styles.pressable, { opacity: pressed ? 0.75 : 1 }]}>
      {children}
    </Pressable>
  );
}

/** Tone of a {@link Badge} or {@link Banner}. */
export type Tone = "info" | "success" | "warning" | "danger" | "neutral";

/** Background and text colour of a tone. */
function useTone(tone: Tone): { background: string; text: string } {
  const { colors } = useTheme();
  switch (tone) {
    case "success":
      return { background: colors.successSurface, text: colors.onSuccessSurface };
    case "warning":
      return { background: colors.warningSurface, text: colors.onWarningSurface };
    case "danger":
      return { background: colors.dangerSurface, text: colors.onDangerSurface };
    case "info":
      return { background: colors.infoSurface, text: colors.onInfoSurface };
    case "neutral":
      return { background: colors.surfaceAlt, text: colors.text };
  }
}

/** Small label for a status. The text carries the meaning; colour is only a support. */
export function Badge({ label, tone = "neutral" }: { label: string; tone?: Tone }) {
  const palette = useTone(tone);
  return (
    <View style={[styles.badge, { backgroundColor: palette.background }]}>
      <AppText variant="label" color={palette.text}>
        {label}
      </AppText>
    </View>
  );
}

/** Props of {@link Banner}. */
export interface BannerProps {
  tone: Tone;
  /** Short headline. */
  title: string;
  /** Explanation below the headline. */
  message?: string | null;
  /** Optional action, e.g. "try again". */
  actionLabel?: string;
  onAction?: () => void;
  testID?: string;
}

/** Message block for errors, warnings and confirmations. Announced by screen readers. */
export function Banner({ tone, title, message, actionLabel, onAction, testID }: BannerProps) {
  const palette = useTone(tone);
  return (
    <View accessibilityRole="alert" style={[styles.banner, { backgroundColor: palette.background, borderColor: palette.text }]} testID={testID}>
      <AppText variant="bodyStrong" color={palette.text}>
        {title}
      </AppText>
      {message ? (
        <AppText variant="body" color={palette.text}>
          {message}
        </AppText>
      ) : null}
      {actionLabel && onAction ? <Button label={actionLabel} onPress={onAction} variant="secondary" style={styles.bannerAction} /> : null}
    </View>
  );
}

/** Centered spinner with a message, shown while data loads. */
export function LoadingView({ label }: { label: string }) {
  const { colors } = useTheme();
  return (
    <View style={styles.center} accessibilityRole="progressbar" accessibilityLabel={label}>
      <ActivityIndicator size="large" color={colors.primary} />
      <AppText muted style={styles.centerText}>
        {label}
      </AppText>
    </View>
  );
}

/** Message for a list without rows. */
export function EmptyView({ title, message }: { title: string; message?: string }) {
  return (
    <View style={styles.center}>
      <AppText variant="heading" style={styles.centerText}>
        {title}
      </AppText>
      {message ? (
        <AppText muted style={styles.centerText}>
          {message}
        </AppText>
      ) : null}
    </View>
  );
}

/** Label and value on one row, used on detail screens. */
export function Field({ label, value, strong = false }: { label: string; value: string; strong?: boolean }) {
  return (
    <View style={styles.fieldRow}>
      <AppText variant="label" muted style={styles.fieldLabel}>
        {label}
      </AppText>
      <AppText variant={strong ? "number" : "bodyStrong"} style={styles.fieldValue} selectable>
        {value}
      </AppText>
    </View>
  );
}

/** Heading of a section inside a screen. */
export function SectionTitle({ children }: { children: string }) {
  return (
    <AppText variant="heading" accessibilityRole="header" style={styles.section}>
      {children}
    </AppText>
  );
}

const styles = StyleSheet.create({
  flex: { flex: 1 },
  content: { padding: spacing.lg },
  footer: { paddingHorizontal: spacing.lg, paddingTop: spacing.md, borderTopWidth: 1, gap: spacing.sm },
  card: { borderWidth: 1, borderRadius: radius.md, padding: spacing.lg, marginBottom: spacing.md },
  pressable: { minHeight: TOUCH_TARGET },
  badge: { alignSelf: "flex-start", borderRadius: radius.sm, paddingHorizontal: spacing.sm, paddingVertical: 2 },
  banner: { borderLeftWidth: 6, borderRadius: radius.sm, padding: spacing.md, marginBottom: spacing.md, gap: spacing.xs },
  bannerAction: { marginTop: spacing.sm },
  center: { flex: 1, alignItems: "center", justifyContent: "center", padding: spacing.xl, minHeight: 200 },
  centerText: { textAlign: "center", marginTop: spacing.sm },
  fieldRow: { flexDirection: "row", justifyContent: "space-between", alignItems: "flex-start", paddingVertical: spacing.xs, gap: spacing.md },
  fieldLabel: { flex: 1 },
  fieldValue: { flex: 1.4, textAlign: "right" },
  section: { marginTop: spacing.md, marginBottom: spacing.sm },
});
