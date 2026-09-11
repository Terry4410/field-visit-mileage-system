#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root"

image="${EA0_SQLSERVER_IMAGE:-mcr.microsoft.com/mssql/server:2022-latest}"
container_name="fieldvisit-ea0-${GITHUB_RUN_ID:-local}-$$"
sa_password='Ea0!NotificationSchema2026_TestOnly'
db_name="FieldVisitEA0_${GITHUB_RUN_ID:-local}_$$"
sqlcmd_path=''

cleanup() {
  docker rm -f "$container_name" >/dev/null 2>&1 || true
}
trap cleanup EXIT INT TERM
fail() { echo "EA0-NOTIFICATION-SCHEMA-FAIL: $*" >&2; exit 1; }

command -v docker >/dev/null 2>&1 || fail "Docker is required."

echo "EA0_SQL_IMAGE=$image"
docker run -d --name "$container_name" \
  -e ACCEPT_EULA=Y \
  -e MSSQL_PID=Developer \
  -e MSSQL_SA_PASSWORD="$sa_password" \
  "$image" >/dev/null

for candidate in /opt/mssql-tools18/bin/sqlcmd /opt/mssql-tools/bin/sqlcmd; do
  if docker exec "$container_name" test -x "$candidate" >/dev/null 2>&1; then
    sqlcmd_path="$candidate"
    break
  fi
done
[[ -n "$sqlcmd_path" ]] || { docker logs "$container_name" >&2 || true; fail "sqlcmd not found."; }

sqlcmd_base=(docker exec -i "$container_name" "$sqlcmd_path" -S localhost -U sa -P "$sa_password" -C -I -b -r1)
ready=0
for _ in $(seq 1 90); do
  if "${sqlcmd_base[@]}" -d master -Q 'SET NOCOUNT ON; SELECT 1;' >/dev/null 2>&1; then
    ready=1
    break
  fi
  sleep 1
done
[[ "$ready" == 1 ]] || { docker logs "$container_name" >&2 || true; fail "SQL Server did not become ready."; }

echo "EA0_SQLSERVER_VERSION=$("${sqlcmd_base[@]}" -d master -h -1 -W -Q "SET NOCOUNT ON; SELECT CAST(SERVERPROPERTY('ProductVersion') AS nvarchar(128));" | tr -d '\r' | tail -n1)"
"${sqlcmd_base[@]}" -d master -Q "CREATE DATABASE [$db_name];" >/dev/null

"${sqlcmd_base[@]}" -d "$db_name" <<'SQL'
SET NOCOUNT ON;
CREATE TABLE dbo.SchemaVersions
(
    VersionNumber NVARCHAR(50) NOT NULL CONSTRAINT PK_EA0_SchemaVersions PRIMARY KEY,
    Description NVARCHAR(500) NOT NULL,
    AppliedAt DATETIME2(3) NOT NULL,
    AppliedBy NVARCHAR(200) NULL
);
CREATE TABLE dbo.Users
(
    UserId INT NOT NULL CONSTRAINT PK_EA0_Users PRIMARY KEY
);
CREATE TABLE dbo.Employments
(
    EmploymentId BIGINT NOT NULL CONSTRAINT PK_EA0_Employments PRIMARY KEY
);
CREATE TABLE dbo.VisitTripSnapshots
(
    VisitTripSnapshotId BIGINT NOT NULL CONSTRAINT PK_EA0_VisitTripSnapshots PRIMARY KEY
);
CREATE TABLE dbo.SchemaMigrationDataBaselines
(
    MigrationVersion NVARCHAR(50) NOT NULL CONSTRAINT PK_EA0_SchemaMigrationDataBaselines PRIMARY KEY,
    VisitTripSnapshotCount BIGINT NOT NULL
);
INSERT dbo.SchemaVersions(VersionNumber, Description, AppliedAt, AppliedBy)
VALUES(N'1.8.0-005', N'E-A0 disposable prerequisite marker', SYSUTCDATETIME(), N'E-A0 disposable harness');
INSERT dbo.SchemaMigrationDataBaselines(MigrationVersion, VisitTripSnapshotCount)
VALUES(N'1.8.0-001', 0);
SQL

