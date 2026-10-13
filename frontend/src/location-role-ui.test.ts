import {describe,expect,it} from "vitest";
import {leaderScopedTeamOptions,mayEditVisitorDraft} from "./location-ownership-ui";
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
 it("keeps the shared single-record edit action pending, inactive and owner-only",()=>{
  expect(mayEditVisitorDraft(row(),7)).toBe(true);
  expect(mayEditVisitorDraft(row({createdByUserId:8}),7)).toBe(false);
  expect(mayEditVisitorDraft(row({approvalStatus:"Approved"}),7)).toBe(false);
  expect(mayEditVisitorDraft(row({isActive:true}),7)).toBe(false);
  expect(mayEditVisitorDraft(row({locationType:"Official"}),7)).toBe(false);
 });
});
