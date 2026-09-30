import { apiUrl } from "../playwright.config";

/**
 * Fails the run with a clear message when the API cannot serve requests, instead of letting
 * every test time out. `/healthz` alone is not enough: it answers without touching the database.
 */
export default async function globalSetup(): Promise<void> {
  const explain = (problem: string) =>
    new Error(
      `${problem}\nThe e2e tests need the real API at ${apiUrl} (set E2E_API_URL to change it). ` +
        "Start it with its database, then run the tests again.",
    );

  let health: Response;
  try {
    health = await fetch(`${apiUrl}/healthz`, { signal: AbortSignal.timeout(10_000) });
  } catch (error) {
    throw explain(`The API is not reachable: ${error instanceof Error ? error.message : String(error)}.`);
  }
  if (!health.ok) {
    throw explain(`The API health check answered HTTP ${health.status}.`);
  }

  // A sign-in with unknown credentials must be rejected with 401; a 5xx means the database is down.
  const probe = await fetch(`${apiUrl}/api/v1/auth/login`, {
    method: "POST",
    headers: { "content-type": "application/json" },
    body: JSON.stringify({ companyCode: "e2e-probe-unknown", userName: "probe", password: "probe-password-1" }),
    signal: AbortSignal.timeout(15_000),
  });
  if (probe.status >= 500) {
    throw explain(`The API is up but cannot reach its database: sign-in probe answered HTTP ${probe.status}.`);
  }
}
