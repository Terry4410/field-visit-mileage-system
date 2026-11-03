# Source, binary and schema inventory

## Exact source

| Item | Exact identity | Status |
|---|---|---|
| Application source | `d6c220877b9976b6478d85b5171d4e06293595ae` | Accepted |
| Backend | `.NET 8`, `backend/` at the exact source SHA | Complete source |
| Frontend | React 18 / TypeScript / Vite, `frontend/` at the exact source SHA | Complete source |
| Database | `database/`, clean-install source, migrations, security scripts and verifier source | Complete source |
| UAT | `docs/uat/` and `uat/` | 25 canonical cases preserved; Business execution not run |
| Release controls | `scripts/ci/uat_release/`, Path B contracts and PR #84 preflight candidate | Source/evidence available |

The handover Git HEAD is a documentation-only descendant of the approved source.
IT must treat the application source SHA above as the runtime identity and the
handover HEAD as its documentation envelope.

## Accepted CI and binaries

Run `40438452922` succeeded at the exact source and produced the aggregate
Path B build artifact. Run `40438452961` succeeded at the same source and
produced the matching API, Frontend, source, ACL, promotion and synthetic
evidence artifacts. Exact IDs, byte sizes, retention dates and SHA-256 values
are in `handover-manifest.json`.

Primary paired application candidates:

| Component | Artifact | SHA-256 |
|---|---|---|
| API | `uat010-integrated-api-candidate-d6c2208…` | `323da7400698f8d03ee456c07273b9de7fe63a127a41dd8a6e14e55edbbbc084` |
| Frontend | `uat010-optionc-frontend-dual-NONDEPLOY-d6c2208…` | `743182631cddc98037e3a967e1c7b1fc4c447505721f77864a2bd234bb0d63b7` |
| Aggregate Path B build | `pathb-build-d6c2208…` | `5cc23ff55fbb7ef86b7c00aa3a695326f012c8f96a2b1793f82c2e737ec6894a` |

GitHub Actions artifact retention currently ends on 2026-11-26 UTC. IT should
download and verify these artifacts before that date or rebuild them from the
exact source under its own accepted pipeline. Expiring artifact retention is a
handover logistics item; it does not change the accepted source identity.

## Schema and immutable installer

- Private storage: `stfvsc261026a/schema-evidence`.
- Package: `pathb-sql-install-package-d6c220877b9976b6-20261027.zip`.
- Package, Manifest, Seal, Installer and 14 ordered SQL layers were verified.
- Expected post-install result: 69 user tables and exactly three required markers.
- Original package and SQL layers must never be regenerated during execution.
- The sealed private package is transferred through Owner/IT-controlled private
  custody; it is intentionally absent from this public source handover.

## Existing evidence retained

- CI `40438452922`: SUCCESS.
- CI `40438452961`: SUCCESS.
- PR #84 offline controls: 50/50 tests, 34/34 Seal and 33/33 checksum checks PASS.
- Issue #41 comment `6594045913`: live installation attempt stopped before a
  verified SQL phase because the App Service management channel timed out.

