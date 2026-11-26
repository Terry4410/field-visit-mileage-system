import {describe,expect,it} from "vitest";
import {createElement} from "react";
import {renderToStaticMarkup} from "react-dom/server";
import {PersonDetailView,PersonDetailButton,resolvePersonSummary} from "./components/PersonDetail";
import type {V170PeopleRow,V170PersonDetail} from "./types";
const row:V170PeopleRow={userId:7,userCode:"pilotv01",userType:"Internal",employeeNo:"E07",displayName:"測試員工",email:"uat@example.test",employmentStatus:"Active",adminEnabled:true,actualAccess:true,roles:["visitor"],teamAssignments:[],primaryDeploymentSiteId:9,primaryCenterName:"測試中心",primaryDeploymentSiteName:"測試鄉鎮台"};
const detail:V170PersonDetail={userId:7,userCode:"pilotv01",userType:"Internal",identityProvider:"Demo",employeeNo:"E07",displayName:"測試員工",email:"uat@example.test",adminEnabled:true,actualAccess:false,employmentStatus:"Leave",hireDate:"2020-01-01",employmentPeriods:[{userEmploymentPeriodId:1,employmentStatus:"Leave",effectiveFrom:"2026-11-01",sourceType:"UAT",isCurrent:true}],roleAssignments:[{roleCode:"visitor",isCurrent:true},{roleCode:"admin",isCurrent:false}],teamAssignments:[{teamId:1,teamCode:"T1",teamName:"主要測試小組",isPrimary:true,isCurrent:true},{teamId:2,teamCode:"T2",teamName:"其他測試小組",isPrimary:false,isCurrent:true}],dataScopes:[],capabilities:[]};
describe("shared read-only personnel detail",()=>{
 it("shows current authorized detail with list site and history but no editing controls",()=>{
  const html=renderToStaticMarkup(createElement(PersonDetailView,{row,detail,onClose:()=>{}}));
  for(const value of ["E07","測試員工","uat@example.test","留停","外訪員","主要測試小組","其他測試小組","測試中心","測試鄉鎮台","2026-11-01","暫不可登入"])expect(html).toContain(value);
  expect(html).not.toContain(">儲存");expect(html).not.toContain(">管理者</td>");
 });
 it("fails closed for mismatched person",()=>expect(()=>resolvePersonSummary(row,{...detail,userId:8})).toThrow("不一致"));
 it.each(["code","name"] as const)("provides %s detail entry",field=>{
  const html=renderToStaticMarkup(createElement(PersonDetailButton,{row,field,onOpen:()=>{}}));
  expect(html).toContain("查看人員明細");expect(html).toContain(field==="code"?">E07</button>":">測試員工</button>");
 });
});
