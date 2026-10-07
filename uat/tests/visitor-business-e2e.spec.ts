import { expect, test, type APIRequestContext, type Page, type Route } from "@playwright/test";
import {
  apiBaseUrl,
  addDays,
  approve,
  assertLeaderTeamScope,
  auth,
  cleanup,
  demoPassword,
  deleteLocation,
  job,
  login,
  loginUi,
  ok,
  query,
  rates,
  slot,
  teamId,
  unique
} from "./uat-test-helpers";

type ProjectRow = {
  projectId: number;
  projectCode: string;
  projectName: string;
};

type VisitTypeRow = {
  visitTypeId: number;
  visitTypeCode: string;
  visitTypeName: string;
};

type UiMasterData = {
  project: ProjectRow;
  visitType: VisitTypeRow;
};

async function createUiMasterData(
  request: APIRequestContext,
  admin: any,
  tid: number,
  visitDate: string,
  label: string
): Promise<UiMasterData> {
  const stamp = Date.now().toString().slice(-9);
  const projectCode = `BUAT-${label}-${stamp}`.slice(0, 40);
  const projectName = unique(`${label}-PROJECT`);
  const visitTypeCode = `B${stamp}`.slice(0, 20);
  const visitTypeName = unique(`${label}-VISIT-TYPE`);

  let response = await request.post(`${apiBaseUrl}/api/v1/projects`, {
    headers: auth(admin.accessToken, "admin"),
    data: {
      teamId: tid,
      projectCode,
      projectName,
      description: projectName,
      locationMode: "SelfMaintained",
      startDate: addDays(visitDate, -1),
      endDate: addDays(visitDate, 1),
      isActive: true
    }
  });
  await ok(response, "create Business UAT UI project");
  const project = (await response.json()) as ProjectRow;

  response = await request.post(`${apiBaseUrl}/api/v1/visit-types`, {
    headers: auth(admin.accessToken, "admin"),
    data: {
      visitTypeCode,
      visitTypeName,
      description: visitTypeName,
      sortOrder: null,
      isActive: true
    }
  });
  await ok(response, "create Business UAT UI visit type");
  const visitType = (await response.json()) as VisitTypeRow;

  return { project, visitType };
}

async function permanentlyDeleteMasterData(
  request: APIRequestContext,
  admin: any,
  master: UiMasterData | null
) {
  if (!master) return;

  let response = await request.get(
    `${apiBaseUrl}/api/v1/admin/projects/${master.project.projectId}/delete-impact`,
    { headers: auth(admin.accessToken, "admin") }
  );
  await ok(response, "project safe-delete impact after Business UAT cleanup");
  const projectImpact = await response.json();
  expect(projectImpact.canDelete, projectImpact.reason ?? "project cannot be safely deleted").toBe(true);

  response = await request.delete(
    `${apiBaseUrl}/api/v1/admin/projects/${master.project.projectId}/permanent`,
    { headers: auth(admin.accessToken, "admin") }
  );
  await ok(response, "permanent-delete Business UAT project");

  response = await request.get(
    `${apiBaseUrl}/api/v1/admin/visit-types/${master.visitType.visitTypeId}/delete-impact`,
    { headers: auth(admin.accessToken, "admin") }
  );
  await ok(response, "visit-type safe-delete impact after Business UAT cleanup");
  const visitTypeImpact = await response.json();
  expect(visitTypeImpact.canDelete, visitTypeImpact.reason ?? "visit type cannot be safely deleted").toBe(true);

  response = await request.delete(
    `${apiBaseUrl}/api/v1/admin/visit-types/${master.visitType.visitTypeId}/permanent`,
    { headers: auth(admin.accessToken, "admin") }
  );
  await ok(response, "permanent-delete Business UAT visit type");
}

async function injectAutomationPurpose(page: Page, purpose: string) {
  expect(purpose.startsWith("UAT-AUTO-")).toBe(true);

  const rewriteTripWrite = async (route: Route) => {
    const request = route.request();
    const path = new URL(request.url()).pathname;
    const method = request.method();
    const isCreate = method === "POST" && /\/api\/v1\/trips$/.test(path);
    const isUpdate = method === "PUT" && /\/api\/v1\/trips\/\d+$/.test(path);

    if (!isCreate && !isUpdate) {
      await route.continue();
      return;
    }

    const body = request.postDataJSON() as Record<string, unknown>;
    await route.continue({
      headers: {
        ...request.headers(),
        "content-type": "application/json"
      },
      postData: JSON.stringify({ ...body, purpose })
    });
  };

  await page.route("**/api/v1/trips", rewriteTripWrite);
  await page.route("**/api/v1/trips/*", rewriteTripWrite);
}