cat database/development/v1.8.0/compat/session-options.sql \
    database/migrations/1800_006_notification_framework/Up.sql \
  | "${sqlcmd_base[@]}" -d "$db_name"

echo 'EA0_1800_006_APPLY=PASS'

cat database/development/v1.8.0/compat/session-options.sql \
    database/migrations/1800_006_notification_framework/Verify.sql \
  | "${sqlcmd_base[@]}" -d "$db_name"

echo 'EA0_VERIFY_INITIAL=PASS'

"${sqlcmd_base[@]}" -d "$db_name" <<'SQL'
SET NOCOUNT ON;
SET XACT_ABORT OFF;

DECLARE @Rejected BIT;

-- A. Same BusinessEventKey + same RecipientKey is rejected, even with NULL email.
INSERT dbo.MailOutbox
(
    EnvironmentCode, EventCode, BusinessEventKey, EventOccurredAt, AggregateType, AggregateId,
    RecipientKey, RecipientEmail, TemplateCode, TemplateDataJson, Status, AvailableAt, CorrelationId
)
VALUES
(N'UAT',N'TripSubmitted',N'EA0-A',SYSUTCDATETIME(),N'Trip',N'1',N'EMP:1001',NULL,N'TripSubmitted.v1',N'{}',N'Pending',SYSUTCDATETIME(),NEWID());
SET @Rejected = 0;
BEGIN TRY
    INSERT dbo.MailOutbox
    (
        EnvironmentCode, EventCode, BusinessEventKey, EventOccurredAt, AggregateType, AggregateId,
        RecipientKey, RecipientEmail, TemplateCode, TemplateDataJson, Status, AvailableAt, CorrelationId
    )
    VALUES
    (N'UAT',N'TripSubmitted',N'EA0-A',SYSUTCDATETIME(),N'Trip',N'1',N'EMP:1001',NULL,N'TripSubmitted.v1',N'{}',N'Pending',SYSUTCDATETIME(),NEWID());
END TRY
BEGIN CATCH
    IF ERROR_NUMBER() IN(2601,2627) SET @Rejected = 1; ELSE THROW;
END CATCH;
IF @Rejected = 0 THROW 54200, N'EA0-A expected duplicate BusinessEventKey + RecipientKey rejection.', 1;
PRINT N'EA0_CASE_A=PASS';

-- NULL email rows do not collide when RecipientKey differs.
INSERT dbo.MailOutbox
(
    EnvironmentCode, EventCode, BusinessEventKey, EventOccurredAt, AggregateType, AggregateId,
    RecipientKey, RecipientEmail, TemplateCode, TemplateDataJson, Status, AvailableAt, CorrelationId
)
VALUES
(N'UAT',N'TripSubmitted',N'EA0-A-NULL',SYSUTCDATETIME(),N'Trip',N'2',N'EMP:1002',NULL,N'TripSubmitted.v1',N'{}',N'Pending',SYSUTCDATETIME(),NEWID()),
(N'UAT',N'TripSubmitted',N'EA0-A-NULL',SYSUTCDATETIME(),N'Trip',N'2',N'EMP:1003',NULL,N'TripSubmitted.v1',N'{}',N'Pending',SYSUTCDATETIME(),NEWID());
IF (SELECT COUNT(*) FROM dbo.MailOutbox WHERE BusinessEventKey=N'EA0-A-NULL') <> 2
    THROW 54201, N'EA0 null-email distinct recipients should coexist.', 1;
PRINT N'EA0_NULL_EMAIL_DISTINCT_RECIPIENTS=PASS';

