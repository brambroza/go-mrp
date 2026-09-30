import { defineConfig, devices } from "@playwright/test";

/** Port of the web app under test. 3000 is often taken on developer machines, so e2e uses its own. */
const port = Number(process.env.E2E_WEB_PORT ?? 3100);

/** Base URL of the real API the tests run against. */
export const apiUrl = (process.env.E2E_API_URL ?? process.env.API_BASE_URL ?? "http://localhost:5080").replace(/\/+$/, "");

/** Set `E2E_BASE_URL` to test an already deployed web app instead of starting one. */
const baseURL = process.env.E2E_BASE_URL ?? `http://localhost:${port}`;

export default defineConfig({
  testDir: "./e2e",
  globalSetup: "./e2e/global-setup.ts",
  // One signed-in page walks through the flows in order: sign-in endpoints of the API are rate limited.
  fullyParallel: false,
  workers: 1,
  forbidOnly: Boolean(process.env.CI),
  retries: 0,
  timeout: 60_000,
  expect: { timeout: 10_000 },
  reporter: process.env.CI ? [["github"], ["html", { open: "never" }]] : [["list"]],
  use: {
    baseURL,
    locale: "th-TH",
    timezoneId: "Asia/Bangkok",
    trace: "retain-on-failure",
    screenshot: "only-on-failure",
  },
  projects: [{ name: "chromium", use: { ...devices["Desktop Chrome"] } }],
  webServer: process.env.E2E_BASE_URL
    ? undefined
    : {
        command: "pnpm build && node scripts/start-standalone.mjs",
        url: `${baseURL}/login`,
        timeout: 300_000,
        reuseExistingServer: !process.env.CI,
        env: {
          PORT: String(port),
          HOSTNAME: "127.0.0.1",
          API_BASE_URL: apiUrl,
          NEXT_PUBLIC_API_BASE_URL: "",
        },
      },
});
