# Accepted delta preservation / targeted verification supplement

Accepted package remains byte-for-byte unchanged: original19 files,
ZIP63a2bea2527d9ce87f3bbf3199057b3f03dc2aaf70e6d018b24763430261445c;
manifest63c81e16359aa82112d4f19f6e6eb3cdcaeece136e93196b11f12731cda3947b.
Old validation-results.json is historical accepted OFFLINE evidence, not the
result of this new GitHub delivery. New run artifacts are separately source-bound.

## Bounded010 / EF source comparison

| Requirement | Exact frozen source | Source conclusion / actual UAT status |
|---|---|---|
| Existing Locations/Trip/Snapshot base columns, PK/defaults | database/015_schema_v1_5_verify.sql only lists selected columns; AppDbContext maps model | No original1.5 CREATE baseline. Model specifies required behavior; complete deployed base/default equivalence UNKNOWN, not waived |
| Organization/Center/Team lifecycle, dates/defaults, unique keys | database/migrations/1800_001_organization_center_team_lifecycle/Up.sql | Exact additive DDL is reviewable; deployed clauses/trigger state UNKNOWN |
| Person/Employment/HR/role/team/leader periods |1800_002_person_employment_role_membership/Up.sql; AppDbContext| Created-object PK/default/FK/CHECK definitions are explicit; date-overlap/primary-team triggers are required, actual state UNKNOWN |
| Deployment Site / primary assignment / start-end snapshot facets |1800_003_deployment_sites/Up.sql; EF VisitTripSnapshot mapping| Exact additive types/nullability/defaults and period relationships available; original base remainder UNKNOWN |
| Location notes/governance and protected active site relationships |1800_004_location_governance/Up.sql| Note/history PK/defaults and protected-location/site triggers required, actual state UNKNOWN |
| Project/VisitType/Rate lifecycle / overlap rules |1800_005_project_visit_rate_lifecycle/Up.sql and1800_008_rate_manual_effective_dates/Up.sql|008 supersedes rate-series trigger/constraints;005 text alone is insufficient |
| Provider/mileage approval evidence and snapshot FK |1800_007_mileage_google_governance/Up.sql| Types and NoAction FKs, basis-trip and snapshot-route trigger source explicit; live data consistency UNKNOWN |
| One-stop provider reason / Background worker contract |1800_009_one_stop_route_attempt_constraint/Up.sql;1800_010_background_mileage_reason_constraint/Up.sql|010 replaces CK_RouteCalculationAttempts_Reason: VisitorCalculate,LeaderRetry,CorrectionRecalculate,BackgroundMileageJob; enabled+trusted required |
| Snapshot required EF key/index/facets |AppDbContext VisitTripSnapshot/Stop mappings| CI model-only probe validates PK, two unique key sequences, snapshot-stop Cascade, decimal precision and varbinary32 approval hash without connecting SQL |
| Source011–020 necessary contract |accepted necessary-reference.json| Reuse74 columns/30CHECK/18FK/15indexes,019 excluded for frozen25,013 reserved. No proposed ledger/checksum/alias prerequisites |
| Historical preservation |accepted PR49/50 real writer/rollback evidence; promotion/HistoryContract.sql| Accepted synthetic invariants reused. No observed UAT historical fingerprint/compatibility inferred from a successful new collector test |

All22 source SHA256s are in the accepted necessary-reference.json and rechecked
against frozenRC and checkout bytes. Candidate CHECK expressions in EF retain
older B3 annotations while final migration source replaces them: final reviewed
SQL contract owns CHECK states; model annotations must NOT be used to recreate
or certify candidate constraints. Compiled model output is a scoped application
requirement reference, NOT EnsureCreated, a clean baseline or observed schema.

Essential trigger source requirements include:
-002: TR_EmploymentStatusPeriods_NoOverlap, TR_EmploymentRoleAssignments_NoOverlap,
 TR_TeamMemberships_OnePrimary, TR_TeamLeaderAssignments_NoOverlap,
 TR_TeamLeaderDelegations_NoOverlap.
