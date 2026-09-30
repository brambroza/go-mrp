"use client";

import { EyeIcon, EyeOffIcon } from "lucide-react";
import { useTranslations } from "next-intl";
import { useId, useState, type ComponentProps, type ReactNode } from "react";
import { useController, type Control, type FieldPath, type FieldValues } from "react-hook-form";

import { Checkbox } from "@/components/ui/checkbox";
import { Field, FieldContent, FieldDescription, FieldError, FieldLabel } from "@/components/ui/field";
import { Input } from "@/components/ui/input";
import { InputGroup, InputGroupAddon, InputGroupButton, InputGroupInput } from "@/components/ui/input-group";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Switch } from "@/components/ui/switch";
import { Textarea } from "@/components/ui/textarea";

import { DateInput, type DateInputProps } from "./date-input";
import { NumberInput, type NumberInputProps } from "./number-input";
import { useFieldMessage } from "./use-field-message";

/** Props every form field shares. */
export interface BaseFieldProps<TValues extends FieldValues> {
  /** `form.control` of the react-hook-form instance. */
  // eslint-disable-next-line @typescript-eslint/no-explicit-any -- accepts forms with any context and output type
  control: Control<TValues, any, any>;
  /** Path of the value in the form, for example `code` or `steps.0.name`. */
  name: FieldPath<TValues>;
  /** Visible label. */
  label: ReactNode;
  /** Help text under the input. */
  description?: ReactNode;
  /** Shows the required marker; validation itself comes from the zod schema. */
  required?: boolean;
  disabled?: boolean;
  className?: string;
}

/** Ids and ARIA attributes that connect an input with its label, description and error. */
interface FieldAria {
  id: string;
  "aria-invalid": boolean;
  "aria-describedby": string | undefined;
  "aria-required": boolean | undefined;
}

/** Props of {@link FieldShell}. */
interface FieldShellProps<TValues extends FieldValues> extends BaseFieldProps<TValues> {
  /** Layout: label above the input (default) or next to a switch/checkbox. */
  orientation?: "vertical" | "horizontal";
  /** Renders the input; receives the controller and the ARIA attributes. */
  children: (field: ReturnType<typeof useController<TValues>>["field"], aria: FieldAria) => ReactNode;
}

/** Label, description and error around an input; the base of every field component. */
export function FieldShell<TValues extends FieldValues>({
  control,
  name,
  label,
  description,
  required,
  className,
  orientation = "vertical",
  children,
}: FieldShellProps<TValues>) {
  const { field, fieldState } = useController({ control, name });
  const messageOf = useFieldMessage();
  const id = useId();
  const error = messageOf(fieldState.error?.message);
  const invalid = fieldState.invalid;
  const describedBy = [description ? `${id}-description` : null, error ? `${id}-error` : null].filter(Boolean).join(" ");
  const aria: FieldAria = {
    id,
    "aria-invalid": invalid,
    "aria-describedby": describedBy || undefined,
    "aria-required": required || undefined,
  };
  const labelNode = (
    <FieldLabel htmlFor={id}>
      {label}
      {required ? (
        <span aria-hidden="true" className="text-destructive">
          *
        </span>
      ) : null}
    </FieldLabel>
  );
  const messages = (
    <>
      {description ? <FieldDescription id={`${id}-description`}>{description}</FieldDescription> : null}
      {error ? <FieldError id={`${id}-error`}>{error}</FieldError> : null}
    </>
  );

  if (orientation === "horizontal") {
    return (
      <Field orientation="horizontal" data-invalid={invalid} data-field={name} className={className}>
        {children(field, aria)}
        <FieldContent>
          {labelNode}
          {messages}
        </FieldContent>
      </Field>
    );
  }
  return (
    <Field data-invalid={invalid} data-field={name} className={className}>
      {labelNode}
      {children(field, aria)}
      {messages}
    </Field>
  );
}

/** Props of {@link TextField}. */
export interface TextFieldProps<TValues extends FieldValues>
  extends BaseFieldProps<TValues>,
    Pick<ComponentProps<"input">, "maxLength" | "placeholder" | "autoComplete" | "autoFocus" | "inputMode" | "type" | "readOnly"> {
  /** Converts what was typed, for example to upper case for codes. */
  transform?: (value: string) => string;
}

/** Single-line text input bound to a form field. */
export function TextField<TValues extends FieldValues>({
  maxLength,
  placeholder,
  autoComplete = "off",
  autoFocus,
  inputMode,
  type = "text",
  readOnly,
  transform,
  ...shell
}: TextFieldProps<TValues>) {
  return (
    <FieldShell {...shell}>
      {(field, aria) => (
        <Input
          {...aria}
          ref={field.ref}
          name={field.name}
          value={(field.value as string | null | undefined) ?? ""}
          onChange={(event) => field.onChange(transform ? transform(event.target.value) : event.target.value)}
          onBlur={field.onBlur}
          disabled={shell.disabled || field.disabled}
          {...{ maxLength, placeholder, autoComplete, autoFocus, inputMode, type, readOnly }}
        />
      )}
    </FieldShell>
  );
}

