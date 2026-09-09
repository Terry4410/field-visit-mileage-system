export interface V180DeploymentSiteLocationSummary{
  locationId:number;
  code?:string|null;
  name:string;
  address?:string|null;
}

export interface V180DeploymentSite{
  deploymentSiteId:number;
  centerId:number;
  centerCode:string;
  centerName:string;
  code:string;
  name:string;
  effectiveFrom:string;
  effectiveTo?:string|null;
  isActive:boolean;
  notes?:string|null;
  currentLocation?:V180DeploymentSiteLocationSummary|null;
  teamAssignmentCount:number;
  employmentAssignmentCount:number;
  version:string;
}

export interface V180DeploymentSiteLocationAssignment{
  deploymentSiteLocationAssignmentId:number;
  deploymentSiteId:number;
  locationId:number;
  locationCode?:string|null;
  locationName:string;
  address?:string|null;
  effectiveFrom:string;
  effectiveTo?:string|null;
  changeReason?:string|null;
  version:string;
}

export interface V180TeamDeploymentSiteAssignment{
  teamDeploymentSiteAssignmentId:number;
  teamId:number;
  teamCode:string;
  teamName:string;
  deploymentSiteId:number;
  siteCode:string;
  siteName:string;
  effectiveFrom:string;
  effectiveTo?:string|null;
  version:string;
}

export interface V180EmploymentDeploymentSiteAssignment{
  employmentDeploymentSiteAssignmentId:number;
  employmentId:number;
  employeeNo?:string|null;
  displayName:string;
  deploymentSiteId:number;
  siteCode:string;
  siteName:string;
  isPrimary:boolean;
  effectiveFrom:string;
  effectiveTo?:string|null;
  version:string;
}

export const V180_DEPLOYMENT_SITE_API={
  sites:"/admin/v180/deployment-sites",
  site:(id:number)=>`/admin/v180/deployment-sites/${id}`,
  deactivateSite:(id:number)=>`/admin/v180/deployment-sites/${id}/deactivate`,
  locationHistory:(siteId:number)=>`/admin/v180/deployment-sites/${siteId}/location-assignments?includeHistory=true`,
  locationAssignments:"/admin/v180/deployment-site-location-assignments",
  locationAssignment:(id:number)=>`/admin/v180/deployment-site-location-assignments/${id}`,
  endLocationAssignment:(id:number)=>`/admin/v180/deployment-site-location-assignments/${id}/end`,
  teamHistory:(siteId:number)=>`/admin/v180/deployment-sites/${siteId}/team-assignments?includeHistory=true`,
  teamAssignments:"/admin/v180/team-deployment-site-assignments",
  teamAssignment:(id:number)=>`/admin/v180/team-deployment-site-assignments/${id}`,
  endTeamAssignment:(id:number)=>`/admin/v180/team-deployment-site-assignments/${id}/end`,
  employmentHistory:(siteId:number)=>`/admin/v180/deployment-sites/${siteId}/employment-assignments?includeHistory=true`,
  employmentAssignments:"/admin/v180/employment-deployment-site-assignments",
  employmentAssignment:(id:number)=>`/admin/v180/employment-deployment-site-assignments/${id}`,
  endEmploymentAssignment:(id:number)=>`/admin/v180/employment-deployment-site-assignments/${id}/end`
}as const;

export function deploymentSiteLifecycleStatus(from:string,to:string|null|undefined,asOf:string){
  if(from>asOf)return"未來";
  if(to&&to<asOf)return"已結束";
  return"生效中";
}

export function deploymentSiteEndDate(from:string,today:string){
  return from>today?from:today;
}

export function deploymentSiteErrorMessage(error:unknown,fallback="操作失敗"){
  const message=error instanceof Error?error.message:String(error||fallback);
  if(message.includes("ROWVERSION_CONFLICT"))return"資料已被其他人更新，請重新整理後再試。";
  if(message.includes("DEPLOYMENT_SITE_DEACTIVATION_BLOCKED"))return"派駐點仍有生效或未來的地點、小組或人員關聯，請先結束相關設定。";
  if(message.includes("TEAM_SITE_CENTER_CONFLICT"))return"小組與派駐點必須由同一中心的有效 Team-Center 關聯完整涵蓋。";
  if(message.includes("DEPLOYMENT_LOCATION_ORGANIZATION_MISMATCH"))return"選擇的地點不屬於此派駐點的組織。";
  if(message.includes("EMPLOYMENT_SITE_PRIMARY_OVERLAP"))return"同一人員在重疊期間只能有一個主要派駐點。";
  if(message.includes("OVERLAP")||message.includes("重疊"))return`有效期間重疊：${message}`;
  if(message.includes("PERIOD_CONFLICT")||message.includes("LIFECYCLE_CONFLICT"))return`生命週期不相容：${message}`;
  return message||fallback;
}
