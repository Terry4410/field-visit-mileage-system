SET NOCOUNT ON;

IF DB_NAME() IS NULL
    THROW 55010, N'Baseline verification requires a database context.', 1;

IF OBJECT_ID(N'dbo.SchemaVersions', N'U') IS NULL
    THROW 55011, N'dbo.SchemaVersions is missing.', 1;

IF (SELECT COUNT_BIG(*) FROM dbo.SchemaVersions WHERE VersionNumber = N'1.8.0-007') <> 1
    THROW 55012, N'Expected exactly one 1.8.0-007 schema version row.', 1;

IF EXISTS
(
    SELECT required.ObjectName
    FROM
    (
        VALUES
            (N'Organizations'), (N'Teams'), (N'Centers'),
            (N'TeamCenterAssignments'), (N'Persons'), (N'Employments'),
            (N'EmploymentStatusPeriods'), (N'EmploymentRoleAssignments'),
            (N'TeamMemberships'), (N'TeamLeaderAssignments'),
            (N'TeamLeaderDelegations'), (N'DeploymentSites'),
            (N'DeploymentSiteLocationAssignments'),
            (N'TeamDeploymentSiteAssignments'),
            (N'EmploymentDeploymentSiteAssignments'), (N'TeamLocationNotes'),
            (N'TeamLocationNoteHistory'), (N'NotificationEnvironmentPolicies'),
            (N'NotificationEmailAllowlist'), (N'NotificationSettings'),
            (N'MailOutbox'), (N'MailDeliveryLogs'), (N'GeocodingAttempts'),
            (N'RouteCalculationAttempts'), (N'MileageGovernanceEvents')
    ) AS required(ObjectName)
    WHERE OBJECT_ID(N'dbo.' + required.ObjectName, N'U') IS NULL
)
    THROW 55013, N'One or more required v1.8.0 tables are missing.', 1;

IF COL_LENGTH(N'dbo.VisitTripSnapshots', N'CenterIdSnapshot') IS NULL
   OR COL_LENGTH(N'dbo.VisitTripSnapshots', N'StartDeploymentSiteIdSnapshot') IS NULL
   OR COL_LENGTH(N'dbo.VisitTripSnapshots', N'ApprovalBasisHashSnapshot') IS NULL
    THROW 55014, N'Required protected Snapshot columns are missing.', 1;

IF EXISTS
(
    SELECT EmployeeNo
    FROM dbo.Employments
    WHERE EmployeeNo IS NOT NULL
    GROUP BY OrganizationId, EmployeeNo
    HAVING COUNT_BIG(*) > 1
)
    THROW 55015, N'Duplicate employment number exists inside an organization.', 1;

IF EXISTS
(
    SELECT EmploymentId
    FROM dbo.TeamMemberships
    WHERE IsPrimary = 1
      AND EffectiveTo IS NULL
    GROUP BY EmploymentId
    HAVING COUNT_BIG(*) > 1
)
    THROW 55016, N'More than one current Primary Team exists for an employment.', 1;

SELECT
    N'PASS' AS BaselineVerification,
    DB_NAME() AS DatabaseName,
    N'1.8.0-007' AS ExpectedSchemaVersion,
    SYSUTCDATETIME() AS VerifiedAtUtc;
