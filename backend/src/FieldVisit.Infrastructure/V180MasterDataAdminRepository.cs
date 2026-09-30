using System.Text.Json;
using FieldVisit.Application;
using FieldVisit.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FieldVisit.Infrastructure;

public sealed class V180MasterDataAdminRepository(AppDbContext db) : IV180MasterDataAdminRepository
{
    public async Task<V180MasterDataWorkspaceDto> GetWorkspaceAsync(CurrentUserDto admin, DateOnly asOf, CancellationToken ct)
    {
        var orgId=RequireOrg(admin);
        var employments=await db.Employments.AsNoTracking().Where(x=>x.OrganizationId==orgId&&x.EmployeeNo!=null).OrderBy(x=>x.EmployeeNo).ToListAsync(ct);
        var empIds=employments.Select(x=>x.EmploymentId).ToList();
        var userIds=employments.Where(x=>x.LegacyUserId.HasValue).Select(x=>x.LegacyUserId!.Value).ToList();
        var names=await db.Users.AsNoTracking().Where(x=>userIds.Contains(x.UserId)).ToDictionaryAsync(x=>x.UserId,x=>x.DisplayName,ct);
        var statuses=await db.EmploymentStatusPeriods.AsNoTracking().Where(x=>empIds.Contains(x.EmploymentId)).ToListAsync(ct);
        var memberships=await db.TeamMemberships.AsNoTracking().Where(x=>empIds.Contains(x.EmploymentId)).ToListAsync(ct);
        var teams=await db.Teams.AsNoTracking().Where(x=>x.OrganizationId==orgId).OrderBy(x=>x.TeamCode).ToListAsync(ct);
        var teamIds=teams.Select(x=>x.TeamId).ToList(); var teamById=teams.ToDictionary(x=>x.TeamId);
        var locations=await db.Locations.AsNoTracking().Where(x=>x.OrganizationId==orgId&&x.LocationCode!=null).OrderBy(x=>x.LocationCode).ToListAsync(ct);
        var centers=await db.Centers.AsNoTracking().Where(x=>x.OrganizationId==orgId).OrderBy(x=>x.CenterCode).ToListAsync(ct);
        var centerIds=centers.Select(x=>x.CenterId).ToList(); var centerById=centers.ToDictionary(x=>x.CenterId);
        var teamCenters=await db.TeamCenterAssignments.AsNoTracking().Where(x=>teamIds.Contains(x.TeamId)&&centerIds.Contains(x.CenterId)).ToListAsync(ct);
        var sites=await db.DeploymentSites.AsNoTracking().Where(x=>centerIds.Contains(x.CenterId)).OrderBy(x=>x.SiteCode).ToListAsync(ct);
        var siteIds=sites.Select(x=>x.DeploymentSiteId).ToList(); var siteById=sites.ToDictionary(x=>x.DeploymentSiteId);
        var siteLocs=await db.DeploymentSiteLocationAssignments.AsNoTracking().Where(x=>siteIds.Contains(x.DeploymentSiteId)).ToListAsync(ct);
        var locationById=locations.ToDictionary(x=>x.LocationId);
        var teamSites=await db.TeamDeploymentSiteAssignments.AsNoTracking().Where(x=>teamIds.Contains(x.TeamId)&&siteIds.Contains(x.DeploymentSiteId)).ToListAsync(ct);
        var empSites=await db.EmploymentDeploymentSiteAssignments.AsNoTracking().Where(x=>empIds.Contains(x.EmploymentId)&&siteIds.Contains(x.DeploymentSiteId)).ToListAsync(ct);

        var currentStatus=statuses.Where(x=>V180MasterDataRules.IsEffective(x.EffectiveFrom,x.EffectiveTo,asOf)).GroupBy(x=>x.EmploymentId).ToDictionary(g=>g.Key,g=>g.OrderByDescending(x=>x.EffectiveFrom).First());
        var currentMemberships=memberships.Where(x=>V180MasterDataRules.IsEffective(x.EffectiveFrom,x.EffectiveTo,asOf)).ToList();
        var primaryMembership=currentMemberships.Where(x=>x.IsPrimary).GroupBy(x=>x.EmploymentId).ToDictionary(g=>g.Key,g=>g.OrderByDescending(x=>x.EffectiveFrom).First());
        var primarySites=empSites.Where(x=>x.IsPrimary&&V180MasterDataRules.IsEffective(x.EffectiveFrom,x.EffectiveTo,asOf)).GroupBy(x=>x.EmploymentId).ToDictionary(g=>g.Key,g=>g.OrderByDescending(x=>x.EffectiveFrom).First());

        var employmentRows=employments.Select(e=>{
            currentStatus.TryGetValue(e.EmploymentId,out var status); primaryMembership.TryGetValue(e.EmploymentId,out var membership); primarySites.TryGetValue(e.EmploymentId,out var ps);
            teamById.TryGetValue(membership?.TeamId??-1,out var team); siteById.TryGetValue(ps?.DeploymentSiteId??-1,out var site);
            var display=e.LegacyUserId.HasValue&&names.TryGetValue(e.LegacyUserId.Value,out var n)?n:e.EmployeeNo!;
            return new V180EmploymentMasterDataDto(e.EmploymentId,e.EmployeeNo!,display,status?.EmploymentStatus,status?.EffectiveFrom,status?.EffectiveTo,team?.TeamCode,team?.TeamName,site?.SiteCode,site?.SiteName);
        }).ToList();

        var tcRows=teamCenters.OrderBy(x=>x.TeamId).ThenBy(x=>x.EffectiveFrom).Select(x=>new V180TeamCenterMasterDataDto(x.TeamCenterAssignmentId,teamById[x.TeamId].TeamCode,teamById[x.TeamId].TeamName,centerById[x.CenterId].CenterCode,centerById[x.CenterId].CenterName,x.EffectiveFrom,x.EffectiveTo,x.ChangeReason)).ToList();
        var siteRows=sites.Select(s=>{
            var locRow=siteLocs.Where(x=>x.DeploymentSiteId==s.DeploymentSiteId&&V180MasterDataRules.IsEffective(x.EffectiveFrom,x.EffectiveTo,asOf)).OrderByDescending(x=>x.EffectiveFrom).FirstOrDefault()
                ?? siteLocs.Where(x=>x.DeploymentSiteId==s.DeploymentSiteId).OrderByDescending(x=>x.EffectiveFrom).FirstOrDefault();
            Location? loc=null; if(locRow!=null) locationById.TryGetValue(locRow.LocationId,out loc);
            return new V180DeploymentSiteMasterDataDto(s.DeploymentSiteId,centerById[s.CenterId].CenterCode,s.SiteCode,s.SiteName,loc?.LocationCode,loc?.LocationName,s.EffectiveFrom,s.EffectiveTo,s.IsActive,s.Notes);
        }).ToList();
        var tsRows=teamSites.OrderBy(x=>x.TeamId).ThenBy(x=>x.EffectiveFrom).Select(x=>new V180TeamSiteMasterDataDto(x.TeamDeploymentSiteAssignmentId,teamById[x.TeamId].TeamCode,siteById[x.DeploymentSiteId].SiteCode,siteById[x.DeploymentSiteId].SiteName,x.EffectiveFrom,x.EffectiveTo)).ToList();
        var empById=employments.ToDictionary(x=>x.EmploymentId);
        var esRows=empSites.OrderBy(x=>x.EmploymentId).ThenBy(x=>x.EffectiveFrom).Select(x=>{
            var e=empById[x.EmploymentId]; var display=e.LegacyUserId.HasValue&&names.TryGetValue(e.LegacyUserId.Value,out var n)?n:e.EmployeeNo!; var site=siteById[x.DeploymentSiteId];
            return new V180EmploymentSiteMasterDataDto(x.EmploymentDeploymentSiteAssignmentId,e.EmployeeNo!,display,site.SiteCode,site.SiteName,x.IsPrimary,x.EffectiveFrom,x.EffectiveTo);
        }).ToList();

        var activeEmp=employments.Where(e=>currentStatus.TryGetValue(e.EmploymentId,out var st)&&st.EmploymentStatus.Equals("Active",StringComparison.OrdinalIgnoreCase)).ToList();
        var missingStatus=employments.Count(e=>!currentStatus.TryGetValue(e.EmploymentId,out var st)||!st.EmploymentStatus.Equals("Active",StringComparison.OrdinalIgnoreCase));
        var activeTeams=teams.Where(t=>t.IsActive&&(!t.EffectiveFrom.HasValue||t.EffectiveFrom.Value<=asOf)&&(!t.EffectiveTo.HasValue||t.EffectiveTo.Value>=asOf)).ToList();
        var currentTC=teamCenters.Where(x=>V180MasterDataRules.IsEffective(x.EffectiveFrom,x.EffectiveTo,asOf)).ToList();
        var missingTC=activeTeams.Count(t=>!currentTC.Any(x=>x.TeamId==t.TeamId));
        var activeSiteCount=sites.Count(s=>s.IsActive&&V180MasterDataRules.IsEffective(s.EffectiveFrom,s.EffectiveTo,asOf));
        var currentTS=teamSites.Where(x=>V180MasterDataRules.IsEffective(x.EffectiveFrom,x.EffectiveTo,asOf)).ToList();
        var activeTeamIds=currentMemberships.Where(m=>activeEmp.Any(e=>e.EmploymentId==m.EmploymentId)).Select(m=>m.TeamId).Distinct().ToList();
        var missingTS=activeTeamIds.Count(id=>!currentTS.Any(x=>x.TeamId==id));
        var missingPS=activeEmp.Count(e=>!primarySites.ContainsKey(e.EmploymentId));
        var rateReady=await db.MileageRateRules.AsNoTracking().AnyAsync(x=>x.OrganizationId==orgId&&x.IsActive&&x.VehicleType=="Motorcycle"&&x.EffectiveFrom<=asOf&&(x.EffectiveTo==null||x.EffectiveTo>=asOf),ct);
        var issues=new List<V180MasterDataReadinessIssueDto>();
        Issue(issues,"EMPLOYMENT_STATUS_GAP","人員缺少目前有效的 Active 在職期間。",missingStatus);
        Issue(issues,"TEAM_CENTER_GAP","啟用小組缺少目前有效的 Center 歸屬。",missingTC);
        Issue(issues,"TEAM_SITE_GAP","目前有人員的小組缺少可用 Deployment Site。",missingTS);
        Issue(issues,"EMPLOYMENT_PRIMARY_SITE_GAP","有效在職人員缺少 Primary Deployment Site。",missingPS);
        if(!rateReady)Issue(issues,"MILEAGE_RATE_GAP","目前沒有有效的 Motorcycle 補助費率。",1);

        return new V180MasterDataWorkspaceDto(
            new V180MasterDataReadinessDto(asOf,issues.Count==0,employments.Count,missingStatus,activeTeams.Count,missingTC,activeSiteCount,missingTS,missingPS,rateReady,issues),
            employmentRows,
            teams.Select(x=>new V180TeamMasterDataDto(x.TeamId,x.TeamCode,x.TeamName,x.IsActive,x.EffectiveFrom,x.EffectiveTo)).ToList(),
            locations.Select(x=>new V180LocationMasterDataDto(x.LocationId,x.LocationCode!,x.LocationName,x.Address??x.PlusCode,x.IsActive,x.ApprovalStatus)).ToList(),
            centers.Select(x=>new V180CenterMasterDataDto(x.CenterId,x.CenterCode,x.CenterName,x.EffectiveFrom,x.EffectiveTo,x.IsActive,x.Notes)).ToList(),
            tcRows,siteRows,tsRows,esRows);
    }

