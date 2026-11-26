import{describe,expect,it}from"vitest";
import{isEligibleOfficialLocation,officialMaintenanceVisibility}from"./components/OfficialSiteMaintenance";
import{createElement}from"react";
import{renderToStaticMarkup}from"react-dom/server";
import OfficialSiteMaintenance from"./components/OfficialSiteMaintenance";

describe("official site admin location eligibility",()=>{
 it("accepts only active approved formal locations",()=>{
  expect(isEligibleOfficialLocation({isActive:true,isTemporary:false,approvalStatus:"Approved"})).toBe(true);
  expect(isEligibleOfficialLocation({isActive:false,isTemporary:false,approvalStatus:"Approved"})).toBe(false);
  expect(isEligibleOfficialLocation({isActive:true,isTemporary:true,approvalStatus:"Approved"})).toBe(false);
  expect(isEligibleOfficialLocation({isActive:true,isTemporary:false,approvalStatus:"Pending"})).toBe(false);
 });
});
describe("official site maintenance panels",()=>{
 it("keeps creation separate from list and editing in the list context",()=>{
  expect(officialMaintenanceVisibility("list",false)).toEqual({showList:true,showEditor:false});
  expect(officialMaintenanceVisibility("new",false)).toEqual({showList:false,showEditor:true});
  expect(officialMaintenanceVisibility("list",true)).toEqual({showList:true,showEditor:true});
 });
 it.each(["centers","sites"] as const)("defaults %s to list without rendering the creation inputs",mode=>{
  const html=renderToStaticMarkup(createElement(OfficialSiteMaintenance,{mode}));
  const label=mode==="centers"?"就業中心":"鄉鎮台";
  expect(html).toContain(label+"清單");
  expect(html).toContain("新增"+label);
  expect(html).not.toContain('placeholder="例如 C-CHANGHUA"');
  expect(html).not.toContain('placeholder="例如 S-CHANGHUA"');
 });
});
