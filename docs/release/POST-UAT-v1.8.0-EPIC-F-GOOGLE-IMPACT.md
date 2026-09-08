# Epic F — Google Maps Platform Integration Impact Analysis

Status: **Review Gate correction / design only**. No live Google API call, key use, Azure SQL change, migration or application deployment has occurred.

## Decision summary

Google Maps Platform output is external content governed by the company's actual Google agreement and the then-current product terms. Until IT and Legal approve a written retention decision, the application must not treat Google-derived coordinates, route distance, duration, request identifiers, polyline, turn-by-turn data or raw responses as permanent company records.

The permanent record is the company's business decision and its audit trail: `ApprovedDistanceKm`, decision source/basis, approver, approval time, success/failure, error code, correlation ID and request-basis hash. A Google response may be used transiently to support the user's or leader's decision, but the response itself is not the durable audit record.

This is a conservative implementation gate, not a legal conclusion. IT/Legal must review the company-specific Google agreement, billing entity/region and current terms before live UAT.

## Frozen functional scope

- Google Routes API, Google Geocoding and Google Map visualization.
- `Car → DRIVE`; `Motorcycle → TWO_WHEELER`.
- Exactly one Google-recommended route; no alternative routes.
- Preserve the external visitor's specified visit order; no stop-sequence optimization.
- Preserve separately entered visitor-calculated mileage.
- Google failure permits manual fallback and must not block the business workflow.
- A leader may retry, using the immutable submitted Trip Snapshot address/order basis.
- Places API, Google-created Location Master, multi-route comparison and sequence optimization remain out of scope.

## Persistence classification before IT/Legal approval

| Data | Runtime use | Permanent SQL/log/Snapshot rule |
|---|---|---|
| Geocoding latitude/longitude | May be held in memory or approved short-lived cache to render/compute the current interaction | **Do not persist** until a product-specific retention period and deletion control are approved |
| Route distance/duration | May be shown transiently as the provider suggestion | **Do not persist as Google output**; persist only the later company-approved distance decision and evidence |
| Provider request ID | Troubleshooting only if contractually permitted | **Do not persist** in v1.8.0 preparation schema |
| Polyline / legs / steps / turn-by-turn | Current Google Map rendering only | **Never persist** in SQL, logs, telemetry, outbox or Snapshot |
| Raw response / alternative routes / optimized order | Not required | **Never request or persist** |
| Provider/status/error/correlation/timestamps | Operational/audit metadata generated or assigned by the application | May persist; error text must be sanitized and must not embed Google response content |
| Request-basis hash | SHA-256 of canonical company request basis | May persist; canonical source fields remain company Trip/Snapshot data |
| `ApprovedDistanceKm` and approval evidence | Company business decision | May persist with source/basis code, basis hash, approver and timestamp |
| Place ID | Not used in this release | No schema is introduced. If later used, follow the then-current product policy and add an IT/Legal-reviewed migration; Places API remains out of scope |

`SystemDistanceKm` must not become a permanent copy of the Google distance. Existing v1.7.2 values are untouched and are not reclassified or rewritten by this preparation package. New Epic F code must keep the provider suggestion transient and write only the explicit company decision fields.

## `1800_007` corrected data design

`GeocodingAttempts` records only Location link, provider label, address-basis hash, status, sanitized error, application correlation ID, timestamps and requester. It contains no latitude/longitude or provider request ID.

`RouteCalculationAttempts` records only Trip/Snapshot basis, reason, requested business vehicle/mode, provider label, stop count, request-basis hash, status, sanitized error, application correlation ID, timestamps and requester. It contains no route distance, duration, provider request ID, polyline or response payload.

`MileageCalculations` adds durable company-decision evidence: governance version, `ApprovedDistanceSource`, `ApprovalBasisCode`, `ApprovalBasisHash`, `DistanceApprovedAt` and `DistanceApprovedByUserId`. Existing v1.7.2 approvals remain legacy rows with no invented backfill. A trigger requires complete evidence for every new approval or changed approved distance. New Snapshots freeze the company decision source/basis/hash and approval time, not the Google output.

Trip Snapshot stops do not receive coordinate columns. Snapshot retry reconstructs the request from immutable company-owned Snapshot address/order/vehicle facts, not from persisted Google coordinates.

