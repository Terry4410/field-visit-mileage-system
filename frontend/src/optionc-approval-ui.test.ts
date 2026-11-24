import{describe,it,expect}from"vitest";
import{OPTIONC_APPROVAL_UI_ENABLED}from"./optionc-approval-flags";
import{adminDecisionPath,differences,isTerminalStatus,mayLeaderReview,mayReview,reviewPayload,safeFields,statusLabel,matchingHighRiskStage,mayPublishHighRisk,lowRiskReviewStage,type OptionCRequest}from"./optionc-approval-ui";
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
  expect(statusLabel("Pending")).toBe("待審核");
  expect(statusLabel("Rejected")).toBe("已退回");
  expect(statusLabel("Applied")).toBe("已套用");
  expect(isTerminalStatus("Pending")).toBe(false);
  expect(isTerminalStatus("Applied")).toBe(true);
 });
 it("routes Create only to reviewed publication, never generic approve",()=>{
  expect(adminDecisionPath({...row,operationCode:"CreateLocation"},"approve")).toBe("publish-reviewed");
  expect(adminDecisionPath({...row,operationCode:"UpdatePublishedLocation"},"approve")).toBe("approve");
  expect(adminDecisionPath({...row,operationCode:"CreateLocation"},"reject")).toBe("reject");
  expect(()=>adminDecisionPath(row,"approve")).toThrow();
  expect(()=>adminDecisionPath({...row,operationCode:"CreateLocation",status:"Applied"},"approve")).toThrow();
 });
 it("requires reason and one-time decision key",()=>{expect(reviewPayload(row," OK ","uuid").reason).toBe("OK");expect(()=>reviewPayload(row,"","uuid")).toThrow();expect(()=>reviewPayload(row,"OK","")).toThrow();});
});

describe("Admin high-risk stage evidence for create AND published update",()=>{
 const ready={requestPublicId:"id",stageStatus:"ReadyForPublish",geocodeStatus:"Succeeded",duplicateReviewStatus:"Cleared",stageRowVersion:"stage-rv"};
 it("requires matching ready stage for either operation",()=>{
  for(const operationCode of ["CreateLocation","UpdatePublishedLocation"]){
   const request={...row,operationCode};
   expect(mayPublishHighRisk(request,ready)).toBe(true);
   expect(mayPublishHighRisk(request,null)).toBe(false);
   expect(mayPublishHighRisk(request,{...ready,stageStatus:"PendingGeocode"})).toBe(false);
   expect(mayPublishHighRisk(request,{...ready,stageStatus:"PendingDuplicateReview"})).toBe(false);
  }
 });
 it("rejects mismatched request identity, version, operation, status and risk",()=>{
  const request={...row,operationCode:"UpdatePublishedLocation"};
  expect(matchingHighRiskStage(request,{...ready,requestPublicId:"other"})).toBeNull();
  expect(mayPublishHighRisk(request,{...ready,stageRowVersion:""})).toBe(false);
  expect(mayPublishHighRisk({...request,status:"Rejected"},ready)).toBe(false);
  expect(mayPublishHighRisk({...request,riskCode:"Low"},ready)).toBe(false);
  expect(mayPublishHighRisk({...request,operationCode:"Unknown"},ready)).toBe(false);
 });
 it("requires completed evidence, not the stage label alone",()=>{
  const request={...row,operationCode:"UpdatePublishedLocation"};
  expect(mayPublishHighRisk(request,{...ready,geocodeStatus:"Pending"})).toBe(false);
  expect(mayPublishHighRisk(request,{...ready,duplicateReviewStatus:"Blocked"})).toBe(false);
 });
});

describe("Low-risk Leader continuation",()=>{
 const request={requestPublicId:"id",operationCode:"UpdatePublishedLocation",riskCode:"Low",requestStatus:"Pending",leaderStatus:"Approved",requestedByUserId:7,rowVersion:"rv"};
 const stage={requestPublicId:"id",stageStatus:"PendingDuplicateReview",geocodeStatus:"NotRequired",duplicateReviewStatus:"Pending",stageRowVersion:"sv"};
 it("binds independent approved low-risk continuation to the matching stage",()=>{
  expect(lowRiskReviewStage(request,stage,8)).toEqual(stage);
  expect(lowRiskReviewStage(request,stage,7)).toBeNull();
  expect(lowRiskReviewStage({...request,leaderStatus:"Pending"},stage,8)).toBeNull();
  expect(lowRiskReviewStage({...request,riskCode:"High"},stage,8)).toBeNull();
  expect(lowRiskReviewStage(request,{...stage,requestPublicId:"other"},8)).toBeNull();
 });
});
