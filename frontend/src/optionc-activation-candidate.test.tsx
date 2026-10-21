import React from "react";
import { renderToStaticMarkup } from "react-dom/server";
import { MemoryRouter } from "react-router-dom";
import { describe, it, expect, vi } from "vitest";
import type { ManagedLocation, ImportPreview } from "./types";

const auth = vi.hoisted(() => ({
 user: {userId: 7, roles: ["visitor"], teamScopes: [{teamId: 4, teamName: "Synthetic", isPrimary: true}]}
}));
vi.mock("./auth", () => ({useAuth: () => ({user: auth.user})}));
vi.stubEnv("MODE", "optionc-activation-candidate");

vi.stubGlobal("sessionStorage",{getItem:()=>null,setItem:()=>{}});
const flags = await import("./optionc-approval-flags");
const {default: App} = await import("./App");
const {default: AdminReview} = await import("./pages/LocationApprovalQueuePage");
const {default: LeaderReview} = await import("./pages/LeaderLocationReviewPage");
const {default: Scoped, LeaderLowRiskAction} = await import("./pages/ScopedLocationPage");
const {OptionCExcelActions} = await import("./components/AdminImportPanel");
const {mayReview, mayLeaderReview} = await import("./optionc-approval-ui");
const html = (element: React.ReactElement) => renderToStaticMarkup(element);

const location: ManagedLocation = {
 locationId: 1,locationCode:"L1",locationName:"Synthetic",locationType:"Customer",
 isTemporary:false,approvalStatus:"Approved",geocodingStatus:"Pending",isActive:true,
 createdAt:"",rowVersion:"r",teamId:4
};
const preview = (action:"Create"|"Update"):ImportPreview => ({
 importBatchId:"synthetic",importType:"locations",totalCount:1,validCount:0,errorCount:1,
 items:[{rowNumber:2,entityType:"Location",action,status:"Error",displayKey:"synthetic",
 errorMessage:action==="Create"?"LOCATION_EXCEL_CREATE_REQUIRES_CANONICAL_REVIEW":"LOCATION_EXCEL_PUBLISHED_CHANGE_REQUIRES_UNIFIED_APPROVAL"}]
});
const actions=(p:ImportPreview,type:"locations"|"projects"="locations") => html(
 React.createElement(OptionCExcelActions,{type,preview:p,busy:false,onCreate:()=>{},onUpdate:()=>{}})
);

describe("explicit nondeploy Option C activation candidate",()=>{
 it("enables all six flags only in the explicit candidate mode",()=>{
  expect(flags.OPTIONC_ACTIVATION_CANDIDATE).toBe(true);
  expect([flags.OPTIONC_APPROVAL_UI_ENABLED,flags.OPTIONC_LEADER_REVIEW_UI_ENABLED,
   flags.OPTIONC_CREATE_LOCATION_UI_ENABLED,flags.OPTIONC_DIRECT_LOWRISK_UI_ENABLED,
   flags.OPTIONC_EXCEL_CREATE_UI_ENABLED,flags.OPTIONC_EXCEL_UPDATE_UI_ENABLED]).toEqual([true,true,true,true,true,true]);
 });
 it("exposes the intended navigation and route only for each selected role",()=>{
  const screen=(role:string,path:string)=>{auth.user.roles=[role];return html(React.createElement(MemoryRouter,{initialEntries:[path]},React.createElement(App)));};
  expect(screen("admin","/admin/locations/approvals")).toContain("地點變更獨立 Admin 複審");
  expect(screen("leader","/leader/location-review")).toContain("地點變更獨立初審");
  expect(screen("visitor","/locations/manage")).toContain("我的地點申請");
  expect(screen("visitor","/")).not.toContain("地點變更核准");
 });
 it("shows Admin review only to Admin",()=>{
  auth.user.roles=["admin"]; expect(html(React.createElement(AdminReview))).toContain("地點變更獨立 Admin 複審");
  auth.user.roles=["visitor"]; expect(html(React.createElement(AdminReview))).toContain("沒有管理員權限");
 });
 it("shows Leader review only to Leader",()=>{
  auth.user.roles=["leader"]; expect(html(React.createElement(LeaderReview))).toContain("地點變更獨立初審");
  auth.user.roles=["visitor"]; expect(html(React.createElement(LeaderReview))).toContain("沒有有效的小組長權限");
 });
 it("shows Create request entry for Visitor and Leader",()=>{
  for(const role of ["visitor","leader"] as const){
   auth.user.roles=[role]; expect(html(React.createElement(Scoped,{role}))).toContain("我的地點申請");
  }
 });
 it("shows LowRisk only for an eligible managed-team published location",()=>{
  const render=(teamIds:number[],row:ManagedLocation=location)=>html(React.createElement(LeaderLowRiskAction,{row,teamIds,busy:false,onClick:()=>{}}));
  expect(render([4])).toContain("LowRisk 主檔備註");
  expect(render([5])).toBe("");
  expect(render([4],{...location,isActive:false})).toBe("");
 });
 it("exposes Excel Create and Update only for their valid canonical previews",()=>{
  expect(actions(preview("Create"))).toContain("提交 Excel 新增地點");
  expect(actions(preview("Create"))).not.toContain("提交 Excel 已發布");
  expect(actions(preview("Update"))).toContain("提交 Excel 已發布地點更新");
  expect(actions(preview("Update"))).not.toContain("提交 Excel 新增");
  expect(actions({...preview("Create"),totalCount:2})).toBe("");
  expect(actions(preview("Create"),"projects")).toBe("");
 });
 it("continues to deny self review in both review paths",()=>{
  expect(mayReview({requestPublicId:"synthetic",entityId:"1",teamId:4,riskCode:"High",
   status:"Pending",requestedByUserId:7,submittedAt:"",beforeJson:"{}",proposedJson:"{}",rowVersion:"x"},7)).toBe(false);
  expect(mayLeaderReview({requestedByUserId:7,requestStatus:"Pending",leaderStatus:"Pending",rowVersion:"x"},7)).toBe(false);
 });
});
