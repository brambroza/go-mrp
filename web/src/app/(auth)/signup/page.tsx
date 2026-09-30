import type { Metadata } from "next";
import { getTranslations } from "next-intl/server";

import { SignupForm } from "@/features/auth/signup-form";

/** Page title. */
export async function generateMetadata(): Promise<Metadata> {
  return { title: (await getTranslations("auth"))("signup.title") };
}

/** Sign-up page: creates a company. */
export default function SignupPage() {
  return <SignupForm />;
}
