import { forwardRef, useState } from "react";
import { StyleSheet, TextInput, View, type TextInputProps } from "react-native";

import { useTheme } from "@/theme/ThemeProvider";
import { radius, spacing, TOUCH_TARGET, typography } from "@/theme/tokens";

import { AppText } from "./AppText";

/** Props of {@link TextField}. */
export interface TextFieldProps extends Omit<TextInputProps, "style"> {
  /** Label shown above the input. */
  label: string;
  /** Error message (already translated) shown below the input. */
  error?: string | null;
  /** Hint shown below the input when there is no error. */
  hint?: string;
  /** Element at the right edge inside the row, e.g. a scan button. */
  trailing?: React.ReactNode;
}

/** Text input with a visible label, an error message below the field and a clear focus ring. */
export const TextField = forwardRef<TextInput, TextFieldProps>(function TextField({ label, error, hint, trailing, editable = true, ...rest }, ref) {
  const { colors } = useTheme();
  const [focused, setFocused] = useState(false);
  const borderColor = error ? colors.danger : focused ? colors.focus : colors.border;

  return (
    <View style={styles.field}>
      <AppText variant="label" style={styles.label}>
        {label}
      </AppText>
      <View style={styles.row}>
        <TextInput
          ref={ref}
          accessibilityLabel={label}
          accessibilityHint={error ?? hint}
          editable={editable}
          placeholderTextColor={colors.textMuted}
          {...rest}
          onFocus={(event) => {
            setFocused(true);
            rest.onFocus?.(event);
          }}
          onBlur={(event) => {
            setFocused(false);
            rest.onBlur?.(event);
          }}
          style={[
            styles.input,
            typography.body,
            {
              color: colors.text,
              backgroundColor: editable ? colors.surface : colors.surfaceAlt,
              borderColor,
              borderWidth: focused || error ? 3 : 2,
            },
          ]}
        />
        {trailing ? <View style={styles.trailing}>{trailing}</View> : null}
      </View>
      {error ? (
        <AppText variant="caption" color={colors.danger} accessibilityRole="alert" style={styles.message}>
          {error}
        </AppText>
      ) : hint ? (
        <AppText variant="caption" muted style={styles.message}>
          {hint}
        </AppText>
      ) : null}
    </View>
  );
});

const styles = StyleSheet.create({
  field: { marginBottom: spacing.md },
  label: { marginBottom: spacing.xs },
  row: { flexDirection: "row", alignItems: "stretch" },
  input: {
    flex: 1,
    minHeight: TOUCH_TARGET,
    borderRadius: radius.sm,
    paddingHorizontal: spacing.md,
    paddingVertical: spacing.sm,
  },
  trailing: { marginLeft: spacing.sm, justifyContent: "center" },
  message: { marginTop: spacing.xs },
});
