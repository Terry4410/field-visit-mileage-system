import{describe,it,expect}from"vitest";
import{mayEditScopedDraft}from"./location-ownership-ui";
describe("scoped drafts",()=>{
 const r={teamId:7,approvalStatus:"Pending",isActive:false,createdByUserId:11};
 it("visitor owns only their own pending draft",()=>{expect(mayEditScopedDraft(r,"visitor",11,[7])).toBe(true);expect(mayEditScopedDraft(r,"visitor",12,[7])).toBe(false);expect(mayEditScopedDraft(r,"visitor",11,[8])).toBe(false)});
 it("leader UI never implies peer-draft authority before independently attested grant",()=>{expect(mayEditScopedDraft(r,"leader",8,[7])).toBe(false);expect(mayEditScopedDraft(r,"leader",11,[7])).toBe(true);expect(mayEditScopedDraft({...r,teamId:null},"leader",11,[7])).toBe(false);expect(mayEditScopedDraft({...r,isActive:true},"leader",11,[7])).toBe(false)});
});
