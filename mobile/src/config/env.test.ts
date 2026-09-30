import { isLocalHost, validateBaseUrl } from "./env";

describe("API base URL", () => {
  it("accepts HTTPS for any host", () => {
    expect(validateBaseUrl("https://api.example.co.th")).toEqual({ ok: true, baseUrl: "https://api.example.co.th" });
    expect(validateBaseUrl(" https://api.example.co.th/ ")).toEqual({ ok: true, baseUrl: "https://api.example.co.th" });
  });

  it("accepts HTTP only for local and private hosts", () => {
    for (const url of ["http://localhost:5080", "http://127.0.0.1:5080", "http://10.0.2.2:5080", "http://192.168.1.10:5080", "http://172.20.0.5:5080", "http://dev-mac.local:5080"]) {
      expect(validateBaseUrl(url)).toEqual({ ok: true, baseUrl: url });
    }
  });

  it("rejects HTTP for public hosts", () => {
    for (const url of ["http://api.example.com", "http://8.8.8.8:5080", "http://172.32.0.1", "http://192.169.1.1", "http://localhost.example.com"]) {
      expect(validateBaseUrl(url)).toEqual({ ok: false, error: "insecure" });
    }
  });

  it("rejects missing and malformed values", () => {
    expect(validateBaseUrl(undefined)).toEqual({ ok: false, error: "missing" });
    expect(validateBaseUrl("  ")).toEqual({ ok: false, error: "missing" });
    for (const url of ["api.example.com", "ftp://api.example.com", "https://user:pass@api.example.com", "https://api.example.com/?x=1", "javascript:alert(1)", "https://"]) {
      expect(validateBaseUrl(url)).toEqual({ ok: false, error: "invalid" });
    }
  });

  it("recognises private addresses", () => {
    expect(isLocalHost("192.168.0.1")).toBe(true);
    expect(isLocalHost("172.16.0.1")).toBe(true);
    expect(isLocalHost("172.15.0.1")).toBe(false);
    expect(isLocalHost("example.com")).toBe(false);
  });
});
