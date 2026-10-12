using FieldVisit.Application;
using FieldVisit.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace FieldVisit.B3.SqlServer.Tests;

/// <summary>
/// Calls production V180B3ActorEligibility.RequireAsync against disposable SQL
/// with EF-mapped Users, UserIdentityProfiles and both legacy/employment-linked
/// HR status paths. This is NOT reviewer Admin permission validation, live
/// HR source attestation or authority to enable any B3 endpoint.
/// The caller supplies one rollback-only serializable transaction.
/// </summary>
internal static class B312RealB3ActorSqlFixture
{
    internal static async Task CreateAsync(SqlConnection conn,SqlTransaction tx)
    {
        if(!conn.Database.StartsWith("B3SqlFixture_",StringComparison.Ordinal))
            throw new InvalidOperationException("B312_B3_ACTOR_DISPOSABLE_ONLY");
        await B312V170LoginIntegrationFixture.CreateAsync(conn,tx);
        await B312DisposableForwardOnlyRehearsal.ExecuteAsync(conn,tx,"""
            ALTER TABLE dbo.Users ADD
                OrganizationId int NULL,
                TeamId int NULL,
                EmployeeNo nvarchar(max) NULL,
                DisplayName nvarchar(max) NOT NULL
                    CONSTRAINT DF_B312_Users_Display DEFAULT(N''),
                Email nvarchar(max) NULL,
                EntraObjectId uniqueidentifier NULL,
                IsActive bit NOT NULL
                    CONSTRAINT DF_B312_Users_Active DEFAULT(1),
                CreatedAt datetime2(7) NOT NULL
                    CONSTRAINT DF_B312_Users_Created DEFAULT(SYSUTCDATETIME()),
                UpdatedAt datetime2(7) NULL;
            UPDATE dbo.Users SET OrganizationId=1,TeamId=7,
                EmployeeNo=N'B312-10',DisplayName=N'SQL Test Actor',
                IsActive=1 WHERE UserId=10;
            CREATE TABLE dbo.Employments(
                EmploymentId bigint NOT NULL PRIMARY KEY,
                PersonId bigint NOT NULL,
                OrganizationId int NOT NULL,
                EmployeeNo nvarchar(50) NULL,
                Email nvarchar(320) NULL,
                HireDate date NULL,
                TerminationDate date NULL,
                LegacyUserId int NULL,
                SourceType nvarchar(30) NOT NULL,
                SourceReference nvarchar(200) NULL,
                RowVersion rowversion NOT NULL
            );
            CREATE TABLE dbo.EmploymentStatusPeriods(
                EmploymentStatusPeriodId bigint IDENTITY(1,1) NOT NULL PRIMARY KEY,
                EmploymentId bigint NOT NULL,
                EmploymentStatus nvarchar(30) NOT NULL,
                EffectiveFrom date NOT NULL,
                EffectiveTo date NULL,
                SourceType nvarchar(30) NOT NULL,
                SourceReference nvarchar(200) NULL,
                RowVersion rowversion NOT NULL
            );
            """);
    }

    internal static CurrentUserDto Actor(int? org=1,int userId=10,
        IReadOnlyList<string>? assertedRoles=null)=>new(
            userId,"B312-10","SQL Test Actor",null,org,7,"Team7",
            assertedRoles??new[]{"admin"});

    internal static async Task RequireAsync(SqlConnection conn,SqlTransaction tx,
        CurrentUserDto? actor=null)
    {
        if(!conn.Database.StartsWith("B3SqlFixture_",StringComparison.Ordinal))
            throw new InvalidOperationException("B312_B3_ACTOR_DISPOSABLE_ONLY");
        await using var db=new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(conn).Options);
        await db.Database.UseTransactionAsync(tx);
        await V180B3ActorEligibility.RequireAsync(
            db,actor??Actor(),CancellationToken.None);
    }

    internal static async Task EnableEmploymentLinkedHrAsync(
        SqlConnection conn,SqlTransaction tx)
    {
        await B312DisposableForwardOnlyRehearsal.ExecuteAsync(conn,tx,"""
            INSERT INTO dbo.Employments(
                EmploymentId,PersonId,OrganizationId,EmployeeNo,
                HireDate,TerminationDate,LegacyUserId,SourceType)
            VALUES (600,900,1,N'B312-10','2020-01-01',NULL,10,N'TestOnly');
            INSERT INTO dbo.EmploymentStatusPeriods(
                EmploymentId,EmploymentStatus,EffectiveFrom,EffectiveTo,SourceType)
            VALUES(600,N'Active','2020-01-01',NULL,N'TestOnly');
            UPDATE dbo.UserIdentityProfiles SET EmploymentId=600 WHERE UserId=10;
            """);
    }
}
