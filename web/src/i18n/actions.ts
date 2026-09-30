"use server";

import { cookies } from "next/headers";

import { LOCALE_COOKIE, toLocale } from "./config";

/** Stores the visitor's language choice in a cookie; the caller refreshes the page afterwards. */
export async function setLocaleAction(locale: string): Promise<void> {
  (await cookies()).set(LOCALE_COOKIE, toLocale(locale), {
    secure: process.env.COOKIE_SECURE ? process.env.COOKIE_SECURE === "true" : process.env.NODE_ENV === "production",
    sameSite: "lax",
    path: "/",
    maxAge: 365 * 24 * 60 * 60,
  });
}
