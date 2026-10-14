import{describe,it,expect}from"vitest";
import{OPTIONC_APPROVAL_UI_ENABLED}from"./optionc-approval-flags";
import{differences,isTerminalStatus,mayLeaderReview,mayReview,reviewPayload,safeFields,statusLabel,type OptionCRequest}from"./optionc-approval-ui";
const row:OptionCRequest={requestPublicId:"id",entityId:"2",teamId:3,riskCode:"High",status:"Pending",requestedByUserId:7,submittedAt:"",beforeJson:'{"LocationName":"A"}',proposedJson:'{"LocationName":"B"}',rowVersion:"AAAAAAAAAAA="};
describe("Option C review UI fail-closed candidate",()=>{
 it("is OFF",()=>expect(OPTIONC_APPROVAL_UI_ENABLED).toBe(false));
 it("denies self and stale review",()=>{expect(mayReview(row,7)).toBe(false);expect(mayReview({...row,status:"Applied"},8)).toBe(false);expect(mayReview(row,8)).toBe(true);});
 it("denies Leader self-review, completed requests and completed first review",()=>{
  const stage={requestedByUserId:7,requestStatus:"Pending",leaderStatus:"Pending",rowVersion:"AAAAAAAAAAA="};
  expect(mayLeaderReview(stage,7)).toBe(false);
  expect(mayLeaderReview(stage,8)).toBe(true);
  expect(mayLeaderReview({...stage,requestStatus:"Rejected"},8)).toBe(false);
  expect(mayLeaderReview({...stage,leaderStatus:"Approved"},8)).toBe(false);
  expect(mayLeaderReview({...stage,rowVersion:""},8)).toBe(false);
 });
 it("shows only whitelisted changed fields",()=>{expect(differences(row).map(x=>x.field)).toEqual(["LocationName"]);expect(safeFields('{"Secret":"x"}')).not.toHaveProperty("Secret");expect(safeFields("invalid").Address).toBe("");});
 it("keeps Pending/Rejected/Applied status feedback explicit",()=>{
  expect(statusLabel("Pending")).toBe("待管理員審核");
  expect(statusLabel("Rejected")).toBe("已退回");
  expect(statusLabel("Applied")).toBe("已套用");
  expect(isTerminalStatus("Pending")).toBe(false);
  expect(isTerminalStatus("Applied")).toBe(true);
 });
 it("requires reason and one-time decision key",()=>{expect(reviewPayload(row," OK ","uuid").reason).toBe("OK");expect(()=>reviewPayload(row,"","uuid")).toThrow();expect(()=>reviewPayload(row,"OK","")).toThrow();});
});
