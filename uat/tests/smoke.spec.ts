import { expect, test, type Page } from "@playwright/test";

const uatBaseUrl =
  process.env.UAT_BASE_URL ??
  "https://terry4410.github.io/field-visit-mileage-system/";

const apiHealthUrl =
  process.env.UAT_API_HEALTH_URL ??
  "https://api-fieldvisit-uat-cxauf4g8fzdyfsd9.eastasia-01.azurewebsites.net/health";

const demoPassword = process.env.UAT_DEMO_PASSWORD ?? "";

const roles = [
  {
    account: "pilotv01",
    role: "外訪員",
    homeTitle: "今日行程",
    pages: ["今日行程", "歷史紀錄"]
  },
  {
    account: "pilotv03",
    role: "外訪員",
    homeTitle: "今日行程",
    pages: ["今日行程", "歷史紀錄"]
  },
  {
    account: "pilotl01",
    role: "小組長",
    homeTitle: "小組總覽",
    pages: ["小組總覽", "行程審核", "行程查詢", "地點管理"]
  },
  {
    account: "pilotl03",
    role: "小組長",
    homeTitle: "小組總覽",
    pages: ["小組總覽", "行程審核", "行程查詢", "地點管理"]
  },
  {
    account: "pilota01",
    role: "管理者",
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
    account: "pilots02",
    role: "督導",
    homeTitle: "查詢總覽",
    pages: ["查詢總覽", "行程查詢"]
  },
  {
    account: "pilots04",
    role: "督導",
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
  if (!demoPassword) {
    throw new Error(
      "UAT_DEMO_PASSWORD is not available for this workflow event."
    );
  }

  await page.goto(uatBaseUrl, {
    waitUntil: "domcontentloaded"
  });

  const card = page.locator(".login-card");
  await expect(card).toBeVisible();

  const inputs = card.locator("input");
  await inputs.nth(0).fill(account);
  await card.locator('input[type="password"]').fill(demoPassword);

  const loginResponsePromise = page.waitForResponse(
    response =>
      response.url().includes("/api/v1/auth/demo-login") &&
      response.request().method() === "POST"
  );

  await card.getByRole("button", { name: "登入", exact: true }).click();
  const loginResponse = await loginResponsePromise;

  if (!loginResponse.ok()) {
    const uiMessage =
      (await card.locator(".danger-note").textContent().catch(() => null))
        ?.trim() || "登入失敗，畫面未提供錯誤文字。";

    throw new Error(
      `Demo login failed for ${account}: HTTP ${loginResponse.status()} - ${uiMessage}`
    );
  }

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
  test(`${role.account} (${role.role}) can login and open all role pages`, async ({ page }) => {
    test.skip(
      !demoPassword,
      "Login smoke is skipped when this workflow event cannot access UAT_DEMO_PASSWORD."
    );

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

test("pilotv01 mobile shell loads correctly", async ({ page }) => {
  test.skip(
    !demoPassword,
    "Mobile login smoke is skipped when this workflow event cannot access UAT_DEMO_PASSWORD."
  );

  await page.setViewportSize({ width: 390, height: 844 });
  await login(page, "pilotv01", "今日行程");

  await expect(page.locator(".mobile-tabs")).toBeVisible();
  await expect(
    page.locator(".mobile-tabs").getByRole("link", { name: "首頁" })
  ).toBeVisible();
  await expect(
    page.locator(".mobile-tabs").getByRole("link", { name: "紀錄" })
  ).toBeVisible();
});
