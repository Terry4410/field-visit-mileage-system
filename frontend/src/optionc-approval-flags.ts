// UI gates are presentation only. The server independently enforces the
// feature flag, exact execution target, schema and business authorization.
// Default and all non-Path-B environments remain off.
export function pathBFormalLocationUiEnabled(host:string,apiBase:string|undefined,enabled:unknown):boolean{
 return enabled===true && host.toLowerCase()==="ashy-water-021992f00.2.azurestaticapps.net"
  && apiBase==="https://api-fieldvisit-pathb-uat.azurewebsites.net/api/v1";
}
const formal=typeof window!=="undefined"&&pathBFormalLocationUiEnabled(
 window.location.hostname,window.APP_CONFIG?.API_BASE_URL,window.APP_CONFIG?.FORMAL_LOCATION_UAT);
export const OPTIONC_APPROVAL_UI_ENABLED:boolean=formal;
export const OPTIONC_LEADER_REVIEW_UI_ENABLED:boolean=formal;
export const OPTIONC_CREATE_LOCATION_UI_ENABLED:boolean=formal;
export const OPTIONC_DIRECT_LOWRISK_UI_ENABLED:boolean=formal;
// Excel and merge activation are outside this release.
export const OPTIONC_EXCEL_CREATE_UI_ENABLED:boolean=false;
export const OPTIONC_EXCEL_UPDATE_UI_ENABLED:boolean=false;
