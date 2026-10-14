using FieldVisit.Domain.Entities;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace FieldVisit.Infrastructure;

/// <summary>
/// Scheme A fail-closed boundary for the optional additive CreateLocation
/// reservation table. Neither legacy Draft maintenance nor queued geocoding
/// may modify a reserved Location, even when it remains Pending/inactive.
/// </summary>
internal static class V180OptionCCreateReservationGuard
{
    internal static async Task<bool> HasValidatedSchemaAsync(
        AppDbContext db, CancellationToken ct)
    {
        // Production is SQL Server. Keep SQLite/InMemory legacy unit fixtures
        // unaffected; the isolated SQL HTTP suite exercises the real boundary.
        if (!db.Database.IsSqlServer()) return false;
        await db.Database.OpenConnectionAsync(ct);
        var sql = db.Database.GetDbConnection() as SqlConnection
            ?? throw new InvalidOperationException("OPTIONC_RESERVATION_SQLSERVER_REQUIRED");
        await using var check = new SqlCommand("""
            SELECT CASE
              WHEN OBJECT_ID(N'dbo.LocationCreateReservations',N'U') IS NOT NULL
                AND EXISTS (SELECT 1 FROM dbo.SchemaVersions
                            WHERE VersionNumber=N'1.8.0-CREATE-LOWRISK') THEN 1
              WHEN OBJECT_ID(N'dbo.LocationCreateReservations',N'U') IS NULL
                AND NOT EXISTS (SELECT 1 FROM dbo.SchemaVersions
                                WHERE VersionNumber=N'1.8.0-CREATE-LOWRISK') THEN 0
              ELSE -1 END
            """, sql);
        var schemaState = Convert.ToInt32(await check.ExecuteScalarAsync(ct));
        if (schemaState < 0)
            throw new InvalidOperationException("OPTIONC_CREATE_RESERVATION_SCHEMA_DRIFT");
        return schemaState == 1;
    }

    internal static async Task RequireUnreservedDraftAsync(
        AppDbContext db, int locationId, CancellationToken ct)
    {
        if (locationId <= 0)
            throw new InvalidOperationException("OPTIONC_CREATE_RESERVATION_LOCATION_INVALID");
        if (!await HasValidatedSchemaAsync(db, ct)) return;
        var sql = db.Database.GetDbConnection() as SqlConnection
            ?? throw new InvalidOperationException("OPTIONC_RESERVATION_SQLSERVER_REQUIRED");
        await using var check = new SqlCommand("""
            SELECT CASE WHEN EXISTS (
                SELECT 1 FROM dbo.LocationCreateReservations WITH(HOLDLOCK)
                WHERE LocationId=@location) THEN 1 ELSE 0 END
            """, sql);
        check.Parameters.AddWithValue("@location", locationId);
        if (Convert.ToInt32(await check.ExecuteScalarAsync(ct)) != 0)
            throw new InvalidOperationException(
                "LOCATION_CANONICAL_CREATE_RESERVATION_REQUIRES_REVIEW");
    }
}
