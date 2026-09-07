# Epic F — Google Maps Platform Integration Impact Analysis

Status: design and migration preparation only. No key, live API call, Azure SQL change or application deployment has occurred.

## Frozen scope

Epic F integrates Google Routes API, Google Geocoding and browser map visualization. Car maps to `DRIVE`; Motorcycle maps to `TWO_WHEELER`. The route request follows the external visitor's submitted stop order and requests one Google recommendation only.

The following are explicitly excluded: Places API, Google-created official Location Master records, alternative-route comparison, stop-sequence optimization, and persistence of full polyline or turn-by-turn responses.

## Component impact

| Area | Planned change | Required guardrail |
|---|---|---|
| Backend Routes provider | Implement Google Routes `computeRoutes` behind the existing provider abstraction | Do not request alternatives; do not optimize waypoint order; use explicit field masks; apply timeouts/retry budget and redact sensitive response data |
| Backend Geocoding provider | Resolve Location address to coordinate candidates and record success/failure metadata | Geocoding never creates or promotes an official Location automatically; existing review/governance remains authoritative |
| Visitor mileage flow | User explicitly selects Calculate Google Mileage; retain separately entered claimed/manual distance | No automatic API call on every form edit; Google failure must not block manual submission |
| Leader mileage flow | Permit recalculation and manual approval | Every leader retry uses address, coordinate and stop order from the submitted Trip Snapshot, never current Location Master |
| Correction flow | Invalidate selectively when location/order, vehicle or VisitDate changes | Location/order → route recalculation; vehicle → route/rate/amount; VisitDate → effective Employment/Team/Site/rate re-evaluation |
| Browser map | Display the single recommended route and ordered stops | Any polyline is transient response/UI state only; it is not stored in SQL, logs or audit payloads |
| Database | `1800_007` stores geocode/route attempts, distance/duration/status/error/correlation metadata and governance events | No raw Google response, alternative-route collection, full polyline or turn-by-turn columns |
| Snapshot | Store stop coordinates plus selected provider/mode/status/time/error/correlation evidence | Existing v1.7.2 snapshots remain unchanged; new submitted/final snapshots are immutable |

## Request and result rules

For Routes requests, the backend constructs origin, ordered intermediates and destination from the current draft or a submitted Snapshot. `computeAlternativeRoutes` and waypoint optimization are omitted/false. Only one result is accepted. Vehicle mapping is fixed:

| Business vehicle | Google travel mode |
|---|---|
| `Car` | `DRIVE` |
| `Motorcycle` | `TWO_WHEELER` |

Provider results retained as business/audit metadata are limited to provider, distance, duration, requested/completed time, status, error code/message, provider request identifier, request-basis hash, stop count and correlation ID. The live polyline may be returned to the browser for the current map rendering but must not be written to SQL, application logs, telemetry payloads or Snapshot records.

`TWO_WHEELER` availability and Google contractual/region rules remain an IT verification gate. If unavailable or rejected, the system records the provider failure and uses the same manual fallback; it must not silently substitute `DRIVE` for Motorcycle.

## Failure and retry behavior

1. Visitor requests Google calculation.
2. On success, UI shows Google distance alongside the visitor's claimed distance.
3. On timeout, quota, provider, validation or unsupported-mode failure, the audit attempt records failure metadata and the visitor may submit using manual distance.
4. A leader may retry. The server loads the exact submitted Snapshot, verifies its immutable stop basis and sends that order to Google.
5. If retry also fails, the leader may approve a manual distance. Approval still freezes claimed/system/approved distance, rate and amount evidence.
6. Email or Google failures never roll back a successful business transaction.

## Key and configuration separation

No API key or credential may be committed to the repository. Configuration names may exist, but values must come from IT-approved Azure/GitHub secret storage at runtime.

- Backend Routes key: server-side only, restricted to Routes API and the approved backend execution identity/network.
- Backend Geocoding key: preferably separate from Routes for least privilege and quota isolation; at minimum it must remain server-side and API-restricted.
- Browser Maps JavaScript key: a different key, restricted to Maps JavaScript API and approved UAT origins/referrers. It must never authorize Routes or Geocoding backend calls.
- UAT and Production use separate keys/projects or equivalent isolation. This work does not access Production.

Application logs must mask keys and must not log authorization headers, full Google responses or sensitive request payloads.

## Planned tests and regression

- Contract tests assert one-route/no-optimization request construction and exact Car/Motorcycle mapping.
- Provider tests cover success, timeout, quota, invalid address, unsupported mode, malformed response and cancellation.
- Service tests prove manual fallback, leader Snapshot retry, role/team scope, stale/correlation handling and correction invalidation.
- Snapshot tests prove current Location edits do not change retry input or approved history.
- Frontend tests cover explicit calculate action, loading/error/manual states and one-route map rendering.
- CI uses deterministic mocks and contains no live API key. A separately approved UAT smoke test may use restricted UAT keys after IT confirms billing/quota and `TWO_WHEELER` support.
- Regression retains v1.7.2 create → submit → approve → Snapshot → query, including Google failure with manual completion.

## Open IT decisions before live UAT

- Google Cloud project ownership, billing, quota and alert thresholds.
- UAT/Production key separation, API restrictions, origin/referrer restrictions and rotation owner.
- `TWO_WHEELER` support and terms for the operating region.
- Permitted telemetry fields and retention period for route/geocode audit metadata.

These decisions do not change the frozen business rules and must not be replaced with hard-coded values.
