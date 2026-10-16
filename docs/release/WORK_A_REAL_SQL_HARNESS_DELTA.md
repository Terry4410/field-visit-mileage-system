# WORK A — deterministic disposable SQL evidence delta

Baseline: `ef8718082b1b57dcb8074b4465f0d12296a9054c`.
Authority: NONDEPLOY_ONLY. UAT release remains NO-GO.

This delta fixes mutual supersession in the actual final-persistence transaction.
The accepted harness DI, retry guard, atomic job claim and diagnostic allow-list are unchanged.
Final source SHA/run/counters are bound by the exact-head CI acceptance artifact,
not by a self-referential SHA in this document.

## Runtime and boundaries

`V180WorkerSqlHarnessTests.cs` extends the existing Trip/Mileage HTTP-SQL fixture.
It uses real ASP.NET HTTP authentication/controllers, real DI/repositories,
independent scoped Workers, real EF transactions and SqlClient sessions in SQL
Server 2022. Only the external route provider is a synthetic controlled stub;
there is no Google Maps network request or mocked repository.

Each case creates a GUID-named `OptionCIsolated_*` database, validates loopback /
master / sa before creation, disables only that test host's hosted worker, applies
90-second cancellation and drops only its explicitly created database in finally.
No UAT/production connections, deployments or feature flags are changed.
EF EnsureCreated is explicit: these tests are NOT same-canonical-migrated-database
or trusted UAT 001–010 schema-fidelity evidence. Browser remains API-mocked.

## Executable cases (13 rows required by CI)

| Case | Controlled interleaving / persisted assertion |
| --- | --- |
| Own success earliest | Both Attempts terminal before A's apply; A's eligible own success applies once; B cannot overwrite |
| Other success earliest | B terminal before A; A can apply its own eligible success despite B completing first |
| Both before persistence | Both real transactions contend on Trip lock; exactly one matching Attempt/result applies; no sleeps |
| Failure versus success | A failure cannot fall back over B's authoritative success; B alone applies 99.75 km |
| One applies first | A applies 12.5 km while B is in provider I/O; B cannot overwrite it with 99.75 km |
| Submitted route changes | Append a new Submitted snapshot with a changed basis while provider is blocked; old snapshot remains unchanged; reject old result |
| HR revoked | Commit Leave status during provider I/O; no mileage application |
| Role revoked | Commit effective role expiry during provider I/O; no mileage application |
| Team revoked | Commit effective team expiry during provider I/O; no mileage application |
| SQL 1205 | Two real SQL sessions lock different probe rows then request each other's row; app session LOW deadlock priority; observed SqlException.Number=1205 |
| SQL 1222 | Another SQL session retains a row lock; app session's explicit LOCK_TIMEOUT expires; observed SqlException.Number=1222 |
| Provider SQL/token payload | HTTP enqueue → worker/provider → Attempts/Items/Jobs/Governance → HTTP job read; no raw payload |
| Provider identity/business payload | Same real persistence/HTTP path; only fixed allow-listed failure codes |

Fault injection is a test-only SaveChanges interceptor **after actual business
writes reach SQL but before the owning transaction commits**. Assertions cover
probe rollback, Submitted trip state, no partial mileage/status-history publication,
safe durable Job/Item codes/counters, no approval/Approved snapshot, and a new
successful retry job with exactly one fallback status transition. A background
worker has a durable error contract; HTTP 409 is not invented for that path.
This does not independently fault-inject the approval HTTP endpoint.

Every scenario checks terminal Job counters against actual Items, no duplicate
approval or per-Attempt governance event, and original snapshot references.
The CI writes fixed-prefix execution observations to the acceptance summary;
13 nonzero passing rows are required in a dedicated TRX in addition to the full
backend suite. No source-text-only assertion is used for the new runtime proof.

## Exactly-once completion under the existing Trip lock

The serializable transaction retains the Trip UPDLOCK, live HR/role/team checks,
latest Submitted snapshot identity, eligible Trip status and unapplied mileage guard.
It verifies that the current provider result's own immutable Succeeded Attempt
matches the current submitted route. That eligible result may apply while holding
the Trip lock even when another successful Attempt is already durable. The next
transaction sees the completed Trip state and cannot apply again. Completion order
does not elect a provider result; the first eligible apply transaction does.

Failure/fallback still stops when any other authoritative success exists. A claimed
success without its own authoritative current-route Attempt also stops. No Attempt
or Snapshot is rewritten, no other provider result is reconstructed, and no approval
or subsidy is automatically granted. The three formerly zero-application scenarios
now require exactly one application and a matching winner Attempt/distance/history,
one successful Item, one failed Item, consistent counters, and denied extra retry.
The real SQL fault and sensitive-data regression cases remain unchanged.

Runtime PASS is established only by the exact-SHA dedicated SQL TRX and full CI,
not this description. The disposable EF baseline is not UAT compatibility proof.

Remaining release gates: independent audit; trusted 001–010/UAT schema and app
principal fidelity; real Browser→same canonical migrated DB; deployment provenance;
matched rollback artifacts; Owner-led Business UAT and separate release approval.
