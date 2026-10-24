# Existing010 Import Confirming correction — NONDEPLOY source delta only

Parent is the accepted PR50 SHA `feb244d480d35ba7dbadc4d7ae796e8875d705e3`. PR48, PR50 and PR81 branches and accepted artifacts are preserved. This independent stacked Draft lane is not a deployment package and must not be merged or promoted yet.

The existing `1700_005_people_bulk_confirm_claim` migration permits `Confirming`; the unchanged `V170PeopleBulkWorkbookService` still performs an atomic `Previewed -> Confirming` update. The current phase020 CHECK removes that state. It rejects either an already-Confirming row during WITH CHECK, or the existing writer after successful migration. The one-line candidate correction preserves `Confirming` alongside existing and reviewed-submission states; it adds no new business workflow or application permission.

The focused disposable SQL2022 Developer regression uses the frozen old SQL as two negative controls and the corrected SQL for six preservation/security outcomes. It checks an existing Confirming row, the existing claim pattern, prior statuses, review statuses, rejection of unknown statuses, old marker bytes and trusted CHECKs. It is phase020-only, not full-chain, concurrent-request, 25-case, real-UAT, clean-install or rollback-pair acceptance. The frozen 43+5 migration, PR81 history and PR48 suites are not rerun.

The workflow is push-only on this exact NONDEPLOY branch, contents:read, no Azure environment, OIDC, secrets, deployment or workflow_dispatch. It independently binds the accepted parent and exactly four changed files before a loopback-only ephemeral SQL Server. Unexpected provider errors and credentials are suppressed. Only fixed case status/source hashes are archived.

## Mandatory remaining hold

`Promotion.sql`, its manifest, payload checksums and synthetic scoped references remain UNCHANGED accepted inputs. They are deliberately not relabelled as incorporating this corrected candidate SQL. Their source closure will reject the changed input if someone attempts to build an old package from this branch. This Draft cannot be merged until a separately reviewed, correctly regenerated package and predecessor contract exist.

Live read-only evidence in Issue41 comment6385219903 proves the real initial scoped contract mismatches PR50 and is not only a Trigger difference. The original fixture omitted the 1700_005 Confirming state and the existing 004/007 Location Trigger facets. The exact real CHECK/Trigger definitions, full required base metadata and 26 historical content bindings still need source reconciliation; do not replace expected fingerprints with unreviewed observed values or remove legacy triggers/history to fit a test.

Actual API principal/authentication and least privileges, runtime hashes, secret-free backend flags, and the prechange API+Frontend+DB recovery pair remain HOLD. No live migration, deployment, feature activation, restore, permission change or accepted-SHA merge is part of this correction. `G1=HOLD; BusinessUAT=NO-GO; IT_HANDOVER=HOLD` until those actual gates pass.
