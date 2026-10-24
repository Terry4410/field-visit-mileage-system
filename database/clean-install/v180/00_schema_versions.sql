-- Field Visit v1.8.0 CLEAN INSTALL: new consolidated-baseline ledger shape.
-- This is NOT a historical v1.5/1.6/1.7/1.8 migration replay.
-- No historical migration markers are inserted by the partial installer.
-- Review this newly authored target DDL before IT acceptance.
CREATE TABLE dbo.SchemaVersions
(
    VersionNumber NVARCHAR(40) NOT NULL
        CONSTRAINT PK_SchemaVersions PRIMARY KEY,
    Description NVARCHAR(500) NOT NULL,
    AppliedAt DATETIME2(3) NOT NULL,
    AppliedBy NVARCHAR(128) NOT NULL
);
