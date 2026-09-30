import { randomBytes } from "node:crypto";

import { expect, type Locator, type Page } from "@playwright/test";

import account from "../src/messages/th/account.json" with { type: "json" };
import approvals from "../src/messages/th/approvals.json" with { type: "json" };
import auth from "../src/messages/th/auth.json" with { type: "json" };
import common from "../src/messages/th/common.json" with { type: "json" };
import errors from "../src/messages/th/errors.json" with { type: "json" };
import masters from "../src/messages/th/masters.json" with { type: "json" };
import nav from "../src/messages/th/nav.json" with { type: "json" };
import settings from "../src/messages/th/settings.json" with { type: "json" };

/** Thai messages: the tests look for the texts the user sees, read from the same files the app uses. */
export const th = { account, approvals, auth, common, errors, masters, nav, settings };

/** A company that exists only for this test run. */
export interface TestCompany {
  companyCode: string;
  companyName: string;
  displayName: string;
  email: string;
  userName: string;
  password: string;
}

/** Random lower-case suffix, so every run creates its own records. */
export function unique(length = 8): string {
  return randomBytes(length).toString("hex").slice(0, length);
}

/** Creates the data of a fresh company. The owner signs in with the e-mail address as user name. */
export function newCompany(): TestCompany {
  const id = unique(10);
  const email = `owner-${id}@e2e.example.com`;
  return {
    companyCode: `e2e-${id}`,
    companyName: `E2E โรงงานทดสอบ ${id}`,
    displayName: "สมชาย ทดสอบ",
    email,
    userName: email,
    password: `e2e-Pass-${unique(8)}1a`,
  };
}

/** The wrapper of a form field, found by the name of the field in the form. */
export function field(scope: Page | Locator, name: string): Locator {
  return scope.locator(`[data-field="${name}"]`);
}

/** Types into a text, number or password field. */
export async function fill(scope: Page | Locator, name: string, value: string): Promise<void> {
  const input = field(scope, name).locator("input, textarea").first();
  await input.fill(value);
  await input.blur();
}

/** Chooses an option of a drop-down field by its visible text. */
export async function choose(page: Page, scope: Page | Locator, name: string, option: string | RegExp): Promise<void> {
  await field(scope, name).getByRole("combobox").click();
  await page.getByRole("option", { name: option }).click();
}

/** The open drawer or sheet. */
export function drawer(page: Page): Locator {
  return page.getByRole("dialog").last();
}

/** Opens a page through the sidebar, like a user does; keeps the in-memory session. */
export async function openFromMenu(page: Page, label: string, path: string): Promise<void> {
  await page.getByRole("link", { name: label, exact: true }).first().click();
  await expect(page).toHaveURL(new RegExp(`${path.replace(/[/]/g, "\\/")}$`));
  await expect(page.getByRole("heading", { level: 1 })).toBeVisible();
}

/** Waits for the success toast with the given text. */
export async function expectToast(page: Page, text: string): Promise<void> {
  await expect(page.locator("[data-sonner-toast]").filter({ hasText: text }).first()).toBeVisible();
}

/** Signs in through the sign-in form and waits for the dashboard. */
export async function signIn(page: Page, company: TestCompany, password = company.password): Promise<void> {
  await fill(page, "companyCode", company.companyCode);
  await fill(page, "userName", company.userName);
  await fill(page, "password", password);
  await page.getByRole("button", { name: th.auth.login.submit, exact: true }).click();
}

/** Expects the dashboard of the company to be shown. */
export async function expectDashboard(page: Page, company: TestCompany): Promise<void> {
  await expect(page).toHaveURL(/\/$/);
  await expect(page.getByTestId("tenant-name")).toHaveText(company.companyName);
  await expect(page.getByTestId("dashboard-card-pendingApprovals")).toBeVisible();
}