    public async Task<V180MasterDataSaveResultDto> SaveEmploymentStatusAsync(CurrentUserDto admin, SaveV180EmploymentStatusRequest r, CancellationToken ct)
    {
        var org=RequireOrg(admin); var employee=V180MasterDataRules.NormalizeCode(r.EmployeeNo,"EmployeeNo"); var status=V180MasterDataRules.NormalizeEmploymentStatus(r.EmploymentStatus);
        V180MasterDataRules.ValidatePeriod(r.EffectiveFrom,r.EffectiveTo,"Employment Status");
        var e=await Employment(org,employee,ct); var rows=await db.EmploymentStatusPeriods.Where(x=>x.EmploymentId==e.EmploymentId).ToListAsync(ct);
        var current=rows.FirstOrDefault(x=>x.EffectiveFrom==r.EffectiveFrom);
        if(rows.Any(x=>x.EmploymentStatusPeriodId!=current?.EmploymentStatusPeriodId&&V180MasterDataRules.Overlaps(x.EffectiveFrom,x.EffectiveTo,r.EffectiveFrom,r.EffectiveTo))) throw new InvalidOperationException($"{employee} 的 Employment Status effective period 與既有資料重疊。");
        var action="Create";
        if(current==null) db.EmploymentStatusPeriods.Add(new EmploymentStatusPeriod{EmploymentId=e.EmploymentId,EmploymentStatus=status,EffectiveFrom=r.EffectiveFrom,EffectiveTo=r.EffectiveTo,SourceType="BusinessAdmin",SourceReference="UAT self-service",CreatedAt=DateTime.UtcNow,CreatedByUserId=admin.UserId});
        else { action=current.EmploymentStatus==status&&current.EffectiveTo==r.EffectiveTo?"NoChange":"Update"; current.EmploymentStatus=status; current.EffectiveTo=r.EffectiveTo; }
        Audit(admin,"EmploymentStatusPeriod",employee,action,r); await db.SaveChangesAsync(ct); return new("EmploymentStatus",action,employee);
    }

