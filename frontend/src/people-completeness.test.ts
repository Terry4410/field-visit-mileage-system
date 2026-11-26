import {describe,expect,it} from "vitest";
import {businessAccessLabel,completenessWarnings,currentAssignments,validTeamSelection,setupWarnings} from "./people-completeness";
import type {V170PeopleRow} from "./types";

const person=(patch:Partial<V170PeopleRow>={}):V170PeopleRow=>({
 userId:7,userCode:"E7",userType:"Internal",displayName:"測試",adminEnabled:true,
 actualAccess:true,roles:[],teamAssignments:[],...patch
});

describe("business role and staff completeness",()=>{
 it("does not describe HR eligibility as business login when roleless",()=>{
  expect(businessAccessLabel(person())).toContain("不可進入系統");
  expect(completenessWarnings(person())).toContain("缺業務角色");
 });
 it("requires a primary site for Visitor but not review/Admin roles",()=>{
  const visitor=person({roles:["visitor"],teamAssignments:[{teamId:1,teamCode:"T1",teamName:"一組",isPrimary:true}]});
  expect(completenessWarnings(visitor).join(" ")).toContain("主要派駐據點");
  expect(completenessWarnings({...visitor,roles:["leader"]}).join(" ")).not.toContain("主要派駐據點");
  expect(completenessWarnings({...visitor,roles:["admin"]}).join(" ")).not.toContain("主要派駐據點");
 });
});

describe("integrated personnel setup",()=>{
 it("checks role, required teams and primary site without requiring a site for leaders or admins",()=>{
  expect(setupWarnings([],[],false)).toEqual(["業務角色未設定"]);
  expect(setupWarnings(["visitor"],[],false)).toHaveLength(2);
  expect(setupWarnings(["leader"],[{teamId:1,isPrimary:true}],false)).toEqual([]);
  expect(setupWarnings(["admin"],[],false)).toEqual([]);
  expect(setupWarnings(["visitor"],[{teamId:1,isPrimary:true}],null)).toEqual(["主要派駐據點尚未確認"]);
 });
 it("requires exactly one primary and unique team IDs, with optional empty membership",()=>{
  expect(validTeamSelection([],false)).toBe(true);expect(validTeamSelection([],true)).toBe(false);
  expect(validTeamSelection([{teamId:1,isPrimary:true},{teamId:2,isPrimary:false}],true)).toBe(true);
  expect(validTeamSelection([{teamId:1,isPrimary:true},{teamId:1,isPrimary:false}],true)).toBe(false);
  expect(validTeamSelection([{teamId:1,isPrimary:false}],true)).toBe(false);
  expect(validTeamSelection([{teamId:1,isPrimary:true},{teamId:2,isPrimary:true}],true)).toBe(false);
 });
 it("projects only current assignments without changing historical rows",()=>{
  const d={roleAssignments:[{roleCode:"visitor",isCurrent:true},{roleCode:"admin",isCurrent:false}],teamAssignments:[{teamId:1,isPrimary:true,isCurrent:true},{teamId:2,isPrimary:false,isCurrent:false}]} as import("./types").V170PersonDetail;
  expect(currentAssignments(d)).toEqual({roles:["visitor"],teams:[{teamId:1,isPrimary:true}]});
 });
});
