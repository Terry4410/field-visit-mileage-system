# Path B P3 preflight correction candidate — NONDEPLOY

This directory is a complete reviewable **candidate** for the next one-time
Path B installation. It does not authorize SQL access or installation. The
approved release remains `d6c220877b9976b6478d85b5171d4e06293595ae`.
The original private ZIP, Manifest, Seal, `install.py`, 14 SQL layers and
external ODBC adapter are immutable. No accepted CI was rerun.

## Exact scope and artifact names

| Role | Exact value | Comparison |
|---|---|---|
| Private Blob object | `pathb-sql-install-package-d6c220877b9976b6-20261027.zip` | Exact CLI download target, configured separately |
| Seal's `packageFile` | `pathb-sql-install-package.zip` | Original archive basename recorded when sealed |
| Local download | Any safe temporary filename, e.g. `original.zip` | Bytes and SHA-256, never filename equality |
| ZIP SHA-256 | `5a0c956d4ef5c88e0ff49d2d606ee9c1cb34e81e7f0c0f85b5aea8c5743e5828` | Exact bytes |
| Manifest SHA-256 | `e94c76952eebdf55e56ab6fffa9b1dd0dfc38ec1c4ae79cae36dfacb83c1a9c9` | Exact contained bytes |
| Seal SHA-256 | `a37dc3c7fbf1f805639fb72a76cc788bd9bec1f0f9933e07909e2eacae5c4a5f` | Exact original record |

The stopped auxiliary inline verifier compared `seal.packageFile` directly
to the **Blob object name**. That check was erroneous. `seal_verifier.py`
requires both independently pinned names, package hash and 67,543-byte size,
Manifest hash, release, target, installer hash, all 14 ordered layer hashes,
the 34 exact ZIP member hashes in the Seal, and the 33 indexed member hashes
and lengths. Duplicate, missing, extra, traversal and absolute member paths
are rejected. It cannot establish the Blob origin from local bytes alone;
the next authorized download must separately record its exact `--name` and
`--auth-mode login` invocation. No Seal/ZIP content is rewritten.

`original-integrity-results.txt` records a **fresh local read** of the original
private Blob bytes in this engineering session: 34/34, 33/33, 14 layers and
all pinned SHA values PASS. The private ZIP/Seal are not included in this PR.

## One fixed SELECT and complete current-artifact gate

The old one-call preflight returned 11 fields and checked user-table count 0.
That cannot exclude a view, procedure, custom schema, user-defined type,
database-level trigger, XML collection or other partial installation artifact.

`preflight.sql` retains the original server/database/EngineEdition, Owner SID,
CONTROL, VIEW DEFINITION, user-table count, encryption and TCP fields. In the
**same SQL client invocation**, it appends these 11 ordered counts:

1. All non-system `sys.objects` (tables, views, procedures, functions, many triggers/constraints and other schema objects).
2. Schemas other than SQL Server's built-in schemas.
3. User-defined types.
4. Database-level non-system triggers.
5. XML schema collections other than built-in ID 1.
6. Full-text catalogs.
7. External data sources.
8. Database-scoped credentials.
9. Security policies.
10. Database-level extended properties, including potential marker metadata.
11. Explicit `SchemaVersions` or `MigrationLedger` objects (redundant with non-system objects, but diagnostic).

`preflight_row` requires **one 22-field row**, exact Owner SID and target,
EngineEdition 5, CONTROL=1, VIEW DEFINITION=1, tables=0, encryption TRUE,
transport TCP, and each added count to be the canonical decimal `0`. Missing,
NULL, malformed, hidden, extra or nonzero results are HOLD. The query itself
contains SELECT only; a catalog or permission error stops the process. The
`auth_scheme` value is observed but remains outside the mandatory predicate
per the prior Owner decision. Output width is fixed at 65535 to keep all
columns in one row.

This gate proves absence of **currently visible installation artifacts** in
the listed catalogs under database CONTROL and VIEW DEFINITION. It cannot
prove an erased historical operation never happened or that an unsupported
catalog is empty. Such uncertainty, any mismatch, or an unrecognized SQL
error must be reported as HOLD; it never authorizes DDL. No live SQL call was
made in this engineering phase, so actual Azure SQL query compatibility and
current database emptiness are not yet PASS.

## Control delta and hashes

