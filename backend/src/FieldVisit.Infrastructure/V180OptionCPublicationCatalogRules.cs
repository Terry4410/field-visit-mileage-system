using Microsoft.Data.SqlClient;

namespace FieldVisit.Infrastructure;

/// <summary>
/// Fail-closed catalog contract for the isolated Option C publication writer.
/// This is a read-only preflight. It never creates objects, runs migrations,
/// changes permissions, or treats a database name as schema proof.
/// </summary>
internal static class V180OptionCPublicationCatalogRules
{
    internal const string CatalogNotVerified = "OPTIONC_PUBLICATION_SCHEMA_NOT_VERIFIED";

    internal static async Task RequireAsync(
        SqlConnection connection, CancellationToken ct)
    {
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync(ct);
        if (!connection.Database.StartsWith("OptionCIsolated_",
                StringComparison.Ordinal))
            throw new InvalidOperationException(CatalogNotVerified);

        await using var command = new SqlCommand("""
            SELECT CAST(CASE WHEN
                OBJECT_ID(N'dbo.ChangeRequests',N'U') IS NOT NULL
                AND OBJECT_ID(N'dbo.ChangeRequestEvents',N'U') IS NOT NULL
                AND OBJECT_ID(N'dbo.LocationPublicationStages',N'U') IS NOT NULL
                AND OBJECT_ID(N'dbo.LocationPublishedVersions',N'U') IS NOT NULL
                AND NOT EXISTS (
                    SELECT 1 FROM (VALUES
                      (N'ChangeRequests',N'ChangeRequestId'),
                      (N'ChangeRequests',N'RequestPublicId'),
                      (N'ChangeRequests',N'OrganizationId'),
                      (N'ChangeRequests',N'TeamId'),
                      (N'ChangeRequests',N'EntityId'),
                      (N'ChangeRequests',N'ExpectedEntityRowVersion'),
                      (N'ChangeRequests',N'ProposedJson'),
                      (N'ChangeRequests',N'RequestedByUserId'),
                      (N'ChangeRequests',N'Status'),
                      (N'ChangeRequests',N'RowVersion'),
                      (N'ChangeRequestEvents',N'ChangeRequestId'),
                      (N'ChangeRequestEvents',N'EventType'),
                      (N'ChangeRequestEvents',N'ActorUserId'),
                      (N'ChangeRequestEvents',N'CorrelationId'),
                      (N'ChangeRequestEvents',N'DecisionKey'),
                      (N'LocationPublicationStages',N'ChangeRequestId'),
                      (N'LocationPublicationStages',N'LocationId'),
                      (N'LocationPublicationStages',N'ExpectedLocationRowVersion'),
                      (N'LocationPublicationStages',N'ProposedJson'),
                      (N'LocationPublicationStages',N'ProposedSha256'),
                      (N'LocationPublicationStages',N'StageStatus'),
                      (N'LocationPublicationStages',N'GeocodeStatus'),
                      (N'LocationPublicationStages',N'DuplicateReviewStatus'),
                      (N'LocationPublicationStages',N'DuplicateEvidenceSha256'),
                      (N'LocationPublicationStages',N'GeocodeAddressSha256'),
                      (N'LocationPublicationStages',N'Latitude'),
                      (N'LocationPublicationStages',N'Longitude'),
                      (N'LocationPublicationStages',N'RowVersion'),
                      (N'LocationPublishedVersions',N'LocationId'),
                      (N'LocationPublishedVersions',N'VersionNumber'),
                      (N'LocationPublishedVersions',N'ChangeRequestId'),
                      (N'LocationPublishedVersions',N'IsCurrent'),
                      (N'LocationPublishedVersions',N'PublishedJson'),
                      (N'LocationPublishedVersions',N'PublishedSha256'),
                      (N'LocationPublishedVersions',N'PublishedByUserId'),
                      (N'LocationPublishedVersions',N'SourceLocationRowVersion')
                    ) required(TableName,ColumnName)
                    WHERE OBJECT_ID(N'dbo.'+required.TableName,N'U') IS NULL
                       OR COL_LENGTH(N'dbo.'+required.TableName,required.ColumnName) IS NULL)
                AND EXISTS (
                    SELECT 1 FROM sys.indexes
                    WHERE object_id=OBJECT_ID(N'dbo.LocationPublicationStages')
                      AND name=N'UX_OptionC_Stages_Request'
                      AND is_unique=1 AND has_filter=0)
                AND EXISTS (
                    SELECT 1 FROM sys.indexes
                    WHERE object_id=OBJECT_ID(N'dbo.LocationPublicationStages')
                      AND name=N'UX_OptionC_Stages_OpenLocation'
                      AND is_unique=1 AND has_filter=1
                      AND LOWER(REPLACE(REPLACE(REPLACE(
                          ISNULL(filter_definition,N''),' ',''),'[',''),']',''))
                          LIKE N'%stagestatus%')
                AND EXISTS (
                    SELECT 1 FROM sys.indexes
                    WHERE object_id=OBJECT_ID(N'dbo.LocationPublishedVersions')
                      AND name=N'UX_OptionC_Published_Current'
                      AND is_unique=1 AND has_filter=1
                      AND LOWER(REPLACE(REPLACE(REPLACE(
                          ISNULL(filter_definition,N''),' ',''),'[',''),']',''))
                          LIKE N'%iscurrent%1%')
                AND EXISTS (
                    SELECT 1 FROM sys.indexes
                    WHERE object_id=OBJECT_ID(N'dbo.LocationPublishedVersions')
                      AND name=N'UX_OptionC_Published_Request'
                      AND is_unique=1 AND has_filter=1
                      AND LOWER(REPLACE(REPLACE(REPLACE(
                          ISNULL(filter_definition,N''),' ',''),'[',''),']',''))
                          LIKE N'%changerequestid%notnull%')
                AND EXISTS (
                    SELECT 1 FROM sys.indexes
                    WHERE object_id=OBJECT_ID(N'dbo.LocationPublishedVersions')
                      AND name=N'UX_OptionC_Published_Number'
                      AND is_unique=1 AND has_filter=0)
                AND EXISTS (
                    SELECT 1 FROM sys.foreign_keys
                    WHERE name=N'FK_OptionC_Stage_Request'
                      AND parent_object_id=OBJECT_ID(N'dbo.LocationPublicationStages')
                      AND referenced_object_id=OBJECT_ID(N'dbo.ChangeRequests')
                      AND is_disabled=0 AND is_not_trusted=0)
                AND EXISTS (
                    SELECT 1 FROM sys.foreign_keys
                    WHERE name=N'FK_OptionC_Stage_Location'
                      AND parent_object_id=OBJECT_ID(N'dbo.LocationPublicationStages')
                      AND referenced_object_id=OBJECT_ID(N'dbo.Locations')
                      AND is_disabled=0 AND is_not_trusted=0)
                AND EXISTS (
                    SELECT 1 FROM sys.foreign_keys
                    WHERE name=N'FK_OptionC_Stage_Org'
                      AND parent_object_id=OBJECT_ID(N'dbo.LocationPublicationStages')
                      AND referenced_object_id=OBJECT_ID(N'dbo.Organizations')
                      AND is_disabled=0 AND is_not_trusted=0)
                AND EXISTS (
                    SELECT 1 FROM sys.foreign_keys
                    WHERE name=N'FK_OptionC_Stage_Team'
                      AND parent_object_id=OBJECT_ID(N'dbo.LocationPublicationStages')
                      AND referenced_object_id=OBJECT_ID(N'dbo.Teams')
                      AND is_disabled=0 AND is_not_trusted=0)
                AND EXISTS (
                    SELECT 1 FROM sys.foreign_keys
                    WHERE name=N'FK_OptionC_Published_Location'
                      AND parent_object_id=OBJECT_ID(N'dbo.LocationPublishedVersions')
                      AND referenced_object_id=OBJECT_ID(N'dbo.Locations')
                      AND is_disabled=0 AND is_not_trusted=0)
                AND EXISTS (
                    SELECT 1 FROM sys.foreign_keys
                    WHERE name=N'FK_OptionC_Published_Request'
                      AND parent_object_id=OBJECT_ID(N'dbo.LocationPublishedVersions')
                      AND referenced_object_id=OBJECT_ID(N'dbo.ChangeRequests')
                      AND is_disabled=0 AND is_not_trusted=0)
                AND NOT EXISTS (
                    SELECT 1
                    FROM (VALUES
                        (N'CK_OptionC_Stage_Hash',OBJECT_ID(N'dbo.LocationPublicationStages')),
                        (N'CK_OptionC_Stage_AddressBinding',OBJECT_ID(N'dbo.LocationPublicationStages')),
                        (N'CK_OptionC_Stage_DuplicateBinding',OBJECT_ID(N'dbo.LocationPublicationStages')),
                        (N'CK_OptionC_Stage_Ready',OBJECT_ID(N'dbo.LocationPublicationStages')),
                        (N'CK_OptionC_Published_Hash',OBJECT_ID(N'dbo.LocationPublishedVersions')),
                        (N'CK_OptionC_Published_Origin',OBJECT_ID(N'dbo.LocationPublishedVersions'))
                    ) required(Name,ParentObject)
                    LEFT JOIN sys.check_constraints c
                      ON c.name=required.Name
                     AND c.parent_object_id=required.ParentObject
                    WHERE c.object_id IS NULL
                       OR c.is_disabled=1 OR c.is_not_trusted=1)
                AND NOT EXISTS (
                    SELECT 1 FROM sys.check_constraints
                    WHERE parent_object_id IN (
                        OBJECT_ID(N'dbo.LocationPublicationStages'),
                        OBJECT_ID(N'dbo.LocationPublishedVersions'))
                      AND (is_disabled=1 OR is_not_trusted=1))
            THEN 1 ELSE 0 END AS int) AS Value
            """, connection);
        var result = Convert.ToInt32(await command.ExecuteScalarAsync(ct));
        if (result != 1)
            throw new InvalidOperationException(CatalogNotVerified);
    }
}
