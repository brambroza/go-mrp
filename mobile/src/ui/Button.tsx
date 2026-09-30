import { ActivityIndicator, Pressable, StyleSheet, View, type StyleProp, type ViewStyle } from "react-native";

import { useTheme } from "@/theme/ThemeProvider";
import { radius, spacing, TOUCH_TARGET } from "@/theme/tokens";

import { AppText } from "./AppText";

/** Visual style of a button. */
export type ButtonVariant = "primary" | "secondary" | "danger" | "ghost";

/** Props of {@link Button}. */
export interface ButtonProps {
  /** Text of the button. */
  label: string;
  onPress: () => void;
  variant?: ButtonVariant;
  /** Shows a spinner and blocks presses. */
  loading?: boolean;
  disabled?: boolean;
  /** Text read by screen readers when it differs from the label. */
  accessibilityLabel?: string;
  testID?: string;
  style?: StyleProp<ViewStyle>;
}

/** Large button (at least 56 dp high) that can be pressed with gloves. */
export function Button({ label, onPress, variant = "primary", loading = false, disabled = false, accessibilityLabel, testID, style }: ButtonProps) {
  const { colors } = useTheme();
  const blocked = disabled || loading;
  const palette = {
    primary: { background: colors.primary, text: colors.onPrimary, border: colors.primary },
    secondary: { background: colors.surface, text: colors.primary, border: colors.primary },
    danger: { background: colors.danger, text: colors.onDanger, border: colors.danger },
    ghost: { background: "transparent", text: colors.primary, border: "transparent" },
  }[variant];

  return (
    <Pressable
      accessibilityRole="button"
      accessibilityLabel={accessibilityLabel ?? label}
      accessibilityState={{ disabled: blocked, busy: loading }}
      disabled={blocked}
      onPress={onPress}
      testID={testID}
      hitSlop={4}
      style={({ pressed }) => [
        styles.button,
        { backgroundColor: palette.background, borderColor: palette.border, opacity: blocked ? 0.55 : pressed ? 0.8 : 1 },
        style,
      ]}
    >
      <View style={styles.content}>
        {loading ? <ActivityIndicator color={palette.text} style={styles.spinner} /> : null}
        <AppText variant="button" color={palette.text} numberOfLines={2} style={styles.label}>
          {label}
        </AppText>
      </View>
    </Pressable>
  );
}

const styles = StyleSheet.create({
  button: {
    minHeight: TOUCH_TARGET,
    borderRadius: radius.md,
    borderWidth: 2,
    paddingHorizontal: spacing.lg,
    paddingVertical: spacing.sm,
    justifyContent: "center",
  },
  content: { flexDirection: "row", alignItems: "center", justifyContent: "center" },
  spinner: { marginRight: spacing.sm },
  label: { textAlign: "center", flexShrink: 1 },
});