| Item | Previous SHA-256 | Candidate SHA-256 |
|---|---|---|
| Config | `8de0e4eca4f18382f7d93085ed01d0d83a532d23e5ee61f3291d187538e42a15` | `c73f8f2bf5ac3b420a846c26d349664bee24870f15bafb79f3d1f78629a469e1` |
| Supervisor | `400c9ec7fc4bfb38e162c118a4ab2fee52852f4095e1af66ca62188f6790c6b7` | `035b87acb4dd602a40f3edc67c5fa4d01f8fb79934fc5993eb1594ba7db660d6` |
| Transport | `f6e17f68b4282c85791eb798319f5240240ee61ac7931e2bff2414347e5a6b0b` | `98baa1caa79dd67136a85b67fc8feabd2f13bf154b3a72f3785041ac8129ef1e` |
| Fixed preflight SELECT | `101d0011d11881df37f0a877a31176b04318a17c6e5ef555a1335cd9ebcd33e1` | `e87dbd54d4740254b6f0041fe330cc4af71e51a214964c39e1001c4437075add` |
| Auxiliary Seal verifier | Inline check, no pinned standalone SHA | `33d8bd044839a9b549a39eeeb2fd2616e0663bca0a7df41a78fdd49ab5480597` |
| Cleanup | `602bf925d1f26268d403d52d4e54ef74c5ddfcb3d9b98b479a73cec510fc2494` | **unchanged** |
| ODBC adapter | `703d04bffa6542d80a214bd257adb221f86fdc45aee38981267239fc913596e4` | **unchanged** |
| Original installer | `b38f124391d89351f2da6dd01cc4a93e7a515ac4bbda79b6746d5211ce44ba4f` | **unchanged** |
| Original 14 SQL layers/16 inputs | Pinned in `config.json` | **unchanged** |

The candidate `transport.execute` verifies the Seal and original ZIP **before**
management GETs, the parent once marker, token acquisition or App transfer.
The remote Supervisor verifies the new Config and all control hashes and
checks the extended preflight before the original installer's dry-run/execute.
The existing parent/App once guards, watchdog, token TTL, timeout, no-retry,
partial-install stop and independent cleanup behavior remain intact.

The human-readable diff is the GitHub PR diff for `supervisor.py`,
`transport.py`, `config.json` and their newly added verifier/SQL/test files.
The precise erroneous prior auxiliary predicate was:

```python
seal['packageFile'] == 'pathb-sql-install-package-d6c220877b9976b6-20261027.zip'
```

The candidate binds the original Seal basename and versioned Blob name
**separately**, then checks content SHA-256 and length.

## Call budget and next execution contract

| SQL phase | Client invocations |
|---|---:|
| New SELECT-only preflight (22 fields) | 1 |
| Immutable installer empty guard + 14 layers + final guard | 16 |
| Immutable postcheck + schema fingerprint SELECT | 2 |
| **Maximum** | **19** |

The additional catalog subqueries do **not** add a SQL client connection.
The fixed SQL budget remains 40 minutes inside a 60-minute total deadline;
Owner SQL Token must have at least 50 minutes and the existing additional
cleanup/buffer checks. Exact target:
`sql-fieldvisit-jpe-pathb-uat.database.windows.net / db-fieldvisit-pathb-uat`
through existing private `api-fieldvisit-pathb-uat`. SQL public access stays
Disabled and firewall rules zero. Fresh identity, private DNS/TLS, exact
target, permission, metadata, empty-database, hashes, tools and dry-run gates
must **all** PASS. One installer invocation at most; any failure stops without
retry, remaining-layer execution, DROP, reset, restore or repair. A partially
committed schema remains for a separate recovery decision. Postchecks are
read-only; success does not authorize runtime ACL, activation, deployment or
Business UAT GO.

This is a **proposed new exact control version**. A future SQL installation
requires fresh explicit Owner approval referencing the candidate Config,
Supervisor, Transport, preflight and binding-index hashes and the PR HEAD.

## Offline evidence

`python3 -m unittest discover -s . -p 'test_*.py' -v`: 50 test methods PASS
using synthetic data, fake tokens and fake SQL processes only. The tests cover
hash/size/member/path/version/target negatives, marker-only and partial-schema
rows with zero user tables, malformed/unknown metadata, full 16-call synthetic
chain, stop at each failure, once guard, no retry, token/timeout and cleanup.
`offline-test-results.txt` preserves the test names and outcome. No Azure SQL
syntax execution or live empty-database proof occurred in this phase.
