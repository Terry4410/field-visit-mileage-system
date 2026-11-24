import {describe,expect,it} from "vitest";
import {mayEditScopedDraft,mayEditVisitorDraft,mayMaintainLeaderPublishedLowRisk,scopedLocationNoteTeams,mayRequestPublishedChange,validTaiwanTaxId,firstTaxIdPayload} from "./location-ownership-ui";
import {OPTIONC_DIRECT_LOWRISK_UI_ENABLED,OPTIONC_APPROVAL_UI_ENABLED} from "./optionc-approval-flags";
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

describe("Location note team boundary",()=>{
 const scopes=[{teamId:7,teamName:"A",isPrimary:true},{teamId:8,teamName:"B",isPrimary:false},{teamId:0,teamName:"Invalid",isPrimary:false}];
 it("never substitutes a trip team for explicit authorization",()=>{
  expect(scopedLocationNoteTeams(undefined,7)).toEqual([]);
  expect(scopedLocationNoteTeams(scopes,9)).toEqual([]);
  expect(scopedLocationNoteTeams(scopes,8).map(x=>x.teamId)).toEqual([8]);
 });
 it("excludes invalid scopes and permits selection among actual scopes",()=>{
  expect(scopedLocationNoteTeams(scopes).map(x=>x.teamId)).toEqual([7,8]);
  expect(scopedLocationNoteTeams(scopes,0)).toEqual([]);
 });
});

describe("Published location candidate actions",()=>{
 const published=row({teamId:7,createdByUserId:4,approvalStatus:"Approved",isActive:true});
 it("keeps both candidate mutation gates closed",()=>{
  expect(OPTIONC_DIRECT_LOWRISK_UI_ENABLED).toBe(false);
  expect(OPTIONC_APPROVAL_UI_ENABLED).toBe(false);
 });
 it("allows owner Visitor or scoped Leader; rejects peer and out-of-scope rows",()=>{
  expect(mayRequestPublishedChange(published,4,"visitor",[7])).toBe(true);
  expect(mayRequestPublishedChange(published,5,"visitor",[7])).toBe(false);
  expect(mayRequestPublishedChange(published,5,"leader",[7])).toBe(true);
  expect(mayRequestPublishedChange(published,4,"visitor",[8])).toBe(false);
  expect(mayRequestPublishedChange(published,5,"leader",[])).toBe(false);
  expect(mayRequestPublishedChange(published,undefined,"leader",[7])).toBe(false);
 });
 it("excludes pending, official, inactive, temporary, duplicate and stale rows",()=>{
  for(const change of [{approvalStatus:"Pending"},{locationType:"Official"},{isActive:false},{isTemporary:true},{duplicateOfLocationId:2},{duplicateReason:"疑似重複"},{rowVersion:""}])
   expect(mayRequestPublishedChange({...published,...change},4,"visitor",[7])).toBe(false);
 });
 it("accepts current Taiwan checksum including special seventh digit",()=>{
  for(const value of ["04595252","10458570","04595257","10458574"])expect(validTaiwanTaxId(value)).toBe(true);
  for(const value of ["00000000","04595251","1234567","１２３４５６７８",""])expect(validTaiwanTaxId(value)).toBe(false);
 });
 it("sends only first TaxId, never a note change, with row version and operation identity",()=>{
  expect(firstTaxIdPayload(published," 04595252 ","op")).toEqual({expectedRowVersion:"r",masterNote:null,firstTaxId:"04595252",reason:"首次補登統一編號",operationKey:"op"});
  expect(()=>firstTaxIdPayload({...published,taxId:"10458570"},"04595252","op")).toThrow();
  expect(()=>firstTaxIdPayload(published,"04595251","op")).toThrow();
  expect(()=>firstTaxIdPayload({...published,rowVersion:""},"04595252","op")).toThrow();
 });
});