-003: TR_DeploymentSites_CenterPeriod, TR_TeamDeploymentSites_NoOverlap,
 TR_EmploymentDeploymentSites_SameSiteNoOverlap,
 TR_EmploymentDeploymentSites_OnePrimary, TR_TeamCenterAssignments_ProtectTeamSites,
 TR_DeploymentSites_ProtectTeamSites;001 TR_TeamCenterAssignments_NoOverlap.
-004: TR_Locations_ProtectCurrentDeploymentSiteLocations,
 TR_DeploymentSiteLocationAssignments_ProtectActiveLocation.
-007: TR_RouteCalculationAttempts_BasisTrip, TR_Locations_SelectedGeocodingAttempt,
 TR_MileageCalculations_SelectedRouteTrip, TR_MileageCalculations_DecisionEvidence,
 TR_VisitTripSnapshots_RouteAttemptTrip.
-008 final TR_MileageRateRules_ProtectSeries.

Exact source bodies, defaults and additive FK/PK clauses are source-review inputs;
the existing SQL collector does not yet compare all of these010 triggers/defaults.
That missing comparison remains UNKNOWN. Do not claim this targeted run proves
complete010 compatibility or that migration011–020 is authorized/required by IDs.
Missing/incompatible observed necessary contracts plus independently reviewed
remedy mapping is needed to conclude MIGRATION_REQUIRED; otherwise UNKNOWN.

## Actual targeted verification boundary

The existing workflow identity has a single commit-prefix-selected minimal job.
The prior migration suite is skipped only for this scoped preflight commit; its
accepted evidence and prior HOLD are not replaced. The old one-shot lock workflow
now watches only its original diagnostic script, preventing this workflow update
from rerunning the consumed diagnostic. No Azure environment/id-token permission.

Unmodified accepted SQL01 and collector are actually tested to reject loopback
SQL before permission collection. To exercise downstream phases on SQL2022,
temporary copies make explicit LOCAL-ONLY endpoint/database, SQL edition/identity
fixture substitutions; encrypt=False is fixture-only; a timeout case shortens30s
to1s. A test-only trace records SQL filenames, not metadata or business rows.
Manifest hashes are regenerated only for these disposable copies. Originals are
never edited. These cases prove downstream provider/result/parameter/control flow,
NOT Azure target/OIDC compatibility or secure application-identity binding.

Existing synthetic010 bootstrap and unmodified candidate phases supply disposable
fixtures; they are not a clean baseline. No accepted40/790/122 suite or human UAT
is rerun.18 targeted SQL/PowerShell cases include fixed-target rejection, four
phase order after known catalog findings, marker/missing-object STOP, malformed/
extra result sets, syntax/unknown error/timeout suppression, hash rejection,
parameter/facet/FK/index checks, actual constraint/index/type mutations and
restricted synthetic-context metadata/DDL/registry permission observations.

The EF-only probe builds the frozen source and obtains scoped expected relational
facets privately in RUNNER_TEMP. It never calls Open, EnsureCreated or Migrate.
Only sanitized source-bound result flags are uploaded; raw model output and
SQL results, test credentials, error messages and synthetic row values are not.

## API principal / future authorization

API_PRINCIPAL_BINDING remains UNKNOWN. Effective deployed DefaultConnection,
configuration-provider precedence, authentication selection and in-memory
principal match still need separate restricted runtime evidence. GitHub OIDC,
migration/admin or disposable test principals are not substitutes. No new
credential resolver, secret read, SQL endpoint or authentication change is added.

Future read-only decision: after independent exact-source audit and remaining
contract coverage decision, Owner may separately approve one guarded current-app
context inspection on the fixed UAT server/database, connect120/query30s, at most
four commands, no retries. Independently select whether that SAME connection may
wake Paused compute and consume free credits/overage plus auto-pause tail billing.
Absent approval or binding evidence => STOP/UNKNOWN. No additional warm-up probe,
migration, DDL/DML, grants, deployment or flags. Sanitized flags/source/hashes only.
This is DRAFT—NOT AUTHORIZED; SQL test success alone is not G1 or Business UAT GO.
