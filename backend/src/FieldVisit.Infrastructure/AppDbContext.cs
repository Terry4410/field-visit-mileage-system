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
    public DbSet<Center> Centers => Set<Center>();
    public DbSet<TeamCenterAssignment> TeamCenterAssignments => Set<TeamCenterAssignment>();
    public DbSet<Person> Persons => Set<Person>();
    public DbSet<Employment> Employments => Set<Employment>();
    public DbSet<EmploymentStatusPeriod> EmploymentStatusPeriods => Set<EmploymentStatusPeriod>();
    public DbSet<EmploymentRoleAssignment> EmploymentRoleAssignments => Set<EmploymentRoleAssignment>();
    public DbSet<TeamMembership> TeamMemberships => Set<TeamMembership>();
    public DbSet<TeamLeaderAssignment> TeamLeaderAssignments => Set<TeamLeaderAssignment>();
    public DbSet<TeamLeaderDelegation> TeamLeaderDelegations => Set<TeamLeaderDelegation>();

    // v1.7 Location Scale foundation.
    public DbSet<GovernmentLocationSource> GovernmentLocationSources => Set<GovernmentLocationSource>();
    public DbSet<GovernmentLocationSourceArea> GovernmentLocationSourceAreas => Set<GovernmentLocationSourceArea>();
    public DbSet<GovernmentLocationMaster> GovernmentLocationMasters => Set<GovernmentLocationMaster>();
    public DbSet<UserFavoriteLocation> UserFavoriteLocations => Set<UserFavoriteLocation>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Organization>(e =>
        {
            e.ToTable("Organizations"); e.HasKey(x => x.OrganizationId); e.Property(x => x.OrganizationId).ValueGeneratedOnAdd();
            e.Property(x => x.OrganizationCode).HasMaxLength(50); e.Property(x => x.OrganizationName).HasMaxLength(200);
            e.Property(x => x.Notes).HasMaxLength(1000); e.Property(x => x.RowVersion).IsRowVersion().IsConcurrencyToken();
            e.HasOne<User>().WithMany().HasForeignKey(x => x.InactivatedByUserId).OnDelete(DeleteBehavior.NoAction);
        });
        b.Entity<Team>(e =>
        {
            e.ToTable("Teams"); e.HasKey(x => x.TeamId); e.Property(x => x.TeamId).ValueGeneratedOnAdd();
            e.Property(x => x.TeamCode).HasMaxLength(50); e.Property(x => x.TeamName).HasMaxLength(200);
            e.Property(x => x.Notes).HasMaxLength(1000); e.Property(x => x.RowVersion).IsRowVersion().IsConcurrencyToken();
            e.HasIndex(x => new { x.OrganizationId, x.TeamCode }).IsUnique().HasDatabaseName("UX_Teams_Organization_TeamCode");
            e.HasIndex(x => new { x.OrganizationId, x.IsActive, x.EffectiveFrom, x.EffectiveTo }).HasDatabaseName("IX_Teams_Organization_Effective");
            e.HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.InactivatedByUserId).OnDelete(DeleteBehavior.NoAction);
        });
        b.Entity<User>(e => { e.ToTable("Users"); e.HasKey(x => x.UserId); e.Property(x => x.UserId).ValueGeneratedOnAdd(); e.Property(x => x.EmployeeNo).IsRequired(false); });
        b.Entity<Role>(e => { e.ToTable("Roles"); e.HasKey(x => x.RoleId); e.Property(x => x.RoleId).ValueGeneratedOnAdd(); });
        b.Entity<UserRole>(e => { e.ToTable("UserRoles"); e.HasKey(x => x.UserRoleId); e.Property(x => x.UserRoleId).ValueGeneratedOnAdd(); });

        b.Entity<Location>(e =>
        {
            e.ToTable("Locations"); e.HasKey(x => x.LocationId); e.Property(x => x.LocationId).ValueGeneratedOnAdd();
            e.Property(x => x.Latitude).HasPrecision(10, 7); e.Property(x => x.Longitude).HasPrecision(10, 7);
            e.Property(x => x.RowVersion).IsRowVersion().IsConcurrencyToken();
        });
        b.Entity<LocationApprovalHistory>(e => { e.ToTable("LocationApprovalHistory"); e.HasKey(x => x.LocationApprovalHistoryId); e.Property(x => x.LocationApprovalHistoryId).ValueGeneratedOnAdd(); });

        b.Entity<Project>(e => { e.ToTable("Projects"); e.HasKey(x => x.ProjectId); e.Property(x => x.ProjectId).ValueGeneratedOnAdd(); });
        b.Entity<ProjectLocation>(e => { e.ToTable("ProjectLocations"); e.HasKey(x => x.ProjectLocationId); e.Property(x => x.ProjectLocationId).ValueGeneratedOnAdd(); });
        b.Entity<VisitType>(e => { e.ToTable("VisitTypes"); e.HasKey(x => x.VisitTypeId); e.Property(x => x.VisitTypeId).ValueGeneratedOnAdd(); });

        b.Entity<VisitTrip>(e =>
        {
            e.ToTable("VisitTrips"); e.HasKey(x => x.VisitTripId); e.Property(x => x.VisitTripId).ValueGeneratedOnAdd();
            e.Property(x => x.RowVersion).IsRowVersion().IsConcurrencyToken();
            e.HasOne<Employment>().WithMany().HasForeignKey(x => x.EmploymentId).OnDelete(DeleteBehavior.NoAction);
            e.HasMany(x => x.Stops).WithOne(x => x.VisitTrip).HasForeignKey(x => x.VisitTripId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.MileageCalculation).WithOne(x => x.VisitTrip).HasForeignKey<MileageCalculation>(x => x.VisitTripId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<VisitTripStop>(e => { e.ToTable("VisitTripStops"); e.HasKey(x => x.VisitTripStopId); e.Property(x => x.VisitTripStopId).ValueGeneratedOnAdd(); e.HasOne(x => x.Location).WithMany().HasForeignKey(x => x.LocationId).OnDelete(DeleteBehavior.NoAction); });

        b.Entity<MileageCalculation>(e =>
        {
            e.ToTable("MileageCalculations"); e.HasKey(x => x.MileageCalculationId); e.Property(x => x.MileageCalculationId).ValueGeneratedOnAdd();
            e.Property(x => x.SystemDistanceKm).HasPrecision(10,2); e.Property(x => x.ClaimedDistanceKm).HasPrecision(10,2); e.Property(x => x.ApprovedDistanceKm).HasPrecision(10,2);
            e.Property(x => x.RatePerKmSnapshot).HasPrecision(10,2); e.Property(x => x.ClaimedAmount).HasPrecision(12,2); e.Property(x => x.ApprovedAmount).HasPrecision(12,2);
        });
        b.Entity<MileageRateRule>(e => { e.ToTable("MileageRateRules"); e.HasKey(x => x.MileageRateRuleId); e.Property(x => x.MileageRateRuleId).ValueGeneratedOnAdd(); e.Property(x => x.RatePerKm).HasPrecision(10,2); });
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
            e.ToTable("VisitTripSnapshots"); e.HasKey(x => x.VisitTripSnapshotId); e.Property(x => x.VisitTripSnapshotId).ValueGeneratedOnAdd();
            e.HasIndex(x => new { x.VisitTripId, x.SnapshotVersion }).IsUnique();
            e.Property(x => x.ClaimedDistanceKmSnapshot).HasPrecision(10,2);
            e.Property(x => x.SystemDistanceKmSnapshot).HasPrecision(10,2);
            e.Property(x => x.ApprovedDistanceKmSnapshot).HasPrecision(10,2);
            e.Property(x => x.RatePerKmSnapshot).HasPrecision(10,2);
            e.Property(x => x.SubsidyAmountSnapshot).HasPrecision(12,2);
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

        // v1.8 Organization / People / Team read foundation. These mappings
        // mirror 1800_001 and 1800_002; schema changes remain script-owned.
        b.Entity<Center>(e =>
        {
            e.ToTable("Centers"); e.HasKey(x => x.CenterId); e.Property(x => x.CenterId).ValueGeneratedOnAdd();
            e.Property(x => x.CenterCode).HasMaxLength(50); e.Property(x => x.CenterName).HasMaxLength(200);
            e.Property(x => x.Notes).HasMaxLength(1000); e.Property(x => x.RowVersion).IsRowVersion().IsConcurrencyToken();
            e.HasIndex(x => new { x.OrganizationId, x.CenterCode }).IsUnique().HasDatabaseName("UQ_Centers_Organization_Code");
            e.HasIndex(x => new { x.OrganizationId, x.IsActive, x.EffectiveFrom, x.EffectiveTo }).HasDatabaseName("IX_Centers_Organization_Effective");
            e.HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UpdatedByUserId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.InactivatedByUserId).OnDelete(DeleteBehavior.NoAction);
        });
        b.Entity<TeamCenterAssignment>(e =>
        {
            e.ToTable("TeamCenterAssignments"); e.HasKey(x => x.TeamCenterAssignmentId); e.Property(x => x.TeamCenterAssignmentId).ValueGeneratedOnAdd();
            e.Property(x => x.ChangeReason).HasMaxLength(500); e.Property(x => x.RowVersion).IsRowVersion().IsConcurrencyToken();
            e.HasIndex(x => new { x.TeamId, x.EffectiveFrom }).IsUnique().HasDatabaseName("UQ_TeamCenterAssignments_Team_Start");
            e.HasIndex(x => new { x.CenterId, x.EffectiveFrom, x.EffectiveTo, x.TeamId }).HasDatabaseName("IX_TeamCenterAssignments_Center_Effective");
            e.HasOne<Team>().WithMany().HasForeignKey(x => x.TeamId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<Center>().WithMany().HasForeignKey(x => x.CenterId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.NoAction);
        });
        b.Entity<Person>(e =>
        {
            e.ToTable("Persons"); e.HasKey(x => x.PersonId); e.Property(x => x.PersonId).ValueGeneratedOnAdd();
            e.Property(x => x.DisplayName).HasMaxLength(200); e.Property(x => x.RowVersion).IsRowVersion().IsConcurrencyToken();
            e.HasIndex(x => x.LegacyUserId).IsUnique().HasFilter("[LegacyUserId] IS NOT NULL").HasDatabaseName("UX_Persons_LegacyUserId");
            e.HasOne<User>().WithMany().HasForeignKey(x => x.LegacyUserId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UpdatedByUserId).OnDelete(DeleteBehavior.NoAction);
        });
        b.Entity<Employment>(e =>
        {
            e.ToTable("Employments"); e.HasKey(x => x.EmploymentId); e.Property(x => x.EmploymentId).ValueGeneratedOnAdd();
            e.Property(x => x.EmployeeNo).HasMaxLength(50); e.Property(x => x.Email).HasMaxLength(320);
            e.Property(x => x.SourceType).HasMaxLength(30); e.Property(x => x.SourceReference).HasMaxLength(200);
            e.Property(x => x.RowVersion).IsRowVersion().IsConcurrencyToken();
            e.HasIndex(x => new { x.OrganizationId, x.EmployeeNo }).IsUnique().HasFilter("[EmployeeNo] IS NOT NULL").HasDatabaseName("UX_Employments_Organization_EmployeeNo");
            e.HasIndex(x => x.LegacyUserId).IsUnique().HasFilter("[LegacyUserId] IS NOT NULL").HasDatabaseName("UX_Employments_LegacyUserId");
            e.HasIndex(x => new { x.PersonId, x.HireDate, x.TerminationDate }).HasDatabaseName("IX_Employments_Person");
            e.HasIndex(x => x.Email).HasFilter("[Email] IS NOT NULL").HasDatabaseName("IX_Employments_Email");
            e.HasOne<Person>().WithMany().HasForeignKey(x => x.PersonId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.LegacyUserId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UpdatedByUserId).OnDelete(DeleteBehavior.NoAction);
        });
        b.Entity<EmploymentStatusPeriod>(e =>
        {
            e.ToTable("EmploymentStatusPeriods"); e.HasKey(x => x.EmploymentStatusPeriodId); e.Property(x => x.EmploymentStatusPeriodId).ValueGeneratedOnAdd();
            e.Property(x => x.EmploymentStatus).HasMaxLength(30); e.Property(x => x.SourceType).HasMaxLength(30); e.Property(x => x.SourceReference).HasMaxLength(200);
            e.Property(x => x.RowVersion).IsRowVersion().IsConcurrencyToken();
            e.HasIndex(x => new { x.EmploymentId, x.EffectiveFrom }).IsUnique().HasDatabaseName("UQ_EmploymentStatusPeriods_Start");
            e.HasIndex(x => new { x.EmploymentId, x.EffectiveFrom, x.EffectiveTo, x.EmploymentStatus }).HasDatabaseName("IX_EmploymentStatusPeriods_AsOf");
            e.HasOne<Employment>().WithMany().HasForeignKey(x => x.EmploymentId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.NoAction);
        });
        b.Entity<EmploymentRoleAssignment>(e =>
        {
            e.ToTable("EmploymentRoleAssignments"); e.HasKey(x => x.EmploymentRoleAssignmentId); e.Property(x => x.EmploymentRoleAssignmentId).ValueGeneratedOnAdd();
            e.Property(x => x.RowVersion).IsRowVersion().IsConcurrencyToken();
            e.HasIndex(x => new { x.EmploymentId, x.RoleId, x.EffectiveFrom }).IsUnique().HasDatabaseName("UQ_EmploymentRoleAssignments_Start");
            e.HasIndex(x => new { x.EmploymentId, x.EffectiveFrom, x.EffectiveTo, x.RoleId }).HasDatabaseName("IX_EmploymentRoleAssignments_AsOf");
            e.HasOne<Employment>().WithMany().HasForeignKey(x => x.EmploymentId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<Role>().WithMany().HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.AssignedByUserId).OnDelete(DeleteBehavior.NoAction);
        });
        b.Entity<TeamMembership>(e =>
        {
            e.ToTable("TeamMemberships"); e.HasKey(x => x.TeamMembershipId); e.Property(x => x.TeamMembershipId).ValueGeneratedOnAdd();
            e.Property(x => x.ChangeReason).HasMaxLength(500); e.Property(x => x.RowVersion).IsRowVersion().IsConcurrencyToken();
            e.HasIndex(x => new { x.EmploymentId, x.TeamId, x.EffectiveFrom }).IsUnique().HasDatabaseName("UQ_TeamMemberships_Start");
            e.HasIndex(x => new { x.EmploymentId, x.EffectiveFrom, x.EffectiveTo, x.IsPrimary, x.TeamId }).HasDatabaseName("IX_TeamMemberships_Employment_AsOf");
            e.HasIndex(x => new { x.TeamId, x.EffectiveFrom, x.EffectiveTo, x.EmploymentId }).HasDatabaseName("IX_TeamMemberships_Team_AsOf");
            e.HasOne<Employment>().WithMany().HasForeignKey(x => x.EmploymentId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<Team>().WithMany().HasForeignKey(x => x.TeamId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.AssignedByUserId).OnDelete(DeleteBehavior.NoAction);
        });
        b.Entity<TeamLeaderAssignment>(e =>
        {
            e.ToTable("TeamLeaderAssignments"); e.HasKey(x => x.TeamLeaderAssignmentId); e.Property(x => x.TeamLeaderAssignmentId).ValueGeneratedOnAdd();
            e.Property(x => x.RowVersion).IsRowVersion().IsConcurrencyToken();
            e.HasIndex(x => new { x.TeamId, x.EmploymentId, x.EffectiveFrom }).IsUnique().HasDatabaseName("UQ_TeamLeaderAssignments_Start");
            e.HasIndex(x => new { x.TeamId, x.EffectiveFrom, x.EffectiveTo, x.EmploymentId }).HasDatabaseName("IX_TeamLeaderAssignments_Team_AsOf");
            e.HasOne<Team>().WithMany().HasForeignKey(x => x.TeamId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<Employment>().WithMany().HasForeignKey(x => x.EmploymentId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.AssignedByUserId).OnDelete(DeleteBehavior.NoAction);
        });
        b.Entity<TeamLeaderDelegation>(e =>
        {
            e.ToTable("TeamLeaderDelegations"); e.HasKey(x => x.TeamLeaderDelegationId); e.Property(x => x.TeamLeaderDelegationId).ValueGeneratedOnAdd();
            e.Property(x => x.Reason).HasMaxLength(500); e.Property(x => x.RowVersion).IsRowVersion().IsConcurrencyToken();
            e.HasIndex(x => new { x.TeamLeaderAssignmentId, x.DelegateEmploymentId, x.EffectiveFrom }).IsUnique().HasDatabaseName("UQ_TeamLeaderDelegations_Start");
            e.HasIndex(x => new { x.DelegateEmploymentId, x.EffectiveFrom, x.EffectiveTo, x.TeamLeaderAssignmentId }).HasDatabaseName("IX_TeamLeaderDelegations_AsOf");
            e.HasOne<TeamLeaderAssignment>().WithMany().HasForeignKey(x => x.TeamLeaderAssignmentId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<Employment>().WithMany().HasForeignKey(x => x.DelegateEmploymentId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.NoAction);
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
    }
}
