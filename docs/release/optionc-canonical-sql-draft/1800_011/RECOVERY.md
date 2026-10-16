# 1800_011 candidate recovery

Before `COMMIT`, any error rolls back all candidate DDL and the version row.
After a successful commit this is forward-only: do not drop request or event
history. Stop application deployment, preserve the SQL error/output and use a
reviewed forward-fix or IT-controlled restore decision. This candidate does
not authorize a UAT run, Grant, deployment, feature enablement or B3 approval.