## Request, failure and retry behavior

1. The visitor explicitly requests Google calculation; normal form edits do not call Google.
2. The backend constructs origin, ordered intermediate stops and destination from the current company draft or submitted Snapshot. Alternative routes and waypoint optimization are omitted/false.
3. The backend requests the minimum response fields needed for the current interaction. Provider results remain transient.
4. On success, the UI clearly labels and attributes the transient Google suggestion. The visitor's own distance remains separate.
5. On timeout, quota, invalid address, unsupported mode or provider failure, the system stores only sanitized failure/audit metadata and permits manual fallback.
6. Leader retry always reloads the exact submitted Snapshot and its request-basis hash. It never uses the current Location Master.
7. The leader explicitly records the company-approved distance. That decision—not the Google response—is frozen with the approver, time, source/basis code and hash.
8. The system must not silently substitute `DRIVE` when `TWO_WHEELER` is unavailable.

## Attribution requirement

- When Google Maps content is displayed on a Google Map, preserve all attribution supplied by the map; never hide, obscure, crop or modify it.
- When Google content is displayed without a corresponding Google Map, show compliant `Google Maps` logo/text attribution in the same visual container and clearly distinguish Google content from company/manual content.
- Use the current Google-provided attribution assets and accessibility/legibility rules. Do not localize the words `Google Maps`.
- Any third-party attribution returned by Google must also remain visible.
- UI acceptance tests must verify attribution at desktop and iPhone/mobile breakpoints.

## Terms of Use / Privacy Policy review gate

Before enabling any live UAT key, IT/Legal must record approval of all of the following:

1. The contracting/billing entity, account region and applicable Google Maps Platform agreement, including any EEA-specific terms if relevant.
2. Product-specific Routes API, Geocoding API and Maps JavaScript API caching/storage rules and the technical deletion controls for any approved temporary cache.
3. The application's Terms of Use notice that it includes Google Maps features/content and that use is subject to the current Google Maps end-user terms.
4. The application's publicly accessible Privacy Policy and its disclosure of Google Maps processing, data categories, purpose, retention and user rights as applicable.
5. Attribution placement and the prohibition on combining Google content with a non-Google map where restricted.
6. Whether any Google-specific result may be retained long term. If yes, Legal must identify the contractual basis, exact fields, retention period, deletion process and approved migration before implementation.

No live UAT is permitted while any item is unresolved.

## Key and configuration separation

- No key or credential in the repository, logs, SQL, telemetry or client bundle except the browser-restricted key intended for browser delivery.
- Backend Routes key: server-side, Routes-only, restricted to approved backend identity/network.
- Backend Geocoding key: separate server-side key where feasible; Geocoding-only and quota-isolated.
- Browser Maps JavaScript key: distinct key restricted to approved API and UAT origins/referrers; it must not authorize backend Routes/Geocoding.
- UAT and Production use separate projects/keys or an IT-approved equivalent boundary.

## Test and regression gates

- CI uses deterministic mocks only; no live Google API call.
- Contract tests assert one route, no optimization, minimum field mask and exact vehicle mapping.
- Persistence tests assert forbidden fields and payloads never reach SQL, logs, telemetry, outbox or Snapshot.
- Service tests cover transient success, timeout/quota/invalid address/unsupported mode, manual fallback, Snapshot retry, correction invalidation and sanitized errors.
- Decision tests prove complete company approval evidence and prove Google suggestion is not written to `SystemDistanceKm` or another durable output field.
- Frontend tests prove clear provider labeling and compliant attribution.
- A later live UAT smoke test requires a separate approval after billing/quota, key restrictions, `TWO_WHEELER`, Terms/Privacy, attribution and retention gates pass.

## Official sources reviewed

- [Google Maps Platform Terms of Service](https://cloud.google.com/maps-platform/terms)
- [Google Maps Platform Service Specific Terms](https://cloud.google.com/maps-platform/terms/maps-service-terms)
- [Routes API policies and attribution](https://developers.google.com/maps/documentation/routes/policies)
- [Geocoding API policies and attribution](https://developers.google.com/maps/documentation/geocoding/policies)

Terms can change. IT/Legal must re-check the current versions at the live-UAT and Production gates rather than relying only on this dated design review.
