"use client";

import { useTranslations } from "next-intl";
import type { Control, FieldPath, FieldValues } from "react-hook-form";

import { IntegerField, TextareaField, TextField } from "@/components/form/fields";
import { msg, optionalEmail, optionalText, requiredInteger, requiredText, z } from "@/lib/validation/zod";

import { codeText, toCode } from "./shared";

/** Fields customers and suppliers share; mirrors the contact part of their request records. */
export const partnerShape = {
  code: codeText(),
  name: requiredText(200),
  nameEn: optionalText(200),
  taxId: optionalText(20).refine((value) => /^[0-9A-Za-z-]*$/.test(value), { error: msg("taxId") }),
  branchNo: optionalText(10),
  address: optionalText(500),
  phone: optionalText(50),
  email: optionalEmail(200),
  contactName: optionalText(100),
  creditDays: requiredInteger(0, 365),
  isActive: z.boolean(),
};

/** Form values customers and suppliers share. */
export interface PartnerValues {
  code: string;
  name: string;
  nameEn: string;
  taxId: string;
  branchNo: string;
  address: string;
  phone: string;
  email: string;
  contactName: string;
  creditDays: number;
  isActive: boolean;
}

/** Default values of the shared partner fields. */
export const partnerDefaults: PartnerValues = {
  code: "",
  name: "",
  nameEn: "",
  taxId: "",
  branchNo: "",
  address: "",
  phone: "",
  email: "",
  contactName: "",
  creditDays: 0,
  isActive: true,
};

/** Formats a Thai phone number while typing: `0812345678` → `081-234-5678`; other input is kept. */
export function formatThaiPhone(value: string): string {
  const digits = value.replace(/\D/g, "");
  if (!/^[\d\s-]*$/.test(value) || !digits.startsWith("0") || digits.length > 10) {
    return value;
  }
  if (digits.length <= 3) {
    return digits;
  }
  if (digits.startsWith("02")) {
    return digits.length <= 5 ? `${digits.slice(0, 2)}-${digits.slice(2)}` : `${digits.slice(0, 2)}-${digits.slice(2, 5)}-${digits.slice(5, 9)}`;
  }
  return digits.length <= 6 ? `${digits.slice(0, 3)}-${digits.slice(3)}` : `${digits.slice(0, 3)}-${digits.slice(3, 6)}-${digits.slice(6)}`;
}

/** The shared partner fields, rendered by the customer and the supplier form. */
export function PartnerFields<TValues extends FieldValues & PartnerValues>({ control }: { control: Control<TValues, unknown, TValues> }) {
  const t = useTranslations("masters");
  const tc = useTranslations("common");
  const name = (field: keyof PartnerValues) => field as FieldPath<TValues>;
  return (
    <>
      <TextField control={control} name={name("code")} label={tc("fields.code")} required maxLength={40} transform={toCode} autoFocus />
      <TextField control={control} name={name("name")} label={tc("fields.name")} required maxLength={200} />
      <TextField control={control} name={name("nameEn")} label={tc("fields.nameEn")} maxLength={200} />
      <div className="grid gap-5 sm:grid-cols-2">
        <TextField control={control} name={name("taxId")} label={t("partners.taxId")} description={t("partners.taxIdHint")} maxLength={20} inputMode="numeric" />
        <TextField control={control} name={name("branchNo")} label={t("partners.branchNo")} description={t("partners.branchNoHint")} maxLength={10} />
      </div>
      <TextareaField control={control} name={name("address")} label={t("partners.address")} maxLength={500} />
      <div className="grid gap-5 sm:grid-cols-2">
        <TextField control={control} name={name("contactName")} label={t("partners.contactName")} maxLength={100} />
        <TextField control={control} name={name("phone")} label={t("partners.phone")} maxLength={50} inputMode="tel" transform={formatThaiPhone} />
        <TextField control={control} name={name("email")} label={t("partners.email")} maxLength={200} type="email" inputMode="email" />
        <IntegerField control={control} name={name("creditDays")} label={t("partners.creditDays")} suffix={tc("units.days")} required />
      </div>
    </>
  );
}
