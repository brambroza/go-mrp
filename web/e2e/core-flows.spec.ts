import { expect, test, type Page } from "@playwright/test";

import { choose, drawer, expectDashboard, expectToast, field, fill, newCompany, openFromMenu, signIn, th, unique } from "./support";

/**
 * The main flows against the real API. The tests share one signed-in page and run in order:
 * the company created by the first test is the one the others work in.
 */
test.describe.configure({ mode: "serial" });

const company = newCompany();
const suffix = unique(6).toUpperCase();
const unit = { code: `KG${suffix}`, name: `กิโลกรัม ${suffix}` };
const item = { code: `RM-${suffix}`, name: `น้ำมันมะพร้าว ${suffix}` };
const role = { name: `หัวหน้าจัดซื้อ ${suffix}` };
const user = { userName: `buyer.${suffix.toLowerCase()}`, displayName: `สมหญิง จัดซื้อ ${suffix}`, email: `buyer-${suffix.toLowerCase()}@e2e.example.com` };

let page: Page;
const browserErrors: string[] = [];

test.beforeAll(async ({ browser }) => {
  page = await browser.newPage();
  page.on("pageerror", (error) => browserErrors.push(`pageerror: ${error.message}`));
  page.on("console", (message) => {
    if (message.type() === "error" && /Content Security Policy|Refused to/i.test(message.text())) {
      browserErrors.push(`csp: ${message.text()}`);
    }
  });
});

test.afterAll(async () => {
  await page.close();
});

test("visitors without a session are sent to the sign-in page", async ({ browser }) => {
  const visitor = await browser.newPage();
  await visitor.goto("/masters/items");
  await expect(visitor).toHaveURL(/\/login\?next=%2Fmasters%2Fitems$/);
  await expect(visitor.getByRole("button", { name: th.auth.login.submit, exact: true })).toBeVisible();
  await visitor.close();
});

test("sign-up creates a company and lands on the dashboard", async () => {
  await page.goto("/signup");
  await fill(page, "companyCode", company.companyCode);
  await fill(page, "companyName", company.companyName);
  await fill(page, "displayName", company.displayName);
  await fill(page, "email", company.email);
  await fill(page, "password", company.password);
  await page.getByRole("radio", { name: /Pro/ }).check();
  await page.getByRole("button", { name: th.auth.signup.submit, exact: true }).click();

  await expectDashboard(page, company);
  await expect(page.getByTestId("plan-badge")).toHaveText("Pro");

  // The tokens must not be readable by scripts: nothing in web storage, refresh cookie is HttpOnly.
  const storage = await page.evaluate(() => ({ local: { ...localStorage }, session: { ...sessionStorage }, cookie: document.cookie }));
  expect(JSON.stringify(storage.local) + JSON.stringify(storage.session)).not.toMatch(/eyJ|refresh/i);
  expect(storage.cookie).not.toContain("mrp_rt");
  const cookies = await page.context().cookies();
  const refresh = cookies.find((cookie) => cookie.name === "mrp_rt");
  expect(refresh, "refresh cookie").toBeDefined();
  expect(refresh?.httpOnly).toBe(true);
  expect(refresh?.sameSite).toBe("Lax");
});

test("the session survives a reload", async () => {
  await page.reload();
  await expectDashboard(page, company);
});

test("sign-out, a failed sign-in and a successful sign-in", async () => {
  await page.getByTestId("user-menu").click();
  await page.getByTestId("sign-out").click();
  await expect(page).toHaveURL(/\/login$/);
  expect((await page.context().cookies()).find((cookie) => cookie.name === "mrp_rt")).toBeUndefined();

  // Signed out means signed out: protected pages redirect again.
  await page.goto("/settings/users");
  await expect(page).toHaveURL(/\/login\?next=%2Fsettings%2Fusers$/);

  await signIn(page, company, "wrong-password-1");
  await expect(page.getByTestId("login-error")).toContainText(th.errors.platform.auth.invalid_credentials);

  await signIn(page, company);
  // `next` brings the user to the page that was asked for.
  await expect(page).toHaveURL(/\/settings\/users$/);
  await expect(page.getByRole("heading", { level: 1, name: th.settings.users.title })).toBeVisible();
});

