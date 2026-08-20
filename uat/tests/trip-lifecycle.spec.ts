import {
  expect,
  test,
  type APIRequestContext,
  type APIResponse
} from "@playwright/test";

const apiBaseUrl =
  process.env.UAT_API_BASE_URL ??
  "https://api-fieldvisit-uat-cxauf4g8fzdyfsd9.eastasia-01.azurewebsites.net";

const demoPassword = process.env.UAT_DEMO_PASSWORD ?? "";

type LoginUser = {
  userId: number;
  teamId: number | null;
  roles: string[];
  teamScopes?: Array<{ teamId: number; isPrimary: boolean }> | null;
};

type LoginResponse = {
  accessToken: string;
  user: LoginUser;
};

type LocationDto = {
  locationId: number;
  teamId: number | null;
  locationName: string;
  address: string | null;
  approvalStatus: string;
  isActive: boolean;
};

type MileageRateDto = {
  mileageRateRuleId: number;
  vehicleType: string;
  ratePerKm: number;
  effectiveFrom: string;
  effectiveTo: string | null;
  isActive: boolean;
};

type TripDto = {
  visitTripId: number;
  tripNo: string;
  userId: number;
  teamId: number | null;
  visitDate: string;
  status: string;
  purpose: string | null;
  claimedDistanceKm: number | null;
  systemDistanceKm: number | null;
  approvedDistanceKm: number | null;
  ratePerKmSnapshot: number | null;
  approvedAmount: number | null;
  rowVersion: string;
};

type BackgroundJobDto = {
  backgroundJobId: string;
  status: string;
  totalCount: number;
  successCount: number;
  failedCount: number;
  skippedCount: number;
  errorMessage: string | null;
};

type TripQueryRow = {
  visitTripId: number;
  tripNo: string;
  status: string;
  snapshotVersion: number;
  isSnapshot: boolean;
  systemDistanceKm: number | null;
  approvedDistanceKm: number | null;
  ratePerKmSnapshot: number | null;
  subsidyAmount: number | null;
};

type PagedTripResult = {
  items: TripQueryRow[];
  totalCount: number;
};

function authHeaders(token: string, role?: string): Record<string, string> {
  return {
    Authorization: `Bearer ${token}`,
    ...(role ? { "X-Active-Role": role } : {})
  };
}

async function ensureOk(response: APIResponse, label: string) {
  if (response.ok()) return;
  throw new Error(
    `${label} failed: HTTP ${response.status()} - ${await response.text()}`
  );
}

async function login(
  request: APIRequestContext,
  account: string
): Promise<LoginResponse> {
  const response = await request.post(
    `${apiBaseUrl}/api/v1/auth/demo-login`,
    { data: { account, password: demoPassword } }
  );
  await ensureOk(response, `login ${account}`);
  return await response.json();
}

function taipeiToday(): string {
  const parts = new Intl.DateTimeFormat("en-US", {
    timeZone: "Asia/Taipei",
    year: "numeric",
    month: "2-digit",
    day: "2-digit"
  }).formatToParts(new Date());

  const year = parts.find(x => x.type === "year")!.value;
  const month = parts.find(x => x.type === "month")!.value;
  const day = parts.find(x => x.type === "day")!.value;
  return `${year}-${month}-${day}`;
}

function addDays(isoDate: string, days: number): string {
  const date = new Date(`${isoDate}T00:00:00Z`);
  date.setUTCDate(date.getUTCDate() + days);
  return date.toISOString().slice(0, 10);
}

function laterDate(a: string, b: string): string {
  return a > b ? a : b;
}

async function chooseFreeRatedSlot(
  request: APIRequestContext,
  visitorToken: string,
  rates: MileageRateDto[]
): Promise<{ visitDate: string; startTime: string; endTime: string }> {
  const today = taipeiToday();
  const preferredStart = addDays(today, 14);
  const activeRates = rates
    .filter(
      x =>
        x.isActive &&
        x.vehicleType.toLowerCase() === "motorcycle" &&
        (!x.effectiveTo || x.effectiveTo >= today)
    )
    .sort((a, b) => b.effectiveFrom.localeCompare(a.effectiveFrom));

  const slots = [
    ["08:10:00", "08:40:00"],
    ["09:10:00", "09:40:00"],
    ["10:10:00", "10:40:00"],
    ["13:10:00", "13:40:00"],
    ["14:10:00", "14:40:00"],
    ["15:10:00", "15:40:00"]
  ] as const;

  for (const rate of activeRates) {
    const firstDate = laterDate(preferredStart, rate.effectiveFrom);

    for (let dayOffset = 0; dayOffset < 45; dayOffset++) {
      const visitDate = addDays(firstDate, dayOffset);
      if (rate.effectiveTo && visitDate > rate.effectiveTo) break;

      for (const [startTime, endTime] of slots) {
        const overlap = await request.post(
          `${apiBaseUrl}/api/v1/trips/time-overlap-check`,
          {
            headers: authHeaders(visitorToken, "visitor"),
            data: {
              visitDate,
              startTime,
              endTime,
              excludeVisitTripId: null
            }
          }
        );
        await ensureOk(overlap, "time overlap check");
        const body = await overlap.json();
        if (!body.hasOverlap) return { visitDate, startTime, endTime };
      }
    }
  }

  throw new Error(
    "No active Motorcycle mileage-rate date with a free UAT time slot was found."
  );
}

