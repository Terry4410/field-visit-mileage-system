export type OptionCRequest={operationCode?:string;requestPublicId:string;entityId:string;teamId:number|null;riskCode:string;status:string;requestedByUserId:number;applicantName?:string|null;submittedAt:string;reviewedAt?:string|null;beforeJson:string|null;proposedJson:string;rowVersion:string};
export const fields=["LocationName","City","District","Address","PlusCode","TaxId","MasterNote"] as const;
type Field=typeof fields[number];
export function safeFields(json:string|null):Record<Field,string>{
 const result=Object.fromEntries(fields.map(f=>[f,""])) as Record<Field,string>;
 if(!json||json.length>10000)return result;
 try{const data:unknown=JSON.parse(json);if(!data||typeof data!=="object"||Array.isArray(data))return result;
  for(const key of fields){const v=(data as Record<string,unknown>)[key];if(typeof v==="string")result[key]=v;}
 }catch{/* fail closed */}return result;
}
export function differences(row:Pick<OptionCRequest,"beforeJson"|"proposedJson">){
 const before=safeFields(row.beforeJson),after=safeFields(row.proposedJson);
 return fields.filter(k=>before[k]!==after[k]).map(k=>({field:k,before:before[k],after:after[k]}));
}
export function mayLeaderReview(row:{
 requestedByUserId:number;requestStatus:string;leaderStatus:string;rowVersion:string;
},actorId:number):boolean{
 return actorId>0&&actorId!==row.requestedByUserId&&row.requestStatus==="Pending"
  &&row.leaderStatus==="Pending"&&!!row.rowVersion;
}
export function mayReview(row:OptionCRequest,actorId:number){
 return actorId>0&&actorId!==row.requestedByUserId&&row.status==="Pending"&&row.riskCode==="High"&&!!row.rowVersion;
}
export function reviewPayload(row:OptionCRequest,reason:string,decisionKey:string){
 const trimmed=reason.trim();
 if(!trimmed||trimmed.length>1000||!decisionKey||!row.rowVersion)throw new Error("請提供審核理由與有效決策識別碼。");
 return {requestRowVersion:row.rowVersion,decisionKey,reason:trimmed};
}

export function statusLabel(status:string):string{
 switch(status){
  case "Pending": return "待審核";
  case "Rejected": return "已退回";
  case "Applied": return "已完成";
  default: return "狀態待查核";
 }
}
export function isTerminalStatus(status:string):boolean{
 return status==="Rejected"||status==="Applied";
}

export function adminDecisionPath(row:OptionCRequest,action:"approve"|"reject"):string{
 if(row.status!=="Pending")throw new Error("非待審案件不可提交決策。");
 if(row.operationCode!=="CreateLocation"&&row.operationCode!=="UpdatePublishedLocation")
  throw new Error("未知地點作業類型，不可核准。");
 return action==="reject"?"reject":row.operationCode==="CreateLocation"?"publish-reviewed":"approve";
}

/** Both new locations and high-risk published updates have a staged review.
 * A stale response for a different request must never unlock a decision. */
export type ReviewStage = {requestPublicId:string;stageStatus:string;geocodeStatus:string;duplicateReviewStatus:string;stageRowVersion:string};
export function matchingHighRiskStage(row:OptionCRequest|undefined,stage:ReviewStage|null):ReviewStage|null{
 if(!row||!stage||row.status!=="Pending"||row.riskCode!=="High"
    ||(row.operationCode!=="CreateLocation"&&row.operationCode!=="UpdatePublishedLocation")
    ||stage.requestPublicId!==row.requestPublicId||!stage.stageRowVersion)return null;
 return stage;
}
export function mayPublishHighRisk(row:OptionCRequest|undefined,stage:ReviewStage|null){
 const matched=matchingHighRiskStage(row,stage);
 return !!matched&&matched.stageStatus==="ReadyForPublish"
   &&(matched.geocodeStatus==="Succeeded"||matched.geocodeStatus==="NotRequired")
   &&matched.duplicateReviewStatus==="Cleared";
}

export function lowRiskReviewStage(row:{requestPublicId:string;operationCode:string;riskCode:string;requestStatus:string;leaderStatus:string;requestedByUserId:number;rowVersion:string}|undefined,stage:ReviewStage|null,actorId:number){
 if(!row||!stage||actorId<=0||row.requestedByUserId===actorId||row.operationCode!=="UpdatePublishedLocation"||row.riskCode!=="Low"||row.requestStatus!=="Pending"||row.leaderStatus!=="Approved"||!row.rowVersion||stage.requestPublicId!==row.requestPublicId||!stage.stageRowVersion||stage.geocodeStatus!=="NotRequired")return null;
 return stage;
}