/** Password input with a show/hide button. */
export function PasswordField<TValues extends FieldValues>({
  maxLength = 100,
  placeholder,
  autoComplete = "current-password",
  autoFocus,
  ...shell
}: Omit<TextFieldProps<TValues>, "type" | "transform" | "inputMode">) {
  const t = useTranslations("common");
  const [visible, setVisible] = useState(false);
  return (
    <FieldShell {...shell}>
      {(field, aria) => (
        <InputGroup>
          <InputGroupInput
            {...aria}
            ref={field.ref}
            name={field.name}
            type={visible ? "text" : "password"}
            value={(field.value as string | null | undefined) ?? ""}
            onChange={field.onChange}
            onBlur={field.onBlur}
            disabled={shell.disabled || field.disabled}
            {...{ maxLength, placeholder, autoComplete, autoFocus }}
          />
          <InputGroupAddon align="inline-end">
            <InputGroupButton
              type="button"
              size="icon-xs"
              aria-label={visible ? t("password.hide") : t("password.show")}
              aria-pressed={visible}
              onClick={() => setVisible((current) => !current)}
            >
              {visible ? <EyeOffIcon /> : <EyeIcon />}
            </InputGroupButton>
          </InputGroupAddon>
        </InputGroup>
      )}
    </FieldShell>
  );
}

/** Props of {@link TextareaField}. */
export interface TextareaFieldProps<TValues extends FieldValues>
  extends BaseFieldProps<TValues>,
    Pick<ComponentProps<"textarea">, "maxLength" | "placeholder" | "rows"> {}

/** Multi-line text input bound to a form field. */
export function TextareaField<TValues extends FieldValues>({ maxLength, placeholder, rows = 3, ...shell }: TextareaFieldProps<TValues>) {
  return (
    <FieldShell {...shell}>
      {(field, aria) => (
        <Textarea
          {...aria}
          ref={field.ref}
          name={field.name}
          value={(field.value as string | null | undefined) ?? ""}
          onChange={field.onChange}
          onBlur={field.onBlur}
          disabled={shell.disabled || field.disabled}
          {...{ maxLength, placeholder, rows }}
        />
      )}
    </FieldShell>
  );
}

/** Props of {@link NumberField} and its presets. */
export interface NumberFieldProps<TValues extends FieldValues>
  extends BaseFieldProps<TValues>,
    Pick<NumberInputProps, "decimals" | "minDecimals" | "allowNegative" | "prefix" | "suffix" | "placeholder"> {}

/** Number input bound to a form field whose value is `number | null`. */
export function NumberField<TValues extends FieldValues>({
  decimals,
  minDecimals,
  allowNegative,
  prefix,
  suffix,
  placeholder,
  ...shell
}: NumberFieldProps<TValues>) {
  return (
    <FieldShell {...shell}>
      {(field, aria) => (
        <NumberInput
          {...aria}
          ref={field.ref}
          name={field.name}
          value={field.value as number | null | undefined}
          onValueChange={field.onChange}
          onBlur={field.onBlur}
          disabled={shell.disabled || field.disabled}
          {...{ decimals, minDecimals, allowNegative, prefix, suffix, placeholder }}
        />
      )}
    </FieldShell>
  );
}

/** Props of the preset number fields. */
export type PresetNumberFieldProps<TValues extends FieldValues> = Omit<NumberFieldProps<TValues>, "decimals">;

/** Money field: 4 decimals stored, 2 shown, baht sign unless `prefix` says otherwise. */
export function MoneyField<TValues extends FieldValues>({ prefix = "฿", minDecimals = 2, ...props }: PresetNumberFieldProps<TValues>) {
  return <NumberField decimals={4} minDecimals={minDecimals} prefix={prefix} {...props} />;
}

/** Quantity field: up to 6 decimals; pass the unit code as `suffix`. */
export function QuantityField<TValues extends FieldValues>(props: PresetNumberFieldProps<TValues>) {
  return <NumberField decimals={6} {...props} />;
}

/** Percent field with up to 2 decimals. */
export function PercentField<TValues extends FieldValues>({ suffix = "%", ...props }: PresetNumberFieldProps<TValues>) {
  return <NumberField decimals={2} suffix={suffix} {...props} />;
}

/** Whole-number field. */
export function IntegerField<TValues extends FieldValues>(props: PresetNumberFieldProps<TValues>) {
  return <NumberField decimals={0} {...props} />;
}

/** One choice of a {@link SelectField}. */
export interface SelectOption {
  /** Stored value; must not be empty. */
  value: string;
  /** Visible text. */
  label: string;
  disabled?: boolean;
}

/** Props of {@link SelectField}. */
export interface SelectFieldProps<TValues extends FieldValues> extends BaseFieldProps<TValues> {
  /** Choices. */
  options: readonly SelectOption[];
  /** Text when nothing is selected. */
  placeholder?: string;
  /** Adds a choice that clears the field (value becomes an empty string). */
  emptyLabel?: string;
}