-- B. Same business event + equivalent normalized non-null email is rejected.
INSERT dbo.MailOutbox
(
    EnvironmentCode, EventCode, BusinessEventKey, EventOccurredAt, AggregateType, AggregateId,
    RecipientKey, RecipientEmail, TemplateCode, TemplateDataJson, Status, AvailableAt, CorrelationId
)
VALUES
(N'UAT',N'TripSubmitted',N'EA0-B',SYSUTCDATETIME(),N'Trip',N'3',N'RULE:ONE',N'  Test.User@Example.invalid  ',N'TripSubmitted.v1',N'{}',N'Pending',SYSUTCDATETIME(),NEWID());
SET @Rejected = 0;
BEGIN TRY
    INSERT dbo.MailOutbox
    (
        EnvironmentCode, EventCode, BusinessEventKey, EventOccurredAt, AggregateType, AggregateId,
        RecipientKey, RecipientEmail, TemplateCode, TemplateDataJson, Status, AvailableAt, CorrelationId
    )
    VALUES
    (N'UAT',N'TripSubmitted',N'EA0-B',SYSUTCDATETIME(),N'Trip',N'3',N'RULE:TWO',N'test.user@example.invalid',N'TripSubmitted.v1',N'{}',N'Pending',SYSUTCDATETIME(),NEWID());
END TRY
BEGIN CATCH
    IF ERROR_NUMBER() IN(2601,2627) SET @Rejected = 1; ELSE THROW;
END CATCH;
IF @Rejected = 0 THROW 54202, N'EA0-B expected normalized email duplicate rejection.', 1;
PRINT N'EA0_CASE_B=PASS';

-- C. Different legitimate BusinessEventKey values are allowed.
INSERT dbo.MailOutbox
(
    EnvironmentCode, EventCode, BusinessEventKey, EventOccurredAt, AggregateType, AggregateId,
    RecipientKey, RecipientEmail, TemplateCode, TemplateDataJson, Status, AvailableAt, CorrelationId
)
VALUES
(N'UAT',N'TripSubmitted',N'EA0-C-RETURN',SYSUTCDATETIME(),N'Trip',N'4',N'EMP:2001',N'c@example.invalid',N'TripSubmitted.v1',N'{}',N'Pending',SYSUTCDATETIME(),NEWID()),
(N'UAT',N'TripSubmitted',N'EA0-C-RESUBMIT',SYSUTCDATETIME(),N'Trip',N'4',N'EMP:2001',N'c@example.invalid',N'TripSubmitted.v1',N'{}',N'Pending',SYSUTCDATETIME(),NEWID());
IF (SELECT COUNT(*) FROM dbo.MailOutbox WHERE BusinessEventKey IN(N'EA0-C-RETURN',N'EA0-C-RESUBMIT')) <> 2
    THROW 54203, N'EA0-C distinct BusinessEventKey values should be allowed.', 1;
PRINT N'EA0_CASE_C=PASS';

-- D. Processing without token/lease is rejected.
SET @Rejected = 0;
BEGIN TRY
    INSERT dbo.MailOutbox
    (
        EnvironmentCode, EventCode, BusinessEventKey, EventOccurredAt, AggregateType, AggregateId,
        RecipientKey, RecipientEmail, TemplateCode, TemplateDataJson, Status, AvailableAt, CorrelationId
    )
    VALUES
    (N'UAT',N'TripSubmitted',N'EA0-D',SYSUTCDATETIME(),N'Trip',N'5',N'EMP:3001',N'd@example.invalid',N'TripSubmitted.v1',N'{}',N'Processing',SYSUTCDATETIME(),NEWID());
END TRY
BEGIN CATCH
    IF ERROR_NUMBER()=547 SET @Rejected = 1; ELSE THROW;
END CATCH;
IF @Rejected = 0 THROW 54204, N'EA0-D Processing without token/lease should fail.', 1;
PRINT N'EA0_CASE_D=PASS';

-- E. Non-Processing with active token/lease is rejected.
SET @Rejected = 0;
BEGIN TRY
    INSERT dbo.MailOutbox
    (
        EnvironmentCode, EventCode, BusinessEventKey, EventOccurredAt, AggregateType, AggregateId,
        RecipientKey, RecipientEmail, TemplateCode, TemplateDataJson, Status, AvailableAt,
        ProcessingToken, ProcessingLeaseUntil, CorrelationId
    )
    VALUES
    (N'UAT',N'TripSubmitted',N'EA0-E',SYSUTCDATETIME(),N'Trip',N'6',N'EMP:3002',N'e@example.invalid',N'TripSubmitted.v1',N'{}',N'Pending',SYSUTCDATETIME(),NEWID(),DATEADD(MINUTE,5,SYSUTCDATETIME()),NEWID());