    public async Task<V180MasterDataSaveResultDto> SaveCenterAsync(CurrentUserDto admin, SaveV180CenterRequest r, CancellationToken ct)
    {
        var org=RequireOrg(admin); var code=V180MasterDataRules.NormalizeCode(r.CenterCode,"CenterCode"); var name=V180MasterDataRules.NormalizeName(r.CenterName,"CenterName"); var notes=V180MasterDataRules.NormalizeOptionalText(r.Notes,1000,"Notes");
        V180MasterDataRules.ValidatePeriod(r.EffectiveFrom,r.EffectiveTo,"Center");
        var row=await db.Centers.FirstOrDefaultAsync(x=>x.OrganizationId==org&&x.CenterCode.ToUpper()==code,ct); var action="Create";
        if(row==null){row=new Center{OrganizationId=org,CenterCode=code,CenterName=name,EffectiveFrom=r.EffectiveFrom,EffectiveTo=r.EffectiveTo,IsActive=r.IsActive,Notes=notes,CreatedAt=DateTime.UtcNow,CreatedByUserId=admin.UserId};db.Centers.Add(row);}
        else {if(row.EffectiveFrom!=r.EffectiveFrom)throw new InvalidOperationException($"Center {code} 已存在；EffectiveFrom 必須沿用 {row.EffectiveFrom:yyyy-MM-dd}。"); action=row.CenterName==name&&row.EffectiveTo==r.EffectiveTo&&row.IsActive==r.IsActive&&row.Notes==notes?"NoChange":"Update";row.CenterName=name;row.EffectiveTo=r.EffectiveTo;row.IsActive=r.IsActive;row.Notes=notes;row.UpdatedAt=DateTime.UtcNow;row.UpdatedByUserId=admin.UserId;if(!r.IsActive){row.InactivatedAt??=DateTime.UtcNow;row.InactivatedByUserId??=admin.UserId;}}
        Audit(admin,"Center",code,action,r); await db.SaveChangesAsync(ct); return new("Center",action,code);
    }

