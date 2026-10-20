CREATE TABLE dbo.FieldVisitMigrationLedger
(
 VersionNumber nvarchar(40) NOT NULL CONSTRAINT PK_FieldVisitMigrationLedger PRIMARY KEY,
 PackageId binary(32) NOT NULL,
 PayloadSha256 binary(32) NOT NULL,
 CatalogSha256 binary(32) NOT NULL,
 MarkersSha256 binary(32) NOT NULL,
 AppliedAt datetime2(3) NOT NULL,
 CONSTRAINT CK_FieldVisitMigrationLedger_Version CHECK(VersionNumber IN(N'1.8.0-011',N'1.8.0-012',N'1.8.0-014',N'1.8.0-015',N'1.8.0-016',N'1.8.0-017',N'1.8.0-018',N'1.8.0-019',N'1.8.0-020'))
);
