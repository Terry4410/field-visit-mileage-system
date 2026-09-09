SET NOCOUNT ON;
SET XACT_ABORT ON;

IF NOT EXISTS (SELECT 1 FROM dbo.SchemaVersions WHERE VersionNumber = N'1.8.0-007')
    THROW 55020, N'Development harness verify failed: schema version metadata missing.', 1;

IF EXISTS
(
    SELECT 1
    FROM sys.database_principals
    WHERE name NOT IN (N'dbo', N'guest', N'INFORMATION_SCHEMA', N'sys', N'public')
      AND type IN ('S','U','G','E','X')
)
    THROW 55021, N'Development harness verify failed: environment principal found.', 1;

IF EXISTS (SELECT 1 FROM dbo.Users)
    THROW 55022, N'Development harness verify failed: user/business data found.', 1;

IF EXISTS (SELECT 1 FROM dbo.Organizations)
    THROW 55023, N'Development harness verify failed: organization data found.', 1;

IF EXISTS (SELECT 1 FROM dbo.VisitTrips)
    THROW 55024, N'Development harness verify failed: transaction data found.', 1;

IF EXISTS (SELECT 1 FROM dbo.Persons)
   OR EXISTS (SELECT 1 FROM dbo.Employments)
   OR EXISTS (SELECT 1 FROM dbo.EmploymentStatusPeriods)
   OR EXISTS (SELECT 1 FROM dbo.EmploymentRoleAssignments)
   OR EXISTS (SELECT 1 FROM dbo.TeamMemberships)
   OR EXISTS (SELECT 1 FROM dbo.TeamLeaderAssignments)
   OR EXISTS (SELECT 1 FROM dbo.TeamLeaderDelegations)
    THROW 55025, N'Development harness verify failed: personnel/assignment data found.', 1;

IF EXISTS (SELECT 1 FROM dbo.Teams)
   OR EXISTS (SELECT 1 FROM dbo.Centers)
   OR EXISTS (SELECT 1 FROM dbo.TeamCenterAssignments)
   OR EXISTS (SELECT 1 FROM dbo.DeploymentSites)
   OR EXISTS (SELECT 1 FROM dbo.Locations)
   OR EXISTS (SELECT 1 FROM dbo.Projects)
   OR EXISTS (SELECT 1 FROM dbo.VisitTypes)
   OR EXISTS (SELECT 1 FROM dbo.MileageRateRules)
    THROW 55026, N'Development harness verify failed: business master data found.', 1;

IF EXISTS (SELECT 1 FROM dbo.VisitTripSnapshots)
   OR EXISTS (SELECT 1 FROM dbo.VisitTripSnapshotStops)
   OR EXISTS (SELECT 1 FROM dbo.RouteCalculationAttempts)
   OR EXISTS (SELECT 1 FROM dbo.GeocodingAttempts)
   OR EXISTS (SELECT 1 FROM dbo.MileageGovernanceEvents)
   OR EXISTS (SELECT 1 FROM dbo.MailOutbox)
   OR EXISTS (SELECT 1 FROM dbo.MailDeliveryLogs)
    THROW 55027, N'Development harness verify failed: transaction/operational data found.', 1;

SELECT N'PASS' AS DevelopmentHarnessVerification, DB_NAME() AS DatabaseName;
