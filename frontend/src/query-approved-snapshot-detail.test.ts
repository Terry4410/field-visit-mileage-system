import { describe, expect, it } from "vitest";
import { resolveQueryTripDetail } from "./query-approved-snapshot-detail";
import type { Trip, TripQueryRow } from "./types";

const baseTrip = (): Trip => ({
  visitTripId: 41,
  tripNo: "T20261124160346544",
  userId: 7,
  visitorName: "Path B UAT Visitor",
  teamId: 8,
  teamName: "Path B Test Team",
  visitDate: "2026-11-24",
  startTime: "08:30:00",
  endTime: "17:00:00",
  hasTimeOverlapWarning: true,
  timeOverlapConfirmed: true,
  status: "Approved",
  statusName: "已核准",
  claimedDistanceKm: undefined,
  systemDistanceKm: 10.94,
  approvedDistanceKm: 10.94,
  ratePerKmSnapshot: 2.50,
  approvedAmount: 27.35,
  mileageSource: "GoogleMapsAPI",
  stops: [{ sourceType: "Master", locationName: "原始拜訪", address: "舊地址", visitPurpose: "原始目的" }],
  rowVersion: "AA=="
});

const latest = (): TripQueryRow => ({
  visitTripId: 41,
  tripNo: "T20261124160346544",
  visitDate: "2026-11-24",
  startTime: "08:30:00",
  endTime: "17:00:00",
  visitorId: 7,
  employeeNo: "pilotv01",
  visitorName: "Path B UAT Visitor",
  teamId: 8,
  teamName: "Path B Test Team",
  route: "Path B VIS01 UAT Visit",
  projectNames: "Path B VIS01 Synthetic Project",
  visitTypeNames: "External",
  systemDistanceKm: 10.95,
  approvedDistanceKm: 10.95,
  ratePerKmSnapshot: 2.50,
  subsidyAmount: 27.38,
  mileageSource: "GoogleMapsAPI",
  mileageState: "Calculated",
  status: "Approved",
  statusName: "已核准",
  snapshotVersion: 3,
  isSnapshot: true,
  notes: "更正後的核准 Snapshot",
  stops: [
    { stopSequence: 1, locationName: "Path B VIS01 UAT Visit", address: "新地址", visitPurpose: "更正目的" }
  ]
});

describe("CORR-04 exact approved Snapshot detail projection", () => {
  it("uses approved Snapshot V3 and corrected 10.95 km / 27.38, without mutating original trip", () => {
    const stored = baseTrip();
    const row = latest();
    const result = resolveQueryTripDetail(row, stored);
    expect(result.snapshotVersion).toBe(3);
    expect(result.trip.approvedDistanceKm).toBe(10.95);
    expect(result.trip.systemDistanceKm).toBe(10.95);
    expect(result.trip.ratePerKmSnapshot).toBe(2.50);
    expect(result.trip.approvedAmount).toBe(27.38);
    expect(result.trip.stops[0].locationName).toBe("Path B VIS01 UAT Visit");
    expect(result.trip.stops[0].visitPurpose).toBe("更正目的");
    expect(result.trip.notes).toBe("更正後的核准 Snapshot");
    expect(stored.approvedDistanceKm).toBe(10.94);
    expect(stored.approvedAmount).toBe(27.35);
    expect(stored.stops[0].locationName).toBe("原始拜訪");
    expect(row.snapshotVersion).toBe(3);
  });

  it("retains first-time approved Snapshot V1 as its own latest source", () => {
    const row = { ...latest(), snapshotVersion: 1, systemDistanceKm: 10.94,
      approvedDistanceKm: 10.94, subsidyAmount: 27.35 };
    const result = resolveQueryTripDetail(row, baseTrip());
    expect(result.snapshotVersion).toBe(1);
    expect(result.trip.approvedDistanceKm).toBe(10.94);
    expect(result.trip.approvedAmount).toBe(27.35);
  });

  it.each(["Draft", "Returned", "PendingApproval"])("does not replace %s trip data with Snapshot fields", status => {
    const trip = { ...baseTrip(), status };
    const row = { ...latest(), status, isSnapshot: false, snapshotVersion: 0 };
    const result = resolveQueryTripDetail(row, trip);
    expect(result.trip).toBe(trip);
    expect(result.snapshotVersion).toBeNull();
  });

  it("fails closed if the selected row belongs to a different TripNo or visitTripId", () => {
    expect(() => resolveQueryTripDetail({ ...latest(), tripNo: "T20261124155949128" }, baseTrip())).toThrow("行程明細與查詢結果不一致");
    expect(() => resolveQueryTripDetail({ ...latest(), visitTripId: 42 }, baseTrip())).toThrow("行程明細與查詢結果不一致");
  });

  it("fails closed on role-visible status change or a missing approved Snapshot", () => {
    expect(() => resolveQueryTripDetail(latest(), { ...baseTrip(), status: "Returned" })).toThrow("行程狀態已變更");
    expect(() => resolveQueryTripDetail({ ...latest(), isSnapshot: false }, baseTrip())).toThrow("無法確認已核准行程");
    expect(() => resolveQueryTripDetail({ ...latest(), snapshotVersion: 0 }, baseTrip())).toThrow("無法確認已核准行程");
  });
});