/** Value of the "nothing selected" item; Radix Select does not allow empty item values. */
const EMPTY_OPTION = "__empty__";

/** Drop-down bound to a form field whose value is a string (empty = nothing selected). */
export function SelectField<TValues extends FieldValues>({ options, placeholder, emptyLabel, ...shell }: SelectFieldProps<TValues>) {
  const t = useTranslations("common");
  return (
    <FieldShell {...shell}>
      {(field, aria) => (
        <Select
          value={(field.value as string | null | undefined) || (emptyLabel ? EMPTY_OPTION : "")}
          onValueChange={(value) => {
            // Radix reports an empty value when the form resets the field; a user can never pick
            // one (the "nothing" choice has its own value), so it must not overwrite the field.
            if (value !== "") {
              field.onChange(value === EMPTY_OPTION ? "" : value);
            }
          }}
          disabled={shell.disabled || field.disabled}
        >
          <SelectTrigger {...aria} ref={field.ref} onBlur={field.onBlur} className="w-full">
            <SelectValue placeholder={placeholder ?? t("select.placeholder")} />
          </SelectTrigger>
          <SelectContent>
            {emptyLabel ? <SelectItem value={EMPTY_OPTION}>{emptyLabel}</SelectItem> : null}
            {options.map((option) => (
              <SelectItem key={option.value} value={option.value} disabled={option.disabled}>
                {option.label}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
      )}
    </FieldShell>
  );
}

/** Switch bound to a boolean form field, label on the right. */
export function SwitchField<TValues extends FieldValues>(shell: BaseFieldProps<TValues>) {
  return (
    <FieldShell {...shell} orientation="horizontal">
      {(field, aria) => (
        <Switch
          {...aria}
          ref={field.ref}
          name={field.name}
          checked={Boolean(field.value)}
          onCheckedChange={field.onChange}
          onBlur={field.onBlur}
          disabled={shell.disabled || field.disabled}
        />
      )}
    </FieldShell>
  );
}

/** Checkbox bound to a boolean form field, label on the right. */
export function CheckboxField<TValues extends FieldValues>(shell: BaseFieldProps<TValues>) {
  return (
    <FieldShell {...shell} orientation="horizontal">
      {(field, aria) => (
        <Checkbox
          {...aria}
          ref={field.ref}
          name={field.name}
          checked={Boolean(field.value)}
          onCheckedChange={(checked) => field.onChange(checked === true)}
          onBlur={field.onBlur}
          disabled={shell.disabled || field.disabled}
        />
      )}
    </FieldShell>
  );
}

/** Props of {@link DateField}. */
export interface DateFieldProps<TValues extends FieldValues>
  extends BaseFieldProps<TValues>,
    Pick<DateInputProps, "min" | "max" | "clearable" | "placeholder"> {}

/** Date picker bound to a form field whose value is `YYYY-MM-DD` (empty = not set). */
export function DateField<TValues extends FieldValues>({ min, max, clearable, placeholder, ...shell }: DateFieldProps<TValues>) {
  return (
    <FieldShell {...shell}>
      {(field, aria) => (
        <DateInput
          id={aria.id}
          aria-invalid={aria["aria-invalid"]}
          aria-describedby={aria["aria-describedby"]}
          value={field.value as string | null | undefined}
          onChange={field.onChange}
          onBlur={field.onBlur}
          disabled={shell.disabled || field.disabled}
          {...{ min, max, clearable, placeholder }}
        />
      )}
    </FieldShell>
  );
}

/** Props of {@link CheckboxGroupField}. */
export interface CheckboxGroupFieldProps<TValues extends FieldValues> extends BaseFieldProps<TValues> {
  /** Choices; the field value is the array of selected `value`s. */
  options: readonly SelectOption[];
}

/** List of checkboxes bound to a form field whose value is `string[]`. */
export function CheckboxGroupField<TValues extends FieldValues>({ options, ...shell }: CheckboxGroupFieldProps<TValues>) {
  return (
    <FieldShell {...shell}>
      {(field, aria) => {
        const selected = new Set((field.value as string[] | undefined) ?? []);
        return (
          <div
            role="group"
            aria-describedby={aria["aria-describedby"]}
            className="grid gap-2 sm:grid-cols-2"
          >
            {options.map((option, index) => {
              const optionId = `${aria.id}-${index}`;
              return (
                <label key={option.value} htmlFor={optionId} className="flex items-center gap-2 text-sm">
                  <Checkbox
                    id={index === 0 ? aria.id : optionId}
                    checked={selected.has(option.value)}
                    disabled={shell.disabled || option.disabled}
                    onBlur={field.onBlur}
                    onCheckedChange={(checked) => {
                      const next = new Set(selected);
                      if (checked === true) {
                        next.add(option.value);
                      } else {
                        next.delete(option.value);
                      }
                      field.onChange(options.map((item) => item.value).filter((value) => next.has(value)));
                    }}
                  />
                  {option.label}
                </label>
              );
            })}
          </div>
        );
      }}
    </FieldShell>
  );
}
