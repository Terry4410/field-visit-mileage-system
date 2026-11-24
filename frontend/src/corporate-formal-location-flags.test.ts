import {describe,it,expect} from "vitest";
import {corporateFormalLocationUiEnabled,pathBFormalLocationUiEnabled,OPTIONC_EXCEL_CREATE_UI_ENABLED,OPTIONC_EXCEL_UPDATE_UI_ENABLED} from "./optionc-approval-flags";
const f="https://visit.corp.example",a="https://visit-api.corp.example/api/v1";
describe("corporate location presentation boundary",()=>{
 it("allows only exact approved Entra HTTPS pair",()=>expect(corporateFormalLocationUiEnabled(f,a,true,"Entra",f,a)).toBe(true));
 it.each([
 [f,a,false,"Entra",f,a],[f,a,true,"Demo",f,a],[f,a,true,"Entra",undefined,a],
 ["https://wrong.corp.example",a,true,"Entra",f,a],[f,"https://wrong.corp.example/api/v1",true,"Entra",f,a],
 ["http://visit.corp.example",a,true,"Entra","http://visit.corp.example",a],
 [f,"https://api-fieldvisit-pathb-uat.azurewebsites.net/api/v1",true,"Entra",f,"https://api-fieldvisit-pathb-uat.azurewebsites.net/api/v1"],
 [f,a+"?x=1",true,"Entra",f,a+"?x=1"]
 ])("denies mismatched or unsafe settings %#",(x,y,z,m,o,p)=>expect(corporateFormalLocationUiEnabled(x as string,y as string,z,m as string,o as string|undefined,p as string)).toBe(false));
 it("keeps exact UAT boundary and Excel disabled",()=>{
 expect(pathBFormalLocationUiEnabled("ashy-water-021992f00.2.azurestaticapps.net","https://api-fieldvisit-pathb-uat.azurewebsites.net/api/v1",true)).toBe(true);
 expect(pathBFormalLocationUiEnabled("visit.corp.example",a,true)).toBe(false);
 expect(OPTIONC_EXCEL_CREATE_UI_ENABLED||OPTIONC_EXCEL_UPDATE_UI_ENABLED).toBe(false);
 });
});
