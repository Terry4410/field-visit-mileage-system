using FieldVisit.Domain.Entities;
using FieldVisit.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace FieldVisit.B3.SqlServer.Tests;

/// <summary>
/// Disposable-only expanded Locations column contract against current EF.
/// Does NOT prove the full production table, FK targets, indexes or UAT DDL.
/// </summary>
internal static class B312ExpandedLocationsColumnContract
{
    internal static async Task CreateAsync(SqlConnection conn,SqlTransaction tx)
    {
        if(!conn.Database.StartsWith("B3SqlFixture_",StringComparison.Ordinal))
            throw new InvalidOperationException("012_DISPOSABLE_ONLY");
        await B312DisposableForwardOnlyRehearsal.ExecuteAsync(conn,tx,"""
            CREATE TABLE dbo.Locations(
                LocationId int IDENTITY(1,1) NOT NULL PRIMARY KEY,
                OrganizationId int NULL,TeamId int NULL,
                LocationCode nvarchar(max) NULL,
                LocationName nvarchar(max) NOT NULL,
                LocationType nvarchar(max) NOT NULL,
                PostalCode nvarchar(max) NULL,
                City nvarchar(max) NULL,District nvarchar(max) NULL,
                Address nvarchar(max) NULL,PlusCode nvarchar(max) NULL,
                Latitude decimal(10,7) NULL,Longitude decimal(10,7) NULL,
                IsTemporary bit NOT NULL DEFAULT(0),
                ApprovalStatus nvarchar(max) NOT NULL,
                GeocodingStatus nvarchar(max) NOT NULL DEFAULT(N'Pending'),
                GeocodedAt datetime2(7) NULL,
                CreatedByUserId int NULL,IsActive bit NOT NULL,
                CreatedAt datetime2(7) NOT NULL DEFAULT(SYSUTCDATETIME()),
                UpdatedAt datetime2(7) NULL,
                SelectedGeocodingAttemptId bigint NULL,
                TaxId nvarchar(max) NULL,MasterNote nvarchar(max) NULL,
                InactivatedAt datetime2(7) NULL,
                InactivatedByUserId int NULL,
                DuplicateOfLocationId int NULL,
                DuplicateReason nvarchar(max) NULL,
                RowVersion rowversion NOT NULL
            )
            """);
    }

    internal static async Task RequireEfColumnsAsync(
        DisposableSqlServer fixture,SqlConnection conn,SqlTransaction tx)
    {
        if(conn.Database!=fixture.DatabaseName ||
            !conn.Database.StartsWith("B3SqlFixture_",StringComparison.Ordinal))
            throw new InvalidOperationException("012_DISPOSABLE_ONLY");
        await using var db=new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(fixture.DatabaseConnection).Options);
        var entity=db.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(Location))
            ?? throw new InvalidOperationException("012_EF_LOCATION_MISSING");
        var store=StoreObjectIdentifier.Create(entity,StoreObjectType.Table)
            ?? throw new InvalidOperationException("012_EF_TABLE_MISSING");
        var properties=entity.GetProperties()
            .Select(p=>new {Name=p.GetColumnName(store),
                Type=p.GetRelationalTypeMapping().StoreType.ToLowerInvariant(),
                p.IsNullable})
            .Where(x=>x.Name is not null)
            .ToDictionary(x=>x.Name!,StringComparer.Ordinal);
        await using var cmd=new SqlCommand("""
            SELECT c.name,t.name,c.max_length,c.is_nullable,c.precision,
                   c.scale,c.is_identity,c.system_type_id
            FROM sys.columns c
            JOIN sys.types t ON t.user_type_id=c.user_type_id
            WHERE c.object_id=OBJECT_ID(N'dbo.Locations',N'U')
            """,conn,tx);
        var columns=new Dictionary<string,(string Type,int Width,bool Nullable,
            int Precision,int Scale,bool Identity,int SystemId)>(StringComparer.Ordinal);
        await using(var reader=await cmd.ExecuteReaderAsync())
        {
            while(await reader.ReadAsync())
                columns.Add(reader.GetString(0),(reader.GetString(1).ToLowerInvariant(),
                    reader.GetInt16(2),reader.GetBoolean(3),reader.GetByte(4),
                    reader.GetByte(5),reader.GetBoolean(6),reader.GetByte(7)));
        }
        if(columns.Count!=properties.Count)
            throw new InvalidOperationException("012_LOCATIONS_COLUMN_COUNT_DRIFT");
        foreach(var (name,p) in properties)
        {
            if(!columns.TryGetValue(name,out var c) ||
               c.Nullable!=p.IsNullable ||
               c.Identity!=(name=="LocationId"))
                throw new InvalidOperationException("012_LOCATIONS_COLUMN_SHAPE_DRIFT");
            var type=p.Type.Replace(" ","",StringComparison.Ordinal);
            var ok=type switch
            {
                "int"=>c.Type=="int" && c.SystemId==56,
                "bigint"=>c.Type=="bigint" && c.SystemId==127,
                "bit"=>c.Type=="bit" && c.SystemId==104,
                "nvarchar(max)"=>c.Type=="nvarchar" && c.Width==-1
                    && c.SystemId==231,
                "decimal(10,7)"=>c.Type=="decimal" && c.Precision==10
                    && c.Scale==7 && c.SystemId==106,
                "datetime2" or "datetime2(7)"=>c.Type=="datetime2"
                    && c.Scale==7 && c.SystemId==42,
                "rowversion" or "timestamp"=>c.SystemId==189
                    && c.Width==8 && !c.Nullable,
                _=>false
            };
            if(!ok) throw new InvalidOperationException(
                "012_LOCATIONS_COLUMN_TYPE_DRIFT:"+name+":"+type);
        }
    }

    internal const string NonEditableSnapshotSql="""
        SELECT (SELECT LocationId,OrganizationId,TeamId,LocationCode,
                       LocationType,PostalCode,Latitude,Longitude,
                       IsTemporary,ApprovalStatus,GeocodingStatus,GeocodedAt,
                       CreatedByUserId,IsActive,CreatedAt,UpdatedAt,
                       SelectedGeocodingAttemptId,InactivatedAt,
                       InactivatedByUserId,DuplicateOfLocationId,DuplicateReason
                FROM dbo.Locations WHERE LocationId=@id
                FOR JSON PATH,WITHOUT_ARRAY_WRAPPER,INCLUDE_NULL_VALUES)
        """;
}
