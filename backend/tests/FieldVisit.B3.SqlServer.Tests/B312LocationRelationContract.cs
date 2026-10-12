using Microsoft.Data.SqlClient;

namespace FieldVisit.B3.SqlServer.Tests;

/// <summary>
/// WORK-only extra relation contract for a reduced set of actual EF relationship
/// names/columns; all DDL is inside the disposable rollback-only 012 transaction.
/// This is NOT full UAT FK/Index parity nor a migration. No app executor wiring.
/// </summary>
internal static class B312LocationRelationContract
{
    internal static async Task BuildAsync(SqlConnection conn,SqlTransaction tx)
    {
        if(!conn.Database.StartsWith("B3SqlFixture_",StringComparison.Ordinal))
            throw new InvalidOperationException("012_DISPOSABLE_ONLY");
        await B312DisposableForwardOnlyRehearsal.ExecuteAsync(conn,tx,"""
            CREATE TABLE dbo.GeocodingAttempts(
                GeocodingAttemptId bigint NOT NULL PRIMARY KEY);
            CREATE TABLE dbo.VisitTripStops(
                VisitTripStopId bigint IDENTITY(1,1) NOT NULL PRIMARY KEY,
                LocationId int NOT NULL, RecordedName nvarchar(200) NULL);
            CREATE TABLE dbo.TeamLocationNotes(
                TeamLocationNoteId bigint IDENTITY(1,1) NOT NULL PRIMARY KEY,
                LocationId int NOT NULL, Note nvarchar(200) NULL);
            CREATE TABLE dbo.TeamLocationNoteHistory(
                TeamLocationNoteHistoryId bigint IDENTITY(1,1) NOT NULL PRIMARY KEY,
                LocationId int NOT NULL, PreviousNote nvarchar(200) NULL);
            ALTER TABLE dbo.Locations WITH CHECK ADD CONSTRAINT
                FK_B312_Locations_DuplicateOfLocationId
                FOREIGN KEY(DuplicateOfLocationId)
                REFERENCES dbo.Locations(LocationId) ON DELETE NO ACTION;
            ALTER TABLE dbo.Locations WITH CHECK ADD CONSTRAINT
                FK_B312_Locations_SelectedGeocodingAttemptId
                FOREIGN KEY(SelectedGeocodingAttemptId)
                REFERENCES dbo.GeocodingAttempts(GeocodingAttemptId)
                ON DELETE NO ACTION;
            ALTER TABLE dbo.VisitTripStops WITH CHECK ADD CONSTRAINT
                FK_B312_VisitTripStops_LocationId
                FOREIGN KEY(LocationId) REFERENCES dbo.Locations(LocationId)
                ON DELETE NO ACTION;
            ALTER TABLE dbo.TeamLocationNotes WITH CHECK ADD CONSTRAINT
                FK_B312_TeamLocationNotes_LocationId
                FOREIGN KEY(LocationId) REFERENCES dbo.Locations(LocationId)
                ON DELETE NO ACTION;
            ALTER TABLE dbo.TeamLocationNoteHistory WITH CHECK ADD CONSTRAINT
                FK_B312_TeamLocationNoteHistory_LocationId
                FOREIGN KEY(LocationId) REFERENCES dbo.Locations(LocationId)
                ON DELETE NO ACTION;
            CREATE INDEX IX_Locations_SelectedGeocodingAttempt
                ON dbo.Locations(SelectedGeocodingAttemptId)
                WHERE SelectedGeocodingAttemptId IS NOT NULL;
            """);
    }

