import {describe,expect,it} from "vitest";
import {mayEditVisitorDraft} from "./location-ownership-ui";
import type {ManagedLocation} from "./types";

const row=(x:Partial<ManagedLocation>={}):ManagedLocation=>({
 locationId:1,locationCode:"L1",locationName:"客戶",locationType:"Customer",isTemporary:false,
 approvalStatus:"Pending",geocodingStatus:"Pending",isActive:false,createdAt:"",rowVersion:"r",...x
});

describe("visitor location UI guard",()=>{
 it("allows only the owner of an inactive pending customer draft",()=>expect(mayEditVisitorDraft(row({createdByUserId:7}),7)).toBe(true));
 it("denies peer, active, approved and official rows",()=>{
  expect(mayEditVisitorDraft(row({createdByUserId:8}),7)).toBe(false);
  expect(mayEditVisitorDraft(row({createdByUserId:7,isActive:true}),7)).toBe(false);
  expect(mayEditVisitorDraft(row({createdByUserId:7,approvalStatus:"Approved"}),7)).toBe(false);
  expect(mayEditVisitorDraft(row({createdByUserId:7,locationType:"Official"}),7)).toBe(false);
 });
});
