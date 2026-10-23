/*
 * New consolidated v1.8.0 target-state constraints for route attempts.
 * Original constraints: 1800_007_mileage_google_governance/Up.sql.
 * Latest StopCount >= 1: 1800_009_one_stop_route_attempt_constraint/Up.sql.
 * Latest four allowed reasons: 1800_010_background_mileage_reason_constraint/Up.sql.
 * All sources from accepted RC 9d431ea318fcec34f04f3cc36a8fb6d01d00c48a.
 * NONDEPLOY / disposable SQL Server 2022 candidate only.
 * This is NOT a replay of historical migrations and sets NO historical markers.
 */
SET XACT_ABORT ON;
SET NOCOUNT ON;

IF DB_NAME() <> N'OptionCIsolated_FieldVisitV180CleanInstall'
    THROW 56040, N'CLEAN_ROUTE_DISPOSABLE_TARGET_REQUIRED', 1;
IF OBJECT_ID(N'dbo.RouteCalculationAttempts', N'U') IS NULL
   OR COL_LENGTH(N'dbo.RouteCalculationAttempts', N'CalculationReason') IS NULL
   OR COL_LENGTH(N'dbo.RouteCalculationAttempts', N'StopCount') IS NULL
    THROW 56041, N'CLEAN_ROUTE_SOURCE_TABLE_MISSING', 1;

IF EXISTS (
    SELECT 1 FROM sys.check_constraints
    WHERE parent_object_id=OBJECT_ID(N'dbo.RouteCalculationAttempts', N'U')
      AND name IN
      (N'CK_RouteCalculationAttempts_Basis',
       N'CK_RouteCalculationAttempts_Reason',
       N'CK_RouteCalculationAttempts_LeaderBasis',
       N'CK_RouteCalculationAttempts_Vehicle',
       N'CK_RouteCalculationAttempts_Mode',
       N'CK_RouteCalculationAttempts_VehicleMode',
       N'CK_RouteCalculationAttempts_StopCount',
       N'CK_RouteCalculationAttempts_Status',
       N'CK_RouteCalculationAttempts_Result')
)
    THROW 56042, N'CLEAN_ROUTE_PREEXISTING_CHECK_REQUIRES_REVIEW', 1;

ALTER TABLE dbo.RouteCalculationAttempts WITH CHECK ADD
    CONSTRAINT CK_RouteCalculationAttempts_Basis
    CHECK (
        (BasisType=N'Draft' AND BasisVisitTripSnapshotId IS NULL)
        OR (BasisType=N'SubmittedSnapshot' AND BasisVisitTripSnapshotId IS NOT NULL)
    );

ALTER TABLE dbo.RouteCalculationAttempts WITH CHECK ADD
    CONSTRAINT CK_RouteCalculationAttempts_Reason
    CHECK (
        CalculationReason=N'VisitorCalculate'
        OR CalculationReason=N'LeaderRetry'
        OR CalculationReason=N'CorrectionRecalculate'
        OR CalculationReason=N'BackgroundMileageJob'
    );

ALTER TABLE dbo.RouteCalculationAttempts WITH CHECK ADD
    CONSTRAINT CK_RouteCalculationAttempts_LeaderBasis
    CHECK (CalculationReason<>N'LeaderRetry' OR BasisType=N'SubmittedSnapshot');

ALTER TABLE dbo.RouteCalculationAttempts WITH CHECK ADD
    CONSTRAINT CK_RouteCalculationAttempts_Vehicle
    CHECK (RequestedVehicleType IN (N'Motorcycle', N'Car'));

ALTER TABLE dbo.RouteCalculationAttempts WITH CHECK ADD
    CONSTRAINT CK_RouteCalculationAttempts_Mode
    CHECK (TravelMode IN (N'DRIVE', N'TWO_WHEELER'));

ALTER TABLE dbo.RouteCalculationAttempts WITH CHECK ADD
    CONSTRAINT CK_RouteCalculationAttempts_VehicleMode
    CHECK (
        (RequestedVehicleType=N'Car' AND TravelMode=N'DRIVE')
        OR (RequestedVehicleType=N'Motorcycle' AND TravelMode=N'TWO_WHEELER')
    );

-- 1.8.0-009 supersedes the original 007 >= 2 check: one-stop trips allowed.
ALTER TABLE dbo.RouteCalculationAttempts WITH CHECK ADD
    CONSTRAINT CK_RouteCalculationAttempts_StopCount CHECK (StopCount >= 1);

ALTER TABLE dbo.RouteCalculationAttempts WITH CHECK ADD
    CONSTRAINT CK_RouteCalculationAttempts_Status
    CHECK (Status IN (N'Pending', N'Succeeded', N'Failed'));

ALTER TABLE dbo.RouteCalculationAttempts WITH CHECK ADD
    CONSTRAINT CK_RouteCalculationAttempts_Result
    CHECK (
        (Statu