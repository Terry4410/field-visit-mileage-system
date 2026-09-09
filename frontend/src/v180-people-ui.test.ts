import{describe,expect,it}from"vitest";
import type{V180PersonRow}from"./types";
import{canEditV180InternalAccess,isV180Supervisor,V180_INTERNAL_ROLE_LABELS,v180AccessErrorMessage,v180RoleCodes}from"./v180-people-ui";

const person=(roles:string[],adminEnabled:boolean|null=true):V180PersonRow=>({
 personId:1,employmentId:10,legacyUserId:100,employeeNo:"E001",displayName:"測試人員",
 organization:{organizationId:1,code:"ORG",name:"測試組織"},employmentStatus:"Active",
 roles:roles.map((code,index)=>({roleId:index+1,code,name:code})),teamMemberships:[],
 primaryTeam:null,adminEnabled,version:"AQIDBAUGBwg="
});

describe("v1.8 people ui",()=>{
 it("normalizes supervisor aliases and prevents supervisor editing in the internal modal",()=>{
  expect(v180RoleCodes(person(["government"]))).toEqual(["supervisor"]);
  expect(isV180Supervisor(person(["supervisor"]))).toBe(true);
  expect(canEditV180InternalAccess(person(["supervisor"]))).toBe(false);
  expect(V180_INTERNAL_ROLE_LABELS).not.toHaveProperty("supervisor");
 });
 it("requires a real login-account enabled state before internal access editing",()=>{
  expect(canEditV180InternalAccess(person(["visitor"],null))).toBe(false);
  expect(canEditV180InternalAccess(person(["visitor"],true))).toBe(true);
 });
 it("turns rowversion conflicts into a clear reload instruction",()=>{
  expect(v180AccessErrorMessage(new Error("ROWVERSION_CONFLICT：stale"))).toContain("重新整理");
 });
 it("preserves ordinary backend error details",()=>{
  expect(v180AccessErrorMessage(new Error("角色資料錯誤"))).toBe("角色資料錯誤");
 });
});
