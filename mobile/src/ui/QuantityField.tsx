import { useTranslation } from "react-i18next";

import type { QuantityError } from "@/lib/quantity";

import { TextField, type TextFieldProps } from "./TextField";

/** Props of {@link QuantityField}. */
export interface QuantityFieldProps extends Omit<TextFieldProps, "keyboardType" | "error" | "value" | "onChangeText"> {
  /** Text typed by the user. */
  value: string;
  onChangeText: (text: string) => void;
  /** Validation result of the text, from `parseQuantity`. */
  errorCode?: QuantityError | null;
  /** Unit shown in the label, e.g. `KG`. */
  unit?: string;
}

/** Numeric input for quantities with up to 6 decimals. */
export function QuantityField({ value, onChangeText, errorCode, unit, label, ...rest }: QuantityFieldProps) {
  const { t } = useTranslation();
  return (
    <TextField
      {...rest}
      label={unit ? `${label} (${unit})` : label}
      value={value}
      onChangeText={onChangeText}
      keyboardType="decimal-pad"
      inputMode="decimal"
      selectTextOnFocus
      maxLength={20}
      error={errorCode ? t(`validation.quantity.${errorCode}`) : null}
    />
  );
}
