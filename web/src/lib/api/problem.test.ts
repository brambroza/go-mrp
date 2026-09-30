import { describe, expect, it, vi } from "vitest";

import {
  ApiError,
  NETWORK_ERROR_CODE,
  VALIDATION_ERROR_CODE,
  applyProblemToForm,
  normalizeError,
  problemFieldErrors,
  problemMessage,
  toApiError,
  toFormPath,
  unwrap,
} from "./problem";

const translations: Record<string, string> = {
  "inventory.insufficient_stock": "สต็อกไม่พอ",
  "http.403": "ไม่มีสิทธิ์",
  "http.500": "ระบบขัดข้อง",
  "http.404": "ไม่พบข้อมูล",
};
const translate = (code: string) => translations[code];

function response(status: number): Response {
  return new Response(null, { status });
}

describe("toFormPath", () => {
  it("converts API paths to react-hook-form paths", () => {
    expect(toFormPath("code")).toBe("code");
    expect(toFormPath("steps[0].name")).toBe("steps.0.name");
    expect(toFormPath("lines[12].unitPrice")).toBe("lines.12.unitPrice");
    expect(toFormPath("$.Lines[2].Quantity")).toBe("lines.2.quantity");
    expect(toFormPath("$.steps[0]")).toBe("steps.0");
    expect(toFormPath("CompanyCode")).toBe("companyCode");
    expect(toFormPath("$")).toBe("");
  });
});

describe("problemFieldErrors", () => {
  it("takes the first message of every field", () => {
    expect(
      problemFieldErrors({
        errors: {
          companyCode: ["Too short.", "Wrong format."],
          "steps[1].roleId": ["Unknown role."],
          empty: [],
        },
      }),
    ).toEqual({ companyCode: "Too short.", "steps.1.roleId": "Unknown role." });
  });

  it("returns nothing for problems without field errors", () => {
    expect(problemFieldErrors({ code: "common.not_found" })).toEqual({});
    expect(problemFieldErrors({})).toEqual({});
  });

  it("survives a malformed errors object", () => {
    expect(problemFieldErrors({ errors: { code: "not an array" as unknown as string[] } })).toEqual({});
  });
});

describe("applyProblemToForm", () => {
  const values = { code: "", name: "", nameEn: undefined, steps: [{ name: "", roleId: "" }] };

  it("sets the errors on the matching fields and focuses the first one", () => {
    const setError = vi.fn();
    const unmatched = applyProblemToForm(
      { setError, getValues: () => values },
      { errors: { code: ["Code is taken."], "steps[0].roleId": ["Unknown role."] } },
    );
    expect(unmatched).toEqual([]);
    expect(setError).toHaveBeenCalledTimes(2);
    expect(setError).toHaveBeenNthCalledWith(1, "code", { type: "server", message: "Code is taken." }, { shouldFocus: true });
    expect(setError).toHaveBeenNthCalledWith(2, "steps.0.roleId", { type: "server", message: "Unknown role." }, { shouldFocus: false });
  });

  it("matches fields whose value is undefined", () => {
    const setError = vi.fn();
    applyProblemToForm({ setError, getValues: () => values }, { errors: { NameEn: ["Too long."] } });
    expect(setError).toHaveBeenCalledWith("nameEn", { type: "server", message: "Too long." }, { shouldFocus: true });
  });

  it("returns messages of fields the form does not have", () => {
    const setError = vi.fn();
    const unmatched = applyProblemToForm(
      { setError, getValues: () => values },
      { errors: { body: ["Send 1-50 settings."], "steps[3].name": ["Required."], "code.inner": ["Nope."] } },
    );
    expect(unmatched).toEqual(["Send 1-50 settings.", "Required.", "Nope."]);
    expect(setError).not.toHaveBeenCalled();
  });

  it("does nothing for a problem without field errors", () => {
    const setError = vi.fn();
    expect(applyProblemToForm({ setError, getValues: () => values }, { code: "common.duplicate" })).toEqual([]);
    expect(setError).not.toHaveBeenCalled();
  });
});

describe("problemMessage", () => {
  it("prefers the translation of the code", () => {
    const error = new ApiError(409, { code: "inventory.insufficient_stock", detail: "Not enough stock for item X." });
    expect(problemMessage(error, translate, "fallback")).toBe("สต็อกไม่พอ");
  });

  it("falls back to the detail of the API for unknown codes", () => {
    const error = new ApiError(409, { code: "purchasing.something_new", detail: "Explained by the API." });
    expect(problemMessage(error, translate, "fallback")).toBe("Explained by the API.");
  });

  it("uses the status translation for 403 and server errors even when there is a detail", () => {
    expect(problemMessage(new ApiError(403, { title: "Forbidden", detail: "Missing permission masters.manage" }), translate, "fallback")).toBe("ไม่มีสิทธิ์");
    expect(problemMessage(new ApiError(500, { detail: "NullReferenceException at ..." }), translate, "fallback")).toBe("ระบบขัดข้อง");
  });

  it("uses the status translation when the API sent no detail", () => {
    expect(problemMessage(new ApiError(404, { title: "Not Found" }), translate, "fallback")).toBe("ไม่พบข้อมูล");
  });

  it("does not show a title that merely repeats the code", () => {
    const error = new ApiError(418, { code: "x.unknown", title: "x.unknown" });
    expect(problemMessage(error, translate, "fallback")).toBe("fallback");
  });

  it("uses the generic fallback when nothing else is known", () => {
    expect(problemMessage(new ApiError(418, {}), translate, "fallback")).toBe("fallback");
  });
});

describe("toApiError / normalizeError / unwrap", () => {
  it("marks validation problems, which the API sends without a code", () => {
    const error = toApiError({ status: 400, errors: { code: ["Required."] } }, 400);
    expect(error.code).toBe(VALIDATION_ERROR_CODE);
    expect(error.status).toBe(400);
  });

  it("keeps the code of the API", () => {
    expect(toApiError({ code: "common.duplicate", errors: { code: ["Taken."] } }, 400).code).toBe("common.duplicate");
  });

  it("tolerates bodies that are not objects", () => {
    expect(toApiError("Bad Gateway", 502).problem).toEqual({ status: 502 });
    expect(toApiError(undefined, 500).status).toBe(500);
  });

  it("turns thrown errors into network problems", () => {
    const error = normalizeError(new TypeError("Failed to fetch"));
    expect(error.status).toBe(0);
    expect(error.code).toBe(NETWORK_ERROR_CODE);
    const same = new ApiError(404, {});
    expect(normalizeError(same)).toBe(same);
  });

  it("returns the data of a successful call", async () => {
    await expect(unwrap(Promise.resolve({ data: { id: "1" }, response: response(200) }))).resolves.toEqual({ id: "1" });
    await expect(unwrap(Promise.resolve({ data: undefined, response: response(204) }))).resolves.toBeUndefined();
  });

  it("throws an ApiError for a failed call", async () => {
    const call = Promise.resolve({ error: { code: "common.not_found", detail: "Unit not found." }, response: response(404) });
    await expect(unwrap(call)).rejects.toMatchObject({ name: "ApiError", status: 404, code: "common.not_found" });
  });

  it("throws an ApiError when fetch itself fails", async () => {
    await expect(unwrap(Promise.reject(new TypeError("Failed to fetch")))).rejects.toMatchObject({ status: 0, code: NETWORK_ERROR_CODE });
  });
});