async function waitForMileageJob(
  request: APIRequestContext,
  leaderToken: string,
  jobId: string
): Promise<BackgroundJobDto> {
  const terminal = new Set(["Succeeded", "PartiallySucceeded", "Failed"]);

  for (let attempt = 0; attempt < 60; attempt++) {
    const response = await request.get(
      `${apiBaseUrl}/api/v1/jobs/${jobId}`,
      { headers: authHeaders(leaderToken, "leader") }
    );
    await ensureOk(response, "poll mileage job");
    const job = (await response.json()) as BackgroundJobDto;
    if (terminal.has(job.status)) return job;
    await new Promise(resolve => setTimeout(resolve, 500));
  }

  throw new Error(`Mileage job ${jobId} did not finish within 30 seconds.`);
}

async function queryTrip(
  request: APIRequestContext,
  token: string,
  role: "admin" | "supervisor",
  visitDate: string,
  tripId: number
): Promise<TripQueryRow> {
  const response = await request.get(
    `${apiBaseUrl}/api/v1/query/trips?startDate=${visitDate}&endDate=${visitDate}&page=1&pageSize=100&sort=date_desc`,
    { headers: authHeaders(token, role) }
  );
  await ensureOk(response, `${role} trip query`);
  const result = (await response.json()) as PagedTripResult;
  const row = result.items.find(x => x.visitTripId === tripId);
  if (!row) throw new Error(`${role} query cannot find automated trip ${tripId}.`);
  return row;
}