async function fillTripBasics(page: Page, safeSlot: any, purpose: string) {
  const dateInput = page
    .locator(".field")
    .filter({ hasText: "行程日期" })
    .locator('input[type="date"]')
    .first();
  await dateInput.fill(safeSlot.visitDate);

  await page.getByLabel("出發時間－時").selectOption(safeSlot.startTime.slice(0, 2));
  await page.getByLabel("出發時間－分").selectOption(safeSlot.startTime.slice(3, 5));
  await page.getByLabel("結束時間－時").selectOption(safeSlot.endTime.slice(0, 2));
  await page.getByLabel("結束時間－分").selectOption(safeSlot.endTime.slice(3, 5));

  await page
    .locator('textarea[placeholder*="拜訪補充說明"]')
    .fill(purpose);
}

async function addTemporaryStopThroughUi(
  page: Page,
  master: UiMasterData,
  locationName: string,
  address: string,
  stopPurpose: string
) {
  await page.getByRole("button", { name: "＋新增拜訪地點" }).click();
  const modal = page.locator(".stop-editor-modal");
  await expect(modal).toBeVisible();

  const projectSelect = modal.locator("select").nth(0);
  const visitTypeSelect = modal.locator("select").nth(1);

  await expect(projectSelect.locator("option", { hasText: master.project.projectName })).toHaveCount(1);
  await expect(visitTypeSelect.locator("option", { hasText: master.visitType.visitTypeName })).toHaveCount(1);

  await projectSelect.selectOption({ label: master.project.projectName });
  await visitTypeSelect.selectOption({ label: master.visitType.visitTypeName });
  await modal.getByRole("button", { name: "臨時新增地點" }).click();

  await modal.locator('input[placeholder="例如：客戶 D"]').fill(locationName);
  await modal.locator('input[placeholder="請輸入完整地址或 Plus Code"]').fill(address);
  await modal
    .locator('input[placeholder*="例行訪視"]')
    .fill(stopPurpose);

  await modal.getByRole("button", { name: "加入行程" }).click();
  await expect(modal).toBeHidden();

  const routeItem = page.locator(".route-item").filter({ hasText: locationName });
  await expect(routeItem).toBeVisible();
  await expect(routeItem).toContainText(`拜訪形式：${master.visitType.visitTypeName}`);
}

async function calculateRouteFromUi(page: Page) {
  const googleButton = page.getByRole("button", { name: "用 Google Maps API 計算里程" });
  await expect(googleButton).toBeEnabled({ timeout: 15_000 });

  const createResponsePromise = page.waitForResponse(response => {
    const url = new URL(response.url());
    return response.request().method() === "POST" && /\/api\/v1\/trips$/.test(url.pathname);
  });
  const previewResponsePromise = page.waitForResponse(response => {
    const url = new URL(response.url());
    return response.request().method() === "POST" && /\/api\/v1\/trips\/\d+\/route-preview$/.test(url.pathname);
  });

  await googleButton.click();

  const createResponse = await createResponsePromise;
  const previewResponse = await previewResponsePromise;
  expect(createResponse.ok(), await createResponse.text()).toBe(true);
  expect(previewResponse.ok(), await previewResponse.text()).toBe(true);

  return {
    trip: await createResponse.json(),
    preview: await previewResponse.json()
  };
}

async function submitFromUi(page: Page, tripId: number) {
  const sendButton = page.locator(".bottom-actions").getByRole("button", { name: /送出$/ });
  await sendButton.click();
  await expect(page.getByRole("heading", { name: "確認送出" })).toBeVisible();

  const updateResponsePromise = page.waitForResponse(response => {
    const url = new URL(response.url());
    return response.request().method() === "PUT" && url.pathname.endsWith(`/api/v1/trips/${tripId}`);
  });
  const submitResponsePromise = page.waitForResponse(response => {
    const url = new URL(response.url());
    return response.request().method() === "POST" && url.pathname.endsWith(`/api/v1/trips/${tripId}/submit`);
  });

  await page.getByRole("button", { name: "確認送出", exact: true }).click();

  const updateResponse = await updateResponsePromise;
  const submitResponse = await submitResponsePromise;
  expect(updateResponse.ok(), await updateResponse.text()).toBe(true);
  expect(submitResponse.ok(), await submitResponse.text()).toBe(true);

  const submitted = await submitResponse.json();
  expect(submitted.status).toBe("Submitted");
  await expect(page.getByText(/已送出 T\d+/)).toBeVisible();
  return submitted;
}

