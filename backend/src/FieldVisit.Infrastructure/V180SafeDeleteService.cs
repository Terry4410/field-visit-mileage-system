using System.Text.Json;
using FieldVisit.Application;
using FieldVisit.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FieldVisit.Infrastructure;

public sealed class V180SafeDeleteService(
    AppDbContext db,
    ICurrentUserService current)
{
    private CurrentUserDto Admin()
    {
        var user=current.GetRequired();
        if(!user.Roles.Any(x=>x.Equals("admin",StringComparison.OrdinalIgnoreCase)))
            throw new UnauthorizedAccessException("只有管理者可以執行永久刪除。");
        if(!user.OrganizationId.HasValue)
            throw new UnauthorizedAccessException("管理者缺少 Organization scope。");
        return user;
    }

    public async Task<V180PersonDeleteImpactDto> PersonImpactAsync(int userId,CancellationToken ct)
        => await PersonImpactAsync(Admin(),userId,ct);

    public async Task DeletePersonAsync(int userId,CancellationToken ct)
    {
        var admin=Admin();
        var impact=await PersonImpactAsync(admin,userId,ct);
        if(!impact.CanDelete)throw new InvalidOperationException(impact.Reason??"此人員不可永久刪除。");

        var identity=await db.UserIdentityProfiles.AsNoTracking().SingleAsync(x=>x.UserId==userId,ct);
        var employmentId=identity.EmploymentId;
        long? personId=null;
        if(employmentId.HasValue)
            personId=await db.Employments.AsNoTracking().Where(x=>x.EmploymentId==employmentId.Value).Select(x=>(long?)x.PersonId).SingleOrDefaultAsync(ct);

        var strategy=db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async()=>{
            db.ChangeTracker.Clear();
            await using var tx=await db.Database.BeginTransactionAsync(ct);
            try
            {
                await db.UserFavoriteLocations.Where(x=>x.UserId==userId).ExecuteDeleteAsync(ct);
                await db.UserCapabilities.Where(x=>x.UserId==userId).ExecuteDeleteAsync(ct);
                await db.UserDataScopes.Where(x=>x.UserId==userId).ExecuteDeleteAsync(ct);
                await db.UserTeamAssignments.Where(x=>x.UserId==userId).ExecuteDeleteAsync(ct);
                await db.UserRoleAssignments.Where(x=>x.UserId==userId).ExecuteDeleteAsync(ct);
                await db.UserEmploymentPeriods.Where(x=>x.UserId==userId).ExecuteDeleteAsync(ct);
                await db.UserTeamScopes.Where(x=>x.UserId==userId).ExecuteDeleteAsync(ct);
                await db.UserRoles.Where(x=>x.UserId==userId).ExecuteDeleteAsync(ct);

                if(employmentId.HasValue)
                {
                    var eid=employmentId.Value;
                    await db.EmploymentDeploymentSiteAssignments.Where(x=>x.EmploymentId==eid).ExecuteDeleteAsync(ct);
                    await db.TeamMemberships.Where(x=>x.EmploymentId==eid).ExecuteDeleteAsync(ct);
                    await db.EmploymentRoleAssignments.Where(x=>x.EmploymentId==eid).ExecuteDeleteAsync(ct);
                    await db.EmploymentStatusPeriods.Where(x=>x.EmploymentId==eid).ExecuteDeleteAsync(ct);
                }

                await db.UserIdentityProfiles.Where(x=>x.UserId==userId).ExecuteDeleteAsync(ct);

                if(employmentId.HasValue)
                    await db.Employments.Where(x=>x.EmploymentId==employmentId.Value).ExecuteDeleteAsync(ct);
                if(personId.HasValue)
                    await db.Persons.Where(x=>x.PersonId==personId.Value).ExecuteDeleteAsync(ct);

                await db.Users.Where(x=>x.UserId==userId).ExecuteDeleteAsync(ct);

                db.AuditLogs.Add(new AuditLog{
                    UserId=admin.UserId,
                    EntityType="User",
                    EntityId=userId.ToString(),
                    Action="PersonPermanentDelete",
                    NewValues=JsonSerializer.Serialize(new{impact.UserCode,impact.DisplayName}),
                    CreatedAt=DateTime.UtcNow
                });
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
            }
            catch(DbUpdateException)
            {
                throw new InvalidOperationException("此人員仍有資料庫歷史關聯，無法永久刪除；請改用離職／歷史資料管理。");
            }
        });
    }

    public async Task<V180TeamDeleteImpactDto> TeamImpactAsync(int teamId,CancellationToken ct)
        => await TeamImpactAsync(Admin(),teamId,ct);

    public async Task DeleteTeamAsync(int teamId,CancellationToken ct)
    {
        var admin=Admin();
        var impact=await TeamImpactAsync(admin,teamId,ct);
        if(!impact.CanDelete)throw new InvalidOperationException(impact.Reason??"此小組不可永久刪除。");
        var row=await db.Teams.SingleAsync(x=>x.TeamId==teamId&&x.OrganizationId==admin.OrganizationId!.Value,ct);
        db.Teams.Remove(row);
        db.AuditLogs.Add(new AuditLog{
            UserId=admin.UserId,EntityType="Team",EntityId=teamId.ToString(),
            Action="TeamPermanentDelete",
            NewValues=JsonSerializer.Serialize(new{impact.TeamCode,impact.TeamName}),
            CreatedAt=DateTime.UtcNow
        });
        await db.SaveChangesAsync(ct);
    }

    public async Task<V180ProjectDeleteImpactDto> ProjectImpactAsync(int projectId,CancellationToken ct)
        => await ProjectImpactAsync(Admin(),projectId,ct);

    public async Task DeleteProjectAsync(int projectId,CancellationToken ct)
    {
        var admin=Admin();
        var impact=await ProjectImpactAsync(admin,projectId,ct);
        if(!impact.CanDelete)throw new InvalidOperationException(impact.Reason??"此專案不可永久刪除。");
        var row=await db.Projects.SingleAsync(x=>x.ProjectId==projectId&&x.OrganizationId==admin.OrganizationId!.Value,ct);
        await db.ProjectLocations.Where(x=>x.ProjectId==projectId).ExecuteDeleteAsync(ct);
        db.Projects.Remove(row);
        db.AuditLogs.Add(new AuditLog{
            UserId=admin.UserId,EntityType="Project",EntityId=projectId.ToString(),
            Action="ProjectPermanentDelete",
            NewValues=JsonSerializer.Serialize(new{impact.ProjectCode,impact.ProjectName}),
            CreatedAt=DateTime.UtcNow
        });
        await db.SaveChangesAsync(ct);
    }

    public Task<V180VisitTypeDeleteImpactDto> VisitTypeImpactAsync(int visitTypeId,CancellationToken ct)
        => VisitTypeImpactAsync(Admin(),visitTypeId,ct);

    public async Task DeleteVisitTypeAsync(int visitTypeId,CancellationToken ct)
    {
        var admin=Admin();
        var impact=await VisitTypeImpactAsync(admin,visitTypeId,ct);
        if(!impact.CanDelete)throw new InvalidOperationException(impact.Reason??"此拜訪形式不可永久刪除。");
        var row=await db.VisitTypes.SingleAsync(x=>x.VisitTypeId==visitTypeId,ct);
        db.VisitTypes.Remove(row);
        db.AuditLogs.Add(new AuditLog{
            UserId=admin.UserId,EntityType="VisitType",EntityId=visitTypeId.ToString(),
            Action="VisitTypePermanentDelete",
            NewValues=JsonSerializer.Serialize(new{impact.VisitTypeCode,impact.VisitTypeName}),
            CreatedAt=DateTime.UtcNow
        });
        try{await db.SaveChangesAsync(ct);}
        catch(DbUpdateException){throw new InvalidOperationException("此拜訪形式仍有歷史資料庫關聯，無法永久刪除；請改用停用。");}
    }

    public Task<V180MileageRateDeleteImpactDto> MileageRateImpactAsync(int mileageRateRuleId,CancellationToken ct)
        => MileageRateImpactAsync(Admin(),mileageRateRuleId,ct);

    public async Task DeleteMileageRateAsync(int mileageRateRuleId,CancellationToken ct)
    {
        var admin=Admin();
        var impact=await MileageRateImpactAsync(admin,mileageRateRuleId,ct);
        if(!impact.CanDelete)throw new InvalidOperationException(impact.Reason??"此補助費率不可永久刪除。");
        var row=await db.MileageRateRules.SingleAsync(
            x=>x.MileageRateRuleId==mileageRateRuleId&&x.OrganizationId==admin.OrganizationId!.Value,ct);
        db.MileageRateRules.Remove(row);
        db.AuditLogs.Add(new AuditLog{
            UserId=admin.UserId,EntityType="MileageRateRule",EntityId=mileageRateRuleId.ToString(),
            Action="MileageRatePermanentDelete",
            NewValues=JsonSerializer.Serialize(new{impact.RuleName,impact.VehicleType,impact.EffectiveFrom}),
            CreatedAt=DateTime.UtcNow
        });
        try{await db.SaveChangesAsync(ct);}
        catch(DbUpdateException){throw new InvalidOperationException("此補助費率仍有歷史資料庫關聯，無法永久刪除；請改用停用。");}
    }

    private async Task<V180VisitTypeDeleteImpactDto> VisitTypeImpactAsync(CurrentUserDto admin,int visitTypeId,CancellationToken ct)
    {
        _=admin;
        var row=await db.VisitTypes.AsNoTracking().SingleOrDefaultAsync(x=>x.VisitTypeId==visitTypeId,ct)
            ??throw new KeyNotFoundException("找不到拜訪形式。");
        var tripStops=await db.VisitTripStops.CountAsync(x=>x.VisitTypeId==visitTypeId,ct);
        var snapshotStops=await db.VisitTripSnapshotStops.CountAsync(x=>x.VisitTypeId==visitTypeId,ct);
        var canDelete=tripStops==0&&snapshotStops==0;
        var reason=canDelete?null:
            $"已有歷史使用：行程停靠 {tripStops}、Snapshot 停靠 {snapshotStops}。為保留歷史完整性，只能停用。";
        return new(visitTypeId,row.VisitTypeCode,row.VisitTypeName,canDelete,tripStops,snapshotStops,reason);
    }

    private async Task<V180MileageRateDeleteImpactDto> MileageRateImpactAsync(CurrentUserDto admin,int mileageRateRuleId,CancellationToken ct)
    {
        var row=await db.MileageRateRules.AsNoTracking().SingleOrDefaultAsync(
            x=>x.MileageRateRuleId==mileageRateRuleId&&x.OrganizationId==admin.OrganizationId!.Value,ct)
            ??throw new KeyNotFoundException("找不到補助費率。");
        var calculations=await db.MileageCalculations.CountAsync(x=>x.MileageRateRuleId==mileageRateRuleId,ct);
        var canDelete=calculations==0;
        var reason=canDelete?null:
            $"已有 {calculations} 筆里程計算使用此費率版本。歷史 Snapshot／核准金額必須保留，只能停用。";
        return new(mileageRateRuleId,row.RuleName,row.VehicleType,row.EffectiveFrom,canDelete,calculations,reason);
    }

    private async Task<V180PersonDeleteImpactDto> PersonImpactAsync(CurrentUserDto admin,int userId,CancellationToken ct)
    {
        var user=await db.Users.AsNoTracking().SingleOrDefaultAsync(x=>x.UserId==userId&&x.OrganizationId==admin.OrganizationId!.Value,ct)
            ??throw new KeyNotFoundException("找不到人員。");
        var identity=await db.UserIdentityProfiles.AsNoTracking().SingleOrDefaultAsync(x=>x.UserId==userId,ct)
            ??throw new InvalidOperationException("人員缺少 Identity Profile，請由 IT 檢查。");
        if(!identity.UserType.Equals(UserTypes.Internal,StringComparison.OrdinalIgnoreCase))
            return new(userId,identity.UserCode,user.DisplayName,false,0,0,0,0,0,0,"External Supervisor 不使用一般人員永久刪除流程。");
        if(userId==admin.UserId)
            return new(userId,identity.UserCode,user.DisplayName,false,0,0,0,0,0,0,"目前登入中的管理者不可刪除自己。");

        var eid=identity.EmploymentId;
        var trip=await db.VisitTrips.CountAsync(x=>x.UserId==userId||(eid.HasValue&&x.EmploymentId==eid.Value),ct);
        var snapshots=await db.VisitTripSnapshots.CountAsync(x=>x.UserId==userId||x.ApproverUserId==userId||x.CreatedByUserId==userId,ct);
        var workflow=
            await db.ApprovalRecords.CountAsync(x=>x.ApproverUserId==userId,ct)
            +await db.VisitTripStatusHistories.CountAsync(x=>x.ActionByUserId==userId,ct)
            +await db.CorrectionRequests.CountAsync(x=>x.RequestedByUserId==userId||x.LeaderReviewedByUserId==userId||x.AdminClosedByUserId==userId,ct)
            +await db.MileageCalculations.CountAsync(x=>x.DistanceApprovedByUserId==userId||x.InvalidatedByUserId==userId,ct)
            +await db.BackgroundJobs.CountAsync(x=>x.RequestedByUserId==userId,ct)
            +await db.GeocodingAttempts.CountAsync(x=>x.RequestedByUserId==userId,ct)
            +await db.RouteCalculationAttempts.CountAsync(x=>x.RequestedByUserId==userId,ct)
            +await db.MileageGovernanceEvents.CountAsync(x=>x.ActorUserId==userId,ct);
        var audit=await db.AuditLogs.CountAsync(x=>x.UserId==userId,ct);

        var leadership=0;
        if(eid.HasValue)
        {
            var leaderIds=await db.TeamLeaderAssignments.AsNoTracking().Where(x=>x.EmploymentId==eid.Value).Select(x=>x.TeamLeaderAssignmentId).ToListAsync(ct);
            leadership=leaderIds.Count
                +await db.TeamLeaderDelegations.CountAsync(x=>x.DelegateEmploymentId==eid.Value||leaderIds.Contains(x.TeamLeaderAssignmentId),ct);
        }

        var adminRefs=
            await db.Persons.CountAsync(x=>x.CreatedByUserId==userId||x.UpdatedByUserId==userId,ct)
            +await db.UserRoleAssignments.CountAsync(x=>x.AssignedByUserId==userId,ct)
            +await db.UserTeamAssignments.CountAsync(x=>x.AssignedByUserId==userId,ct)
            +await db.UserDataScopes.CountAsync(x=>x.GrantedByUserId==userId,ct)
            +await db.UserCapabilities.CountAsync(x=>x.GrantedByUserId==userId,ct)
            +await db.UserTeamScopes.CountAsync(x=>x.AssignedByUserId==userId,ct)
            +await db.TeamMemberships.CountAsync(x=>x.AssignedByUserId==userId,ct)
            +await db.TeamCenterAssignments.CountAsync(x=>x.CreatedByUserId==userId,ct)
            +await db.EmploymentDeploymentSiteAssignments.CountAsync(x=>x.CreatedByUserId==userId,ct)
            +await db.TeamLeaderAssignments.CountAsync(x=>x.AssignedByUserId==userId,ct)
            +await db.TeamLeaderDelegations.CountAsync(x=>x.CreatedByUserId==userId,ct)
            +await db.TeamLocationNotes.CountAsync(x=>x.CreatedByUserId==userId||x.UpdatedByUserId==userId,ct)
            +await db.TeamLocationNoteHistories.CountAsync(x=>x.ChangedByUserId==userId,ct)
            +await db.LocationApprovalHistories.CountAsync(x=>x.ReviewedByUserId==userId,ct)
            +await db.GovernmentLocationMasters.CountAsync(x=>x.ReviewedByUserId==userId,ct)
            +await db.Locations.CountAsync(x=>x.CreatedByUserId==userId,ct);

        var canDelete=trip==0&&snapshots==0&&workflow==0&&audit==0&&leadership==0&&adminRefs==0;
        var reason=canDelete?null:
            $"已有歷史關聯：行程 {trip}、Snapshot {snapshots}、流程/里程證據 {workflow}、Audit {audit}、組長歷史 {leadership}、管理異動 {adminRefs}。請改用離職／歷史資料管理。";
        return new(userId,identity.UserCode,user.DisplayName,canDelete,trip,snapshots,workflow,audit,leadership,adminRefs,reason);
    }

    private async Task<V180TeamDeleteImpactDto> TeamImpactAsync(CurrentUserDto admin,int teamId,CancellationToken ct)
    {
        var row=await db.Teams.AsNoTracking().SingleOrDefaultAsync(x=>x.TeamId==teamId&&x.OrganizationId==admin.OrganizationId!.Value,ct)
            ??throw new KeyNotFoundException("找不到小組。");
        var memberships=
            await db.UserTeamAssignments.CountAsync(x=>x.TeamId==teamId,ct)
            +await db.TeamMemberships.CountAsync(x=>x.TeamId==teamId,ct)
            +await db.UserTeamScopes.CountAsync(x=>x.TeamId==teamId,ct)
            +await db.Users.CountAsync(x=>x.TeamId==teamId,ct)
            +await db.TeamLeaderAssignments.CountAsync(x=>x.TeamId==teamId,ct);
        var scopes=await db.UserDataScopes.CountAsync(x=>x.TeamId==teamId,ct);
        var projects=await db.Projects.CountAsync(x=>x.TeamId==teamId,ct);
        var locations=await db.Locations.CountAsync(x=>x.TeamId==teamId,ct);
        var trips=await db.VisitTrips.CountAsync(x=>x.TeamId==teamId,ct);
        var snapshots=await db.VisitTripSnapshots.CountAsync(x=>x.TeamId==teamId,ct);
        var structure=
            await db.TeamCenterAssignments.CountAsync(x=>x.TeamId==teamId,ct)
            +await db.TeamDeploymentSiteAssignments.CountAsync(x=>x.TeamId==teamId,ct);
        var notes=
            await db.TeamLocationNotes.CountAsync(x=>x.TeamId==teamId,ct)
            +await db.TeamLocationNoteHistories.CountAsync(x=>x.TeamId==teamId,ct);

        var canDelete=memberships==0&&scopes==0&&projects==0&&locations==0&&trips==0&&snapshots==0&&structure==0&&notes==0;
        var reason=canDelete?null:
            $"已有歷史關聯：成員/組長 {memberships}、Scope {scopes}、專案 {projects}、地點 {locations}、行程 {trips}、Snapshot {snapshots}、中心/派駐 {structure}、備註 {notes}。請改用停用。";
        return new(teamId,row.TeamCode,row.TeamName,canDelete,memberships,scopes,projects,locations,trips,snapshots,structure,notes,reason);
    }

    private async Task<V180ProjectDeleteImpactDto> ProjectImpactAsync(CurrentUserDto admin,int projectId,CancellationToken ct)
    {
        var row=await db.Projects.AsNoTracking().SingleOrDefaultAsync(x=>x.ProjectId==projectId&&x.OrganizationId==admin.OrganizationId!.Value,ct)
            ??throw new KeyNotFoundException("找不到專案。");
        var tripStops=await db.VisitTripStops.CountAsync(x=>x.ProjectId==projectId,ct);
        var snapshotStops=await db.VisitTripSnapshotStops.CountAsync(x=>x.ProjectId==projectId,ct);
        var locations=await db.ProjectLocations.CountAsync(x=>x.ProjectId==projectId,ct);
        var canDelete=tripStops==0&&snapshotStops==0;
        var reason=canDelete?null:
            $"已有歷史使用：行程停靠 {tripStops}、Snapshot 停靠 {snapshotStops}。為保留歷史完整性，只能停用。";
        return new(projectId,row.ProjectCode,row.ProjectName,canDelete,tripStops,snapshotStops,locations,reason);
    }
}
