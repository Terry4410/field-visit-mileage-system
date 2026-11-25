import {describe,expect,it} from "vitest";
import {businessAccessLabel,completenessWarnings} from "./people-completeness";
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