END TRY
BEGIN CATCH
    IF ERROR_NUMBER()=547 SET @Rejected = 1; ELSE THROW;
END CATCH;
IF @Rejected = 0 THROW 54205, N'EA0-E non-Processing ownership should fail.', 1;
PRINT N'EA0_CASE_E=PASS';

-- F. Processing with token + lease is allowed.
INSERT dbo.MailOutbox
(
    EnvironmentCode, EventCode, BusinessEventKey, EventOccurredAt, AggregateType, AggregateId,
    RecipientKey, RecipientEmail, TemplateCode, TemplateDataJson, Status, AvailableAt,
    ProcessingToken, ProcessingLeaseUntil, CorrelationId
)
VALUES
(N'UAT',N'TripSubmitted',N'EA0-F',SYSUTCDATETIME(),N'Trip',N'7',N'EMP:3003',N'f@example.invalid',N'TripSubmitted.v1',N'{}',N'Processing',SYSUTCDATETIME(),NEWID(),DATEADD(MINUTE,5,SYSUTCDATETIME()),NEWID());
IF NOT EXISTS(SELECT 1 FROM dbo.MailOutbox WHERE BusinessEventKey=N'EA0-F' AND Status=N'Processing' AND ProcessingToken IS NOT NULL AND ProcessingLeaseUntil IS NOT NULL)
    THROW 54206, N'EA0-F Processing with token + lease should be allowed.', 1;
PRINT N'EA0_CASE_F=PASS';

-- G. Final status without FinalizedAt is rejected.
SET @Rejected = 0;
BEGIN TRY
    INSERT dbo.MailOutbox
    (
        EnvironmentCode, EventCode, BusinessEventKey, EventOccurredAt, AggregateType, AggregateId,
        RecipientKey, RecipientEmail, TemplateCode, TemplateDataJson, Status, AvailableAt, CorrelationId
    )
    VALUES
    (N'UAT',N'TripSubmitted',N'EA0-G',SYSUTCDATETIME(),N'Trip',N'8',N'EMP:4001',N'g@example.invalid',N'TripSubmitted.v1',N'{}',N'Failed',SYSUTCDATETIME(),NEWID());
END TRY
BEGIN CATCH
    IF ERROR_NUMBER()=547 SET @Rejected = 1; ELSE THROW;
END CATCH;
IF @Rejected = 0 THROW 54207, N'EA0-G final status without FinalizedAt should fail.', 1;
PRINT N'EA0_CASE_G=PASS';

-- H. Pending and Processing with FinalizedAt are rejected.
SET @Rejected = 0;
BEGIN TRY
    INSERT dbo.MailOutbox
    (
        EnvironmentCode, EventCode, BusinessEventKey, EventOccurredAt, AggregateType, AggregateId,
        RecipientKey, RecipientEmail, TemplateCode, TemplateDataJson, Status, AvailableAt, FinalizedAt, CorrelationId
    )
    VALUES
    (N'UAT',N'TripSubmitted',N'EA0-H-PENDING',SYSUTCDATETIME(),N'Trip',N'9',N'EMP:4002',N'hp@example.invalid',N'TripSubmitted.v1',N'{}',N'Pending',SYSUTCDATETIME(),SYSUTCDATETIME(),NEWID());
END TRY
BEGIN CATCH
    IF ERROR_NUMBER()=547 SET @Rejected = 1; ELSE THROW;
END CATCH;
IF @Rejected = 0 THROW 54208, N'EA0-H Pending with FinalizedAt should fail.', 1;

