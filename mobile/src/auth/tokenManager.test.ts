import { createMemorySecretStorage, REFRESH_TOKEN_KEY, type SecretStorage } from "@/storage/secureStorage";

import { TokenManager, type RefreshOutcome } from "./tokenManager";

/** Promise that is resolved by the test. */
function deferred<T>() {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>((done) => {
    resolve = done;
  });
  return { promise, resolve };
}

const ok = (n: number): RefreshOutcome => ({
  ok: true,
  tokens: { accessToken: `access-${n}`, refreshToken: `refresh-${n}`, expiresIn: 900 },
});

describe("token refresh single-flight", () => {
  it("sends one refresh request for concurrent callers", async () => {
    const storage = createMemorySecretStorage();
    await storage.set(REFRESH_TOKEN_KEY, "refresh-0");
    const gate = deferred<RefreshOutcome>();
    const exchange = jest.fn((_: string) => gate.promise);
    const manager = new TokenManager({ storage, exchange });

    const calls = [manager.refresh(), manager.refresh(), manager.getAccessToken(), manager.refresh()];
    gate.resolve(ok(1));

    expect(await Promise.all(calls)).toEqual(["access-1", "access-1", "access-1", "access-1"]);
    expect(exchange).toHaveBeenCalledTimes(1);
    expect(exchange).toHaveBeenCalledWith("refresh-0");
  });

  it("uses the rotated refresh token for the next refresh and never an old one", async () => {
    const storage = createMemorySecretStorage();
    await storage.set(REFRESH_TOKEN_KEY, "refresh-0");
    let n = 0;
    const exchange = jest.fn(async (_: string) => ok((n += 1)));
    const manager = new TokenManager({ storage, exchange });

    await manager.refresh();
    await manager.refresh();
    await manager.refresh();

    expect(exchange.mock.calls.map((call) => call[0])).toEqual(["refresh-0", "refresh-1", "refresh-2"]);
    expect(new Set(exchange.mock.calls.map((call) => call[0])).size).toBe(3);
    expect(await storage.get(REFRESH_TOKEN_KEY)).toBe("refresh-3");
  });

  it("persists the new refresh token before the refresh resolves", async () => {
    const events: string[] = [];
    const inner = createMemorySecretStorage();
    await inner.set(REFRESH_TOKEN_KEY, "refresh-0");
    const writeGate = deferred<void>();
    const storage: SecretStorage = {
      get: (key) => inner.get(key),
      remove: (key) => inner.remove(key),
      set: async (key, value) => {
        events.push(`write-start:${value}`);
        await writeGate.promise;
        await inner.set(key, value);
        events.push(`write-done:${value}`);
      },
    };
    const manager = new TokenManager({ storage, exchange: async () => ok(1) });

    const refreshing = manager.refresh().then((token) => {
      events.push(`resolved:${token}`);
      return token;
    });
    await new Promise((done) => setTimeout(done, 5));

    // The write is still running: no caller has the new access token, and a second call joins.
    expect(events).toEqual(["write-start:refresh-1"]);
    expect(manager.peekAccessToken()).toBeNull();
    const joined = manager.refresh();

    writeGate.resolve();
    expect(await refreshing).toBe("access-1");
    expect(await joined).toBe("access-1");
    expect(events).toEqual(["write-start:refresh-1", "write-done:refresh-1", "resolved:access-1"]);
  });

  it("starts a new request after the previous one finished", async () => {
    const storage = createMemorySecretStorage();
    await storage.set(REFRESH_TOKEN_KEY, "refresh-0");
    let n = 0;
    const exchange = jest.fn(async (_: string) => ok((n += 1)));
    const manager = new TokenManager({ storage, exchange });

    expect(await manager.refresh()).toBe("access-1");
    expect(await manager.refresh()).toBe("access-2");
    expect(exchange).toHaveBeenCalledTimes(2);
  });
});

