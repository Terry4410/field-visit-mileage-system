import type {TripContext} from "./types";

export function tripLocationAssignment(context:TripContext|undefined):string{
  if(!context)return "尚未取得派駐地點";
  const site=context.eligibleDeploymentSites.find(x=>x.deploymentSiteId===context.defaultStartDeploymentSiteId)
    ||context.eligibleDeploymentSites.find(x=>x.deploymentSiteId===context.primaryDeploymentSiteId);
  return site?`${site.centerName}／${site.siteName}`:"尚未取得派駐地點";
}