test("BUAT-VISITOR-E2E-01 visitor UI creates one-stop project visit, gets Google mileage, submits, approves and snapshots", async ({ page, request }) => {
  test.setTimeout(150_000);
  if (!demoPassword) throw new Error("UAT_DEMO_PASSWORD is required for Business UAT Visitor E2E.");

  const visitor = await login(request, "pilotv01");
  const leader = await login(request, "pilotl01");
  const admin = await login(request, "pilota01");
  const supervisor = await login(request, "pilots02");
  const tid = await teamId(visitor);
  assertLeaderTeamScope(leader, tid);
  const safeSlot = await slot(request, visitor, await rates(request, visitor.accessToken, "visitor"));
  const purpose = unique("BUAT-VISITOR-GOOGLE");
  const locationName = unique("BUAT-GOOGLE-STOP");

  let master: UiMasterData | null = null;
  let tripId: number | null = null;
  let temporaryLocationId: number | null = null;
  let backgroundJobId: string | null = null;

  try {
    master = await createUiMasterData(request, admin, tid, safeSlot.visitDate, "VG");
    await injectAutomationPurpose(page, purpose);
    await loginUi(page, "pilotv01", "今日行程");
    await fillTripBasics(page, safeSlot, purpose);

    await addTemporaryStopThroughUi(
      page,
      master,
      locationName,
      "台北市信義區市府路45號",
      "Business UAT Google Maps one-stop"
    );

    const { trip, preview } = await calculateRouteFromUi(page);
    tripId = trip.visitTripId;
    temporaryLocationId = trip.stops[0]?.locationId ?? null;
    expect(temporaryLocationId).not.toBeNull();
    expect(trip.purpose).toBe(purpose);
    expect(trip.stops).toHaveLength(1);
    expect(trip.stops[0].projectId).toBe(master.project.projectId);
    expect(trip.stops[0].visitTypeId).toBe(master.visitType.visitTypeId);

    expect(preview.status, preview.errorMessage ?? preview.errorCode ?? "Google route preview failed").toBe("Succeeded");
    expect(Number(preview.suggestedDistanceKm)).toBeGreaterThan(0);
    await expect(page.locator(".ok-note").filter({ hasText: "Google Maps API 里程" })).toBeVisible();
    await expect(page.locator('input[placeholder="Google 無可用結果時才開放"]')).toBeDisabled();

    const submitted = await submitFromUi(page, tripId);
    expect(submitted.purpose).toBe(purpose);
    expect(submitted.stops).toHaveLength(1);

    backgroundJobId = await job(request, leader, tripId);
    const approved = await approve(request, leader, tripId);
    expect(approved.status).toBe("Approved");
    expect(approved.approvedDistanceKm).toBeGreaterThan(0);
    expect(approved.ratePerKmSnapshot).not.toBeNull();
    expect(approved.approvedAmount).not.toBeNull();

    const adminRows = await query(request, admin.accessToken, "admin", {
      startDate: safeSlot.visitDate,
      endDate: safeSlot.visitDate,
      keyword: purpose,
      status: "Approved"
    });
    const adminRow = adminRows.items.find((x: any) => x.visitTripId === tripId);
    expect(adminRow).toBeTruthy();
    expect(adminRow.isSnapshot).toBe(true);
    expect(adminRow.snapshotVersion).toBeGreaterThanOrEqual(1);
    expect(adminRow.projectNames).toContain(master.project.projectName);

    const supervisorRows = await query(request, supervisor.accessToken, "supervisor", {
      startDate: safeSlot.visitDate,
      endDate: safeSlot.visitDate,
      keyword: purpose,
      status: "Approved"
    });
    expect(supervisorRows.items.some((x: any) => x.visitTripId === tripId)).toBe(true);
  } finally {
    if (tripId !== null) await cleanup(request, admin, tripId, purpose, backgroundJobId);
    if (temporaryLocationId !== null) await deleteLocation(request, admin, temporaryLocationId);
    await permanentlyDeleteMasterData(request, admin, master);
  }
});

