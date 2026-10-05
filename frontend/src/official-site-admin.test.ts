import{describe,expect,it}from"vitest";
import{isEligibleOfficialLocation}from"./components/OfficialSiteMaintenance";

describe("official site admin location eligibility",()=>{
 it("accepts only active approved formal locations",()=>{
  expect(isEligibleOfficialLocation({isActive:true,isTemporary:false,approvalStatus:"Approved"})).toBe(true);
  expect(isEligibleOfficialLocation({isActive:false,isTemporary:false,approvalStatus:"Approved"})).toBe(false);
  expect(isEligibleOfficialLocation({isActive:true,isTemporary:true,approvalStatus:"Approved"})).toBe(false);
  expect(isEligibleOfficialLocation({isActive:true,isTemporary:false,approvalStatus:"Pending"})).toBe(false);
 });
});