test("trip lifecycle: create -> submit -> mileage -> approve -> snapshot -> query -> cleanup", async ({ request }) => {
  test.setTimeout(90_000);

  if (!demoPassword) {
    throw new Error("UAT_DEMO_PASSWORD is required for Phase 2 lifecycle UAT.");
  }

  const visitor = await login(request, "visitor01");
  const leader = await login(request, "leader01");
  const admin = await login(request, "admin01");
  const supervisor = await login(request, "gov01");

  const visitorTeamId =
    visitor.user.teamScopes?.find(x => x.isPrimary)?.teamId ??
    visitor.user.teamId;

  if (!visitorTeamId) {
    throw new Error("visitor01 has no primary/active team for lifecycle UAT.");
  }

  const locationsResponse = await request.get(
    `${apiBaseUrl}/api/v1/locations`,
    { headers: authHeaders(visitor.accessToken, "visitor") }
  );
  await ensureOk(locationsResponse, "load visitor locations");
  const locations = (await locationsResponse.json()) as LocationDto[];
  const usableLocations = locations.filter(
    x =>
      x.isActive &&
      x.approvalStatus === "Approved" &&
      (x.teamId === null || x.teamId === visitorTeamId)
  );

  expect(
    usableLocations.length,
    "Phase 2 requires at least two approved active locations in visitor01 scope."
  ).toBeGreaterThanOrEqual(2);

  const ratesResponse = await request.get(
    `${apiBaseUrl}/api/v1/mileage-rate-rules`,
    { headers: authHeaders(visitor.accessToken, "visitor") }
  );
  await ensureOk(ratesResponse, "load mileage rates");
  const rates = (await ratesResponse.json()) as MileageRateDto[];
  const slot = await chooseFreeRatedSlot(
    request,
    visitor.accessToken,
    rates
  );

  const purpose = `UAT-AUTO-${Date.now()}-${Math.random()
    .toString(36)
    .slice(2, 8)}`;

  let tripId: number | null = null;
  let mileageJobId: string | null = null;

  try {
    const createResponse = await request.post(
      `${apiBaseUrl}/api/v1/trips`,
      {
        headers: authHeaders(visitor.accessToken, "visitor"),
        data: {
          visitDate: slot.visitDate,
          startTime: slot.startTime,
          endTime: slot.endTime,
          claimedDistanceKm: 10,
          purpose,
          notes: "Automated Phase 2 lifecycle UAT",
          timeOverlapConfirmed: false,
          teamId: visitorTeamId,
          stops: usableLocations.slice(0, 2).map(location => ({
            locationId: location.locationId,
            projectId: null,
            visitTypeId: null,
            sourceType: "Master",
            locationName: location.locationName,
            address: location.address,
            visitPurpose: "Automated UAT visit",
            notes: null
          }))
        }
      }
    );
    await ensureOk(createResponse, "create automated trip");
    const created = (await createResponse.json()) as TripDto;
    tripId = created.visitTripId;

    expect(created.status).toBe("Draft");
    expect(created.purpose).toBe(purpose);
    expect(created.claimedDistanceKm).toBe(10);

    const submitResponse = await request.post(
      `${apiBaseUrl}/api/v1/trips/${tripId}/submit`,
      {
        headers: {
          ...authHeaders(visitor.accessToken, "visitor"),
          "If-Match": created.rowVersion
        },
        data: { confirmTimeOverlap: false }
      }
    );
    await ensureOk(submitResponse, "submit automated trip");
    const submitted = (await submitResponse.json()) as TripDto;
    expect(submitted.status).toBe("Submitted");

    const enqueueResponse = await request.post(
      `${apiBaseUrl}/api/v1/jobs/mileage`,
      {
        headers: authHeaders(leader.accessToken, "leader"),
        data: {
          mode: "Selected",
          startDate: null,
          endDate: null,
          selectedTripIds: [tripId]
        }
      }
    );
    await ensureOk(enqueueResponse, "enqueue selected mileage job");
    const enqueued = (await enqueueResponse.json()) as BackgroundJobDto;
    mileageJobId = enqueued.backgroundJobId;

    const completedJob = await waitForMileageJob(
      request,
      leader.accessToken,
      mileageJobId
    );
    expect(completedJob.status).toBe("Succeeded");
    expect(completedJob.totalCount).toBe(1);
    expect(completedJob.successCount).toBe(1);
    expect(completedJob.failedCount).toBe(0);

    const pendingResponse = await request.get(
      `${apiBaseUrl}/api/v1/trips/${tripId}`,
      { headers: authHeaders(leader.accessToken, "leader") }
    );
    await ensureOk(pendingResponse, "load pending-approval trip");
    const pending = (await pendingResponse.json()) as TripDto;
    expect(pending.status).toBe("PendingApproval");
    expect(pending.systemDistanceKm).toBe(9.7);

    const approveResponse = await request.post(
      `${apiBaseUrl}/api/v1/trips/${tripId}/approve`,
      {
        headers: authHeaders(leader.accessToken, "leader"),
        data: {
          approvedDistanceKm: pending.systemDistanceKm,
          rowVersion: pending.rowVersion,
          comments: "Automated Phase 2 UAT approval"
        }
      }
    );
    await ensureOk(approveResponse, "approve automated trip");
    const approved = (await approveResponse.json()) as TripDto;

    expect(approved.status).toBe("Approved");
    expect(approved.approvedDistanceKm).toBe(9.7);
    expect(approved.ratePerKmSnapshot).not.toBeNull();
    expect(approved.approvedAmount).not.toBeNull();

    const expectedAmount = Math.round(
      approved.approvedDistanceKm! * approved.ratePerKmSnapshot! * 100
    ) / 100;
    expect(approved.approvedAmount).toBe(expectedAmount);

    const adminRow = await queryTrip(
      request,
      admin.accessToken,
      "admin",
      slot.visitDate,
      tripId
    );
    expect(adminRow.status).toBe("Approved");
    expect(adminRow.isSnapshot).toBe(true);
    expect(adminRow.snapshotVersion).toBeGreaterThanOrEqual(1);
    expect(adminRow.approvedDistanceKm).toBe(9.7);
    expect(adminRow.subsidyAmount).toBe(expectedAmount);

    const supervisorRow = await queryTrip(
      request,
      supervisor.accessToken,
      "supervisor",
      slot.visitDate,
      tripId
    );
    expect(supervisorRow.status).toBe("Approved");
    expect(supervisorRow.isSnapshot).toBe(true);
    expect(supervisorRow.snapshotVersion).toBeGreaterThanOrEqual(1);
    expect(supervisorRow.subsidyAmount).toBe(expectedAmount);
  } finally {
    if (tripId !== null) {
      const cleanupResponse = await request.post(
        `${apiBaseUrl}/api/v1/uat-automation/cleanup-trip`,
        {
          headers: {
            ...authHeaders(admin.accessToken, "admin"),
            "X-UAT-Automation-Key": demoPassword
          },
          data: {
            visitTripId: tripId,
            expectedPurpose: purpose,
            backgroundJobId: mileageJobId
          }
        }
      );
      await ensureOk(cleanupResponse, "cleanup automated trip");
      const cleanup = await cleanupResponse.json();
      expect(cleanup.tripsDeleted).toBe(1);

      const verifyDeleted = await request.get(
        `${apiBaseUrl}/api/v1/trips/${tripId}`,
        { headers: authHeaders(visitor.accessToken, "visitor") }
      );
      expect(verifyDeleted.status()).toBe(404);
    }
  }
});
