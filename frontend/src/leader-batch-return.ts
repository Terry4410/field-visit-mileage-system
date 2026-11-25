export type BatchReturnTrip = {visitTripId:number;tripNo:string;status:string;rowVersion:string};
export async function returnSelectedTrips<T extends BatchReturnTrip>(
  rows: readonly T[], selected: readonly number[], reason: string,
  send: (trip:T,reason:string)=>Promise<unknown>
) {
  const note=reason.trim();
  if(!note)throw new Error("退回原因必填。");
  const ids=new Set(selected);
  const targets=rows.filter(t=>ids.has(t.visitTripId)&&t.status==="PendingApproval");
  const unique=[...new Map(targets.map(t=>[t.visitTripId,t])).values()];
  if(unique.length!==ids.size||unique.some(t=>!t.rowVersion))throw new Error("選取行程狀態已變更或版本不足，請重新整理。");
  let success=0;
  const unconfirmed:string[]=[];
  for(const t of unique){
    try { await send(t,note);success++; }
    catch(e){unconfirmed.push(t.tripNo+"："+(e instanceof Error?e.message:"退回結果未確認"));break;}
  }
  return {success,unconfirmed,notRun:unique.length-success-unconfirmed.length};
}
