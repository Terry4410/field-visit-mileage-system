# 1800_013 VAL-B1-002 candidate recovery

Forward-only: do not drop trusted-grant evidence after any authorized write. Before execution, DBA must take an approved backup and retain Preflight/Up/Verify output. A failed uncommitted transaction rolls back. A post-commit defect is corrected by a separately reviewed forward script that revokes the affected grant; no mass activation or historical rewrite is permitted.
