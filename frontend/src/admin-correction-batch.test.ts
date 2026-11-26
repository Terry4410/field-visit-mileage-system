import{describe,it,expect,vi}from"vitest";
import type{CorrectionRequest}from"./types";
import{adminBatchTargets,assertFreshTargets,canBatchClose,executeAdminBatch,type RouteState}from"./admin-correction-batch";
import{exactHistoryRows}from"./components/CorrectionHistory";
const row=(id:number,patch:Partial<CorrectionRequest>={}):CorrectionRequest=>({correctionRequestId:id,visitTripId:id,tripNo:"T"+id,visitorName:"測試",baseSnapshotVersion:1,status:"PendingAdminClose",reason:"測試",requestedAt:"2026-11-26",requestedBy:"測試",requiresAdminClose:true,proposal:{visitDate:"2026-11-25",approvedDistanceKm:30,stops:[]},changes:[{fieldName:"ApprovedDistanceKm",oldValue:"10",newValue:"30"}],rowVersion:"V1",...patch});
const route=(distance=30,patch:Partial<RouteState>={}):RouteState=>({rowVersion:"V1",result:{routeCalculationAttemptId:1,correlationId:"test",status:"Succeeded",suggestedDistanceKm:distance},...patch});
describe("admin correction batch",()=>{
 it("allows ordinary and verified matching Google; excludes stale, pending, mismatched and manual",()=>{
  const r=row(1),ordinary=row(2,{changes:[{fieldName:"Notes",oldValue:"a",newValue:"b"}]});
  expect(canBatchClose(ordinary)).toBe(true);expect(canBatchClose(r,route())).toBe(true);
  expect(canBatchClose(r,route(29))).toBe(false);expect(canBatchClose(r)).toBe(false);
  expect(canBatchClose(r,route(30,{rowVersion:"V0"}))).toBe(false);expect(canBatchClose(r,route(30,{loading:true}))).toBe(false);
  expect(canBatchClose(r,{rowVersion:"V1",error:"failed"})).toBe(false);expect(canBatchClose(row(1,{changes:[]} ),route())).toBe(false);
 });
 it("allows returning every selected pending admin case, including mismatch, but not closed",()=>{
  const rows=[row(1),row(2),row(3,{status:"Closed"})],states={1:route(),2:route(29)};
  expect(adminBatchTargets(rows,[1,2,3],true,states).map(x=>x.correctionRequestId)).toEqual([1]);
  expect(adminBatchTargets(rows,[1,2,3],false,states).map(x=>x.correctionRequestId)).toEqual([1,2]);
 });
 it("rejects stale preflight for either operation",()=>{const r=row(1);expect(()=>assertFreshTargets([r],[row(1,{rowVersion:"V2"})],false,{})).toThrow();expect(()=>assertFreshTargets([r],[r],true,{})).toThrow();expect(assertFreshTargets([r],[r],true,{1:route()})).toEqual([r])});
 it("requires a reason and stops on first ambiguous write without retry",async()=>{
  const send=vi.fn(async(r:CorrectionRequest)=>{if(r.correctionRequestId===2)throw Error("timeout")});
  await expect(executeAdminBatch([row(1)],false," ",send)).rejects.toThrow();expect(send).not.toHaveBeenCalled();
  const result=await executeAdminBatch([row(1),row(2),row(3)],false,"退回原因",send);
  expect(result).toEqual({success:1,unconfirmed:"T2：timeout",notRun:1});expect(send).toHaveBeenCalledTimes(2);
 });
 it("only shows exact trip identity, preserving all request versions",()=>{const rows=[row(1),row(2,{visitTripId:1,tripNo:"T1",status:"Rejected"}),row(3,{tripNo:"T1"}),row(4,{visitTripId:1,tripNo:"T10"})];expect(exactHistoryRows(rows,{visitTripId:1,tripNo:"T1"}).map(r=>r.correctionRequestId)).toEqual([1,2]);expect(exactHistoryRows(rows)).toEqual(rows)});
});