    internal static async Task RequireShapeAsync(SqlConnection conn,SqlTransaction tx)
    {
        var expected=new HashSet<string>(StringComparer.Ordinal)
        {
            "FK_B312_Locations_DuplicateOfLocationId|Locations|DuplicateOfLocationId|Locations|LocationId",
            "FK_B312_Locations_SelectedGeocodingAttemptId|Locations|SelectedGeocodingAttemptId|GeocodingAttempts|GeocodingAttemptId",
            "FK_B312_VisitTripStops_LocationId|VisitTripStops|LocationId|Locations|LocationId",
            "FK_B312_TeamLocationNotes_LocationId|TeamLocationNotes|LocationId|Locations|LocationId",
            "FK_B312_TeamLocationNoteHistory_LocationId|TeamLocationNoteHistory|LocationId|Locations|LocationId"
        };
        await using var cmd=new SqlCommand("""
            SELECT fk.name,OBJECT_NAME(fk.parent_object_id),p.name,
                OBJECT_NAME(fk.referenced_object_id),r.name,
                fk.is_disabled,fk.is_not_trusted,
                fk.delete_referential_action,fk.update_referential_action,
                fkc.constraint_column_id
            FROM sys.foreign_keys fk
            JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id=fk.object_id
            JOIN sys.columns p ON p.object_id=fkc.parent_object_id
                AND p.column_id=fkc.parent_column_id
            JOIN sys.columns r ON r.object_id=fkc.referenced_object_id
                AND r.column_id=fkc.referenced_column_id
            WHERE fk.parent_object_id IN
                (OBJECT_ID(N'dbo.Locations',N'U'),
                 OBJECT_ID(N'dbo.VisitTripStops',N'U'),
                 OBJECT_ID(N'dbo.TeamLocationNotes',N'U'),
                 OBJECT_ID(N'dbo.TeamLocationNoteHistory',N'U'))
            """,conn,tx);
        var actual=new HashSet<string>(StringComparer.Ordinal);
        await using(var rd=await cmd.ExecuteReaderAsync())
        {
            while(await rd.ReadAsync())
            {
                if(rd.GetBoolean(5)||rd.GetBoolean(6)
                    ||rd.GetByte(7)!=0||rd.GetByte(8)!=0
                    ||rd.GetInt32(9)!=1)
                    throw new InvalidOperationException("012_LOCATION_FK_UNTRUSTED_OR_CASCADE");
                var key=string.Join("|",Enumerable.Range(0,5).Select(rd.GetString));
                if(!actual.Add(key))
                    throw new InvalidOperationException("012_LOCATION_FK_DUPLICATE");
            }
        }
        if(!actual.SetEquals(expected))
            throw new InvalidOperationException("012_LOCATION_FK_SHAPE_DRIFT");
        var index=await B312DisposableForwardOnlyRehearsal.ScalarAsync(conn,tx,"""
            SELECT CAST(CASE WHEN COUNT(*)=1 THEN 1 ELSE 0 END AS int)
            FROM sys.indexes i
            WHERE i.object_id=OBJECT_ID(N'dbo.Locations',N'U')
              AND i.name=N'IX_Locations_SelectedGeocodingAttempt'
              AND i.is_unique=0 AND i.has_filter=1 AND i.is_disabled=0
              AND i.is_hypothetical=0
              AND REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
                  LOWER(i.filter_definition),N'[',N''),N']',N''),
                  N' ',N''),N'(',N''),N')',N'')
                  =N'selectedgeocodingattemptidisnotnull'
              AND (SELECT COUNT(*) FROM sys.index_columns ic
                   WHERE ic.object_id=i.object_id AND ic.index_id=i.index_id
                     AND ic.key_ordinal>0)=1
              AND EXISTS(SELECT 1 FROM sys.index_columns ic
                JOIN sys.columns c ON c.object_id=ic.object_id
                    AND c.column_id=ic.column_id
                WHERE ic.object_id=i.object_id AND ic.index_id=i.index_id
                  AND ic.key_ordinal=1
                  AND c.name=N'SelectedGeocodingAttemptId')
            """);
        if(Convert.ToInt32(index)!=1)
            throw new InvalidOperationException("012_LOCATION_FILTERED_INDEX_DRIFT");
    }
}
