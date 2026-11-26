import type{CorrectionRequest,RoutePreviewResult}from"./types";
export type RouteState={rowVersion:string;loading?:boolean;result?:RoutePreviewResult;error?:string};
type RouteStopShape={stopSequence:number;address?:string|null};
const normalizeAddress=(v?:string|null)=>(v||"").trim().replace(/\s+/g," ");
export const requiresRouteDecision=(r:CorrectionRequest)=>{
 if(r.changes.some(c=>c.fieldName==="ApprovedDistanceKm"))return true;
 const change=r.changes.find(c=>c.fieldName==="Stops");if(!change)return false;
 try{
  const project=(v?:string|null)=>(JSON.parse(v||"[]") as RouteStopShape[]).map(x=>[x.stopSequence,normalizeAddress(x.address)]).sort((a,b)=>Number(a[0])-Number(b[0]));
  return JSON.stringify(project(change.oldValue))!==JSON.stringify(project(change.newValue));
 }catch{return true}
};
export const isDistanceMismatch=(s?:RouteState)=>s?.result?.errorCode==="CORRECTION_DISTANCE_MISMATCH";
export const hasManualFallback=(r:CorrectionRequest)=>(r.proposal.claimedDistanceKm??0)>0;
export const sameDistance=(a?:number|null,b?:number|null)=>a!=null&&b!=null&&Number.isFinite(a)&&Number.isFinite(b)&&Math.abs(a-b)<=0.01;
export const currentRoute=(r:CorrectionRequest,s?:RouteState)=>s?.rowVersion===r.rowVersion?s:undefined;
export function canBatchClose(r:CorrectionRequest,s?:RouteState){
 if(r.status!=="PendingAdminClose"||!r.rowVersion||!r.changes.length)return false;
 if(!requiresRouteDecision(r))return true;
 const route=currentRoute(r,s),result=route?.result;
 return !route?.loading&&!isDistanceMismatch(route)&&result?.status==="Succeeded"&&result.routeCalculationAttemptId>0&&sameDistance(result.suggestedDistanceKm,r.proposal.approvedDistanceKm);
}
export function adminBatchTargets(rows:readonly CorrectionRequest[],selected:readonly number[],approve:boolean,states:Record<number,RouteState>){
 const ids=new Set(selected),targets=rows.filter(r=>ids.has(r.correctionRequestId)&&r.status==="PendingAdminClose"&&!!r.rowVersion);
 return approve?targets.filter(r=>canBatchClose(r,states[r.correctionRequestId])):targets;
}
export function assertFreshTargets(targets:readonly CorrectionRequest[],fresh:readonly CorrectionRequest[],approve:boolean,states:Record<number,RouteState>){
 const latest=new Map(fresh.map(r=>[r.correctionRequestId,r]));
 if(!targets.length||new Set(targets.map(r=>r.correctionRequestId)).size!==targets.length)throw Error("請先選取案件。");
 return targets.map(r=>{const f=latest.get(r.correctionRequestId);if(!f||f.status!=="PendingAdminClose"||!f.rowVersion||f.rowVersion!==r.rowVersion||(approve&&!canBatchClose(f,states[f.correctionRequestId])))throw Error("選取案件狀態、版本或里程證據已變更，請重新查詢。");return f});
}
export async function executeAdminBatch(targets:readonly CorrectionRequest[],approve:boolean,comment:string,send:(r:CorrectionRequest,approve:boolean,comment:string)=>Promise<unknown>){
 const note=comment.trim();if(!approve&&!note)throw Error("退回原因必填。");
 let success=0;for(const r of targets){try{await send(r,approve,note);success++}catch(e){return {success,unconfirmed:r.tripNo+"："+(e instanceof Error?e.message:"結果未確認"),notRun:targets.length-success-1}}}
 return {success,unconfirmed:"",notRun:0};
}
