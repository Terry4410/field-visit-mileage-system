-- Existing Azure SQL Schema 1.5.0 verification only.
-- 本檔不建立第二套 Table，也不 DROP / DELETE 任何既有資料。
SELECT TOP (10) VersionNumber, Description, AppliedAt, AppliedBy
FROM dbo.SchemaVersions ORDER BY AppliedAt DESC;

SELECT TABLE_NAME, COLUMN_NAME, DATA_TYPE
FROM INFORMATION_SCHEMA.COLUMNS
WHERE (TABLE_NAME='VisitTrips' AND COLUMN_NAME IN ('StartTime','EndTime','HasTimeOverlapWarning','TimeOverlapConfirmed','ReturnReason','RowVersion'))
   OR (TABLE_NAME='Locations' AND COLUMN_NAME IN ('TeamId','PlusCode','GeocodingStatus','GeocodedAt','RowVersion'))
   OR (TABLE_NAME='Projects' AND COLUMN_NAME IN ('TeamId','LocationMode'))
ORDER BY TABLE_NAME, ORDINAL_POSITION;

SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME='VisitTripStatusHistory';
