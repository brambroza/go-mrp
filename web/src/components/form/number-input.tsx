"use client";

import { useState, type ComponentProps } from "react";

import { Input } from "@/components/ui/input";
import { InputGroup, InputGroupAddon, InputGroupInput, InputGroupText } from "@/components/ui/input-group";
import { MONEY_DECIMALS, QUANTITY_DECIMALS, formatQuantity, parseDecimal, toInputText } from "@/lib/format/number";

/** Props of {@link NumberInput}. */
export interface NumberInputProps
  extends Omit<ComponentProps<"input">, "value" | "defaultValue" | "onChange" | "type" | "inputMode"> {
  /** Current value; `null` is an empty input. */
  value: number | null | undefined;
  /** Called with the parsed value while typing; `null` for empty or unparsable text. */
  onValueChange: (value: number | null) => void;
  /** Number of decimals kept (0 = whole numbers). */
  decimals: number;
  /** Decimals always shown when the input is not focused (default 0). */
  minDecimals?: number;
  /** Allow a leading minus sign (default `false`). */
  allowNegative?: boolean;
  /** Text before the number, for example `฿`. */
  prefix?: string;
  /** Text after the number, for example a unit code or `%`. */
  suffix?: string;
}

/**
 * Text input for numbers. Shows thousands separators when not focused, accepts only digits,
 * one decimal point and (optionally) a minus sign, and always reports a JavaScript number.
 */
export function NumberInput({
  value,
  onValueChange,
  decimals,
  minDecimals = 0,
  allowNegative = false,
  prefix,
  suffix,
  onBlur,
  onFocus,
  className,
  ...props
}: NumberInputProps) {
  const [draft, setDraft] = useState<string | null>(null);
  const display =
    draft ?? (value === null || value === undefined ? "" : formatQuantity(value, { minDecimals, maxDecimals: decimals }));

  const accept = (text: string): boolean => {
    const pattern = decimals > 0 ? /^-?\d*\.?\d*$/ : /^-?\d*$/;
    const cleaned = text.replace(/,/g, "");
    if (!pattern.test(cleaned) || (!allowNegative && cleaned.startsWith("-"))) {
      return false;
    }
    const fraction = cleaned.split(".")[1];
    return fraction === undefined || fraction.length <= decimals;
  };

  const input = {
    ...props,
    type: "text",
    inputMode: decimals > 0 ? ("decimal" as const) : ("numeric" as const),
    autoComplete: "off",
    value: display,
    className: `text-right tabular-nums ${className ?? ""}`,
    onFocus: (event: React.FocusEvent<HTMLInputElement>) => {
      setDraft(toInputText(value, decimals));
      onFocus?.(event);
    },
    onBlur: (event: React.FocusEvent<HTMLInputElement>) => {
      setDraft(null);
      onBlur?.(event);
    },
    onChange: (event: React.ChangeEvent<HTMLInputElement>) => {
      const text = event.target.value;
      if (!accept(text)) {
        return;
      }
      setDraft(text);
      onValueChange(parseDecimal(text, decimals) ?? null);
    },
  };

  if (!prefix && !suffix) {
    return <Input {...input} />;
  }
  return (
    <InputGroup>
      {prefix ? (
        <InputGroupAddon>
          <InputGroupText>{prefix}</InputGroupText>
        </InputGroupAddon>
      ) : null}
      <InputGroupInput {...input} />
      {suffix ? (
        <InputGroupAddon align="inline-end">
          <InputGroupText>{suffix}</InputGroupText>
        </InputGroupAddon>
      ) : null}
    </InputGroup>
  );
}

/** Props of the money, quantity, percent and integer inputs. */
export type PresetNumberInputProps = Omit<NumberInputProps, "decimals">;

/** Input for money: up to 4 decimals, shows at least 2, baht sign by default. */
export function MoneyInput({ prefix = "฿", minDecimals = 2, ...props }: PresetNumberInputProps) {
  return <NumberInput decimals={MONEY_DECIMALS} minDecimals={minDecimals} prefix={prefix} {...props} />;
}

/** Input for quantities: up to 6 decimals; pass the unit code as `suffix`. */
export function QuantityInput(props: PresetNumberInputProps) {
  return <NumberInput decimals={QUANTITY_DECIMALS} {...props} />;
}

/** Input for percentages with up to 2 decimals. */
export function PercentInput({ suffix = "%", ...props }: PresetNumberInputProps) {
  return <NumberInput decimals={2} suffix={suffix} {...props} />;
}

/** Input for whole numbers. */
export function IntegerInput(props: PresetNumberInputProps) {
  return <NumberInput decimals={0} {...props} />;
}
