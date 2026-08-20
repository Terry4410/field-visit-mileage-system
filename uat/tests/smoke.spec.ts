import { expect, test, type Page } from "@playwright/test";

const uatBaseUrl =
  process.env.UAT_BASE_URL ??
  "https://terry4410.github.io/field-visit-mileage-system/";

const apiHealthUrl =
  process.env.UAT_API_HEALTH_URL ??
  "https://api-fieldvisit-uat-cxauf4g8fzdyfsd9.eastasia-01.azurewebsites.net/health";

const demoPassword =
  process.env.UAT_DEMO_PASSWORD ?? "123456";

const roles = [
  {
    account: "visitor01",
    homeTitle: "今日行程",
    pages: ["今日行程", "歷史紀錄"]
  },
  {
    account: "leader01",
    homeTitle: "小組總覽",
    pages: ["小組總覽", "行程審核", "行程查詢", "地點管理"]
  },
  {
    account: "admin01",
    homeTitle: "管理儀表板",
    pages: [
      "管理儀表板",
      "人員與權限",
      "小組與成員",
      "地點主檔",
      "專案與拜訪形式",
      "補助費率",
      "行程查詢",
      "更正管理"
    ]
  },
  {
    account: "gov01",
    homeTitle: "查詢總覽",
    pages: ["查詢總覽", "行程查詢"]
  }
] as const;

function escapeRegex(value: string) {
  return value.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
}

async function login(
  page: Page,
  account: string,
  homeTitle: string
) {
  await page.goto(uatBaseUrl, {
    waitUntil: "domcontentloaded"
  });

  const card = page.locator(".login-card");
  await expect(card).toBeVisible();

  const inputs = card.locator("input");
  await inputs.nth(0).fill(account);
  await card.locator('input[type="password"]').fill(demoPassword);
  await card.getByRole("button", { name: "登入", exact: true }).click();

  await expect(page.locator(".topbar h1")).toHaveText(homeTitle);
  await expect(page.locator(".sidebar-footer")).toContainText("UAT Pilot");
}

test("API health endpoint is healthy", async ({ request }) => {
  const response = await request.get(apiHealthUrl);

  expect(response.ok()).toBeTruthy();

  const body = await response.json();
  expect(body.status).toBe("ok");
  expect(typeof body.version).toBe("string");
  expect(body.version.length).toBeGreaterThan(0);
  expect(typeof body.schema).toBe("string");
  expect(body.schema.length).toBeGreaterThan(0);
});

for (const role of roles) {
  test(`${role.account} can login and open all role pages`, async ({ page }) => {
    const pageErrors: string[] = [];
    const serverErrors: string[] = [];

    page.on("pageerror", error => {
      pageErrors.push(error.message);
    });

    page.on("response", response => {
      if (response.status() >= 500) {
        serverErrors.push(`${response.status()} ${response.url()}`);
      }
    });

    await login(page, role.account, role.homeTitle);

    const nav = page.locator(".sidebar .nav");

    for (const title of role.pages) {
      const link = nav.getByRole("link", {
        name: new RegExp(escapeRegex(title))
      });

      await expect(link).toBeVisible();
      await link.click();
      await expect(page.locator(".topbar h1")).toHaveText(title);
    }

    expect(pageErrors).toEqual([]);
    expect(serverErrors).toEqual([]);
  });
}

test("visitor mobile shell loads correctly", async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await login(page, "visitor01", "今日行程");

  await expect(page.locator(".mobile-tabs")).toBeVisible();
  await expect(
    page.locator(".mobile-tabs").getByRole("link", { name: "首頁" })
  ).toBeVisible();
  await expect(
    page.locator(".mobile-tabs").getByRole("link", { name: "紀錄" })
  ).toBeVisible();
});
