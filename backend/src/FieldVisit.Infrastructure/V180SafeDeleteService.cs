using System.Text.Json;
using FieldVisit.Application;
using FieldVisit.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FieldVisit.Infrastructure;

public sealed class V180SafeDeleteService(
    AppDbContext db,
    ICurrentUserService current)
{
    /// <summary>
    /// All seven safe-delete domains must recheck current DB authority on
    /// preview and execution, not just the possibly stale Admin JWT.
    /// No Manager grant, B3 approval or new destructive scope is conferred.
    /// </summary>
    private async Task<CurrentUserDto> LiveAdminAsync(CancellationToken ct)
    {
        var user=current.GetRequired();
        if(!user.OrganizationId.HasValue)
            throw new UnauthorizedAccessException("管理者缺少 Organization scope。");
        var account=await db.Users.AsNoTracking().FirstOrDefaultAsync(x=>
            x.UserId==user.UserId&&x.OrganizationId==user.OrganizationId,ct)
            ??throw new UnauthorizedAccessException("管理帳號或組織授權已失效。");
        // Internal HR eligibility alone does not prove the Users account is
        // enabled. Refuse an explicitly disabled account before ANY preview
        // or destructive SafeDelete operation, even if HR status is Active.
        if(!account.IsActive)
            throw new UnauthorizedAccessException("管理帳號已停用，禁止永久刪除。");
        if(!(await new V170AccessControl(db).EvaluateLoginAsync(
            user.UserId,account.IsActive,ct)).IsAllowed)
            throw new UnauthorizedAccessException("目前 HR 身分不允許永久刪除。");
        var today=BusinessTime.Today;
        var dated=await (
            from grant in db.UserRoleAssignments.AsNoTracking()
            join role in db.Roles.AsNoTracking() on grant.RoleId equals role.RoleId
            where grant.UserId==user.UserId&&role.IsActive
                &&grant.EffectiveFrom<=today
                &&(!grant.EffectiveTo.HasValue||grant.EffectiveTo>=today)
            select role.RoleCode).ToListAsync(ct);
        var projected=await (
            from grant in db.UserRoles.AsNoTracking()
            join role in db.Roles.AsNoTracking() on grant.RoleId equals role.RoleId
            where grant.UserId==user.UserId&&role.IsActive
            select role.RoleCode).ToListAsync(ct);
        V180LocationAdminMutationRules.RequireCurrentAdmin(user,dated,projected);
        return user;
    }

    // OWNER-BUAT-SAFE-DELETE-002: dependency inventory is authoritative on both preview and execution.
    public async Task<V180CenterDeleteImpactDto> CenterImpactAsync(int id,CancellationToken ct)
        => await CenterImpactAsync(await LiveAdminAsync(ct),id,ct);
    private async Task<V180CenterDeleteImpactDto> CenterImpactAsync(CurrentUserDto admin,int id,CancellationToken ct)
    {
        var c=await db.Centers.AsNoTracking().SingleOrDefaultAsync(x=>x.CenterId==id&&x.OrganizationId==admin.OrganizationId!.Value,ct)
            ??throw new KeyNotFoundException("找不到此組織的就業中心。");
        var sites=await db.DeploymentSites.CountAsync(x=>x.CenterId==id,ct);
        var assignments=await db.TeamCenterAssignments.CountAsync(x=>x.CenterId==id,ct);
        var history=await db.VisitTripSnapshots.CountAsync(x=>EF.Property<int?>(x,"CenterIdSnapshot")==id,ct);
        var can=sites==0&&assignments==0&&history==0;
        return new(id,c.CenterCode,c.CenterName,can,sites,assignments,history,
            can?null:$"存在歷史或關聯：官方據點 {sites}、小組中心關聯 {assignments}、歷史 Snapshot {history}。只能停用，不能永久刪除。");
    }
    public async Task DeleteCenterAsync(int id,CancellationToken ct)
    {
        var admin=await LiveAdminAsync(ct);
        await using var tx=await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable,ct);
        var impact=await CenterImpactAsync(admin,id,ct);
        if(!impact.CanDelete)throw new InvalidOperationException(impact.Reason??"中心仍有關聯，不能永久刪除。");
        var c=await db.Centers.SingleAsync(x=>x.CenterId==id&&x.OrganizationId==admin.OrganizationId!.Value,ct);
        db.Centers.Remove(c);
        db.AuditLogs.Add(new AuditLog{UserId=admin.UserId,EntityType="Center",EntityId=id.ToString(),Action="CenterPermanentDelete",NewValues=JsonSerializer.Serialize(new{impact.Code,impact.Name}),CreatedAt=DateTime.UtcNow});
        try{await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);}
        catch(DbUpdateException){throw new InvalidOperationException("就業中心有未列入的資料庫關聯；永久刪除已拒絕，請改用停用。");}
    }
    public async Task<V180SiteDeleteImpactDto> SiteImpactAsync(int id,CancellationToken ct)
        => await SiteImpactAsync(await LiveAdminAsync(ct),id,ct);
    private async Task<V180SiteDeleteImpactDto> SiteImpactAsync(CurrentUserDto admin,int id,CancellationToken ct)
    {
        var s=await (from site in db.DeploymentSites.AsNoTracking() join center in db.Centers on site.CenterId equals center.CenterId
            where site.DeploymentSiteId==id&&center.OrganizationId==admin.OrganizationId!.Value select site).SingleOrDefaultAsync(ct)
            ??throw new KeyNotFoundException("找不到此組織的官方據點。");
        var loc=await db.DeploymentSiteLocationAssignments.AsNoTracking().Where(x=>x.DeploymentSiteId==id).ToListAsync(ct);
        var teams=await db.TeamDeploymentSiteAssignments.CountAsync(x=>x.DeploymentSiteId==id,ct);
        var employment=await db.EmploymentDeploymentSiteAssignments.CountAsync(x=>x.DeploymentSiteId==id,ct);
        var trips=await db.VisitTrips.CountAsync(x=>x.StartDeploymentSiteId==id||x.EndDeploymentSiteId==id,ct);
        var snapshots=await db.VisitTripSnapshots.CountAsync(x=>EF.Property<int?>(x,"StartDeploymentSiteIdSnapshot")==id||EF.Property<int?>(x,"EndDeploymentSiteIdSnapshot")==id,ct);
        // The single automatic initial Location mapping is configuration, not historical use.
        // Any relocation / multiple or changed periods must remain preserved.
        var initialOnly=loc.Count==1&&loc[0].EffectiveFrom==s.EffectiveFrom&&loc[0].EffectiveTo==s.EffectiveTo&&string.Equals(loc[0].ChangeReason,"UAT Business Admin initial assignment",StringComparison.Ordinal);
        var can=initialOnly&&teams==0&&employment==0&&trips==0&&snapshots==0;
        return new(id,s.SiteCode,s.SiteName,can,loc.Count,teams,employment,trips,snapshots,
            can?null:$"存在使用或搬遷歷史：Location 關聯 {loc.Count}、小組派駐 {teams}、人員派駐 {employment}、行程 {trips}、Snapshot {snapshots}。僅允許未使用且僅有初始 Location 關聯的誤建據點永久刪除；其他請停用。");
    }
    public async Task DeleteSiteAsync(int id,CancellationToken ct)
    {
        var admin=await LiveAdminAsync(ct);
        await using var tx=await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable,ct);
        var impact=await SiteImpactAsync(admin,id,ct);
        if(!impact.CanDelete)throw new InvalidOperationException(impact.Reason??"此據點仍有關聯，不能永久刪除。");
        var s=await (from site in db.DeploymentSites join center in db.Centers on site.CenterId equals center.CenterId
            where site.DeploymentSiteId==id&&center.OrganizationId==admin.OrganizationId!.Value select site).SingleAsync(ct);
        await db.DeploymentSiteLocationAssignments.Where(x=>x.DeploymentSiteId==id).ExecuteDeleteAsync(ct);
        db.DeploymentSites.Remove(s);
        db.AuditLogs.Add(new AuditLog{UserId=admin.UserId,EntityType="DeploymentSite",EntityId=id.ToString(),Action="DeploymentSitePermanentDelete",NewValues=JsonSerializer.Serialize(new{impact.Code,impact.Name}),CreatedAt=DateTime.UtcNow});
        try{await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);}
        catch(DbUpdateException){throw new InvalidOperationException("官方據點有其他資料庫關聯；永久刪除已拒絕，請改用停用。");}
    }

    public async Task<V180PersonDeleteImpactDto> PersonImpactAsync(int userId,CancellationToken ct)
        => await PersonImpactAsync(await LiveAdminAsync(ct),userId,ct);

    public async Task DeletePersonAsync(int userId,CancellationToken ct)
    {
        var admin=await LiveAdminAsync(ct);
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
        => await TeamImpactAsync(await LiveAdminAsync(ct),teamId,ct);

    public async Task DeleteTeamAsync(int teamId,CancellationToken ct)
    {
        var admin=await LiveAdminAsync(ct);
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
        => await ProjectImpactAsync(await LiveAdminAsync(ct),projectId,ct);

    public async Task DeleteProjectAsync(int projectId,CancellationToken ct)
    {
        var admin=await LiveAdminAsync(ct);
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

    public async Task<V180VisitTypeDeleteImpactDto> VisitTypeImpactAsync(int visitTypeId,CancellationToken ct)
        => await VisitTypeImpactAsync(await LiveAdminAsync(ct),visitTypeId,ct);

    public async Task DeleteVisitTypeAsync(int visitTypeId,CancellationToken ct)
    {
        var admin=await LiveAdminAsync(ct);
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

    public async Task<V180MileageRateDeleteImpactDto> MileageRateImpactAsync(int mileageRateRuleId,CancellationToken ct)
        => await MileageRateImpactAsync(await LiveAdminAsync(ct),mileageRateRuleId,ct);

    public async Task DeleteMileageRateAsync(int mileageRateRuleId,CancellationToken ct)
    {
        var admin=await LiveAdminAsync(ct);
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
