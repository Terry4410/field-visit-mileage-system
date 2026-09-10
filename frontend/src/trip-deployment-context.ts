import type{V180TripContext}from"./types";

export const V180_TRIP_CONTEXT_API="/trips/context";

export function reconcileDeploymentSite(
 current:number|undefined,
 eligibleIds:number[],
 deterministicDefault:number|undefined
){
 if(current&&eligibleIds.includes(current))return current;
 return deterministicDefault;
}

export function deploymentSiteWarning(context:V180TripContext){
 if(context.validationCode==="NO_ELIGIBLE_DEPLOYMENT_SITE")return "此日期沒有可用的派駐點；草稿仍可儲存，但送出前必須完成派駐點設定。";
 if(context.eligibleDeploymentSites.length>0&&!context.primaryDeploymentSiteId)return "此日期沒有有效的主要派駐點，請自行選擇出發與返回派駐點。";
 return context.eligibleForTrip?"":context.validationMessage;
}

export function canSubmitV180Trip(context:V180TripContext|undefined,start?:number,end?:number){
 return !!context?.eligibleForTrip&&!!start&&!!end;
}