test("create a unit and an item", async () => {
  await openFromMenu(page, th.nav.items.units, "/masters/units");
  await page.getByTestId("crud-add").click();
  await fill(drawer(page), "code", unit.code.toLowerCase());
  await fill(drawer(page), "name", unit.name);
  await drawer(page).getByRole("button", { name: th.common.actions.save, exact: true }).click();
  await expectToast(page, th.masters.units.saved);
  await expect(page.getByRole("row").filter({ hasText: unit.code })).toContainText(unit.name);

  await openFromMenu(page, th.nav.items.items, "/masters/items");
  await page.getByTestId("crud-add").click();

  // Client-side validation: saving an empty form marks the required fields and sends nothing.
  await drawer(page).getByRole("button", { name: th.common.actions.save, exact: true }).click();
  await expect(field(drawer(page), "code")).toContainText("กรุณากรอกข้อมูล");
  await expect(field(drawer(page), "stockUnitId")).toContainText("กรุณากรอกข้อมูล");

  await fill(drawer(page), "code", item.code);
  await fill(drawer(page), "name", item.name);
  await choose(page, drawer(page), "itemType", th.masters.itemTypes.RawMaterial);
  await choose(page, drawer(page), "stockUnitId", new RegExp(unit.code));
  await fill(drawer(page), "standardCost", "1234.5");
  await fill(drawer(page), "leadTimeDays", "7");
  await drawer(page).getByRole("button", { name: th.common.actions.save, exact: true }).click();
  await expectToast(page, th.masters.items.saved);

  const row = page.getByRole("row").filter({ hasText: item.code });
  await expect(row).toContainText(item.name);
  await expect(row).toContainText(unit.code);
  await expect(row).toContainText("1,234.50");

  // The search box filters on the server.
  await page.getByRole("searchbox").fill(`no-such-item-${suffix}`);
  await expect(page.getByText(th.common.table.emptySearch)).toBeVisible();
  await page.getByRole("searchbox").fill(item.code);
  await expect(page.getByRole("row").filter({ hasText: item.code })).toBeVisible();

  // A duplicate code is rejected by the API and explained in Thai.
  await page.getByTestId("crud-add").click();
  await fill(drawer(page), "code", item.code);
  await fill(drawer(page), "name", "ซ้ำ");
  await choose(page, drawer(page), "stockUnitId", new RegExp(unit.code));
  await drawer(page).getByRole("button", { name: th.common.actions.save, exact: true }).click();
  await expect(page.locator("[data-sonner-toast]").filter({ hasText: th.errors.common.duplicate }).first()).toBeVisible();
  await drawer(page).getByRole("button", { name: th.common.actions.cancel, exact: true }).click();
});

test("create a role and a user", async () => {
  await openFromMenu(page, th.nav.items.roles, "/settings/roles");
  await page.getByTestId("crud-add").click();
  await fill(drawer(page), "name", role.name);
  for (const permission of ["masters.read", "purchasing.read", "purchasing.po.manage"]) {
    await drawer(page).locator(`[data-permission="${permission}"]`).click();
  }
  await drawer(page).getByRole("button", { name: th.common.actions.save, exact: true }).click();
  await expectToast(page, th.settings.roles.saved);
  const roleRow = page.getByRole("row").filter({ hasText: role.name });
  await expect(roleRow).toContainText("3");

  await openFromMenu(page, th.nav.items.users, "/settings/users");
  await page.getByTestId("crud-add").click();
  await fill(drawer(page), "userName", user.userName);
  await fill(drawer(page), "displayName", user.displayName);
  await fill(drawer(page), "email", user.email);
  await fill(drawer(page), "password", "short");
  await drawer(page).getByRole("button", { name: th.common.actions.save, exact: true }).click();
  await expect(field(drawer(page), "password")).toContainText("รหัสผ่านต้องมี 8 ตัวอักษรขึ้นไป");
  await expect(field(drawer(page), "roleIds")).toContainText("กรุณากรอกข้อมูล");

  await fill(drawer(page), "password", `user-Pass-${unique(6)}1a`);
  await field(drawer(page), "roleIds").getByText(role.name, { exact: true }).click();
  await drawer(page).getByRole("button", { name: th.common.actions.save, exact: true }).click();
  await expectToast(page, th.settings.users.saved);

  const userRow = page.getByRole("row").filter({ hasText: user.userName });
  await expect(userRow).toContainText(user.displayName);
  await expect(userRow).toContainText(role.name);
});

test("configure an approval route", async () => {
  await openFromMenu(page, th.nav.items.approvalRoutes, "/settings/approval-routes");
  const row = page.locator('[data-row-id="PO"]');
  await expect(row).toContainText(th.settings.routes.autoApprove);
  await row.click();

  await page.getByTestId("route-add-step").click();
  const step = page.getByTestId("route-step-0");
  await fill(step, "steps.0.name", "หัวหน้าอนุมัติ");
  await choose(page, step, "steps.0.roleId", role.name);
  await fill(step, "steps.0.minAmount", "50000");
  await field(step, "steps.0.requireTwoFactor").getByRole("switch").click();
  await drawer(page).getByRole("button", { name: th.common.actions.save, exact: true }).click();
  await expectToast(page, th.settings.routes.saved);

  await expect(row).toContainText("หัวหน้าอนุมัติ");
  await expect(row).toContainText(role.name);
  await expect(row).toContainText("50,000.00");
  await expect(row).toContainText(th.settings.routes.twoFactorShort);

  // The route is stored on the server, not only shown.
  await page.reload();
  await expect(page.locator('[data-row-id="PO"]')).toContainText("หัวหน้าอนุมัติ");
});

test("the language can be switched to English and back", async () => {
  await page.getByTestId("language-switch").click();
  await page.getByRole("menuitemradio", { name: /English/ }).click();
  await expect(page.getByRole("heading", { level: 1 })).toHaveText("Approval routes");
  await expect(page.locator("html")).toHaveAttribute("lang", "en");

  await page.getByTestId("language-switch").click();
  await page.getByRole("menuitemradio", { name: /ไทย/ }).click();
  await expect(page.getByRole("heading", { level: 1 })).toHaveText(th.settings.routes.title);
});

test("the browser reported no script errors and no CSP violations", () => {
  expect(browserErrors).toEqual([]);
});
