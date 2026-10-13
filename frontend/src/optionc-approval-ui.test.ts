import{describe,it,expect}from"vitest";
import{OPTIONC_APPROVAL_UI_ENABLED}from"./optionc-approval-flags";
import{differences,mayReview,reviewPayload,safeFields,type OptionCRequest}from"./optionc-approval-ui";
const row:OptionCRequest={requestPublicId:"id",entityId:"2",teamId:3,riskCode:"High",status:"Pending",requestedByUserId:7,submittedAt:"",beforeJson:'{"LocationName":"A"}',proposedJson:'{"LocationName":"B"}',rowVersion:"AAAAAAAAAAA="};
describe("Option C review UI fail-closed candidate",()=>{
 it("is OFF",()=>expect(OPTIONC_APPROVAL_UI_ENABLED).toBe(false));
 it("denies self and stale review",()=>{expect(mayReview(row,7)).toBe(false);expect(mayReview({...row,status:"Applied"},8)).toBe(false);expect(mayReview(row,8)).toBe(true);});
 it("shows only whitelisted changed fields",()=>{expect(differences(row).map(x=>x.field)).toEqual(["LocationName"]);expect(safeFields('{"Secret":"x"}')).not.toHaveProperty("Secret");expect(safeFields("invalid").Address).toBe("");});
 it("requires reason and one-time decision key",()=>{expect(reviewPayload(row," OK ","uuid").reason).toBe("OK");expect(()=>reviewPayload(row,"","uuid")).toThrow();expect(()=>reviewPayload(row,"OK","")).toThrow();});
});