    public async Task<V180MasterDataSaveResultDto> SaveTeamCenterAsync(CurrentUserDto admin, SaveV180TeamCenterRequest r, CancellationToken ct)
    {
        var org=RequireOrg(admin); var teamCode=V180MasterDataRules.NormalizeCode(r.TeamCode,"TeamCode"); var centerCode=V180MasterDataRules.NormalizeCode(r.CenterCode,"CenterCode"); var reason=V180MasterDataRules.NormalizeOptionalText(r.ChangeReason,500,"ChangeReason");
        V180MasterDataRules.ValidatePeriod(r.EffectiveFrom,r.EffectiveTo,"Team-Center"); var team=await Team(org,teamCode,ct);var center=await Center(org,centerCode,ct);
        if(team.OrganizationId!=center.OrganizationId)throw new InvalidOperationException("Team 與 Center 必須屬於同一 Organization。");
        if(!V180MasterDataRules.Covers(team.EffectiveFrom??DateOnly.MinValue,team.EffectiveTo,r.EffectiveFrom,r.EffectiveTo)||!V180MasterDataRules.Covers(center.EffectiveFrom,center.EffectiveTo,r.EffectiveFrom,r.EffectiveTo))throw new InvalidOperationException("Team-Center period 必須完整位於 Team 與 Center 有效期間內。");
        var rows=await db.TeamCenterAssignments.Where(x=>x.TeamId==team.TeamId).ToListAsync(ct); var current=rows.FirstOrDefault(x=>x.EffectiveFrom==r.EffectiveFrom);
        if(rows.Any(x=>x.TeamCenterAssignmentId!=current?.TeamCenterAssignmentId&&V180MasterDataRules.Overlaps(x.EffectiveFrom,x.EffectiveTo,r.EffectiveFrom,r.EffectiveTo)))throw new InvalidOperationException($"Team {teamCode} 的 Center effective period 與既有資料重疊。");
        var action="Create"; if(current==null)db.TeamCenterAssignments.Add(new TeamCenterAssignment{TeamId=team.TeamId,CenterId=center.CenterId,EffectiveFrom=r.EffectiveFrom,EffectiveTo=r.EffectiveTo,ChangeReason=reason,CreatedAt=DateTime.UtcNow,CreatedByUserId=admin.UserId});
        else{action=current.CenterId==center.CenterId&&current.EffectiveTo==r.EffectiveTo&&current.ChangeReason==reason?"NoChange":"Update";current.CenterId=center.CenterId;current.EffectiveTo=r.EffectiveTo;current.ChangeReason=reason;}
        Audit(admin,"TeamCenterAssignment",teamCode,action,r);await db.SaveChangesAsync(ct);return new("TeamCenter",action,$"{teamCode}/{centerCode}");
    }

