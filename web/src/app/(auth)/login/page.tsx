import type { Metadata } from "next";
import { getTranslations } from "next-intl/server";
import { Suspense } from "react";

import { LoginForm } from "@/features/auth/login-form";

/** Page title. */
export async function generateMetadata(): Promise<Metadata> {
  return { title: (await getTranslations("auth"))("login.title") };
}

/** Sign-in page. */
export default function LoginPage() {
  return (
    <Suspense>
      <LoginForm />
    </Suspense>
  );
}
