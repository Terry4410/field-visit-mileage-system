/*
 VAL-B1-002 read-only reconciliation candidate (NOT a migration; NEVER deploy).
 DBA/HR runs this only against an approved read-only copy or with a separately
 authorized read-only UAT session.  It does not insert, update, delete, grant,
 revoke, or execute any B3 approval path.

 Important: 1800_002 backfilled TeamLeaderAssignments from effective Leader
 role plus TeamMembership.  The current table has no SourceReference,
 AttestedBy, or AttestedAt column.  Consequently this output can establish
 current appointment/delegation facts, but CANNOT independently attest them.
 Every returned row remains BLOCKED_UNSIGNED until the Owner evidence form is
 completed and HR/IT supply a controlled evidence identifier.
*/
SET NOCOUNT ON;
DECLARE @AsOf date = CONVERT(date, SYSUTCDATETIME());

SELECT
    t.TeamId,
    t.TeamCode,
    e.EmploymentId,
    e.EmployeeNo,
    CASE WHEN EXISTS
    (
        SELECT 1
        FROM dbo.EmploymentStatusPeriods esp
        WHERE esp.EmploymentId = e.EmploymentId
          AND esp.EmploymentStatus = N'Active'
          AND esp.EffectiveFrom <= @AsOf
          AND (esp.EffectiveTo IS NULL OR esp.EffectiveTo >= @AsOf)
    ) THEN N'CURRENT_ACTIVE' ELSE N'NOT_CURRENT_ACTIVE' END AS HrStatus,
    CASE WHEN EXISTS
    (
        SELECT 1
        FROM dbo.EmploymentRoleAssignments era
        JOIN dbo.Roles r ON r.RoleId = era.RoleId
        WHERE era.EmploymentId = e.EmploymentId
          AND LOWER(LTRIM(RTRIM(r.RoleCode))) = N'leader'
          AND r.IsActive = 1
          AND era.EffectiveFrom <= @AsOf
          AND (era.EffectiveTo IS NULL OR era.EffectiveTo >= @AsOf)
    ) THEN N'CURRENT_LEADER_ROLE' ELSE N'NO_CURRENT_LEADER_ROLE' END AS LeaderRole,
    CASE WHEN EXISTS
    (
        SELECT 1
        FROM dbo.TeamMemberships tm
        WHERE tm.EmploymentId = e.EmploymentId AND tm.TeamId = t.TeamId
          AND tm.EffectiveFrom <= @AsOf
          AND (tm.EffectiveTo IS NULL OR tm.EffectiveTo >= @AsOf)
    ) THEN N'CURRENT_MEMBER' ELSE N'NOT_CURRENT_MEMBER' END AS TeamMembership,
    tla.TeamLeaderAssignmentId,
    tla.EffectiveFrom AS AppointmentFrom,
    tla.EffectiveTo AS AppointmentTo,
    tla.AssignedByUserId,
    d.TeamLeaderDelegationId,
    d.DelegateEmploymentId,
    d.EffectiveFrom AS DelegationFrom,
    d.EffectiveTo AS DelegationTo,
    N'BLOCKED_UNSIGNED_NO_ATTESTATION_COLUMNS' AS VAL_B1_002_Status
FROM dbo.TeamLeaderAssignments tla
JOIN dbo.Teams t ON t.TeamId = tla.TeamId
JOIN dbo.Employments e ON e.EmploymentId = tla.EmploymentId
LEFT JOIN dbo.TeamLeaderDelegations d ON d.TeamLeaderAssignmentId = tla.TeamLeaderAssignmentId
WHERE tla.EffectiveFrom <= @AsOf
  AND (tla.EffectiveTo IS NULL OR tla.EffectiveTo >= @AsOf)
ORDER BY t.TeamCode, e.EmployeeNo, d.EffectiveFrom;
