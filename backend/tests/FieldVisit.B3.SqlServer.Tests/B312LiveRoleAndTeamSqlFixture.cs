using FieldVisit.Application;
using FieldVisit.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace FieldVisit.B3.SqlServer.Tests;

/// <summary>
/// Actual shared B3 live role and team read-query execution over the EF
/// table names, only in the always-rolled-back disposable SQL transaction.
/// Simulated HR/IT grants; not an authority attestation or approval executor.
/// </summary>
internal static class B312LiveRoleAndTeamSqlFixture
{
    internal static async Task CreateAsync(SqlConnection conn,SqlTransaction tx)
    {
        if(!conn.Database.StartsWith("B3SqlFixture_",StringComparison.Ordinal))
            throw new InvalidOperationException("B312_LIVE_SCOPE_DISPOSABLE_ONLY");
        await B312RealB3ActorSqlFixture.CreateAsync(conn,tx);
        await B312RealB3ActorSqlFixture.EnableEmploymentLinkedHrAsync(conn,tx);
        await B312DisposableForwardOnlyRehearsal.ExecuteAsync(conn,tx,"""
            ALTER TABLE dbo.Teams ADD
                OrganizationId int NOT NULL
                    CONSTRAINT DF_B312_TeamOrg DEFAULT(1),
                TeamCode nvarchar(max) NOT NULL
                    CONSTRAINT DF_B312_TeamCode DEFAULT(N'B312T'),
                TeamName nvarchar(max) NOT NULL
                    CONSTRAINT DF_B312_TeamName DEFAULT(N'Team'),
                IsActive bit NOT NULL
                    CONSTRAINT DF_B312_TeamActive DEFAULT(1),
                EffectiveFrom date NULL,EffectiveTo date NULL,
                CreatedAt datetime2(7) NOT NULL
                    CONSTRAINT DF_B312_TeamCreated DEFAULT(SYSUTCDATETIME()),
                UpdatedAt datetime2(7) NULL;
            CREATE TABLE dbo.Roles (
                RoleId int NOT NULL PRIMARY KEY,RoleCode nvarchar(max) NOT NULL,
                RoleName nvarchar(max) NOT NULL,Description nvarchar(max) NULL,
                IsActive bit NOT NULL,CreatedAt datetime2(7) NOT NULL);
            CREATE TABLE dbo.UserRoles (
                UserRoleId int NOT NULL PRIMARY KEY,UserId int NOT NULL,
                RoleId int NOT NULL,AssignedAt datetime2(7) NOT NULL);
            CREATE TABLE dbo.UserRoleAssignments (
                UserRoleAssignmentId bigint NOT NULL PRIMARY KEY,
                UserId int NOT NULL,RoleId int NOT NULL,
                EffectiveFrom date NOT NULL,EffectiveTo date NULL,
                AssignedByUserId int NULL,CreatedAt datetime2(7) NOT NULL);
            CREATE TABLE dbo.UserTeamScopes (
                UserTeamScopeId int NOT NULL PRIMARY KEY,
                UserId int NOT NULL,TeamId int NOT NULL,IsPrimary bit NOT NULL,
                IsActive bit NOT NULL,AssignedAt datetime2(7) NOT NULL,
                AssignedByUserId int NULL,EndedAt datetime2(7) NULL);
            CREATE TABLE dbo.UserTeamAssignments (
                UserTeamAssignmentId bigint NOT NULL PRIMARY KEY,
                UserId int NOT NULL,TeamId int NOT NULL,IsPrimary bit NOT NULL,
                EffectiveFrom date NOT NULL,EffectiveTo date NULL,
                AssignedByUserId int NULL,CreatedAt datetime2(7) NOT NULL);
            CREATE TABLE dbo.TeamMemberships (
                TeamMembershipId bigint NOT NULL PRIMARY KEY,
                EmploymentId bigint NOT NULL,TeamId int NOT NULL,
                IsPrimary bit NOT NULL,EffectiveFrom date NOT NULL,
                EffectiveTo date NULL,ChangeReason nvarchar(500) NULL,
                AssignedByUserId int NULL,RowVersion rowversion NOT NULL);
            INSERT INTO dbo.Roles
                (RoleId,RoleCode,RoleName,IsActive,CreatedAt)
            VALUES (1,N'admin',N'Admin',1,SYSUTCDATETIME()),
                   (2,N'visitor',N'Visitor',1,SYSUTCDATETIME());
            INSERT INTO dbo.UserRoles(UserRoleId,UserId,RoleId,AssignedAt)
            VALUES(101,10,1,SYSUTCDATETIME()),
                  (102,10,2,SYSUTCDATETIME());
            INSERT INTO dbo.UserRoleAssignments(
                UserRoleAssignmentId,UserId,RoleId,EffectiveFrom,CreatedAt)
            VALUES(101,10,1,'2020-01-01',SYSUTCDATETIME()),
                  (102,10,2,'2020-01-01',SYSUTCDATETIME());
            INSERT INTO dbo.UserTeamScopes
                (UserTeamScopeId,UserId,TeamId,IsPrimary,IsActive,AssignedAt)
            VALUES(101,10,7,1,1,SYSUTCDATETIME());
            INSERT INTO dbo.UserTeamAssignments
                (UserTeamAssignmentId,UserId,TeamId,IsPrimary,EffectiveFrom,CreatedAt)
            VALUES(101,10,7,1,'2020-01-01',SYSUTCDATETIME());
            INSERT INTO dbo.TeamMemberships
                (TeamMembershipId,EmploymentId,TeamId,IsPrimary,EffectiveFrom)
            VALUES(101,600,7,1,'2020-01-01');
            """);
    }

    internal static async Task<(bool Admin,bool Visitor)> ReadRolesAsync(
        SqlConnection conn,SqlTransaction tx,IReadOnlyList<string>? tokenRoles=null)
    {
        if(!conn.Database.StartsWith("B3SqlFixture_",StringComparison.Ordinal))
            throw new InvalidOperationException("B312_LIVE_SCOPE_DISPOSABLE_ONLY");
        await using var db=new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(conn).Options);
        await db.Database.UseTransactionAsync(tx);
        var result=await V180B3LiveAuthorizationRead.EvaluateActorAsync(
            db,B312RealB3ActorSqlFixture.Actor(
                assertedRoles:tokenRoles??new[]{"admin","visitor"}),
            BusinessTime.Today,CancellationToken.None);
        return(result.Admin,result.Visitor);
    }

    internal static async Task<bool> ReadTeamAsync(
        SqlConnection conn,SqlTransaction tx,int userId=10,
        int organizationId=1,int teamId=7)
    {
        if(!conn.Database.StartsWith("B3SqlFixture_",StringComparison.Ordinal))
            throw new InvalidOperationException("B312_LIVE_SCOPE_DISPOSABLE_ONLY");
        await using var db=new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(conn).Options);
        await db.Database.UseTransactionAsync(tx);
        return await V180B3LiveAuthorizationRead.IsVisitorTeamValidAsync(
            db,userId,organizationId,teamId,BusinessTime.Today,CancellationToken.None);
    }
}
