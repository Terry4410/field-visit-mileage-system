using Microsoft.Data.SqlClient;

namespace FieldVisit.B3.SqlServer.Tests;

/// <summary>
/// WORK-only test for a materialized SnapshotStop (not a foreign key to
/// Locations). It checks frozen descriptive values, not full historical
/// authenticity, published snapshots, or actual UAT schemas.
/// </summary>
internal static class B312SnapshotHistoricalInvariantFixture
{
    internal static async Task CreateAsync(SqlConnection conn,SqlTransaction tx)
    {
        if(!conn.Database.StartsWith("B3SqlFixture_",StringComparison.Ordinal))
            throw new InvalidOperationException("B312_SNAPSHOT_DISPOSABLE_ONLY");
        await B312DisposableForwardOnlyRehearsal.ExecuteAsync(conn,tx,"""
            CREATE TABLE dbo.VisitTripSnapshots(
                VisitTripSnapshotId bigint NOT NULL PRIMARY KEY,
                ApprovedDistanceKmSnapshot decimal(10,2) NOT NULL,
                OrganizationNameSnapshot nvarchar(200) NOT NULL);
            CREATE TABLE dbo.VisitTripSnapshotStops(
                VisitTripSnapshotStopId bigint IDENTITY(1,1) NOT NULL PRIMARY KEY,
                VisitTripSnapshotId bigint NOT NULL,
                StopSequence int NOT NULL,LocationId int NULL,
                LocationNameSnapshot nvarchar(200) NOT NULL,
                AddressSnapshot nvarchar(500) NULL,
                ProjectNameSnapshot nvarchar(200) NULL,
                CONSTRAINT FK_B312_SnapshotStops_Snapshot
                  FOREIGN KEY(VisitTripSnapshotId)
                  REFERENCES dbo.VisitTripSnapshots(VisitTripSnapshotId)
                  ON DELETE NO ACTION);
            CREATE UNIQUE INDEX UX_B312_SnapshotStop_Sequence
                ON dbo.VisitTripSnapshotStops(VisitTripSnapshotId,StopSequence);
            """);
    }

    internal static async Task SeedAsync(SqlConnection conn,SqlTransaction tx,
        int locationId)
    {
        await B312DisposableForwardOnlyRehearsal.ExecuteAsync(conn,tx,"""
            INSERT INTO dbo.VisitTripSnapshots
                (VisitTripSnapshotId,ApprovedDistanceKmSnapshot,OrganizationNameSnapshot)
            VALUES(901,12.50,N'Org at approval time');
            INSERT INTO dbo.VisitTripSnapshotStops(
                VisitTripSnapshotId,StopSequence,LocationId,
                LocationNameSnapshot,AddressSnapshot,ProjectNameSnapshot)
            VALUES(901,1,@location,N'Original fixture location',
                N'Original address',N'Frozen project');
            """,("@location",locationId));
    }

    internal static async Task<string> CaptureAsync(
        SqlConnection conn,SqlTransaction tx,long snapshot=901)
    {
        var result=await B312DisposableForwardOnlyRehearsal.ScalarAsync(conn,tx,"""
            SELECT (SELECT s.VisitTripSnapshotId,s.ApprovedDistanceKmSnapshot,
               s.OrganizationNameSnapshot,t.VisitTripSnapshotStopId,t.StopSequence,
               t.LocationId,t.LocationNameSnapshot,t.AddressSnapshot,
               t.ProjectNameSnapshot
               FROM dbo.VisitTripSnapshots s
               JOIN dbo.VisitTripSnapshotStops t
                   ON s.VisitTripSnapshotId=t.VisitTripSnapshotId
               WHERE s.VisitTripSnapshotId=@id
               ORDER BY t.StopSequence
               FOR JSON PATH,INCLUDE_NULL_VALUES)
            """,("@id",snapshot));
        return result as string ?? throw new InvalidOperationException("012_SNAPSHOT_MISSING");
    }

    internal static async Task RequireFrozenAsync(
        SqlConnection conn,SqlTransaction tx,string before)
    {
        if(before.Length<20 || !string.Equals(before,await CaptureAsync(conn,tx),
                StringComparison.Ordinal))
            throw new InvalidOperationException("012_SNAPSHOT_HISTORY_DRIFT");
    }
}
