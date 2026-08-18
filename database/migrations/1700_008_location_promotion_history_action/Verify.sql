SET NOCOUNT ON;

/* 1. Migration must exist exactly once. */
SELECT
    VersionNumber,
    Description,
    AppliedAt,
    AppliedBy
FROM dbo.SchemaVersions
WHERE VersionNumber = N'1.7.0-008';


/* 2. CHECK constraint must be enabled, trusted and contain new action. */
SELECT
    cc.name AS ConstraintName,
    cc.is_disabled AS IsDisabled,
    cc.is_not_trusted AS IsNotTrusted,
    CASE
        WHEN UPPER(cc.definition)
             LIKE N'%''PROMOTEDTOOFFICIAL''%'
        THEN 'YES'
        ELSE 'NO'
    END AS PromotedToOfficialAllowed,
    cc.definition AS ConstraintDefinition
FROM sys.check_constraints cc
WHERE cc.parent_object_id =
      OBJECT_ID(N'dbo.LocationApprovalHistory')
  AND cc.name =
      N'CK_LocationApprovalHistory_Action';


/* 3. Confirm Action column still has enough capacity. */
SELECT
    COLUMN_NAME,
    DATA_TYPE,
    CHARACTER_MAXIMUM_LENGTH,
    IS_NULLABLE
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_SCHEMA = N'dbo'
  AND TABLE_NAME = N'LocationApprovalHistory'
  AND COLUMN_NAME = N'Action';


/* 4. Current actions remain intact; after UAT retry a
      PromotedToOfficial row should appear. */
SELECT
    Action,
    COUNT(*) AS Qty
FROM dbo.LocationApprovalHistory
GROUP BY Action
ORDER BY Action;


/* 5. UAT sample before/after promotion. */
SELECT
    LocationId,
    LocationCode,
    LocationName,
    IsTemporary,
    ApprovalStatus,
    GeocodingStatus,
    IsActive
FROM dbo.Locations
WHERE LocationId = 56
   OR LocationCode = N'LOC-260817-5B5319';
