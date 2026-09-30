import type account from "@/messages/th/account.json";
import type approvals from "@/messages/th/approvals.json";
import type auth from "@/messages/th/auth.json";
import type common from "@/messages/th/common.json";
import type dashboard from "@/messages/th/dashboard.json";
import type errors from "@/messages/th/errors.json";
import type masters from "@/messages/th/masters.json";
import type nav from "@/messages/th/nav.json";
import type placeholder from "@/messages/th/placeholder.json";
import type settings from "@/messages/th/settings.json";
import type status from "@/messages/th/status.json";
import type validation from "@/messages/th/validation.json";

import type { AppLocale } from "./config";

/** Shape of the messages; the Thai files are the reference, `messages.test.ts` keeps English in sync. */
export interface Messages {
  account: typeof account;
  approvals: typeof approvals;
  auth: typeof auth;
  common: typeof common;
  dashboard: typeof dashboard;
  errors: typeof errors;
  masters: typeof masters;
  nav: typeof nav;
  placeholder: typeof placeholder;
  settings: typeof settings;
  status: typeof status;
  validation: typeof validation;
}

declare module "next-intl" {
  interface AppConfig {
    Locale: AppLocale;
    Messages: Messages;
  }
}