test("BUAT-VISITOR-E2E-02 visitor UI exposes manual fallback after Google failure and preserves fallback through approval", async ({ page, request }) => {
  test.setTimeout(150_000);
  if (!demoPassword) throw new Error("UAT_DEMO_PASSWORD is required for Business UAT Visitor E2E.");

  const visitor = await login(request, "pilotv01");
  const leader = await login(request, "pilotl01");
  const admin = await login(request, "pilota01");
  const tid = await teamId(visitor);
  assertLeaderTeamScope(leader, tid);
  const safeSlot = await slot(request, visitor, await rates(request, visitor.accessToken, "visitor"));
  const purpose = unique("BUAT-VISITOR-MANUAL");
  const locationName = unique("BUAT-MANUAL-STOP");

  let master: UiMasterData | null = null;
  let tripId: number | null = null;
  let temporaryLocationId: number | null = null;
  let backgroundJobId: string | null = null;

  try {
    master = await createUiMasterData(request, admin, tid, safeSlot.visitDate, "VM");
    await injectAutomationPurpose(page, purpose);
    await loginUi(page, "pilotv01", "今日行程");
    await fillTripBasics(page, safeSlot, purpose);

    await addTemporaryStopThroughUi(
      page,
      master,
      locationName,
      `UAT-AUTO-NON-GEOCODABLE-${Date.now()}`,
      "Business UAT manual fallback"
    );

    const { trip, preview } = await calculateRouteFromUi(page);
    tripId = trip.visitTripId;
    temporaryLocationId = trip.stops[0]?.locationId ?? null;
    expect(temporaryLocationId).not.toBeNull();
    expect(trip.purpose).toBe(purpose);
    expect(preview.status).not.toBe("Succeeded");

    await expect(page.locator(".danger-note").filter({ hasText: "Google Maps API 未取得可用里程" })).toBeVisible();
    const manualInput = page.locator('input[placeholder="請輸入人工備援里程"]');
    await expect(manualInput).toBeEnabled();
    await manualInput.fill("12.3");

    const submitted = await submitFromUi(page, tripId);
    expect(submitted.claimedDistanceKm).toBe(12.3);

    backgroundJobId = await job(request, leader, tripId);

    let response = await request.get(`${apiBaseUrl}/api/v1/trips/${tripId}`, {
      headers: auth(leader.accessToken, "leader")
    });
    await ok(response, "load manual-fallback trip for approval");
    const pending = await response.json();
    expect(pending.status).toBe("PendingApproval");
    expect(pending.claimedDistanceKm).toBe(12.3);
    expect(pending.mileageSource).toBe("ManualFallback");

    response = await request.post(`${apiBaseUrl}/api/v1/trips/${tripId}/approve`, {
      headers: auth(leader.accessToken, "leader"),
      data: {
        approvedDistanceKm: 12.3,
        rowVersion: pending.rowVersion,
        distanceDecisionSource: "ManualFallback",
        routeCalculationAttemptId: null,
        comments: "Business UAT manual fallback approval"
      }
    });
    await ok(response, "approve manual-fallback Business UAT trip");
    const approved = await response.json();
    expect(approved.status).toBe("Approved");
    expect(approved.approvedDistanceKm).toBe(12.3);
    expect(approved.ratePerKmSnapshot).not.toBeNull();
    expect(approved.approvedAmount).not.toBeNull();

    const rows = await query(request, admin.accessToken, "admin", {
      startDate: safeSlot.visitDate,
      endDate: safeSlot.visitDate,
      keyword: purpose,
      status: "Approved"
    });
    const row = rows.items.find((x: any) => x.visitTripId === tripId);
    expect(row).toBeTruthy();
    expect(row.mileageSource).toBe("ManualFallback");
    expect(row.approvedDistanceKm).toBe(12.3);
    expect(row.snapshotVersion).toBeGreaterThanOrEqual(1);
  } finally {
    if (tripId !== null) await cleanup(request, admin, tripId, purpose, backgroundJobId);
    if (temporaryLocationId !== null) await deleteLocation(request, admin, temporaryLocationId);
    await permanentlyDeleteMasterData(request, admin, master);
  }
});