    public async Task<V180MasterDataSaveResultDto> SaveDeploymentSiteAsync(CurrentUserDto admin, SaveV180DeploymentSiteRequest r, CancellationToken ct)
    {
        var org=RequireOrg(admin);var centerCode=V180MasterDataRules.NormalizeCode(r.CenterCode,"CenterCode");var siteCode=V180MasterDataRules.NormalizeCode(r.SiteCode,"SiteCode");var siteName=V180MasterDataRules.NormalizeName(r.SiteName,"SiteName");var locCode=V180MasterDataRules.NormalizeCode(r.LocationCode,"LocationCode");
        var notes=V180MasterDataRules.NormalizeOptionalText(r.Notes,1000,"Notes");var reason=V180MasterDataRules.NormalizeOptionalText(r.ChangeReason,500,"ChangeReason");V180MasterDataRules.ValidatePeriod(r.EffectiveFrom,r.EffectiveTo,"Deployment Site");
        var center=await Center(org,centerCode,ct);if(!center.IsActive||!V180MasterDataRules.Covers(center.EffectiveFrom,center.EffectiveTo,r.EffectiveFrom,r.EffectiveTo))throw new InvalidOperationException($"Deployment Site {siteCode} 必須完整位於啟用 Center {centerCode} 有效期間內。");
        var loc=await Location(org,locCode,ct);if(!loc.IsActive||!loc.ApprovalStatus.Equals("Approved",StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException($"Location {locCode} 必須 Approved 且啟用。");
        var duplicate=await (from s in db.DeploymentSites join c in db.Centers on s.CenterId equals c.CenterId where c.OrganizationId==org&&s.SiteCode.ToUpper()==siteCode select s).ToListAsync(ct);
        if(duplicate.Count>1)throw new InvalidOperationException($"SiteCode={siteCode} 在多個 Center 重複；請由 IT Review。");
        var site=duplicate.SingleOrDefault();var action="Create";var isNew=site==null;
        if(site==null){site=new DeploymentSite{CenterId=center.CenterId,SiteCode=siteCode,SiteName=siteName,EffectiveFrom=r.EffectiveFrom,EffectiveTo=r.EffectiveTo,IsActive=r.IsActive,Notes=notes,CreatedAt=DateTime.UtcNow,CreatedByUserId=admin.UserId};db.DeploymentSites.Add(site);}
        else{if(site.CenterId!=center.CenterId)throw new InvalidOperationException($"SiteCode={siteCode} 已存在於其他 Center。");if(site.EffectiveFrom!=r.EffectiveFrom)throw new InvalidOperationException($"Deployment Site {siteCode} 的 EffectiveFrom 必須沿用 {site.EffectiveFrom:yyyy-MM-dd}。");action=site.SiteName==siteName&&site.EffectiveTo==r.EffectiveTo&&site.IsActive==r.IsActive&&site.Notes==notes?"NoChange":"Update";site.SiteName=siteName;site.EffectiveTo=r.EffectiveTo;site.IsActive=r.IsActive;site.Notes=notes;site.UpdatedAt=DateTime.UtcNow;site.UpdatedByUserId=admin.UserId;if(!r.IsActive){site.InactivatedAt??=DateTime.UtcNow;site.InactivatedByUserId??=admin.UserId;}}
        var locRows=isNew?new List<DeploymentSiteLocationAssignment>():await db.DeploymentSiteLocationAssignments.Where(x=>x.DeploymentSiteId==site!.DeploymentSiteId).ToListAsync(ct);var current=locRows.FirstOrDefault(x=>x.EffectiveFrom==r.EffectiveFrom);
        if(locRows.Any(x=>x.DeploymentSiteLocationAssignmentId!=current?.DeploymentSiteLocationAssignmentId&&V180MasterDataRules.Overlaps(x.EffectiveFrom,x.EffectiveTo,r.EffectiveFrom,r.EffectiveTo)))throw new InvalidOperationException($"Deployment Site {siteCode} 的 Location period 與既有資料重疊。");
        if(current==null){var a=new DeploymentSiteLocationAssignment{LocationId=loc.LocationId,EffectiveFrom=r.EffectiveFrom,EffectiveTo=r.EffectiveTo,ChangeReason=reason,CreatedAt=DateTime.UtcNow,CreatedByUserId=admin.UserId};if(isNew)a.DeploymentSite=site!;else a.DeploymentSiteId=site!.DeploymentSiteId;db.DeploymentSiteLocationAssignments.Add(a);}
        else{if(current.LocationId!=loc.LocationId||current.EffectiveTo!=r.EffectiveTo||current.ChangeReason!=reason)action="Update";current.LocationId=loc.LocationId;current.EffectiveTo=r.EffectiveTo;current.ChangeReason=reason;}
        Audit(admin,"DeploymentSite",siteCode,action,r);await db.SaveChangesAsync(ct);return new("DeploymentSite",action,$"{centerCode}/{siteCode}");
    }

    public async Task<V180MasterDataSaveResultDto> SaveTeamSiteAsync(CurrentUserDto admin, SaveV180TeamSiteRequest r, CancellationToken ct)
    {
        var org=RequireOrg(admin);var teamCode=V180MasterDataRules.NormalizeCode(r.TeamCode,"TeamCode");var siteCode=V180MasterDataRules.NormalizeCode(r.SiteCode,"SiteCode");V180MasterDataRules.ValidatePeriod(r.EffectiveFrom,r.EffectiveTo,"Team-Site");
        var team=await Team(org,teamCode,ct);var site=await Site(org,siteCode,ct);if(!site.IsActive||!V180MasterDataRules.Covers(site.EffectiveFrom,site.EffectiveTo,r.EffectiveFrom,r.EffectiveTo))throw new InvalidOperationException($"Site {siteCode} 必須啟用且完整涵蓋 Team-Site period。");
        var tc=await db.TeamCenterAssignments.AsNoTracking().Where(x=>x.TeamId==team.TeamId&&x.CenterId==site.CenterId).ToListAsync(ct);if(!tc.Any(x=>V180MasterDataRules.Covers(x.EffectiveFrom,x.EffectiveTo,r.EffectiveFrom,r.EffectiveTo)))throw new InvalidOperationException($"Team {teamCode} 在整段期間沒有被指派至 Site {siteCode} 所屬 Center。");
        var rows=await db.TeamDeploymentSiteAssignments.Where(x=>x.TeamId==team.TeamId&&x.DeploymentSiteId==site.DeploymentSiteId).ToListAsync(ct);var current=rows.FirstOrDefault(x=>x.EffectiveFrom==r.EffectiveFrom);
        if(rows.Any(x=>x.TeamDeploymentSiteAssignmentId!=current?.TeamDeploymentSiteAssignmentId&&V180MasterDataRules.Overlaps(x.EffectiveFrom,x.EffectiveTo,r.EffectiveFrom,r.EffectiveTo)))throw new InvalidOperationException($"{teamCode}/{siteCode} period 與既有資料重疊。");
        var action="Create";if(current==null)db.TeamDeploymentSiteAssignments.Add(new TeamDeploymentSiteAssignment{TeamId=team.TeamId,DeploymentSiteId=site.DeploymentSiteId,EffectiveFrom=r.EffectiveFrom,EffectiveTo=r.EffectiveTo,CreatedAt=DateTime.UtcNow,CreatedByUserId=admin.UserId});else{action=current.EffectiveTo==r.EffectiveTo?"NoChange":"Update";current.EffectiveTo=r.EffectiveTo;}
        Audit(admin,"TeamDeploymentSiteAssignment",$"{teamCode}/{siteCode}",action,r);await db.SaveChangesAsync(ct);return new("TeamSite",action,$"{teamCode}/{siteCode}");
    }

    public async Task<V180MasterDataSaveResultDto> SaveEmploymentSiteAsync(CurrentUserDto admin, SaveV180EmploymentSiteRequest r, CancellationToken ct)
    {
        var org=RequireOrg(admin);var employee=V180MasterDataRules.NormalizeCode(r.EmployeeNo,"EmployeeNo");var siteCode=V180MasterDataRules.NormalizeCode(r.SiteCode,"SiteCode");V180MasterDataRules.ValidatePeriod(r.EffectiveFrom,r.EffectiveTo,"Employment-Site");
        var e=await Employment(org,employee,ct);var site=await Site(org,siteCode,ct);if(!site.IsActive||!V180MasterDataRules.Covers(site.EffectiveFrom,site.EffectiveTo,r.EffectiveFrom,r.EffectiveTo))throw new InvalidOperationException($"Site {siteCode} 必須啟用且完整涵蓋 Employment-Site period。");
        var status=await db.EmploymentStatusPeriods.AsNoTracking().Where(x=>x.EmploymentId==e.EmploymentId&&x.EmploymentStatus=="Active").ToListAsync(ct);if(!status.Any(x=>V180MasterDataRules.Covers(x.EffectiveFrom,x.EffectiveTo,r.EffectiveFrom,r.EffectiveTo)))throw new InvalidOperationException($"{employee} 在整段期間沒有 Active Employment Status。");
        var memberships=await db.TeamMemberships.AsNoTracking().Where(x=>x.EmploymentId==e.EmploymentId).ToListAsync(ct);var covering=memberships.Where(x=>V180MasterDataRules.Covers(x.EffectiveFrom,x.EffectiveTo,r.EffectiveFrom,r.EffectiveTo)).ToList();if(covering.Count==0)throw new InvalidOperationException($"{employee} 在整段期間沒有有效 Team membership。");
        var teamIds=covering.Select(x=>x.TeamId).Distinct().ToList();var teamSite=await db.TeamDeploymentSiteAssignments.AsNoTracking().Where(x=>teamIds.Contains(x.TeamId)&&x.DeploymentSiteId==site.DeploymentSiteId).ToListAsync(ct);if(!teamSite.Any(x=>V180MasterDataRules.Covers(x.EffectiveFrom,x.EffectiveTo,r.EffectiveFrom,r.EffectiveTo)))throw new InvalidOperationException($"{employee} 的 Team 在整段期間沒有使用 Site {siteCode} 的資格。");
        var rows=await db.EmploymentDeploymentSiteAssignments.Where(x=>x.EmploymentId==e.EmploymentId).ToListAsync(ct);var current=rows.FirstOrDefault(x=>x.DeploymentSiteId==site.DeploymentSiteId&&x.EffectiveFrom==r.EffectiveFrom);
        if(rows.Any(x=>x.EmploymentDeploymentSiteAssignmentId!=current?.EmploymentDeploymentSiteAssignmentId&&x.DeploymentSiteId==site.DeploymentSiteId&&V180MasterDataRules.Overlaps(x.EffectiveFrom,x.EffectiveTo,r.EffectiveFrom,r.EffectiveTo)))throw new InvalidOperationException($"{employee}/{siteCode} period 與既有資料重疊。");
        if(r.IsPrimary&&rows.Any(x=>x.EmploymentDeploymentSiteAssignmentId!=current?.EmploymentDeploymentSiteAssignmentId&&x.IsPrimary&&V180MasterDataRules.Overlaps(x.EffectiveFrom,x.EffectiveTo,r.EffectiveFrom,r.EffectiveTo)))throw new InvalidOperationException($"{employee} 同一天只能有一個 Primary Deployment Site。");
        var action="Create";if(current==null)db.EmploymentDeploymentSiteAssignments.Add(new EmploymentDeploymentSiteAssignment{EmploymentId=e.EmploymentId,DeploymentSiteId=site.DeploymentSiteId,IsPrimary=r.IsPrimary,EffectiveFrom=r.EffectiveFrom,EffectiveTo=r.EffectiveTo,CreatedAt=DateTime.UtcNow,CreatedByUserId=admin.UserId});else{action=current.IsPrimary==r.IsPrimary&&current.EffectiveTo==r.EffectiveTo?"NoChange":"Update";current.IsPrimary=r.IsPrimary;current.EffectiveTo=r.EffectiveTo;}
        Audit(admin,"EmploymentDeploymentSiteAssignment",$"{employee}/{siteCode}",action,r);await db.SaveChangesAsync(ct);return new("EmploymentSite",action,$"{employee}/{siteCode}");
    }

    private async Task<Employment> Employment(int org,string code,CancellationToken ct)=>await db.Employments.FirstOrDefaultAsync(x=>x.OrganizationId==org&&x.EmployeeNo!=null&&x.EmployeeNo.ToUpper()==code,ct)??throw new KeyNotFoundException($"找不到 EmployeeNo={code}。");
    private async Task<Team> Team(int org,string code,CancellationToken ct)=>await db.Teams.FirstOrDefaultAsync(x=>x.OrganizationId==org&&x.TeamCode.ToUpper()==code,ct)??throw new KeyNotFoundException($"找不到 TeamCode={code}。");
    private async Task<Center> Center(int org,string code,CancellationToken ct)=>await db.Centers.FirstOrDefaultAsync(x=>x.OrganizationId==org&&x.CenterCode.ToUpper()==code,ct)??throw new KeyNotFoundException($"找不到 CenterCode={code}。");
    private async Task<Location> Location(int org,string code,CancellationToken ct)=>await db.Locations.FirstOrDefaultAsync(x=>x.OrganizationId==org&&x.LocationCode!=null&&x.LocationCode.ToUpper()==code,ct)??throw new KeyNotFoundException($"找不到 LocationCode={code}。");
    private async Task<DeploymentSite> Site(int org,string code,CancellationToken ct){var rows=await(from s in db.DeploymentSites join c in db.Centers on s.CenterId equals c.CenterId where c.OrganizationId==org&&s.SiteCode.ToUpper()==code select s).ToListAsync(ct);return rows.Count switch{1=>rows[0],0=>throw new KeyNotFoundException($"找不到 SiteCode={code}。"),_=>throw new InvalidOperationException($"SiteCode={code} 在多個 Center 重複；請由 IT Review。")};}
    private static int RequireOrg(CurrentUserDto u)=>u.OrganizationId??throw new InvalidOperationException("目前 Business Admin 缺少 OrganizationId。");
    private void Audit(CurrentUserDto u,string type,string id,string action,object value)=>db.AuditLogs.Add(new AuditLog{UserId=u.UserId,EntityType=type,EntityId=id,Action=$"MasterData{action}",NewValues=JsonSerializer.Serialize(value),CreatedAt=DateTime.UtcNow});
    private static void Issue(ICollection<V180MasterDataReadinessIssueDto> list,string code,string msg,int count){if(count>0)list.Add(new(code,msg,count));}
}
