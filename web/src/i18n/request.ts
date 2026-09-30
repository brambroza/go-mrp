import { cookies } from "next/headers";
import { getRequestConfig } from "next-intl/server";

import { DEFAULT_TIME_ZONE } from "@/lib/format/date";

import { LOCALE_COOKIE, toLocale } from "./config";
import { loadMessages } from "./messages";

/** Resolves locale and messages of a request from the language cookie. */
export default getRequestConfig(async () => {
  const locale = toLocale((await cookies()).get(LOCALE_COOKIE)?.value);
  return {
    locale,
    messages: await loadMessages(locale),
    timeZone: DEFAULT_TIME_ZONE,
  };
});
