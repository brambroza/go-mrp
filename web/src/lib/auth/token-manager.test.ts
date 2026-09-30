import { describe, expect, it, vi } from "vitest";

import { createTokenManager, type Session, type UserProfile } from "./token-manager";

const user: UserProfile = {
  id: "00000000-0000-0000-0000-000000000001",
  userName: "owner",
  email: "owner@example.com",
  displayName: "Owner",
  language: "th",
  twoFactorEnabled: false,
  tenantId: "00000000-0000-0000-0000-000000000002",
  tenantName: "Test Factory",
  tenantSlug: "test-factory",
  plan: "Starter",
  timeZone: "Asia/Bangkok",
  roles: ["Owner"],
  permissions: ["*"],
};

function session(accessToken: string, expiresIn = 900): Session {
  return { accessToken, expiresIn, user };
}

/** A refresh function whose calls resolve only when the test says so. */
function deferredRefresh() {
  const pending: { resolve: (value: Session | null) => void; reject: (error: unknown) => void }[] = [];
  const refresh = vi.fn(
    () =>
      new Promise<Session | null>((resolve, reject) => {
        pending.push({ resolve, reject });
      }),
  );
  return { refresh, pending };
}

describe("token manager: single-flight refresh", () => {
  it("runs one refresh for many concurrent callers", async () => {
    const { refresh, pending } = deferredRefresh();
    const manager = createTokenManager({ refresh });

    const calls = Array.from({ length: 10 }, () => manager.refresh());
    expect(refresh).toHaveBeenCalledTimes(1);

    pending[0]?.resolve(session("token-1"));
    await expect(Promise.all(calls)).resolves.toEqual(Array.from({ length: 10 }, () => "token-1"));
    expect(refresh).toHaveBeenCalledTimes(1);
    expect(manager.getSession()?.accessToken).toBe("token-1");
  });

  it("starts a new refresh after the previous one finished", async () => {
    const { refresh, pending } = deferredRefresh();
    const manager = createTokenManager({ refresh });

    const first = manager.refresh();
    pending[0]?.resolve(session("token-1"));
    await first;

    const second = manager.refresh();
    expect(refresh).toHaveBeenCalledTimes(2);
    pending[1]?.resolve(session("token-2"));
    await expect(second).resolves.toBe("token-2");
  });

  it("shares a failure with every waiting caller and recovers afterwards", async () => {
    const { refresh, pending } = deferredRefresh();
    const manager = createTokenManager({ refresh });
    manager.setSession(session("old"));

    const calls = [manager.refresh(), manager.refresh(), manager.refresh()];
    pending[0]?.reject(new Error("API down"));
    const results = await Promise.allSettled(calls);
    expect(results.map((result) => result.status)).toEqual(["rejected", "rejected", "rejected"]);
    expect(refresh).toHaveBeenCalledTimes(1);
    // A temporary failure must not sign the user out.
    expect(manager.getSession()?.accessToken).toBe("old");

    const retry = manager.refresh();
    expect(refresh).toHaveBeenCalledTimes(2);
    pending[1]?.resolve(session("new"));
    await expect(retry).resolves.toBe("new");
  });

  it("clears the session when the refresh token is no longer valid", async () => {
    const manager = createTokenManager({ refresh: () => Promise.resolve(null) });
    manager.setSession(session("old"));
    await expect(manager.refresh()).resolves.toBeNull();
    expect(manager.getSession()).toBeNull();
  });
});

describe("token manager: access token", () => {
  it("returns null when signed out, without refreshing", async () => {
    const refresh = vi.fn(() => Promise.resolve(session("x")));
    const manager = createTokenManager({ refresh });
    await expect(manager.getAccessToken()).resolves.toBeNull();
    expect(refresh).not.toHaveBeenCalled();
  });

  it("returns the stored token while it is fresh", async () => {
    let now = 1_000_000;
    const refresh = vi.fn(() => Promise.resolve(session("token-2")));
    const manager = createTokenManager({ refresh, now: () => now });
    manager.setSession(session("token-1", 900));

    now += 800_000;
    await expect(manager.getAccessToken()).resolves.toBe("token-1");
    expect(refresh).not.toHaveBeenCalled();
  });

  it("refreshes once before the token expires, also for concurrent requests", async () => {
    let now = 1_000_000;
    const { refresh, pending } = deferredRefresh();
    const manager = createTokenManager({ refresh, now: () => now, leewayMs: 30_000 });
    manager.setSession(session("token-1", 900));

    now += 871_000; // 29 s before expiry: inside the leeway
    const requests = [manager.getAccessToken(), manager.getAccessToken(), manager.getAccessToken()];
    expect(refresh).toHaveBeenCalledTimes(1);
    pending[0]?.resolve(session("token-2", 900));
    await expect(Promise.all(requests)).resolves.toEqual(["token-2", "token-2", "token-2"]);

    await expect(manager.getAccessToken()).resolves.toBe("token-2");
    expect(refresh).toHaveBeenCalledTimes(1);
  });

  it("falls back to the old token when the refresh fails for a temporary reason", async () => {
    let now = 0;
    const manager = createTokenManager({ refresh: () => Promise.reject(new Error("offline")), now: () => now });
    manager.setSession(session("token-1", 900));
    now = 899_000;
    await expect(manager.getAccessToken()).resolves.toBe("token-1");
  });
});

describe("token manager: listeners", () => {
  it("notifies on sign-in, profile change, refresh and sign-out", async () => {
    const manager = createTokenManager({ refresh: () => Promise.resolve(session("token-2")) });
    const seen: (string | null)[] = [];
    const unsubscribe = manager.subscribe((next) => seen.push(next ? `${next.accessToken}:${next.user.displayName}` : null));

    manager.setSession(session("token-1"));
    manager.setUser({ ...user, displayName: "Renamed" });
    await manager.refresh();
    manager.setSession(null);
    unsubscribe();
    manager.setSession(session("token-3"));

    expect(seen).toEqual(["token-1:Owner", "token-1:Renamed", "token-2:Owner", null]);
  });

  it("ignores a profile update while signed out", () => {
    const manager = createTokenManager({ refresh: () => Promise.resolve(null) });
    const listener = vi.fn();
    manager.subscribe(listener);
    manager.setUser(user);
    expect(listener).not.toHaveBeenCalled();
    expect(manager.getSession()).toBeNull();
  });
});