describe("access token", () => {
  it("returns the token in memory while it is valid and refreshes shortly before expiry", async () => {
    const storage = createMemorySecretStorage();
    let clock = 1_000_000;
    const exchange = jest.fn(async (_: string) => ok(2));
    const manager = new TokenManager({ storage, exchange, now: () => clock, refreshSkewMs: 30_000 });
    await manager.setSession({ accessToken: "access-1", refreshToken: "refresh-1", expiresIn: 900 });

    expect(await manager.getAccessToken()).toBe("access-1");
    clock += 860_000;
    expect(await manager.getAccessToken()).toBe("access-1");
    expect(exchange).not.toHaveBeenCalled();

    clock += 15_000; // 25 s before expiry
    expect(await manager.getAccessToken()).toBe("access-2");
    expect(exchange).toHaveBeenCalledWith("refresh-1");
  });

  it("keeps the session when the API is unreachable", async () => {
    const storage = createMemorySecretStorage();
    let clock = 0;
    const onSessionExpired = jest.fn();
    const manager = new TokenManager({
      storage,
      exchange: async () => ({ ok: false, reason: "unreachable" }),
      onSessionExpired,
      now: () => clock,
    });
    await manager.setSession({ accessToken: "access-1", refreshToken: "refresh-1", expiresIn: 900 });

    clock = 880_000; // inside the refresh window, token not expired yet
    expect(await manager.getAccessToken()).toBe("access-1");
    clock = 901_000; // expired
    expect(await manager.getAccessToken()).toBeNull();
    expect(await storage.get(REFRESH_TOKEN_KEY)).toBe("refresh-1");
    expect(onSessionExpired).not.toHaveBeenCalled();
  });

  it("clears everything and reports once when the API rejects the refresh token", async () => {
    const storage = createMemorySecretStorage();
    const onSessionExpired = jest.fn();
    const manager = new TokenManager({
      storage,
      exchange: async () => ({ ok: false, reason: "rejected" }),
      onSessionExpired,
      now: () => 0,
    });
    await manager.setSession({ accessToken: "access-1", refreshToken: "refresh-1", expiresIn: 900 });

    expect(await Promise.all([manager.refresh(), manager.refresh()])).toEqual([null, null]);
    expect(onSessionExpired).toHaveBeenCalledTimes(1);
    expect(await storage.get(REFRESH_TOKEN_KEY)).toBeNull();
    expect(manager.peekAccessToken()).toBeNull();
  });

  it("does not call the API without a stored refresh token", async () => {
    const exchange = jest.fn(async (_: string) => ok(1));
    const manager = new TokenManager({ storage: createMemorySecretStorage(), exchange });
    expect(await manager.getAccessToken()).toBeNull();
    expect(exchange).not.toHaveBeenCalled();
  });

  it("ignores a refresh that finishes after sign-out", async () => {
    const storage = createMemorySecretStorage();
    const gate = deferred<RefreshOutcome>();
    const manager = new TokenManager({ storage, exchange: () => gate.promise, now: () => 0 });
    await manager.setSession({ accessToken: "access-1", refreshToken: "refresh-1", expiresIn: 900 });

    const refreshing = manager.refresh();
    await Promise.resolve();
    await manager.clear();
    gate.resolve(ok(2));

    expect(await refreshing).toBeNull();
    expect(await storage.get(REFRESH_TOKEN_KEY)).toBeNull();
    expect(manager.peekAccessToken()).toBeNull();
  });

  it("passes the profile of a refresh to the listener", async () => {
    const storage = createMemorySecretStorage();
    await storage.set(REFRESH_TOKEN_KEY, "refresh-0");
    const onRefreshed = jest.fn();
    const manager = new TokenManager<{ name: string }>({
      storage,
      exchange: async () => ({ ...ok(1), extra: { name: "somchai" } }) as RefreshOutcome<{ name: string }>,
      onRefreshed,
    });
    await manager.refresh();
    expect(onRefreshed).toHaveBeenCalledWith({ name: "somchai" });
  });
});
