import { useState } from "react";
import { useTranslation } from "react-i18next";
import { StyleSheet, TextInput, View } from "react-native";

import { displayYear, isoFromDisplayInput, parseIsoDate, type DateLanguage } from "@/lib/date";
import { normalizeDigits } from "@/lib/quantity";
import { useTheme } from "@/theme/ThemeProvider";
import { radius, spacing, TOUCH_TARGET, typography } from "@/theme/tokens";

import { AppText } from "./AppText";

/** Props of {@link DateField}. */
export interface DateFieldProps {
  label: string;
  /** ISO Gregorian date `YYYY-MM-DD`, or `null` when empty. */
  value: string | null;
  /** Called with an ISO Gregorian date, or `null` when the field is empty or incomplete. */
  onChange: (iso: string | null) => void;
  /** Called with `true` when the typed text is not a real date. */
  onValidityChange?: (invalid: boolean) => void;
  /** Error message (already translated) from the form. */
  error?: string | null;
  testID?: string;
}

/** Splits an ISO date into the texts of the three inputs, with the year in the display era. */
function partsOf(iso: string | null, language: DateLanguage): { day: string; month: string; year: string } {
  const parts = parseIsoDate(iso);
  if (!parts) {
    return { day: "", month: "", year: "" };
  }
  return { day: String(parts.day).padStart(2, "0"), month: String(parts.month).padStart(2, "0"), year: String(displayYear(parts.year, language)) };
}

/**
 * Date input as day / month / year with large numeric fields. In Thai the year is typed and shown
 * in the Buddhist era (พ.ศ.); the value handed to the form is always ISO Gregorian.
 */
export function DateField({ label, value, onChange, onValidityChange, error, testID }: DateFieldProps) {
  const { t, i18n } = useTranslation();
  const { colors } = useTheme();
  const language: DateLanguage = i18n.language === "en" ? "en" : "th";
  const [text, setText] = useState(() => partsOf(value, language));
  const [invalid, setInvalid] = useState(false);

  const [seen, setSeen] = useState({ value, language });

  // Follow changes from outside (form reset, language switch) without fighting the user's typing.
  if (seen.value !== value || seen.language !== language) {
    setSeen({ value, language });
    const typed = isoFromDisplayInput(text.day, text.month, text.year, seen.language);
    if (typed !== value || seen.language !== language) {
      setText(partsOf(value, language));
      setInvalid(false);
    }
  }

  const update = (next: { day: string; month: string; year: string }) => {
    setText(next);
    const empty = next.day === "" && next.month === "" && next.year === "";
    const iso = empty ? null : isoFromDisplayInput(next.day, next.month, next.year, language);
    const bad = !empty && iso === null;
    setInvalid(bad);
    onValidityChange?.(bad);
    onChange(iso);
  };

  const digits = (raw: string, length: number) => normalizeDigits(raw).replace(/\D/g, "").slice(0, length);
  const message = error ?? (invalid ? t("validation.date_invalid") : null);
  const inputStyle = [styles.input, typography.body, { color: colors.text, backgroundColor: colors.surface, borderColor: message ? colors.danger : colors.border }];
  const eraLabel = language === "th" ? t("date.yearBuddhist") : t("date.yearGregorian");

  return (
    <View style={styles.field} testID={testID}>
      <AppText variant="label" style={styles.label}>
        {label}
      </AppText>
      <View style={styles.row}>
        <TextInput
          accessibilityLabel={`${label} ${t("date.day")}`}
          keyboardType="number-pad"
          inputMode="numeric"
          maxLength={2}
          placeholder={t("date.dayPlaceholder")}
          placeholderTextColor={colors.textMuted}
          value={text.day}
          onChangeText={(raw) => update({ ...text, day: digits(raw, 2) })}
          style={[inputStyle, styles.small]}
        />
        <TextInput
          accessibilityLabel={`${label} ${t("date.month")}`}
          keyboardType="number-pad"
          inputMode="numeric"
          maxLength={2}
          placeholder={t("date.monthPlaceholder")}
          placeholderTextColor={colors.textMuted}
          value={text.month}
          onChangeText={(raw) => update({ ...text, month: digits(raw, 2) })}
          style={[inputStyle, styles.small]}
        />
        <TextInput
          accessibilityLabel={`${label} ${eraLabel}`}
          keyboardType="number-pad"
          inputMode="numeric"
          maxLength={4}
          placeholder={eraLabel}
          placeholderTextColor={colors.textMuted}
          value={text.year}
          onChangeText={(raw) => update({ ...text, year: digits(raw, 4) })}
          style={[inputStyle, styles.large]}
        />
      </View>
      {message ? (
        <AppText variant="caption" color={colors.danger} accessibilityRole="alert" style={styles.message}>
          {message}
        </AppText>
      ) : (
        <AppText variant="caption" muted style={styles.message}>
          {t("date.hint", { era: eraLabel })}
        </AppText>
      )}
    </View>
  );
}

const styles = StyleSheet.create({
  field: { marginBottom: spacing.md },
  label: { marginBottom: spacing.xs },
  row: { flexDirection: "row", gap: spacing.sm },
  input: { minHeight: TOUCH_TARGET, borderWidth: 2, borderRadius: radius.sm, paddingHorizontal: spacing.md, textAlign: "center" },
  small: { flex: 1 },
  large: { flex: 1.6 },
  message: { marginTop: spacing.xs },
});
