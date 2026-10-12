# Package B SQL candidates (not deployment inputs)

`1800_011_b3_change_requests_candidate` and
`1800_012_b3_atomic_apply_candidate` are executable SQL **candidates under
WORK review**.  Each package contains `Preflight.sql`, `Up.sql`, `Verify.sql`
and forward-recovery instructions.

They are deliberately outside `database/migrations`. Existing deployment
workflows name their approved script paths under `database/migrations` and do
not discover or execute this directory. These candidates do not authorize a
UAT or Production database change.

Before either candidate can enter an approved migration process, a later Owner
decision must pin exact file SHA-256 values, a named UAT target, DBA/IT
executor, backup evidence, recovery evidence, and the separately authorized
execution window.
