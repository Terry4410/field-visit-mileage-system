using FieldVisit.Application;
using FieldVisit.Domain.Entities;
using FieldVisit.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

internal sealed class Ea1Fixture(string connectionString)
{
    public readonly DateTime EventAt = new(2026, 9, 11, 10, 0, 0, DateTimeKind.Utc);
    public readonly DateTime ProcessingAt = new(2026, 9, 11, 10, 5, 0, DateTimeKind.Utc);

    public AppDbContext NewDb() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseSqlServer(connectionString)
        .ReplaceService<IModelCustomizer, NotificationModelCustomizer>()
        .Options);

    public async Task SeedAsync()
    {
        await using var db = NewDb();
        await db.Database.ExecuteSqlRawAsync("""
INSERT dbo.Users(UserId,OrganizationId,TeamId,EmployeeNo,DisplayName,Email,IsActive,CreatedAt) VALUES
(1001,1,10,N'U1001',N'Initiator',N'initiator@example.invalid',1,SYSUTCDATETIME()),
(1002,1,10,N'U1002',N'Legacy Linked',N'legacy-linked@example.invalid',1,SYSUTCDATETIME()),
(1003,1,10,N'U1003',N'Email Fallback',N'fallback@example.invalid',1,SYSUTCDATETIME()),
(1004,1,10,N'U1004',N'No Email',NULL,1,SYSUTCDATETIME()),
(1011,1,10,N'U1011',N'Dedup Person',N'dedup@example.invalid',1,SYSUTCDATETIME());
INSERT dbo.Employments(EmploymentId,PersonId,OrganizationId,EmployeeNo,Email,HireDate,TerminationDate,LegacyUserId,SourceType,CreatedAt,OptionalEmailNotificationEnabled) VALUES
(2001,1,1,N'E2001',N'owner@example.invalid','2020-01-01',NULL,1001,N'Test',SYSUTCDATETIME(),0),
(2002,2,1,N'E2002',N'shared-leader@example.invalid','2020-01-01',NULL,NULL,N'Test',SYSUTCDATETIME(),1),
(2003,3,1,N'E2003',N'shared-leader@example.invalid','2020-01-01',NULL,NULL,N'Test',SYSUTCDATETIME(),1),
(2004,4,1,N'E2004',N'admin@example.invalid','2020-01-01',NULL,NULL,N'Test',SYSUTCDATETIME(),1),
(2005,5,1,N'E2005',N'reminder-off@example.invalid','2020-01-01',NULL,NULL,N'Test',SYSUTCDATETIME(),0),
(2006,6,1,N'E2006',N'reminder-on@example.invalid','2020-01-01',NULL,NULL,N'Test',SYSUTCDATETIME(),1),
(2007,7,1,N'E2007',N'future-leader@example.invalid','2026-10-01',NULL,NULL,N'Test',SYSUTCDATETIME(),1),
(2008,8,1,N'E2008',N'legacy-linked@example.invalid','2020-01-01',NULL,1002,N'Test',SYSUTCDATETIME(),1),
(2009,9,1,N'E2009',NULL,'2020-01-01',NULL,1003,N'Test',SYSUTCDATETIME(),1),
(2010,10,1,N'E2010',NULL,'2020-01-01',NULL,1004,N'Test',SYSUTCDATETIME(),1),
(2011,11,1,N'E2011',N'dedup@example.invalid','2020-01-01',NULL,1011,N'Test',SYSUTCDATETIME(),1);
INSERT dbo.UserIdentityProfiles(UserId,EmploymentId,UserType,UserCode,IdentityProvider,CreatedAt) VALUES(1001,2001,N'Internal',N'U1001',N'Test',SYSUTCDATETIME());
INSERT dbo.Roles(RoleId,RoleCode,RoleName,IsActive,CreatedAt) VALUES(1,N'admin',N'Admin',1,SYSUTCDATETIME());
INSERT dbo.EmploymentRoleAssignments(EmploymentRoleAssignmentId,EmploymentId,RoleId,EffectiveFrom,CreatedAt) VALUES(1,2004,1,'2020-01-01',SYSUTCDATETIME());
INSERT dbo.TeamLeaderAssignments(TeamLeaderAssignmentId,TeamId,EmploymentId,EffectiveFrom,EffectiveTo,CreatedAt) VALUES
(1,10,2002,'2020-01-01','2026-09-30',SYSUTCDATETIME()),(2,10,2007,'2026-10-01',NULL,SYSUTCDATETIME());
INSERT dbo.TeamLeaderDelegations(TeamLeaderDelegationId,TeamLeaderAssignmentId,DelegateEmploymentId,EffectiveFrom,EffectiveTo,CreatedAt) VALUES
(1,1,2003,'2026-09-01','2026-09-30',SYSUTCDATETIME());
UPDATE dbo.NotificationSettings SET IsEnabled=1 WHERE EventCode=N'DeploymentSiteChangeEffective';
""");
    }

    public NotificationEventContext Ctx(
        string code,
        string businessEventKey,
        long? owner = null,
        int? initiator = null,
        long? affected = null,
        int? team = null,
        INotificationTemplatePayload? payload = null,
        DateTime? eventAt = null,
        Guid? correlationId = null)
        => new(
            code,
            "TestAggregate",
            businessEventKey.Split(':').Last(),
            businessEventKey,
            eventAt ?? EventAt,
            1,
            team,
            owner,
            initiator,
            affected,
            payload ?? new NotificationTemplatePayloadV1(businessEventKey, "fixture"),
            correlationId ?? Guid.Parse("11111111-1111-1111-1111-111111111111"));

    public async Task<(NotificationQueueResult Result, AppDbContext Db)> QueueAsync(
        NotificationEventContext context,
        INotificationRecipientResolver? resolver = null)
    {
        var db = NewDb();
        resolver ??= new EfNotificationRecipientResolver(db);
        var writer = new EfNotificationOutboxWriter(
            db,
            resolver,
            new NotificationRuntimeEnvironment("UAT"),
            new FixedTimeProvider(new DateTimeOffset(ProcessingAt, TimeSpan.Zero)));
        return (await writer.QueueAsync(context, CancellationToken.None), db);
    }
}

internal sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => utcNow;
}

internal sealed class StaticRecipientResolver(IReadOnlyList<NotificationRecipient> recipients) : INotificationRecipientResolver
{
    public Task<IReadOnlyList<NotificationRecipient>> ResolveAsync(
        NotificationEventContext context,
        IReadOnlyCollection<string> recipientRuleCodes,
        CancellationToken ct)
        => Task.FromResult(recipients);
}
