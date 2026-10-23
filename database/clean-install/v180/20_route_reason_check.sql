/*
  Field Visit v1.8.0 CLEAN INSTALL — source-derived target constraint only.
  Derived from 1800_010_background_mileage_reason_constraint/Up.sql
  at immutable accepted RC 9d431ea318fcec34f04f3cc36a8fb6d01d00c48a.
  This is NOT executing old migration, and DOES NOT insert a historical marker.
  NONDEPLOY — only FieldVisitV180CleanInstall disposable SQL2022.
*/
SET XACT_ABORT ON;
SET NOCOUNT ON;

IF DB_NAME() <> N'FieldVisitV180CleanInstall'
    THROW 56040, N'CLEAN_REASON_DISPOSABLE_TARGET_REQUIRED', 1;
IF OBJECT_ID(N'dbo.RouteCalculationAttempts', N'U') IS NULL
   OR COL_LENGTH(N'dbo.RouteCalculationAttempts', N'CalculationReason') IS NULL
    THROW 56041, N'CLEAN_REASON_SOURCE_TABLE_MISSING', 1;
IF OBJECT_ID(N'dbo.CK_RouteCalculationAttempts_Reason', N'C') IS NOT NULL
    THROW 56042, N'CLEAN_REASON_EXISTING_CONSTRAINT_REQUIRES_REVIEW', 1;

ALTER TABLE dbo.RouteCalculationAttempts WITH CHECK ADD
    CONSTRAINT CK_RouteCalculationAttempts_Reason
    CHECK
    (
        CalculationReason = N'VisitorCalculate'
        OR CalculationReason = N'LeaderRetry'
        OR CalculationReason = N'CorrectionRecalculate'
        OR CalculationReason = N'BackgroundMileageJob'
    );

ALTER TABLE dbo.RouteCalculationAttempts
    CHECK CONSTRAINT CK_RouteCalculationAttempts_Reason;
