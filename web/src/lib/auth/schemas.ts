import { msg, password, requiredEmail, requiredText, z } from "@/lib/validation/zod";

/** Packages a company can sign up for. */
export const tenantPlans = ["Starter", "Pro", "Enterprise"] as const;

const companyCode = z
  .string()
  .trim()
  .min(1)
  .min(3)
  .max(40)
  .regex(/^[a-zA-Z0-9][a-zA-Z0-9-]*[a-zA-Z0-9]$/, { error: msg("companyCode") });

/** Sign-in form; limits mirror `LoginRequest` of the API. */
export const loginSchema = z.object({
  companyCode,
  userName: requiredText(200),
  password: z.string().min(1).max(100),
  twoFactorCode: z
    .string()
    .trim()
    .max(10)
    .refine((value) => value === "" || /^[0-9A-Za-z-]{6,10}$/.test(value), { error: msg("totp") }),
});

/** Values of the sign-in form. */
export type LoginValues = z.infer<typeof loginSchema>;

/** Sign-up form; limits mirror `SignupRequest` of the API. */
export const signupSchema = z.object({
  companyCode,
  companyName: requiredText(200, 2),
  displayName: requiredText(100),
  email: requiredEmail(200),
  password: password(),
  plan: z.enum(tenantPlans),
});

/** Values of the sign-up form. */
export type SignupValues = z.infer<typeof signupSchema>;

/** TOTP code from an authenticator app. */
export const twoFactorCodeSchema = z.object({
  code: z
    .string()
    .trim()
    .regex(/^\d{6}$/, { error: msg("totp") }),
});

/** Values of the 2FA code form. */
export type TwoFactorCodeValues = z.infer<typeof twoFactorCodeSchema>;
