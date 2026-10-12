using Microsoft.Data.SqlClient;

namespace FieldVisit.B3.SqlServer.Tests;

/// <summary>
/// WORK-only read-only simulation of live HR/Admin/Team authorization.
/// Deliberately named B312Auth* tables: NOT authoritative HR master tables,
/// NOT a product code permission service and NOT approval authority.
/// The caller owns the same rollback-only serializable SQL transaction.
/// </summary>
internal static class B312EffectiveDatedAuthorityFixture
{
    internal static readonly DateTime AsOf=new(2026,10,12);

    internal static async Task CreateAsync(SqlConnection conn,SqlTransaction tx)
    {
        if(!conn.Database.StartsWith("B3SqlFixture_",StringComparison.Ordinal))
            throw new InvalidOperationException("B312_AUTH_TEST_DATABASE_REQUIRED");
        await B312DisposableForwardOnlyRehearsal.ExecuteAsync(conn,tx,"""
            CREATE TABLE dbo.B312AuthActors (
                UserId int NOT NULL PRIMARY KEY,
                OrganizationId int NOT NULL,IsActive bit NOT NULL,
                UserType nvarchar(20) NOT NULL);
            CREATE TABLE dbo.B312AuthEmployment (
                UserId int NOT NULL,Status nvarchar(30) NOT NULL,
                EffectiveFrom date NOT NULL,EffectiveTo date NULL);
            CREATE TABLE dbo.B312AuthRoles (
                UserId int NOT NULL,RoleCode nvarchar(30) NOT NULL,
                IsActive bit NOT NULL,EffectiveFrom date NOT NULL,
                EffectiveTo date NULL);
            CREATE TABLE dbo.B312AuthTeams (
                TeamId int NOT NULL PRIMARY KEY,
                OrganizationId int NOT NULL,IsActive bit NOT NULL,
                EffectiveFrom date NOT NULL,EffectiveTo date NULL);
            CREATE TABLE dbo.B312AuthTeamScopes (
                UserId int NOT NULL,TeamId int NOT NULL,IsActive bit NOT NULL);
            CREATE TABLE dbo.B312AuthTeamAssignments (
                UserId int NOT NULL,TeamId int NOT NULL,
                EffectiveFrom date NOT NULL,EffectiveTo date NULL);
            CREATE TABLE dbo.B312AuthMemberships (
                UserId int NOT NULL,TeamId int NOT NULL,
                EffectiveFrom date NOT NULL,EffectiveTo date NULL);
            INSERT INTO dbo.B312AuthActors VALUES
               (10,1,1,N'Internal'),(20,1,1,N'Internal');
            INSERT INTO dbo.B312AuthEmployment VALUES
               (10,N'Active','2026-01-01',NULL),
               (20,N'Active','2026-01-01',NULL);
            INSERT INTO dbo.B312AuthRoles VALUES
               (20,N'admin',1,'2026-01-01',NULL);
            INSERT INTO dbo.B312AuthTeams VALUES
               (7,1,1,'2026-01-01',NULL);
            INSERT INTO dbo.B312AuthTeamScopes VALUES(10,7,1);
            INSERT INTO dbo.B312AuthTeamAssignments
               VALUES(10,7,'2026-01-01',NULL);
            INSERT INTO dbo.B312AuthMemberships
               VALUES(10,7,'2026-01-01',NULL);
            """);
    }

    internal static async Task RequireAsync(
        SqlConnection conn,SqlTransaction tx,DateTime asOf,
        int requester=10,int reviewer=20,int organization=1,int team=7)
    {
        if(!conn.Database.StartsWith("B3SqlFixture_",StringComparison.Ordinal)
            || asOf.Kind==DateTimeKind.Utc && asOf.TimeOfDay!=TimeSpan.Zero
            || requester<=0 || reviewer<=0 || requester==reviewer
            || organization<=0 || team<=0)
            throw new UnauthorizedAccessException("B312_AUTHORITY_NOT_PROVEN");
        // Deliberately repeat the effective-date constraints at each layer.
        // EffectiveTo is inclusive. Missing, expired or contradictory rows
        // are denied; role/read scope/JWT alone never authorize mutation.
        var sql="""
            SELECT CAST(CASE WHEN
                EXISTS (SELECT 1 FROM dbo.B312AuthActors
                    WHERE UserId=@requester AND OrganizationId=@org
                      AND IsActive=1 AND UserType=N'Internal')
                AND EXISTS (SELECT 1 FROM dbo.B312AuthActors
                    WHERE UserId=@reviewer AND OrganizationId=@org
                      AND IsActive=1 AND UserType=N'Internal')
                AND EXISTS (SELECT 1 FROM dbo.B312AuthEmployment
                    WHERE UserId=@requester AND Status=N'Active'
                      AND EffectiveFrom<=@date
                      AND (EffectiveTo IS NULL OR EffectiveTo>=@date))
                AND NOT EXISTS (SELECT 1 FROM dbo.B312AuthEmployment
                    WHERE UserId=@requester AND Status<>N'Active'
                      AND EffectiveFrom<=@date
                      AND (EffectiveTo IS NULL OR EffectiveTo>=@date))
                AND EXISTS (SELECT 1 FROM dbo.B312AuthEmployment
                    WHERE UserId=@reviewer AND Status=N'Active'
                      AND EffectiveFrom<=@date
                      AND (EffectiveTo IS NULL OR EffectiveTo>=@date))
                AND NOT EXISTS (SELECT 1 FROM dbo.B312AuthEmployment
                    WHERE UserId=@reviewer AND Status<>N'Active'
                      AND EffectiveFrom<=@date
                      AND (EffectiveTo IS NULL OR EffectiveTo>=@date))
                AND EXISTS (SELECT 1 FROM dbo.B312AuthRoles
                    WHERE UserId=@reviewer AND RoleCode=N'admin'
                      AND IsActive=1 AND EffectiveFrom<=@date
                      AND (EffectiveTo IS NULL OR EffectiveTo>=@date))
                AND EXISTS (SELECT 1 FROM dbo.B312AuthTeams
                    WHERE TeamId=@team AND OrganizationId=@org
                      AND IsActive=1 AND EffectiveFrom<=@date
                      AND (EffectiveTo IS NULL OR EffectiveTo>=@date))
                AND EXISTS (SELECT 1 FROM dbo.B312AuthTeamScopes
                    WHERE UserId=@requester AND TeamId=@team AND IsActive=1)
                AND EXISTS (SELECT 1 FROM dbo.B312AuthTeamAssignments
                    WHERE UserId=@requester AND TeamId=@team
                      AND EffectiveFrom<=@date
                      AND (EffectiveTo IS NULL OR EffectiveTo>=@date))
                AND EXISTS (SELECT 1 FROM dbo.B312AuthMemberships
                    WHERE UserId=@requester AND TeamId=@team
                      AND EffectiveFrom<=@date
                      AND (EffectiveTo IS NULL OR EffectiveTo>=@date))
                THEN 1 ELSE 0 END AS int)
            """;
        await using var cmd=new SqlCommand(sql,conn,tx){CommandTimeout=25};
        cmd.Parameters.AddWithValue("@date",asOf.Date);
        cmd.Parameters.AddWithValue("@requester",requester);
        cmd.Parameters.AddWithValue("@reviewer",reviewer);
        cmd.Parameters.AddWithValue("@org",organization);
        cmd.Parameters.AddWithValue("@team",team);
        if(Convert.ToInt32(await cmd.ExecuteScalarAsync())!=1)
            throw new UnauthorizedAccessException("B312_AUTHORITY_NOT_PROVEN");
    }
}