SET @Rejected = 0;
BEGIN TRY
    INSERT dbo.MailOutbox
    (
        EnvironmentCode, EventCode, BusinessEventKey, EventOccurredAt, AggregateType, AggregateId,
        RecipientKey, RecipientEmail, TemplateCode, TemplateDataJson, Status, AvailableAt,
        ProcessingToken, ProcessingLeaseUntil, FinalizedAt, CorrelationId
    )
    VALUES
    (N'UAT',N'TripSubmitted',N'EA0-H-PROCESSING',SYSUTCDATETIME(),N'Trip',N'10',N'EMP:4003',N'hpr@example.invalid',N'TripSubmitted.v1',N'{}',N'Processing',SYSUTCDATETIME(),NEWID(),DATEADD(MINUTE,5,SYSUTCDATETIME()),SYSUTCDATETIME(),NEWID());
END TRY
BEGIN CATCH
    IF ERROR_NUMBER()=547 SET @Rejected = 1; ELSE THROW;
END CATCH;
IF @Rejected = 0 THROW 54209, N'EA0-H Processing with FinalizedAt should fail.', 1;
PRINT N'EA0_CASE_H=PASS';

-- I. Terminal Failed row with RecipientEmail=NULL is allowed.
INSERT dbo.MailOutbox
(
    EnvironmentCode, EventCode, BusinessEventKey, EventOccurredAt, AggregateType, AggregateId,
    RecipientKey, RecipientEmail, TemplateCode, TemplateDataJson, Status, AvailableAt,
    FinalizedAt, LastErrorCode, CorrelationId
)
VALUES
(N'UAT',N'TripSubmitted',N'EA0-I',SYSUTCDATETIME(),N'Trip',N'11',N'EMP:5001',NULL,N'TripSubmitted.v1',N'{}',N'Failed',SYSUTCDATETIME(),SYSUTCDATETIME(),N'NO_USABLE_EMAIL',NEWID());
IF NOT EXISTS(SELECT 1 FROM dbo.MailOutbox WHERE BusinessEventKey=N'EA0-I' AND Status=N'Failed' AND RecipientEmail IS NULL AND FinalizedAt IS NOT NULL)
    THROW 54210, N'EA0-I terminal Failed NULL-email row should be allowed.', 1;
PRINT N'EA0_CASE_I=PASS';

-- J. ProjectManager rule is dormant; Administrator remains active.
IF NOT EXISTS
(
    SELECT 1 FROM dbo.NotificationSettings s
    JOIN dbo.NotificationSettingRecipients r ON r.NotificationSettingId=s.NotificationSettingId
    WHERE s.EventCode=N'ProjectExpiring' AND r.RecipientRuleCode=N'ProjectManager' AND r.IsActive=0
)
    THROW 54211, N'EA0-J ProjectManager must be inactive.', 1;
IF NOT EXISTS
(
    SELECT 1 FROM dbo.NotificationSettings s
    JOIN dbo.NotificationSettingRecipients r ON r.NotificationSettingId=s.NotificationSettingId
    WHERE s.EventCode=N'ProjectExpiring' AND r.RecipientRuleCode=N'Administrator' AND r.IsActive=1
)
    THROW 54212, N'EA0-J Administrator must remain active.', 1;
PRINT N'EA0_CASE_J=PASS';

-- K. UAT Live remains rejected.
SET @Rejected = 0;
BEGIN TRY
    UPDATE dbo.NotificationEnvironmentPolicies SET EmailMode=N'Live' WHERE EnvironmentCode=N'UAT';
END TRY
BEGIN CATCH
    IF ERROR_NUMBER()=547 SET @Rejected = 1; ELSE THROW;
END CATCH;
IF @Rejected = 0 THROW 54213, N'EA0-K UAT Live should be rejected.', 1;
IF NOT EXISTS(SELECT 1 FROM dbo.NotificationEnvironmentPolicies WHERE EnvironmentCode=N'UAT' AND EmailMode=N'Test' AND IsEnabled=0)
    THROW 54214, N'EA0-K UAT policy changed unexpectedly.', 1;
PRINT N'EA0_CASE_K=PASS';
SQL

cat database/development/v1.8.0/compat/session-options.sql \
    database/migrations/1800_006_notification_framework/Verify.sql \
  | "${sqlcmd_base[@]}" -d "$db_name"

echo 'EA0_VERIFY_FINAL=PASS'
echo 'EA0_NOTIFICATION_SCHEMA_AUTHORITY=PASS'
