/**
 * Opt-in smoke test against a running API (not part of `pnpm test`):
 *
 *   EXPO_PUBLIC_API_BASE_URL=http://localhost:5080 pnpm --filter @mrp/mobile test:live
 *
 * Without credentials only the error paths are checked, which writes nothing to the database.
 * Set MRP_LIVE_COMPANY, MRP_LIVE_USER and MRP_LIVE_PASSWORD (a user of a test tenant) to also
 * check sign-in, token refresh and read-only lists.
 */
import { exchangeRefreshToken, fetchProfile, login, logout } from "@/api/auth";
import { configureApi } from "@/api/client";
import { fetchWarehouses } from "@/api/masters";
import { TokenManager } from "@/auth/tokenManager";
import { AppError } from "@/lib/errors";
import { createMemorySecretStorage } from "@/storage/secureStorage";

const company = process.env.MRP_LIVE_COMPANY;
const user = process.env.MRP_LIVE_USER;
const password = process.env.MRP_LIVE_PASSWORD;
const withCredentials = company && user && password ? describe : describe.skip;

jest.setTimeout(30_000);

describe("live API: error paths", () => {
  it("answers a wrong sign-in with an RFC 7807 problem and a code", async () => {
    const failure = await login({ companyCode: "no-such-company-zz", userName: "nobody", password: "wrong-password-1" }).catch((error: unknown) => error);
    expect(failure).toBeInstanceOf(AppError);
    expect(failure).toMatchObject({ kind: "auth", status: 401, code: "platform.auth.invalid_credentials" });
  });

  it("answers an invalid request with validation errors by field", async () => {
    const failure = (await login({ companyCode: "x", userName: "nobody", password: "p" }).catch((error: unknown) => error)) as AppError;
    expect(failure).toBeInstanceOf(AppError);
    expect(failure.status).toBe(400);
    expect(Object.keys(failure.fieldErrors ?? {})).toContain("companyCode");
  });

  it("treats a refused refresh token as the end of the session", async () => {
    expect(await exchangeRefreshToken("x".repeat(64))).toEqual({ ok: false, reason: "rejected" });
  });

  it("rejects a request without a token", async () => {
    configureApi({ getAccessToken: async () => null, refresh: async () => null });
    await expect(fetchProfile()).rejects.toMatchObject({ kind: "auth", status: 401 });
  });
});

withCredentials("live API: session", () => {
  it("signs in, refreshes with rotation and reads lists", async () => {
    const tokens = await login({ companyCode: company ?? "", userName: user ?? "", password: password ?? "", device: "jest live smoke" });
    expect(tokens.user.permissions.length).toBeGreaterThan(0);

    const manager = new TokenManager({ storage: createMemorySecretStorage(), exchange: exchangeRefreshToken });
    await manager.setSession(tokens);
    configureApi(manager);

    expect((await fetchProfile()).id).toBe(tokens.user.id);
    expect(Array.isArray(await fetchWarehouses())).toBe(true);

    const [first, second] = await Promise.all([manager.refresh(), manager.refresh()]);
    expect(first).toBeTruthy();
    expect(second).toBe(first);
    expect((await fetchProfile()).id).toBe(tokens.user.id);

    const refreshToken = await manager.readRefreshToken();
    expect(refreshToken).not.toBe(tokens.refreshToken);
    await logout(refreshToken ?? "");
    expect(await exchangeRefreshToken(refreshToken ?? "")).toEqual({ ok: false, reason: "rejected" });
  });
});
