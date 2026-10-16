import {describe,expect,it} from "vitest";
import {leaderScopedTeamOptions,mayEditVisitorDraft,mayEditLeaderManagedDraft,mayEditScopedDraft} from "./location-ownership-ui";
import {renderToStaticMarkup} from "react-dom/server";
import {createElement} from "react";
import {ScopedLocationFields} from "./pages/ScopedLocationPage";
import type {ManagedLocation} from "./types";

const row=(x:Partial<ManagedLocation>={}):ManagedLocation=>({
 locationId:1,locationCode:"L1",locationName:"客戶",locationType:"Customer",
 isTemporary:false,approvalStatus:"Pending",geocodingStatus:"Pending",
 isActive:false,createdAt:"",rowVersion:"r",createdByUserId:7,teamId:12,...x
});

describe("UAT010 leader/visitor single-location UI invariants",()=>{
 it("derives leader options only from authenticated team scopes",()=>{
  expect(leaderScopedTeamOptions({teamScopes:[{teamId:12,teamName:"甲組",isPrimary:true}]}))
   .toEqual([{teamId:12,teamName:"甲組",isPrimary:true}]);
  expect(leaderScopedTeamOptions({teamScopes:[]})).toEqual([]);
  expect(leaderScopedTeamOptions(null)).toEqual([]);
 });
 it("rejects invalid or non-positive team IDs in client options",()=>{
  expect(leaderScopedTeamOptions({teamScopes:[
   {teamId:12,teamName:"甲組",isPrimary:true},
   {teamId:0,teamName:"未知",isPrimary:false},
   {teamId:-1,teamName:"無效",isPrimary:false}
  ]}).map(x=>x.teamId)).toEqual([12]);
 });
 it("keeps Visitor edits owner-only while allowing Leaders to edit drafts in their managed teams",()=>{
  const user={userId:7,teamScopes:[{teamId:12,teamName:"甲組",isPrimary:true}]};
  expect(mayEditVisitorDraft(row(),7)).toBe(true);
  expect(mayEditScopedDraft(row({createdByUserId:8}),user,"visitor")).toBe(false);
  expect(mayEditScopedDraft(row({createdByUserId:8}),user,"leader")).toBe(true);
  expect(mayEditLeaderManagedDraft(row({teamId:13}),[12])).toBe(false);
  expect(mayEditScopedDraft(row({approvalStatus:"Approved"}),user,"leader")).toBe(false);
  expect(mayEditScopedDraft(row({isActive:true}),user,"leader")).toBe(false);
  expect(mayEditScopedDraft(row({locationType:"Official"}),user,"leader")).toBe(false);
 });
 it("renders identical single-location fields for Visitor and Leader",()=>{
  const form={teamId:"12",locationName:"",city:"",district:"",address:"",plusCode:"",taxId:"",masterNote:""};
  const teams=[{teamId:12,teamName:"甲組",isPrimary:true}],noop=()=>{};
  const visitor=renderToStaticMarkup(createElement(ScopedLocationFields,{role:"visitor",form,teams,busy:false,editing:false,onChange:noop}));
  const leader=renderToStaticMarkup(createElement(ScopedLocationFields,{role:"leader",form,teams,busy:false,editing:false,onChange:noop}));
  const labels=(html:string)=>[...html.matchAll(/<label>(.*?)<\/label>/g)].map(x=>x[1]);
  expect(labels(leader)).toEqual(labels(visitor));
  expect(labels(leader)).toEqual(["所屬小組","地點名稱","縣市","鄉鎮區","地址（或 Plus Code）","Plus Code","統一編號（選填）","主檔備註（選填）"]);
 });
});
