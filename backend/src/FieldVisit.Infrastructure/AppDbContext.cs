using FieldVisit.Application;
using FieldVisit.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FieldVisit.Infrastructure;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options), IUnitOfWork
{
    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<Team> Teams => Set<Team>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<Location> Locations => Set<Location>();
    public DbSet<LocationApprovalHistory> LocationApprovalHistories => Set<LocationApprovalHistory>();
    public DbSet<V180B3ChangeRequest> ChangeRequests => Set<V180B3ChangeRequest>();
    public DbSet<V180B3ChangeEvent> ChangeRequestEvents => Set<V180B3ChangeEvent>();
    public DbSet<TeamLocationNote> TeamLocationNotes => Set<TeamLocationNote>();
    public DbSet<TeamLocationNoteHistory> TeamLocationNoteHistories => Set<TeamLocationNoteHistory>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<ProjectLocation> ProjectLocations => Set<ProjectLocation>();
    public DbSet<VisitType> VisitTypes => Set<VisitType>();
    public DbSet<VisitTrip> VisitTrips => Set<VisitTrip>();
    public DbSet<VisitTripStop> VisitTripStops => Set<VisitTripStop>();
    public DbSet<MileageCalculation> MileageCalculations => Set<MileageCalculation>();
    public DbSet<MileageRateRule> MileageRateRules => Set<MileageRateRule>();
    public DbSet<ApprovalRecord> ApprovalRecords => Set<ApprovalRecord>();
    public DbSet<VisitTripStatusHistory> VisitTripStatusHistories => Set<VisitTripStatusHistory>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<UserTeamScope> UserTeamScopes => Set<UserTeamScope>();
    public DbSet<VisitTripSnapshot> VisitTripSnapshots => Set<VisitTripSnapshot>();
    public DbSet<VisitTripSnapshotStop> VisitTripSnapshotStops => Set<VisitTripSnapshotStop>();
    public DbSet<GeocodingAttempt> GeocodingAttempts => Set<GeocodingAttempt>();
    public DbSet<RouteCalculationAttempt> RouteCalculationAttempts => Set<RouteCalculationAttempt>();
    public DbSet<MileageGovernanceEvent> MileageGovernanceEvents => Set<MileageGovernanceEvent>();
    public DbSet<CorrectionRequest> CorrectionRequests => Set<CorrectionRequest>();
    public DbSet<CorrectionRequestChange> CorrectionRequestChanges => Set<CorrectionRequestChange>();
    public DbSet<BackgroundJob> BackgroundJobs => Set<BackgroundJob>();
    public DbSet<BackgroundJobItem> BackgroundJobItems => Set<BackgroundJobItem>();
    public DbSet<ImportBatch> ImportBatches => Set<ImportBatch>();
    public DbSet<ImportBatchItem> ImportBatchItems => Set<ImportBatchItem>();

    public DbSet<UserIdentityProfile> UserIdentityProfiles => Set<UserIdentityProfile>();
    public DbSet<UserEmploymentPeriod> UserEmploymentPeriods => Set<UserEmploymentPeriod>();
    public DbSet<UserRoleAssignment> UserRoleAssignments => Set<UserRoleAssignment>();
    public DbSet<UserTeamAssignment> UserTeamAssignments => Set<UserTeamAssignment>();
    public DbSet<UserDataScope> UserDataScopes => Set<UserDataScope>();
    public DbSet<UserCapability> UserCapabilities => Set<UserCapability>();

    // v1.8 trip-context runtime persistence surface. Schema remains migration-owned.
    public DbSet<Center> Centers => Set<Center>();
    public DbSet<TeamCenterAssignment> TeamCenterAssignments => Set<TeamCenterAssignment>();
    public DbSet<Person> Persons => Set<Person>();
    public DbSet<Employment> Employments => Set<Employment>();
    public DbSet<EmploymentStatusPeriod> EmploymentStatusPeriods => Set<EmploymentStatusPeriod>();
    public DbSet<EmploymentRoleAssignment> EmploymentRoleAssignments => Set<EmploymentRoleAssignment>();
    public DbSet<TeamMembership> TeamMemberships => Set<TeamMembership>();
    public DbSet<TeamLeaderAssignment> TeamLeaderAssignments => Set<TeamLeaderAssignment>();
    public DbSet<TeamLeaderDelegation> TeamLeaderDelegations => Set<TeamLeaderDelegation>();
    public DbSet<DeploymentSite> DeploymentSites => Set<DeploymentSite>();
    public DbSet<DeploymentSiteLocationAssignment> DeploymentSiteLocationAssignments => Set<DeploymentSiteLocationAssignment>();
    public DbSet<TeamDeploymentSiteAssignment> TeamDeploymentSiteAssignments => Set<TeamDeploymentSiteAssignment>();
    public DbSet<EmploymentDeploymentSiteAssignment> EmploymentDeploymentSiteAssignments => Set<EmploymentDeploymentSiteAssignment>();

    // v1.7 Location Scale foundation.
    public DbSet<GovernmentLocationSource> GovernmentLocationSources => Set<GovernmentLocationSource>();
    public DbSet<GovernmentLocationSourceArea> GovernmentLocationSourceAreas => Set<GovernmentLocationSourceArea>();
    public DbSet<GovernmentLocationMaster> GovernmentLocationMasters => Set<GovernmentLocationMaster>();
    public DbSet<UserFavoriteLocation> UserFavoriteLocations => Set<UserFavoriteLocation>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Organization>(e => { e.ToTable("Organizations"); e.HasKey(x => x.OrganizationId); e.Property(x => x.OrganizationId).ValueGeneratedOnAdd(); });
        b.Entity<Team>(e => { e.ToTable("Teams"); e.HasKey(x => x.TeamId); e.Property(x => x.TeamId).ValueGeneratedOnAdd(); });
        b.Entity<User>(e => { e.ToTable("Users"); e.HasKey(x => x.UserId); e.Property(x => x.UserId).ValueGeneratedOnAdd(); e.Property(x => x.EmployeeNo).IsRequired(false); });
        b.Entity<Role>(e => { e.ToTable("Roles"); e.HasKey(x => x.RoleId); e.Property(x => x.RoleId).ValueGeneratedOnAdd(); });
        b.Entity<UserRole>(e => { e.ToTable("UserRoles"); e.HasKey(x => x.UserRoleId); e.Property(x => x.UserRoleId).ValueGeneratedOnAdd(); });

        b.Entity<Location>(e =>
        {
            e.ToTable("Locations", table => table.UseSqlOutputClause(false)); e.HasKey(x => x.LocationId); e.Property(x => x.LocationId).ValueGeneratedOnAdd();
            e.Property(x => x.Latitude).HasPrecision(10, 7); e.Property(x => x.Longitude).HasPrecision(10, 7);
            e.Property(x => x.RowVersion).IsRowVersion().IsConcurrencyToken();
            e.HasOne<Location>().WithMany().HasForeignKey(x => x.DuplicateOfLocationId).OnDelete(DeleteBehavior.NoAction);
            e.HasIndex(x => x.SelectedGeocodingAttemptId).HasDatabaseName("IX_Locations_SelectedGeocodingAttempt")
                .HasFilter("[SelectedGeocodingAttemptId] IS NOT NULL");
            e.HasOne<GeocodingAttempt>().WithMany().HasForeignKey(x => x.SelectedGeocodingAttemptId).OnDelete(DeleteBehavior.NoAction);
        });
        b.Entity<LocationApprovalHistory>(e => { e.ToTable("LocationApprovalHistory"); e.HasKey(x => x.LocationApprovalHistoryId); e.Property(x => x.LocationApprovalHistoryId).ValueGeneratedOnAdd(); });
        // Candidate schema only; B3 feature flag is false until approved migration.
        b.Entity<V180B3ChangeRequest>(e =>
        {
            // Candidate model only: NO migration or DB execution authorized.
            e.ToTable("ChangeRequests",table =>
            {
                table.HasCheckConstraint("CK_B3_ChangeRequests_KnownCodes", "[EntityKind]=N'Location' AND [OperationCode]=N'UpdatePublishedLocation' AND [RiskCode]=N'High'");
                table.HasCheckConstraint("CK_B3_ChangeRequests_KnownStatus", "[Status] IN (N'Pending',N'Rejected',N'Returned',N'Applied',N'Cancelled')");
                table.HasCheckConstraint("CK_B3_ChangeRequests_ExpectedLocationVersion", "[ExpectedEntityRowVersion] IS NOT NULL AND DATALENGTH([ExpectedEntityRowVersion])=8");
                table.HasCheckConstraint("CK_B3_ChangeRequests_ProposedJson", "ISJSON([ProposedJson])=1");
                table.HasCheckConstraint("CK_B3_ChangeRequests_ReviewState", "([Status]<>N'Pending' OR ([ReviewedByUserId] IS NULL AND [ReviewedAt] IS NULL AND [ReviewReason] IS NULL AND [AppliedAt] IS NULL)) AND ([Status]<>N'Rejected' OR ([ReviewedByUserId] IS NOT NULL AND [ReviewedByUserId]<>[RequestedByUserId] AND [ReviewedAt] IS NOT NULL AND [ReviewReason] IS NOT NULL AND LEN(LTRIM(RTRIM([ReviewReason])))>0 AND [AppliedAt] IS NULL))");
            });
            e.HasKey(x => x.ChangeRequestId);
            e.Property(x => x.ChangeRequestId).ValueGeneratedOnAdd();
            e.Property(x => x.EntityKind).HasMaxLength(40).IsRequired();
            e.Property(x => x.EntityId).HasMaxLength(80).IsRequired();
            e.Property(x => x.OperationCode).HasMaxLength(80).IsRequired();
            e.Property(x => x.RiskCode).HasMaxLength(20).IsRequired();
            e.Property(x => x.ExpectedEntityRowVersion).HasMaxLength(8);
            e.Property(x => x.BeforeJson).HasColumnType("nvarchar(max)");
            e.Property(x => x.ProposedJson).HasColumnType("nvarchar(max)").IsRequired();
            e.Property(x => x.EvidenceJson).HasColumnType("nvarchar(max)");
            e.Property(x => x.SubmittedAt).HasPrecision(3);
            e.Property(x => x.Status).HasMaxLength(30).IsRequired();
            e.Property(x => x.ReviewedAt).HasPrecision(3);
            e.Property(x => x.ReviewReason).HasMaxLength(1000);
            e.Property(x => x.AppliedAt).HasPrecision(3);
            e.Property(x => x.RowVersion).IsRowVersion().IsConcurrencyToken();

            // Audited request identity must never cascade-delete historical rows.
            e.HasOne<Organization>().WithMany()
                .HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<Team>().WithMany()
                .HasForeignKey(x => x.TeamId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<User>().WithMany()
                .HasForeignKey(x => x.RequestedByUserId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<User>().WithMany()
                .HasForeignKey(x => x.ReviewedByUserId).OnDelete(DeleteBehavior.NoAction);

            e.HasIndex(x => x.RequestPublicId).IsUnique()
                .HasDatabaseName(V180B3SqlSafetyRules.RequestPublicIdIndex);
            e.HasIndex(x => new {x.OrganizationId,x.EntityKind,x.EntityId})
                .IsUnique().HasFilter("[Status] = 'Pending'")
                .HasDatabaseName(V180B3SqlSafetyRules.PendingRequestIndex);
            e.HasIndex(x => new {x.OrganizationId,x.Status,x.SubmittedAt})
                .HasDatabaseName("IX_B3_ChangeRequests_Org_Status_SubmittedAt");
            e.HasIndex(x => new {x.RequestedByUserId,x.SubmittedAt})
                .HasDatabaseName("IX_B3_ChangeRequests_Requester_SubmittedAt");
        });
        b.Entity<V180B3ChangeEvent>(e =>
        {
            e.ToTable("ChangeRequestEvents",table =>
            {
                table.HasCheckConstraint("CK_B3_ChangeRequestEvents_EventType", "[EventType] IN (N'Submitted',N'Rejected')");
                table.HasCheckConstraint("CK_B3_ChangeRequestEvents_DetailsJson", "[DetailsJson] IS NULL OR ISJSON([DetailsJson])=1");
                table.HasCheckConstraint("CK_B3_ChangeRequestEvents_DecisionState", "([EventType]=N'Submitted' AND [DecisionKey] IS NULL AND [ActorUserId] IS NOT NULL) OR ([EventType]=N'Rejected' AND [DecisionKey] IS NOT NULL AND [ActorUserId] IS NOT NULL)");
            });
            e.HasKey(x => x.ChangeRequestEventId);
            e.Property(x => x.ChangeRequestEventId).ValueGeneratedOnAdd();
            e.Property(x => x.EventType).HasMaxLength(40).IsRequired();
            e.Property(x => x.OccurredAt).HasPrecision(3);
            e.Property(x => x.DetailsJson).HasColumnType("nvarchar(max)");
            e.HasOne<V180B3ChangeRequest>().WithMany()
                .HasForeignKey(x => x.ChangeRequestId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<User>().WithMany()
                .HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.NoAction);
            e.HasIndex(x => x.DecisionKey).IsUnique()
                .HasFilter("[DecisionKey] IS NOT NULL")
                .HasDatabaseName(V180B3SqlSafetyRules.DecisionKeyIndex);
            e.HasIndex(x => new {x.ChangeRequestId,x.OccurredAt,x.ChangeRequestEventId})
                .HasDatabaseName("IX_B3_ChangeRequestEvents_Request_OccurredAt");
            e.HasIndex(x => x.CorrelationId)
                .HasDatabaseName("IX_B3_ChangeRequestEvents_CorrelationId");
        });
        b.Entity<TeamLocationNote>(e =>
        {
            e.ToTable("TeamLocationNotes"); e.HasKey(x => x.TeamLocationNoteId); e.Property(x => x.TeamLocationNoteId).ValueGeneratedOnAdd();
            e.Property(x => x.Note).HasMaxLength(1000); e.Property(x => x.RowVersion).IsRowVersion().IsConcurrencyToken();
            e.HasIndex(x => new { x.TeamId, x.LocationId }).IsUnique().HasDatabaseName("UQ_TeamLocationNotes_Team_Location");
            e.HasOne<Team>().WithMany().HasForeignKey(x => x.TeamId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<Location>().WithMany().HasForeignKey(x => x.LocationId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UpdatedByUserId).OnDelete(DeleteBehavior.NoAction);
        });
        b.Entity<TeamLocationNoteHistory>(e =>
        {
            e.ToTable("TeamLocationNoteHistory"); e.HasKey(x => x.TeamLocationNoteHistoryId); e.Property(x => x.TeamLocationNoteHistoryId).ValueGeneratedOnAdd();
            e.HasIndex(x => new { x.TeamLocationNoteId, x.ChangedAt }).HasDatabaseName("IX_TeamLocationNoteHistory_Note_Changed");
            e.HasOne<TeamLocationNote>().WithMany().HasForeignKey(x => x.TeamLocationNoteId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<Team>().WithMany().HasForeignKey(x => x.TeamId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<Location>().WithMany().HasForeignKey(x => x.LocationId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.ChangedByUserId).OnDelete(DeleteBehavior.NoAction);
        });

        b.Entity<Project>(e => { e.ToTable("Projects"); e.HasKey(x => x.ProjectId); e.Property(x => x.ProjectId).ValueGeneratedOnAdd(); });
        b.Entity<ProjectLocation>(e => { e.ToTable("ProjectLocations"); e.HasKey(x => x.ProjectLocationId); e.Property(x => x.ProjectLocationId).ValueGeneratedOnAdd(); });
        b.Entity<VisitType>(e => { e.ToTable("VisitTypes"); e.HasKey(x => x.VisitTypeId); e.Property(x => x.VisitTypeId).ValueGeneratedOnAdd(); });

        b.Entity<VisitTrip>(e =>
        {
            e.ToTable("VisitTrips"); e.HasKey(x => x.VisitTripId); e.Property(x => x.VisitTripId).ValueGeneratedOnAdd();
            e.Property(x => x.EmploymentId);
            e.Property(x => x.StartDeploymentSiteId);
            e.Property(x => x.EndDeploymentSiteId);
            e.Property(x => x.RowVersion).IsRowVersion().IsConcurrencyToken();
            e.HasMany(x => x.Stops).WithOne(x => x.VisitTrip).HasForeignKey(x => x.VisitTripId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.MileageCalculation).WithOne(x => x.VisitTrip).HasForeignKey<MileageCalculation>(x => x.VisitTripId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<VisitTripStop>(e => { e.ToTable("VisitTripStops"); e.HasKey(x => x.VisitTripStopId); e.Property(x => x.VisitTripStopId).ValueGeneratedOnAdd(); e.HasOne(x => x.Location).WithMany().HasForeignKey(x => x.LocationId).OnDelete(DeleteBehavior.NoAction); });

        b.Entity<MileageCalculation>(e =>
        {
            e.ToTable("MileageCalculations", table => table.UseSqlOutputClause(false)); e.HasKey(x => x.MileageCalculationId); e.Property(x => x.MileageCalculationId).ValueGeneratedOnAdd();
            e.Property(x => x.SystemDistanceKm).HasPrecision(10,2); e.Property(x => x.ClaimedDistanceKm).HasPrecision(10,2); e.Property(x => x.ApprovedDistanceKm).HasPrecision(10,2);
            e.Property(x => x.RatePerKmSnapshot).HasPrecision(10,2); e.Property(x => x.ClaimedAmount).HasPrecision(12,2); e.Property(x => x.ApprovedAmount).HasPrecision(12,2);
            e.Property(x => x.ApprovalBasisHash).HasColumnType("varbinary(32)");
            e.Property(x => x.DistanceApprovedAt).HasPrecision(3); e.Property(x => x.InvalidatedAt).HasPrecision(3);
            e.Property(x => x.DistanceDecisionGovernanceVersion).HasMaxLength(20);
            e.Property(x => x.ApprovedDistanceSource).HasMaxLength(30); e.Property(x => x.ApprovalBasisCode).HasMaxLength(80);
            e.Property(x => x.InvalidationReason).HasMaxLength(100);
            e.HasOne<RouteCalculationAttempt>().WithMany().HasForeignKey(x => x.SelectedRouteCalculationAttemptId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.DistanceApprovedByUserId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.InvalidatedByUserId).OnDelete(DeleteBehavior.NoAction);
        });
        b.Entity<MileageRateRule>(e => { e.ToTable("MileageRateRules", table => table.UseSqlOutputClause(false)); e.HasKey(x => x.MileageRateRuleId); e.Property(x => x.MileageRateRuleId).ValueGeneratedOnAdd(); e.Property(x => x.RatePerKm).HasPrecision(10,2); });
        b.Entity<ApprovalRecord>(e => { e.ToTable("ApprovalRecords"); e.HasKey(x => x.ApprovalRecordId); e.Property(x => x.ApprovalRecordId).ValueGeneratedOnAdd(); });
        b.Entity<VisitTripStatusHistory>(e => { e.ToTable("VisitTripStatusHistory"); e.HasKey(x => x.VisitTripStatusHistoryId); e.Property(x => x.VisitTripStatusHistoryId).ValueGeneratedOnAdd(); });
        b.Entity<AuditLog>(e => { e.ToTable("AuditLogs"); e.HasKey(x => x.AuditLogId); e.Property(x => x.AuditLogId).ValueGeneratedOnAdd(); });

        b.Entity<UserTeamScope>(e =>
        {
            e.ToTable("UserTeamScopes"); e.HasKey(x => x.UserTeamScopeId); e.Property(x => x.UserTeamScopeId).ValueGeneratedOnAdd();
            e.HasIndex(x => new { x.UserId, x.TeamId }).IsUnique();
        });
        b.Entity<VisitTripSnapshot>(e =>
        {
            e.ToTable("VisitTripSnapshots", table => table.UseSqlOutputClause(false)); e.HasKey(x => x.VisitTripSnapshotId); e.Property(x => x.VisitTripSnapshotId).ValueGeneratedOnAdd();
            e.HasIndex(x => new { x.VisitTripId, x.SnapshotVersion }).IsUnique();
            e.Property(x => x.ClaimedDistanceKmSnapshot).HasPrecision(10,2);
            e.Property(x => x.SystemDistanceKmSnapshot).HasPrecision(10,2);
            e.Property(x => x.ApprovedDistanceKmSnapshot).HasPrecision(10,2);
            e.Property(x => x.RatePerKmSnapshot).HasPrecision(10,2);
            e.Property(x => x.SubsidyAmountSnapshot).HasPrecision(12,2);
            e.Property<long?>("PersonIdSnapshot");
            e.Property<long?>("EmploymentIdSnapshot");
            e.Property<int?>("CenterIdSnapshot");
            e.Property<string?>("CenterCodeSnapshot").HasMaxLength(50);
            e.Property<string?>("CenterNameSnapshot").HasMaxLength(200);
            e.Property<string?>("TeamCodeSnapshot").HasMaxLength(50);
            e.Property<int?>("StartDeploymentSiteIdSnapshot");
            e.Property<string?>("StartDeploymentSiteNameSnapshot").HasMaxLength(200);
            e.Property<int?>("StartDeploymentLocationIdSnapshot");
            e.Property<int?>("EndDeploymentSiteIdSnapshot");
            e.Property<string?>("EndDeploymentSiteNameSnapshot").HasMaxLength(200);
            e.Property<int?>("EndDeploymentLocationIdSnapshot");
            e.Property(x => x.StartDeploymentSiteCodeSnapshot).HasMaxLength(50);
            e.Property(x => x.StartDeploymentAddressSnapshot).HasMaxLength(500);
            e.Property(x => x.EndDeploymentSiteCodeSnapshot).HasMaxLength(50);
            e.Property(x => x.EndDeploymentAddressSnapshot).HasMaxLength(500);
            e.Property(x => x.RouteTravelModeSnapshot).HasMaxLength(20);
            e.Property(x => x.RouteCalculatedAtSnapshot).HasPrecision(3);
            e.Property(x => x.RouteCalculationStatusSnapshot).HasMaxLength(20);
            e.Property(x => x.RouteErrorCodeSnapshot).HasMaxLength(100);
            e.Property(x => x.ApprovedDistanceSourceSnapshot).HasMaxLength(30);
            e.Property(x => x.ApprovalBasisCodeSnapshot).HasMaxLength(80);
            e.Property(x => x.ApprovalBasisHashSnapshot).HasColumnType("varbinary(32)");
            e.Property(x => x.DistanceApprovedAtSnapshot).HasPrecision(3);
            e.HasOne<RouteCalculationAttempt>().WithMany().HasForeignKey(x => x.MileageRouteAttemptIdSnapshot).OnDelete(DeleteBehavior.NoAction);
            e.HasMany(x => x.Stops).WithOne(x => x.Snapshot).HasForeignKey(x => x.VisitTripSnapshotId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<VisitTripSnapshotStop>(e =>
        {
            e.ToTable("VisitTripSnapshotStops"); e.HasKey(x => x.VisitTripSnapshotStopId); e.Property(x => x.VisitTripSnapshotStopId).ValueGeneratedOnAdd();
            e.HasIndex(x => new { x.VisitTripSnapshotId, x.StopSequence }).IsUnique();
        });
        b.Entity<CorrectionRequest>(e =>
        {
            e.ToTable("CorrectionRequests"); e.HasKey(x => x.CorrectionRequestId); e.Property(x => x.CorrectionRequestId).ValueGeneratedOnAdd();
            e.Property(x => x.RowVersion).IsRowVersion().IsConcurrencyToken();
        });
        b.Entity<CorrectionRequestChange>(e => { e.ToTable("CorrectionRequestChanges"); e.HasKey(x => x.CorrectionRequestChangeId); e.Property(x => x.CorrectionRequestChangeId).ValueGeneratedOnAdd(); });
        b.Entity<BackgroundJob>(e => { e.ToTable("BackgroundJobs"); e.HasKey(x => x.BackgroundJobId); });
        b.Entity<BackgroundJobItem>(e => { e.ToTable("BackgroundJobItems"); e.HasKey(x => x.BackgroundJobItemId); e.Property(x => x.BackgroundJobItemId).ValueGeneratedOnAdd(); });
        b.Entity<ImportBatch>(e => { e.ToTable("ImportBatches"); e.HasKey(x => x.ImportBatchId); });
        b.Entity<ImportBatchItem>(e => { e.ToTable("ImportBatchItems"); e.HasKey(x => x.ImportBatchItemId); e.Property(x => x.ImportBatchItemId).ValueGeneratedOnAdd(); e.HasOne<ImportBatch>().WithMany().HasForeignKey(x => x.ImportBatchId).OnDelete(DeleteBehavior.Cascade); });

        // v1.7 Identity & Access foundation. Additive to v1.6 compatibility tables.
        b.Entity<UserIdentityProfile>(e =>
        {
            e.ToTable("UserIdentityProfiles");
            e.HasKey(x => x.UserId);
            e.Property(x => x.UserId).ValueGeneratedNever();
            e.HasIndex(x => x.UserCode).IsUnique();
            e.HasIndex(x => new { x.EntraTenantId, x.EntraObjectId })
                .IsUnique()
                .HasFilter("[EntraTenantId] IS NOT NULL AND [EntraObjectId] IS NOT NULL");
            e.HasIndex(x => x.EmploymentId).IsUnique()
                .HasFilter("[EmploymentId] IS NOT NULL")
                .HasDatabaseName("UX_UserIdentityProfiles_Employment");
            e.HasOne<Employment>().WithMany().HasForeignKey(x => x.EmploymentId).OnDelete(DeleteBehavior.NoAction);
        });

        b.Entity<UserEmploymentPeriod>(e =>
        {
            e.ToTable("UserEmploymentPeriods");
            e.HasKey(x => x.UserEmploymentPeriodId);
            e.Property(x => x.UserEmploymentPeriodId).ValueGeneratedOnAdd();
            e.HasIndex(x => new { x.UserId, x.EffectiveFrom, x.EffectiveTo });
        });

        b.Entity<UserRoleAssignment>(e =>
        {
            e.ToTable("UserRoleAssignments");
            e.HasKey(x => x.UserRoleAssignmentId);
            e.Property(x => x.UserRoleAssignmentId).ValueGeneratedOnAdd();
            e.HasIndex(x => new { x.UserId, x.RoleId, x.EffectiveFrom }).IsUnique();
        });

        b.Entity<UserTeamAssignment>(e =>
        {
            e.ToTable("UserTeamAssignments");
            e.HasKey(x => x.UserTeamAssignmentId);
            e.Property(x => x.UserTeamAssignmentId).ValueGeneratedOnAdd();
            e.HasIndex(x => new { x.UserId, x.TeamId, x.EffectiveFrom }).IsUnique();
        });

        b.Entity<UserDataScope>(e =>
        {
            e.ToTable("UserDataScopes");
            e.HasKey(x => x.UserDataScopeId);
            e.Property(x => x.UserDataScopeId).ValueGeneratedOnAdd();
            e.HasIndex(x => new { x.UserId, x.EffectiveFrom, x.EffectiveTo });
        });

        b.Entity<UserCapability>(e =>
        {
            e.ToTable("UserCapabilities");
            e.HasKey(x => x.UserCapabilityId);
            e.Property(x => x.UserCapabilityId).ValueGeneratedOnAdd();
            e.HasIndex(x => new
            {
                x.UserId,
                x.CapabilityCode,
                x.EffectiveFrom
            }).IsUnique();
        });


        // v1.8 Trip Context runtime read model. 1800_001..003 remain schema authority.
        b.Entity<Center>(e =>
        {
            e.ToTable("Centers");
            e.HasKey(x => x.CenterId);
            e.Property(x => x.CenterId).ValueGeneratedOnAdd();
            e.Property(x => x.CenterCode).HasMaxLength(50);
            e.Property(x => x.CenterName).HasMaxLength(200);
            e.Property(x => x.RowVersion).IsRowVersion().IsConcurrencyToken();
            e.HasIndex(x => new { x.OrganizationId, x.CenterCode }).IsUnique()
                .HasDatabaseName("UQ_Centers_Organization_Code");
            e.HasIndex(x => new { x.OrganizationId, x.IsActive, x.EffectiveFrom, x.EffectiveTo })
                .HasDatabaseName("IX_Centers_Organization_Effective");
            e.HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.NoAction);
        });
        b.Entity<TeamCenterAssignment>(e =>
        {
            e.ToTable("TeamCenterAssignments", table => table.UseSqlOutputClause(false)); e.HasKey(x => x.TeamCenterAssignmentId); e.Property(x => x.TeamCenterAssignmentId).ValueGeneratedOnAdd();
            e.Property(x => x.ChangeReason).HasMaxLength(500);
            e.Property(x => x.RowVersion).IsRowVersion().IsConcurrencyToken();
            e.HasIndex(x => new { x.TeamId, x.EffectiveFrom }).IsUnique().HasDatabaseName("UQ_TeamCenterAssignments_Team_Start");
            e.HasOne<Team>().WithMany().HasForeignKey(x => x.TeamId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<Center>().WithMany().HasForeignKey(x => x.CenterId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.NoAction);
        });

        b.Entity<Person>(e =>
        {
            e.ToTable("Persons");
            e.HasKey(x => x.PersonId);
            e.Property(x => x.PersonId).ValueGeneratedOnAdd();
            e.Property(x => x.DisplayName).HasMaxLength(200);
            e.Property(x => x.RowVersion).IsRowVersion().IsConcurrencyToken();
            e.HasIndex(x => x.LegacyUserId).IsUnique()
                .HasFilter("[LegacyUserId] IS NOT NULL")
                .HasDatabaseName("UX_Persons_LegacyUserId");
            e.HasOne<User>().WithMany().HasForeignKey(x => x.LegacyUserId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UpdatedByUserId).OnDelete(DeleteBehavior.NoAction);
        });

        b.Entity<Employment>(e =>
        {
            e.ToTable("Employments");
            e.HasKey(x => x.EmploymentId);
            e.Property(x => x.EmploymentId).ValueGeneratedOnAdd();
            e.Property(x => x.EmployeeNo).HasMaxLength(50);
            e.Property(x => x.Email).HasMaxLength(320);
            e.Property(x => x.SourceType).HasMaxLength(30);
            e.Property(x => x.SourceReference).HasMaxLength(200);
            e.Property(x => x.RowVersion).IsRowVersion().IsConcurrencyToken();
            e.HasIndex(x => new { x.OrganizationId, x.EmployeeNo }).IsUnique()
                .HasFilter("[EmployeeNo] IS NOT NULL")
                .HasDatabaseName("UX_Employments_Organization_EmployeeNo");
            e.HasIndex(x => x.LegacyUserId).IsUnique()
                .HasFilter("[LegacyUserId] IS NOT NULL")
                .HasDatabaseName("UX_Employments_LegacyUserId");
            e.HasIndex(x => new { x.PersonId, x.HireDate, x.TerminationDate })
                .HasDatabaseName("IX_Employments_Person");
            e.HasIndex(x => x.Email).HasFilter("[Email] IS NOT NULL")
                .HasDatabaseName("IX_Employments_Email");
            e.HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.LegacyUserId).OnDelete(DeleteBehavior.NoAction);
        });

        b.Entity<EmploymentStatusPeriod>(e =>
        {
            e.ToTable("EmploymentStatusPeriods", table => table.UseSqlOutputClause(false));
            e.HasKey(x => x.EmploymentStatusPeriodId);
            e.Property(x => x.EmploymentStatusPeriodId).ValueGeneratedOnAdd();
            e.Property(x => x.EmploymentStatus).HasMaxLength(30);
            e.Property(x => x.SourceType).HasMaxLength(30);
            e.Property(x => x.SourceReference).HasMaxLength(200);
            e.Property(x => x.RowVersion).IsRowVersion().IsConcurrencyToken();
            e.HasIndex(x => new { x.EmploymentId, x.EffectiveFrom }).IsUnique()
                .HasDatabaseName("UQ_EmploymentStatusPeriods_Start");
            e.HasIndex(x => new { x.EmploymentId, x.EffectiveFrom, x.EffectiveTo, x.EmploymentStatus })
                .HasDatabaseName("IX_EmploymentStatusPeriods_AsOf");
            e.HasOne<Employment>().WithMany().HasForeignKey(x => x.EmploymentId).OnDelete(DeleteBehavior.NoAction);
        });

        b.Entity<EmploymentRoleAssignment>(e =>
        {
            e.ToTable("EmploymentRoleAssignments");
            e.HasKey(x => x.EmploymentRoleAssignmentId);
            e.Property(x => x.EmploymentRoleAssignmentId).ValueGeneratedOnAdd();
            e.Property(x => x.RowVersion).IsRowVersion().IsConcurrencyToken();
            e.HasIndex(x => new { x.EmploymentId, x.RoleId, x.EffectiveFrom }).IsUnique()
                .HasDatabaseName("UQ_EmploymentRoleAssignments_Start");
            e.HasOne<Employment>().WithMany().HasForeignKey(x => x.EmploymentId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<Role>().WithMany().HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.AssignedByUserId).OnDelete(DeleteBehavior.NoAction);
        });

        b.Entity<TeamMembership>(e =>
        {
            e.ToTable("TeamMemberships", table => table.UseSqlOutputClause(false));
            e.HasKey(x => x.TeamMembershipId);
            e.Property(x => x.TeamMembershipId).ValueGeneratedOnAdd();
            e.Property(x => x.ChangeReason).HasMaxLength(500);
            e.Property(x => x.RowVersion).IsRowVersion().IsConcurrencyToken();
            e.HasIndex(x => new { x.EmploymentId, x.TeamId, x.EffectiveFrom }).IsUnique()
                .HasDatabaseName("UQ_TeamMemberships_Start");
            e.HasIndex(x => new { x.EmploymentId, x.EffectiveFrom, x.EffectiveTo, x.IsPrimary, x.TeamId })
                .HasDatabaseName("IX_TeamMemberships_Employment_AsOf");
            e.HasIndex(x => new { x.TeamId, x.EffectiveFrom, x.EffectiveTo, x.EmploymentId })
                .HasDatabaseName("IX_TeamMemberships_Team_AsOf");
            e.HasOne<Employment>().WithMany().HasForeignKey(x => x.EmploymentId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<Team>().WithMany().HasForeignKey(x => x.TeamId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.AssignedByUserId).OnDelete(DeleteBehavior.NoAction);
        });

        b.Entity<TeamLeaderAssignment>(e =>
        {
            e.ToTable("TeamLeaderAssignments");
            e.HasKey(x => x.TeamLeaderAssignmentId);
            e.Property(x => x.TeamLeaderAssignmentId).ValueGeneratedOnAdd();
            e.Property(x => x.RowVersion).IsRowVersion().IsConcurrencyToken();
            e.HasIndex(x => new { x.TeamId, x.EmploymentId, x.EffectiveFrom }).IsUnique()
                .HasDatabaseName("UQ_TeamLeaderAssignments_Start");
            e.HasOne<Team>().WithMany().HasForeignKey(x => x.TeamId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<Employment>().WithMany().HasForeignKey(x => x.EmploymentId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.AssignedByUserId).OnDelete(DeleteBehavior.NoAction);
        });

        b.Entity<TeamLeaderDelegation>(e =>
        {
            e.ToTable("TeamLeaderDelegations");
            e.HasKey(x => x.TeamLeaderDelegationId);
            e.Property(x => x.TeamLeaderDelegationId).ValueGeneratedOnAdd();
            e.Property(x => x.Reason).HasMaxLength(500);
            e.Property(x => x.RowVersion).IsRowVersion().IsConcurrencyToken();
            e.HasIndex(x => new { x.TeamLeaderAssignmentId, x.DelegateEmploymentId, x.EffectiveFrom }).IsUnique()
                .HasDatabaseName("UQ_TeamLeaderDelegations_Start");
            e.HasOne<TeamLeaderAssignment>().WithMany().HasForeignKey(x => x.TeamLeaderAssignmentId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<Employment>().WithMany().HasForeignKey(x => x.DelegateEmploymentId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.NoAction);
        });

        b.Entity<DeploymentSite>(e =>
        {
            e.ToTable("DeploymentSites", table => table.UseSqlOutputClause(false));
            e.HasKey(x => x.DeploymentSiteId);
            e.Property(x => x.DeploymentSiteId).ValueGeneratedOnAdd();
            e.Property(x => x.SiteCode).HasMaxLength(50);
            e.Property(x => x.SiteName).HasMaxLength(200);
            e.Property(x => x.Notes).HasMaxLength(1000);
            e.Property(x => x.RowVersion).IsRowVersion().IsConcurrencyToken();
            e.HasIndex(x => new { x.CenterId, x.SiteCode }).IsUnique()
                .HasDatabaseName("UQ_DeploymentSites_Center_Code");
            e.HasIndex(x => new { x.CenterId, x.IsActive, x.EffectiveFrom, x.EffectiveTo })
                .HasDatabaseName("IX_DeploymentSites_Center_Effective");
            e.HasOne(x => x.Center).WithMany().HasForeignKey(x => x.CenterId).OnDelete(DeleteBehavior.NoAction);
        });

        b.Entity<DeploymentSiteLocationAssignment>(e =>
        {
            e.ToTable("DeploymentSiteLocationAssignments", table => table.UseSqlOutputClause(false));
            e.HasKey(x => x.DeploymentSiteLocationAssignmentId);
            e.Property(x => x.DeploymentSiteLocationAssignmentId).ValueGeneratedOnAdd();
            e.Property(x => x.ChangeReason).HasMaxLength(500);
            e.Property(x => x.RowVersion).IsRowVersion().IsConcurrencyToken();
            e.HasIndex(x => new { x.DeploymentSiteId, x.EffectiveFrom }).IsUnique()
                .HasDatabaseName("UQ_DeploymentSiteLocationAssignments_Start");
            e.HasIndex(x => new { x.DeploymentSiteId, x.EffectiveFrom, x.EffectiveTo, x.LocationId })
                .HasDatabaseName("IX_DeploymentSiteLocationAssignments_AsOf");
            e.HasOne(x => x.DeploymentSite).WithMany(x => x.LocationAssignments)
                .HasForeignKey(x => x.DeploymentSiteId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<Location>().WithMany().HasForeignKey(x => x.LocationId).OnDelete(DeleteBehavior.NoAction);
        });

        b.Entity<TeamDeploymentSiteAssignment>(e =>
        {
            e.ToTable("TeamDeploymentSiteAssignments", table => table.UseSqlOutputClause(false));
            e.HasKey(x => x.TeamDeploymentSiteAssignmentId);
            e.Property(x => x.TeamDeploymentSiteAssignmentId).ValueGeneratedOnAdd();
            e.Property(x => x.RowVersion).IsRowVersion().IsConcurrencyToken();
            e.HasIndex(x => new { x.TeamId, x.DeploymentSiteId, x.EffectiveFrom }).IsUnique()
                .HasDatabaseName("UQ_TeamDeploymentSiteAssignments_Start");
            e.HasIndex(x => new { x.TeamId, x.EffectiveFrom, x.EffectiveTo, x.DeploymentSiteId })
                .HasDatabaseName("IX_TeamDeploymentSiteAssignments_Team_AsOf");
            e.HasOne<Team>().WithMany().HasForeignKey(x => x.TeamId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne(x => x.DeploymentSite).WithMany(x => x.TeamAssignments)
                .HasForeignKey(x => x.DeploymentSiteId).OnDelete(DeleteBehavior.NoAction);
        });

        b.Entity<EmploymentDeploymentSiteAssignment>(e =>
        {
            e.ToTable("EmploymentDeploymentSiteAssignments", table => table.UseSqlOutputClause(false));
            e.HasKey(x => x.EmploymentDeploymentSiteAssignmentId);
            e.Property(x => x.EmploymentDeploymentSiteAssignmentId).ValueGeneratedOnAdd();
            e.Property(x => x.RowVersion).IsRowVersion().IsConcurrencyToken();
            e.HasIndex(x => new { x.EmploymentId, x.DeploymentSiteId, x.EffectiveFrom }).IsUnique()
                .HasDatabaseName("UQ_EmploymentDeploymentSiteAssignments_Start");
            e.HasIndex(x => new { x.EmploymentId, x.EffectiveFrom, x.EffectiveTo, x.IsPrimary, x.DeploymentSiteId })
                .HasDatabaseName("IX_EmploymentDeploymentSiteAssignments_AsOf");
            e.HasOne<Employment>().WithMany().HasForeignKey(x => x.EmploymentId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne(x => x.DeploymentSite).WithMany(x => x.EmploymentAssignments)
                .HasForeignKey(x => x.DeploymentSiteId).OnDelete(DeleteBehavior.NoAction);
        });

        // v1.7 Location Scale foundation.
        b.Entity<GovernmentLocationSource>(e =>
        {
            e.ToTable("GovernmentLocationSources");
            e.HasKey(x => x.GovernmentLocationSourceId);
            e.Property(x => x.GovernmentLocationSourceId).ValueGeneratedOnAdd();
            e.HasIndex(x => x.SourceCode).IsUnique();
        });

        b.Entity<GovernmentLocationSourceArea>(e =>
        {
            e.ToTable("GovernmentLocationSourceAreas");
            e.HasKey(x => x.GovernmentLocationSourceAreaId);
            e.Property(x => x.GovernmentLocationSourceAreaId).ValueGeneratedOnAdd();

            e.HasIndex(x => new
            {
                x.GovernmentLocationSourceId,
                x.City,
                x.District
            }).IsUnique();

            e.HasOne<GovernmentLocationSource>()
                .WithMany()
                .HasForeignKey(x => x.GovernmentLocationSourceId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        b.Entity<GovernmentLocationMaster>(e =>
        {
            e.ToTable("GovernmentLocationMasters");
            e.HasKey(x => x.GovernmentLocationMasterId);
            e.Property(x => x.GovernmentLocationMasterId).ValueGeneratedOnAdd();

            e.Property(x => x.Latitude).HasPrecision(10, 7);
            e.Property(x => x.Longitude).HasPrecision(10, 7);

            e.HasIndex(x => new
            {
                x.GovernmentLocationSourceId,
                x.SourceRecordKey
            }).IsUnique();

            e.HasIndex(x => new
            {
                x.City,
                x.District,
                x.LocationName
            });

            e.HasIndex(x => x.TaxId);

            e.HasOne<GovernmentLocationSource>()
                .WithMany()
                .HasForeignKey(x => x.GovernmentLocationSourceId)
                .OnDelete(DeleteBehavior.NoAction);

            e.HasOne<Location>()
                .WithMany()
                .HasForeignKey(x => x.MatchedLocationId)
                .OnDelete(DeleteBehavior.NoAction);

            e.HasOne<User>()
                .WithMany()
                .HasForeignKey(x => x.ReviewedByUserId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        b.Entity<UserFavoriteLocation>(e =>
        {
            e.ToTable("UserFavoriteLocations");
            e.HasKey(x => x.UserFavoriteLocationId);
            e.Property(x => x.UserFavoriteLocationId).ValueGeneratedOnAdd();

            e.HasIndex(x => new
            {
                x.UserId,
                x.LocationId
            }).IsUnique();

            e.HasIndex(x => new
            {
                x.UserId,
                x.SortOrder
            });

            e.HasOne<User>()
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.NoAction);

            e.HasOne<Location>()
                .WithMany()
                .HasForeignKey(x => x.LocationId)
                .OnDelete(DeleteBehavior.NoAction);
        });
        // v1.8.0-007 mileage / Google governance model alignment.
        // Migration 1800_007 remains the schema authority; this package
        // exposes only the frozen audit/evidence model and no runtime flow.
        b.Entity<GeocodingAttempt>(e =>
        {
            e.ToTable("GeocodingAttempts"); e.HasKey(x => x.GeocodingAttemptId); e.Property(x => x.GeocodingAttemptId).ValueGeneratedOnAdd();
            e.Property(x => x.Provider).HasMaxLength(80); e.Property(x => x.AddressBasisHash).HasColumnType("varbinary(32)");
            e.Property(x => x.Status).HasMaxLength(20); e.Property(x => x.ErrorCode).HasMaxLength(100); e.Property(x => x.ErrorMessage).HasMaxLength(1000);
            e.Property(x => x.RequestedAt).HasPrecision(3); e.Property(x => x.CompletedAt).HasPrecision(3);
            e.HasIndex(x => new { x.LocationId, x.RequestedAt }).HasDatabaseName("IX_GeocodingAttempts_Location_Requested")
                .IncludeProperties(x => new { x.Status, x.Provider, x.ErrorCode, x.CorrelationId });
            e.HasIndex(x => x.CorrelationId).IsUnique().HasDatabaseName("UQ_GeocodingAttempts_Correlation");
            e.HasOne<Location>().WithMany().HasForeignKey(x => x.LocationId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.RequestedByUserId).OnDelete(DeleteBehavior.NoAction);
        });
        b.Entity<RouteCalculationAttempt>(e =>
        {
            e.ToTable("RouteCalculationAttempts", table => table.UseSqlOutputClause(false)); e.HasKey(x => x.RouteCalculationAttemptId); e.Property(x => x.RouteCalculationAttemptId).ValueGeneratedOnAdd();
            e.Property(x => x.BasisType).HasMaxLength(30); e.Property(x => x.CalculationReason).HasMaxLength(30);
            e.Property(x => x.RequestedVehicleType).HasMaxLength(20); e.Property(x => x.TravelMode).HasMaxLength(20); e.Property(x => x.Provider).HasMaxLength(80);
            e.Property(x => x.RequestBasisHash).HasColumnType("varbinary(32)"); e.Property(x => x.Status).HasMaxLength(20);
            e.Property(x => x.ErrorCode).HasMaxLength(100); e.Property(x => x.ErrorMessage).HasMaxLength(1000);
            e.Property(x => x.RequestedAt).HasPrecision(3); e.Property(x => x.CompletedAt).HasPrecision(3);
            e.HasIndex(x => new { x.VisitTripId, x.RequestedAt }).HasDatabaseName("IX_RouteCalculationAttempts_Trip_Requested")
                .IncludeProperties(x => new { x.Status, x.Provider, x.CalculationReason, x.BasisVisitTripSnapshotId, x.CorrelationId });
            e.HasIndex(x => x.BasisVisitTripSnapshotId).HasDatabaseName("IX_RouteCalculationAttempts_BasisSnapshot")
                .HasFilter("[BasisVisitTripSnapshotId] IS NOT NULL");
            e.HasIndex(x => x.CorrelationId).IsUnique().HasDatabaseName("UQ_RouteCalculationAttempts_Correlation");
            e.HasOne<VisitTrip>().WithMany().HasForeignKey(x => x.VisitTripId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<VisitTripSnapshot>().WithMany().HasForeignKey(x => x.BasisVisitTripSnapshotId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.RequestedByUserId).OnDelete(DeleteBehavior.NoAction);
        });
        b.Entity<MileageGovernanceEvent>(e =>
        {
            e.ToTable("MileageGovernanceEvents"); e.HasKey(x => x.MileageGovernanceEventId); e.Property(x => x.MileageGovernanceEventId).ValueGeneratedOnAdd();
            e.Property(x => x.EventType).HasMaxLength(40); e.Property(x => x.ReasonCode).HasMaxLength(100); e.Property(x => x.Message).HasMaxLength(1000);
            e.Property(x => x.OccurredAt).HasPrecision(3);
            e.HasIndex(x => new { x.VisitTripId, x.OccurredAt }).HasDatabaseName("IX_MileageGovernanceEvents_Trip_Occurred");
            e.HasIndex(x => x.CorrelationId).HasDatabaseName("IX_MileageGovernanceEvents_Correlation");
            e.HasOne<VisitTrip>().WithMany().HasForeignKey(x => x.VisitTripId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<VisitTripSnapshot>().WithMany().HasForeignKey(x => x.VisitTripSnapshotId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<RouteCalculationAttempt>().WithMany().HasForeignKey(x => x.RouteCalculationAttemptId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.NoAction);
        });

    }
}
