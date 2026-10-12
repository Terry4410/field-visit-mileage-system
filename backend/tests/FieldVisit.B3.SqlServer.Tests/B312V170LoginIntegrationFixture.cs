using FieldVisit.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace FieldVisit.B3.SqlServer.Tests;

/// <summary>
/// Actual .NET V170AccessControl.EvaluateLoginAsync against real SQL Server
/// with the EXISTING EF names/fields used in the legacy-Internal HR path.
/// Entire set of tables/seed rows is created inside the already rollback-only
/// disposable 011-to-012 transaction. Never deployed or used as live HR truth.
/// </summary>
internal static class B312V170LoginIntegrationFixture
{
    internal static async Task CreateAsync(SqlConnection conn,SqlTransaction tx)
    {
        if(!conn.Database.StartsWith("B3SqlFixture_",StringComparison.Ordinal))
            throw new InvalidOperationException("B312_DISPOSABLE_LOGIN_TEST_ONLY");
        await B312DisposableForwardOnlyRehearsal.ExecuteAsync(conn,tx,"""
            CREATE TABLE dbo.UserIdentityProfiles (
                UserId int NOT NULL PRIMARY KEY,
                EmploymentId bigint NULL,
                UserType nvarchar(max) NOT NULL,
                UserCode nvarchar(max) NOT NULL,
                IdentityProvider nvarchar(max) NOT NULL,
                EntraTenantId uniqueidentifier NULL,
                EntraObjectId uniqueidentifier NULL,
                ExternalOrganization nvarchar(max) NULL,
                ExternalTitle nvarchar(max) NULL,
                AuthorizationFrom date NULL,
                AuthorizationTo date NULL,
                CreatedAt datetime2(7) NOT NULL,
                UpdatedAt datetime2(7) NULL
            );
            CREATE TABLE dbo.UserEmploymentPeriods (
                UserEmploymentPeriodId bigint IDENTITY(1,1) NOT NULL PRIMARY KEY,
                UserId int NOT NULL,
                EmploymentStatus nvarchar(max) NOT NULL,
                EffectiveFrom date NOT NULL,
                EffectiveTo date NULL,
                SourceType nvarchar(max) NOT NULL,
                SourceReference nvarchar(max) NULL,
                CreatedAt datetime2(7) NOT NULL,
                UpdatedAt datetime2(7) NULL
            );
            INSERT INTO dbo.UserIdentityProfiles(
                UserId,EmploymentId,UserType,UserCode,
                IdentityProvider,CreatedAt)
            VALUES (10,NULL,N'Internal',N'B312User10',N'Demo',
                    SYSUTCDATETIME());
            INSERT INTO dbo.UserEmploymentPeriods(
                UserId,EmploymentStatus,EffectiveFrom,EffectiveTo,
                SourceType,CreatedAt)
            VALUES (10,N'Active','2020-01-01',NULL,N'TestOnly',
                    SYSUTCDATETIME());
            """);
    }

    internal static async Task<bool> IsAllowedAsync(
        SqlConnection conn,SqlTransaction tx,int user=10,
        bool accountEnabled=true)
    {
        if(!conn.Database.StartsWith("B3SqlFixture_",StringComparison.Ordinal)
            || user<=0)
            throw new InvalidOperationException("B312_DISPOSABLE_LOGIN_TEST_ONLY");
        await using var db=new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlServer(conn).Options);
        await db.Database.UseTransactionAsync(tx);
        var result=await new V170AccessControl(db).EvaluateLoginAsync(
            user,accountEnabled,CancellationToken.None);
        return result.IsAllowed;
    }
}
