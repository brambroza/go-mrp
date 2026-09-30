import { describe, expect, it } from "vitest";

import { isTrustedOrigin, type OriginCheckInput } from "./origin";

const base: OriginCheckInput = { origin: null, host: "app.example.com", forwardedHost: null, secFetchSite: null, allowed: [] };

describe("isTrustedOrigin", () => {
  it("accepts a request from the same host", () => {
    expect(isTrustedOrigin({ ...base, origin: "https://app.example.com", secFetchSite: "same-origin" })).toBe(true);
    expect(isTrustedOrigin({ ...base, origin: "http://localhost:3100", host: "localhost:3100" })).toBe(true);
  });

  it("rejects other sites", () => {
    expect(isTrustedOrigin({ ...base, origin: "https://evil.example" })).toBe(false);
    expect(isTrustedOrigin({ ...base, origin: "https://app.example.com.evil.example" })).toBe(false);
    expect(isTrustedOrigin({ ...base, origin: "https://evil.example/app.example.com" })).toBe(false);
  });

  it("rejects another port or a sibling subdomain", () => {
    expect(isTrustedOrigin({ ...base, origin: "https://app.example.com:8443" })).toBe(false);
    expect(isTrustedOrigin({ ...base, origin: "https://other.example.com", secFetchSite: "same-site" })).toBe(false);
  });

  it("rejects when the browser says the request is cross-site, whatever Origin claims", () => {
    expect(isTrustedOrigin({ ...base, origin: "https://app.example.com", secFetchSite: "cross-site" })).toBe(false);
    expect(isTrustedOrigin({ ...base, origin: "https://app.example.com", secFetchSite: "same-site" })).toBe(false);
  });

  it("rejects requests without Origin unless the browser marks them same-origin", () => {
    expect(isTrustedOrigin({ ...base })).toBe(false);
    expect(isTrustedOrigin({ ...base, secFetchSite: "none" })).toBe(false);
    expect(isTrustedOrigin({ ...base, secFetchSite: "same-origin" })).toBe(true);
  });

  it("rejects the opaque origin of sandboxed frames and malformed values", () => {
    expect(isTrustedOrigin({ ...base, origin: "null" })).toBe(false);
    expect(isTrustedOrigin({ ...base, origin: "not a url" })).toBe(false);
  });

  it("accepts the host forwarded by the load balancer and configured origins", () => {
    expect(isTrustedOrigin({ ...base, host: "web-abc.a.run.app", forwardedHost: "app.example.com", origin: "https://app.example.com" })).toBe(true);
    expect(isTrustedOrigin({ ...base, host: "internal:3000", origin: "https://app.example.com", allowed: ["https://app.example.com"] })).toBe(true);
  });

  it("compares hosts without regard to case", () => {
    expect(isTrustedOrigin({ ...base, host: "App.Example.com", origin: "https://app.example.com" })).toBe(true);
  });
});
