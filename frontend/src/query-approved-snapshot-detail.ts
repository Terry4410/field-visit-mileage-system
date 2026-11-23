import type { Trip, TripQueryRow } from "./types";

export interface QueryTripDetail {
  trip: Trip;
  snapshotVersion: number | null;
}

/**
 * The approved query row is projected from the latest immutable Snapshot.
 * GET /trips/{id} can still contain original MileageCalculation values.
 * Overlay data only for the exact same approved trip; never change a draft.
 */
export function resolveQueryTripDetail(row: TripQueryRow, trip: Trip): QueryTripDetail {
  if (row.visitTripId !== trip.visitTripId || row.tripNo !== trip.tripNo) {
    throw new Error("行程明細與查詢結果不一致，請重新查詢。");
  }
  if (row.status !== trip.status) {
    throw new Error("行程狀態已變更，請重新查詢。");
  }
  if (row.status !== "Approved") return { trip, snapshotVersion: null };

  if (!row.isSnapshot || !Number.isSafeInteger(row.snapshotVersion) || row.snapshotVersion < 1) {
    throw new Error("無法確認已核准行程的最新 Snapshot，請重新查詢。");
  }

  return {
    snapshotVersion: row.snapshotVersion,
    trip: {
      ...trip,
      visitDate: row.visitDate,
      startTime: row.startTime,
      endTime: row.endTime,
      visitorName: row.visitorName,
      teamName: row.teamName,
      notes: row.notes,
      claimedDistanceKm: row.claimedDistanceKm,
      systemDistanceKm: row.systemDistanceKm,
      approvedDistanceKm: row.approvedDistanceKm,
      ratePerKmSnapshot: row.ratePerKmSnapshot,
      approvedAmount: row.subsidyAmount,
      mileageSource: row.mileageSource,
      // Route and stops reflect the same immutable Snapshot as the table.
      // Deployment endpoints are already supplemented by the existing GET.
      stops: row.stops.map(stop => ({
        locationId: stop.locationId,
        projectId: stop.projectId,
        visitTypeId: stop.visitTypeId,
        sourceType: "Snapshot",
        locationName: stop.locationName,
        address: stop.address,
        visitPurpose: stop.visitPurpose,
        notes: stop.notes
      }))
    }
  };
}
