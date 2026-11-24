// UI gates are presentation only. The server independently enforces the
// feature flag, exact execution target, schema and business authorization.
// Default and all non-Path-B environments remain off.
export function pathBFormalLocationUiEnabled(host:string,apiBase:string|undefined,enabled:unknown):boolean{
 return enabled===true && host.toLowerCase()==="ashy-water-021992f00.2.azurestaticapps.net"
  && apiBase==="https://api-fieldvisit-pathb-uat.azurewebsites.net/api/v1";
}
// Company UI admission is presentation only; the server enforces all security.
export function corporateFormalLocationUiEnabled(frontendOrigin:string, apiBase:string|undefined,
 enabled:unknown, authMode:string|undefined, approvedOrigin:string|undefined, approvedApi:string|undefined):boolean{
 if(enabled!==true||authMode!=="Entra"||!approvedOrigin||!approvedApi||apiBase!==approvedApi||frontendOrigin!==approvedOrigin)return false;
 try{
  const f=new URL(frontendOrigin),a=new URL(approvedApi);
  const valid=(u:URL)=>u.protocol==="https:"&&!u.username&&!u.password&&!u.search&&!u.hash
   &&u.hostname.includes(".")&&!u.hostname.endsWith(".azurestaticapps.net")&&!u.hostname.endsWith(".azurewebsites.net")
   &&u.hostname!=="127.0.0.1"&&!/^\d+\.\d+\.\d+\.\d+$/.test(u.hostname);
  return valid(f)&&valid(a)&&f.origin===frontendOrigin&&a.pathname==="/api/v1"&&a.href===approvedApi;
 }catch{return false;}
}
const pathB=typeof window!=="undefined"&&pathBFormalLocationUiEnabled(
 window.location.hostname,window.APP_CONFIG?.API_BASE_URL,window.APP_CONFIG?.FORMAL_LOCATION_UAT);
const corporate=typeof window!=="undefined"&&corporateFormalLocationUiEnabled(window.location.origin,
 window.APP_CONFIG?.API_BASE_URL,window.APP_CONFIG?.FORMAL_LOCATION_CORPORATE,window.APP_CONFIG?.AUTH_MODE,
 window.APP_CONFIG?.CORPORATE_FRONTEND_ORIGIN,window.APP_CONFIG?.CORPORATE_API_BASE);
const formal=pathB||corporate;
export const OPTIONC_APPROVAL_UI_ENABLED:boolean=formal;
export const OPTIONC_LEADER_REVIEW_UI_ENABLED:boolean=formal;
export const OPTIONC_CREATE_LOCATION_UI_ENABLED:boolean=formal;
export const OPTIONC_DIRECT_LOWRISK_UI_ENABLED:boolean=formal;
// Excel and merge activation are outside this release.
export const OPTIONC_EXCEL_CREATE_UI_ENABLED:boolean=false;
export const OPTIONC_EXCEL_UPDATE_UI_ENABLED:boolean=false;
