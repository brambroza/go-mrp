import type { Schemas } from "@mrp/api-client";

import { loginSchema } from "@/lib/auth/schemas";
import { emptyToNull } from "@/lib/validation/zod";
import { signInWith } from "@/server/auth-handlers";

/** Signs in through the API and keeps the refresh token in an `HttpOnly` cookie. */
export function POST(request: Request): Promise<Response> {
  return signInWith(request, "/api/v1/auth/login", loginSchema, (values, device): Schemas["LoginRequest"] => ({
    companyCode: values.companyCode,
    userName: values.userName,
    password: values.password,
    twoFactorCode: emptyToNull(values.twoFactorCode),
    device,
  }));
}
