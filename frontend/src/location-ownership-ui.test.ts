import {describe,expect,it} from "vitest";
import {mayEditScopedDraft,mayEditVisitorDraft,mayMaintainLeaderPublishedLowRisk} from "./location-ownership-ui";
import {OPTIONC_DIRECT_LOWRISK_UI_ENABLED} from "./optionc-approval-flags";
import type {ManagedLocation} from "./types";

const row=(x:Partial<ManagedLocation>={}):ManagedLocation=>({
 locationId:1,locationCode:"L1",locationName:"客戶",locationType:"Customer",isTemporary:false,
 approvalStatus:"Pending",geocodingStatus:"Pending",isActive:false,createdAt:"",rowVersion:"r",...x
});

describe("Leader published LowRisk maintenance entry",()=>{
 it("keeps the separately controlled mutation gate closed by default",()=>expect(OPTIONC_DIRECT_LOWRISK_UI_ENABLED).toBe(false));
 it("allows only an active published Customer in a live managed team",()=>{
  const published=row({teamId:4,approvalStatus:"Approved",isActive:true});
  expect(mayMaintainLeaderPublishedLowRisk(published,[4])).toBe(true);
  expect(mayMaintainLeaderPublishedLowRisk(published,[5])).toBe(false);
  expect(mayMaintainLeaderPublishedLowRisk({...published,isTemporary:true},[4])).toBe(false);
  expect(mayMaintainLeaderPublishedLowRisk({...published,locationType:"Official"},[4])).toBe(false);
  expect(mayMaintainLeaderPublishedLowRisk({...published,isActive:false},[4])).toBe(false);
  expect(mayMaintainLeaderPublishedLowRisk({...published,approvalStatus:"Pending"},[4])).toBe(false);
 });
});

describe("visitor location UI guard",()=>{
 it("allows only the owner of an inactive pending customer draft",()=>expect(mayEditVisitorDraft(row({createdByUserId:7}),7)).toBe(true));
 it("denies peer, active, approved and official rows",()=>{
  expect(mayEditVisitorDraft(row({createdByUserId:8}),7)).toBe(false);
  expect(mayEditVisitorDraft(row({createdByUserId:7,isActive:true}),7)).toBe(false);
  expect(mayEditVisitorDraft(row({createdByUserId:7,approvalStatus:"Approved"}),7)).toBe(false);
  expect(mayEditVisitorDraft(row({createdByUserId:7,locationType:"Official"}),7)).toBe(false);
  expect(mayEditVisitorDraft(row({createdByUserId:7,isTemporary:true}),7)).toBe(false);
  expect(mayEditVisitorDraft(row({createdByUserId:7,duplicateOfLocationId:99}),7)).toBe(false);
 });
});

describe("leader managed-team draft guard",()=>{
 const leader={userId:4,teamScopes:[{teamId:7,teamName:"Managed",isPrimary:true}]};
 it("keeps drafts inside the leader's managed team",()=>{
  const draft=row({teamId:7,createdByUserId:99});
  expect(mayEditScopedDraft(draft,leader,"leader")).toBe(true);
  expect(mayEditScopedDraft({...draft,teamId:8},leader,"leader")).toBe(false);
 });
 it("never turns an approved published row into an editable draft",()=>{
  expect(mayEditScopedDraft(row({teamId:7,approvalStatus:"Approved",isActive:true}),leader,"leader")).toBe(false);
 });
});
