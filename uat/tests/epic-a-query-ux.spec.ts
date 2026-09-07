import { expect, test, type Page, type Route } from "@playwright/test";

const admin = {
  userId: 1,
  employeeNo: "A001",
  displayName: "UAT Admin",
  organizationId: 1,
  teamId: 10,
  teamName: "Alpha",
  roles: ["admin"],
  teamScopes: [{ teamId: 10, teamName: "Alpha", isPrimary: true }],
  dataScopes: []
};

const visitTypes = [
  { visitTypeId: 1, visitTypeCode: "VISIT", visitTypeName: "拜訪", description: null, sortOrder: 10, isActive: true },
  { visitTypeId: 2, visitTypeCode: "MEET", visitTypeName: "會議", description: null, sortOrder: 20, isActive: true }
];

function json(route: Route, body: unknown) {
  return route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(body) });
}

async function authenticatedAdmin(page: Page, onApi?: (route: Route, url: URL) => Promise<boolean>) {
  await page.addInitScript(() => {
    sessionStorage.setItem("fieldvisit_uat_token", "test-session");
    sessionStorage.setItem("fieldvisit_active_role", "admin");
  });
  await page.route("**/api/v1/**", async route => {
    const url = new URL(route.request().url());
    if (onApi && await onApi(route, url)) return;
    if (url.pathname.endsWith("/me")) return json(route, admin);
    if (url.pathname.endsWith("/teams")) return json(route, [{ teamId: 10, organizationId: 1, teamCode: "T10", teamName: "Alpha" }]);
    if (url.pathname.endsWith("/query/visitors")) return json(route, []);
    if (url.pathname.endsWith("/projects")) return json(route, []);
    if (url.pathname.endsWith("/visit-types")) return json(route, visitTypes);
    return route.fulfill({ status: 404, contentType: "application/problem+json", body: JSON.stringify({ title: `Unmocked ${url.pathname}` }) });
  });
}

test("automatic query debounces text and ignores a late stale response", async ({ page }) => {
  const requestedKeywords: string[] = [];
  let resolveOldRequest!: () => void;
  const oldRequestStarted = new Promise<void>(resolve => { resolveOldRequest = resolve; });

  await authenticatedAdmin(page, async (route, url) => {
    if (!url.pathname.endsWith("/query/trips")) return false;
    const keyword = url.searchParams.get("keyword") ?? "";
    requestedKeywords.push(keyword);
    if (keyword === "old") {
      resolveOldRequest();
      await new Promise(resolve => setTimeout(resolve, 900));
    }
    if (keyword === "new") await new Promise(resolve => setTimeout(resolve, 10));
    const count = keyword === "new" ? 2 : keyword === "old" ? 1 : 0;
    await json(route, { items: [], page: 1, pageSize: 50, totalCount: count, totalPages: count ? 1 : 0 });
    return true;
  });

  await page.goto("./#/admin/query");
  await expect(page.getByRole("heading", { name: "行程查詢" })).toBeVisible();
  const keyword = page.getByPlaceholder("工號、姓名、地點、專案或行程編號");
  await keyword.fill("old");
  await oldRequestStarted;
  await keyword.fill("new");
  await expect(page.getByText("第 1 / 1 頁，共 2 筆")).toBeVisible();
  await page.waitForTimeout(700);
  await expect(page.getByText("第 1 / 1 頁，共 2 筆")).toBeVisible();
  expect(requestedKeywords.filter(x => x === "old")).toHaveLength(1);
  expect(requestedKeywords.filter(x => x === "new")).toHaveLength(1);

  const beforeShortcut = requestedKeywords.length;
  await page.getByRole("button", { name: "前6個月" }).click();
  await expect.poll(() => requestedKeywords.length).toBeGreaterThan(beforeShortcut);
});

test("project and visit-type menus are separate and arrow order is server checked", async ({ page }) => {
  let moveBody: unknown;
  await authenticatedAdmin(page, async (route, url) => {
    if (!url.pathname.endsWith("/visit-types/1/move")) return false;
    moveBody = route.request().postDataJSON();
    await json(route, [visitTypes[1], { ...visitTypes[0], sortOrder: 20 },].map((x, i) => ({ ...x, sortOrder: (i + 1) * 10 })));
    return true;
  });

  await page.goto("./#/admin/visit-types");
  await expect(page.getByRole("link", { name: /專案管理/ })).toBeVisible();
  await expect(page.getByRole("link", { name: /拜訪形式/ })).toBeVisible();
  await page.getByRole("button", { name: "下移 拜訪" }).click();
  await expect.poll(() => moveBody).toEqual({
    direction: "down",
    expectedOrder: [
      { visitTypeId: 1, sortOrder: 10 },
      { visitTypeId: 2, sortOrder: 20 }
    ]
  });
  const names = await page.locator(".route-name").allTextContents();
  expect(names).toEqual(["會議", "拜訪"]);
});

test("mobile permission modal has an explicit close and releases page scroll", async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await authenticatedAdmin(page, async (route, url) => {
    if (!url.pathname.endsWith("/admin/users/search")) return false;
    await json(route, {
      items: [{ userId: 1, employeeNo: "A001", displayName: "UAT Admin", email: "admin@example.test", isActive: true, roles: ["admin"], teamScopes: [] }],
      page: 1, pageSize: 50, totalCount: 1, totalPages: 1
    });
    return true;
  });

  await page.goto("./#/admin/users");
  await page.getByRole("button", { name: "維護" }).click();
  await expect(page.getByRole("dialog", { name: "人員權限" })).toBeVisible();
  await expect.poll(() => page.evaluate(() => document.body.style.overflow)).toBe("hidden");
  await page.getByRole("button", { name: "關閉人員權限視窗" }).click();
  await expect(page.getByRole("dialog", { name: "人員權限" })).toBeHidden();
  await expect.poll(() => page.evaluate(() => document.body.style.overflow)).toBe("");
});
