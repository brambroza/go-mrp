import type { Schemas } from "@mrp/api-client";

import { signupSchema } from "@/lib/auth/schemas";
import { signInWith } from "@/server/auth-handlers";

/** Creates a company with its owner user and signs the owner in. */
export function POST(request: Request): Promise<Response> {
  return signInWith(request, "/api/v1/auth/signup", signupSchema, (values): Schemas["SignupRequest"] => ({
    companyCode: values.companyCode,
    companyName: values.companyName,
    displayName: values.displayName,
    email: values.email,
    password: values.password,
    plan: values.plan,
  }));
}
