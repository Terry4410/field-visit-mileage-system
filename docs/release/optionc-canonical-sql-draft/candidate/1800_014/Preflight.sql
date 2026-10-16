/* OPTION C 014 CANDIDATE — NONEXECUTABLE / ISOLATED REHEARSAL ONLY.
   013 is reserved and must not be occupied. This file deliberately refuses
   UAT, Production, and any database outside the disposable OptionCIsolated_*
   namespace. It is a reviewed design candidate, not migration authorization. */
SET NOCOUNT ON;
IF DB_NAME() NOT LIKE N'OptionCIsolated[_]%'
    THROW 51400, N'OPTIONC_014_DRAFT_DISPOSABLE_DATABASE_ONLY', 1;
IF OBJECT_ID(N'dbo.SchemaVersions',N'U') IS NULL
    OR OBJECT_ID(N'dbo.ChangeRequests',N'U') IS NULL
    OR OBJECT_ID(N'dbo.ChangeRequestEvents',N'U') IS NULL
    OR OBJECT_ID(N'dbo.Locations',N'U') IS NULL
    THROW 51401, N'OPTIONC_014_DRAFT_PREREQUISITE_CATALOG_INCOMPLETE', 1;
IF (SELECT TOP(1) VersionNumber FROM dbo.SchemaVersions
    ORDER BY AppliedAt DESC, VersionNumber DESC) <> N'1.8.0-012'
    THROW 51402, N'OPTIONC_014_DRAFT_REQUIRES_CANONICAL_012', 1;
IF OBJECT_ID(N'dbo.LocationPublicationStages',N'U') IS NOT NULL
    OR OBJECT_ID(N'dbo.LocationPublishedVersions',N'U') IS NOT NULL
    THROW 51403, N'OPTIONC_014_DRAFT_OBJECT_DRIFT_OR_ALREADY_APPLIED', 1;
