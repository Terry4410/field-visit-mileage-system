# Path B install and deployment SOP

Current state: **procedure prepared; execution not authorized by this package**.

## Phase 0 — bind one release pair

1. Fix application source to `d6c220877b9976b6478d85b5171d4e06293595ae`.
2. Verify the API and Frontend artifact hashes from `handover-manifest.json`.
3. Verify the sealed SQL package, Manifest, Seal, Installer and all 14 SQL layers.
4. Fix the only SQL target to `sql-fieldvisit-jpe-pathb-uat.database.windows.net / db-fieldvisit-pathb-uat`.
5. Confirm `db-fieldvisit-uat` and Production are not referenced by any command or setting.

## Phase 1 — fresh SQL preflight

Before DDL, obtain fresh evidence for Owner/operator identity, token audience and
TTL, private DNS, TCP 1433, authenticated TLS, exact target, CONTROL and VIEW
DEFINITION. Run the approved 22-field SELECT-only preflight once. All user
tables, non-system objects/schemas/types, database triggers, user XML
collections, full-text catalogs, external data sources, database-scoped
credentials, security policies, database extended properties and explicit
marker/ledger objects must be zero. Missing, hidden, malformed or unknown values
are HOLD.

Check both Cloud Shell and App-side once markers. Never clear a marker merely to
make a rerun possible. The latest attempt consumed the Cloud Shell parent marker;
its reconciliation requires a separate Owner decision after App-side readback.

## Phase 2 — one create-only installation

Only after every Gate passes:

1. Run the immutable installer once in its original 14-layer order.
2. Retain the fixed maximum of 19 SQL client calls: one preflight, 16 installer
   calls and two read-only postchecks.
3. Stop immediately on any error, timeout or unknown commit state.
4. Never replay remaining layers, reset, drop, restore or repair automatically.

Post-install evidence must show 69 user tables, exactly three required markers,
trusted/enabled constraints, visible enabled triggers and an observed schema
fingerprint. Preserve layer records without tokens, SQL text or personal data.

## Phase 3 — runtime ACL and signed activation

1. Create or bind the dedicated runtime SQL principal.
2. Apply the reviewed object-scoped ACL; prove broad database roles and DDL are absent.
3. Deny runtime writes to migration/history control tables.
4. Record runtime principal SID/name, schema fingerprint and exact database.
5. Generate the signed `PathBNewEmpty` activation using the accepted API and
   Frontend hashes. Signing does not itself deploy or enable features.

## Phase 4 — paired API and Frontend deployment

1. Deploy only the accepted API artifact to `api-fieldvisit-pathb-uat`.
2. Deploy the matching Frontend artifact to the verified Path B Frontend host.
3. Apply exact App settings and secret references; do not expose secret values.
4. Verify `/health`, `/health/live`, `/health/ready`, API version, schema profile,
   Frontend API base URL, CORS and release attestation.
5. Verify Browser → API → SQL with Visitor, Leader and Admin accounts.
6. Confirm SQL public access remains Disabled and firewall rules remain zero.

Deployment success is not Business UAT PASS. Release the UAT URL only after the
three-role smoke, data isolation, Google Routes success/failure and recovery
readiness checks pass.

