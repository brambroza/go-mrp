import { AppError } from "@/lib/errors";

import { snapshotRequest, unwrap, withAuthRetry, withPlainRequests, withTimeout, type FetchLike } from "./http";

const json = (status: number, body: unknown) => new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });

describe("retry after 401", () => {
  it("sends the body again with the new token", async () => {
    const seen: { authorization: string | null; body: string }[] = [];
    const fetchImpl: FetchLike = async (input, init) => {
      expect(typeof input).toBe("string");
      seen.push({ authorization: new Headers(init?.headers).get("Authorization"), body: String(init?.body) });
      return seen.length === 1 ? json(401, {}) : json(200, { ok: true });
    };
    const refresh = jest.fn(async () => "new-token");
    const wrapped = withAuthRetry(fetchImpl, { refresh });

    const request = new Request("https://api.example.com/api/v1/inventory/documents", {
      method: "POST",
      headers: { Authorization: "Bearer old-token", "Content-Type": "application/json" },
      body: JSON.stringify({ quantity: 2.5 }),
    });
    const response = await wrapped(request);

    expect(response.status).toBe(200);
    expect(refresh).toHaveBeenCalledTimes(1);
    expect(seen).toEqual([
      { authorization: "Bearer old-token", body: '{"quantity":2.5}' },
      { authorization: "Bearer new-token", body: '{"quantity":2.5}' },
    ]);
  });

  it("returns the 401 when the session cannot be refreshed and does not loop", async () => {
    const fetchImpl = jest.fn(async () => json(401, {}));
    const wrapped = withAuthRetry(fetchImpl, { refresh: async () => null });
    expect((await wrapped(new Request("https://api.example.com/x"))).status).toBe(401);
    expect(fetchImpl).toHaveBeenCalledTimes(1);

    const always = jest.fn(async () => json(401, {}));
    const retried = withAuthRetry(always, { refresh: async () => "token" });
    expect((await retried(new Request("https://api.example.com/x"))).status).toBe(401);
    expect(always).toHaveBeenCalledTimes(2);
  });

  it("does not refresh for other statuses", async () => {
    const refresh = jest.fn(async () => "token");
    const wrapped = withAuthRetry(async () => json(422, { code: "inventory.insufficient_stock" }), { refresh });
    expect((await wrapped(new Request("https://api.example.com/x"))).status).toBe(422);
    expect(refresh).not.toHaveBeenCalled();
  });
});

describe("plain requests", () => {
  it("turns a Request object into a URL with plain options", async () => {
    const request = new Request("https://api.example.com/api/v1/approvals/1/reject", {
      method: "POST",
      headers: { Authorization: "Bearer token", "Content-Type": "application/json" },
      body: JSON.stringify({ comment: "ราคาสูงเกินงบ" }),
    });
    const snapshot = await snapshotRequest(request);
    expect(snapshot).toMatchObject({ url: "https://api.example.com/api/v1/approvals/1/reject", method: "POST", body: '{"comment":"ราคาสูงเกินงบ"}' });
    expect(Object.fromEntries(snapshot.headers)).toMatchObject({ authorization: "Bearer token", "content-type": "application/json" });
  });

  it("sends no body for GET and for an empty POST", async () => {
    expect((await snapshotRequest(new Request("https://api.example.com/x"))).body).toBeUndefined();
    expect((await snapshotRequest(new Request("https://api.example.com/x", { method: "POST" }))).body).toBeUndefined();
    expect(await snapshotRequest("https://api.example.com/x", { method: "put", body: "{}" })).toMatchObject({ method: "PUT", body: "{}" });
  });

  it("never hands a Request object to the platform fetch", async () => {
    const fetchImpl = jest.fn(async (_input: RequestInfo | URL, _init?: RequestInit) => json(200, {}));
    await withPlainRequests(fetchImpl)(new Request("https://api.example.com/x", { method: "POST", body: "{}" }));
    expect(fetchImpl).toHaveBeenCalledWith("https://api.example.com/x", expect.objectContaining({ method: "POST", body: "{}" }));
  });
});

describe("timeout", () => {
  it("aborts a request that takes too long", async () => {
    const slow: FetchLike = (_input, init) =>
      new Promise((_resolve, reject) => {
        init?.signal?.addEventListener("abort", () => reject(Object.assign(new Error("Aborted"), { name: "AbortError" })));
      });
    await expect(unwrap(withTimeout(slow, 10)("https://api.example.com/x").then((response) => ({ response, data: {} })))).rejects.toMatchObject({ kind: "timeout" });
  });
});

describe("unwrap", () => {
  it("returns the data of a successful call", async () => {
    await expect(unwrap(Promise.resolve({ data: { id: "1" }, response: json(200, {}) }))).resolves.toEqual({ id: "1" });
  });

  it("throws the problem code, detail and field errors of the API", async () => {
    const problem = { status: 400, code: "inventory.line.invalid_quantity", detail: "Line 1: quantity is too small.", errors: { "lines[0].quantity": ["Invalid"] } };
    const failure = await unwrap(Promise.resolve({ error: problem, response: json(400, problem) })).catch((error: unknown) => error);
    expect(failure).toBeInstanceOf(AppError);
    expect(failure).toMatchObject({ kind: "http", status: 400, code: "inventory.line.invalid_quantity", detail: "Line 1: quantity is too small.", fieldErrors: { "lines[0].quantity": ["Invalid"] } });
  });

  it("classifies 401 as an expired session and a thrown fetch as a network error", async () => {
    await expect(unwrap(Promise.resolve({ error: {}, response: json(401, {}) }))).rejects.toMatchObject({ kind: "auth", status: 401 });
    await expect(unwrap(Promise.reject(new TypeError("Network request failed")))).rejects.toMatchObject({ kind: "network" });
  });

  it("never puts a token into the error", async () => {
    const failure = (await unwrap(Promise.resolve({ error: { code: "x", accessToken: "secret-token" }, response: json(400, {}) })).catch((error: unknown) => error)) as AppError;
    expect(JSON.stringify({ ...failure, message: failure.message })).not.toContain("secret-token");
  });
});
