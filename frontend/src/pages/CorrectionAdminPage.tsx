import{useEffect,useState}from"react";
import{api}from"../api";
import{correctionChangeText}from"../correction-ui";
import type{CorrectionRequest,RoutePreviewResult}from"../types";
import{usePagedQuery}from"../use-query";
import{DateFilters,Pagination}from"../components/QueryControls";

type RouteState={loading?:boolean;result?:RoutePreviewResult;error?:string};
type RouteStopShape={stopSequence:number;address?:string|null};
const distanceChanged=(r:CorrectionRequest)=>r.changes.some(c=>c.fieldName==="ApprovedDistanceKm");
const normalizeAddress=(value?:string|null)=>(value||"").trim().replace(/\s+/g," ");
const routeBasisChanged=(r:CorrectionRequest)=>{
 const change=r.changes.find(c=>c.fieldName==="Stops");if(!change)return false;
 try{
  const before=(JSON.parse(change.oldValue||"[]") as RouteStopShape[]).map(x=>[x.stopSequence,normalizeAddress(x.address)]).sort((a,b)=>Number(a[0])-Number(b[0]));
  const after=(JSON.parse(change.newValue||"[]") as RouteStopShape[]).map(x=>[x.stopSequence,normalizeAddress(x.address)]).sort((a,b)=>Number(a[0])-Number(b[0]));
  return JSON.stringify(before)!==JSON.stringify(after);
 }catch{return true}
};
const requiresRouteDecision=(r:CorrectionRequest)=>distanceChanged(r)||routeBasisChanged(r);
const sameDistance=(a?:number|null,b?:number|null)=>a!=null&&b!=null&&Math.abs(a-b)<=0.01;
const isDistanceMismatch=(state?:RouteState)=>state?.result?.errorCode==="CORRECTION_DISTANCE_MISMATCH";
const hasManualFallback=(r:CorrectionRequest)=>(r.proposal.claimedDistanceKm??0)>0;

export default function CorrectionAdminPage(){
 const[keyword,setKeyword]=useState(""),[status,setStatus]=useState("PendingAdminClose"),[start,setStart]=useState(""),[end,setEnd]=useState(""),[selected,setSelected]=useState<number[]>([]),[msg,setMsg]=useState(""),[busy,setBusy]=useState(false),[routeStates,setRouteStates]=useState<Record<number,RouteState>>({});
 const enabled=!!(keyword.trim()||status||start||end)&&(!start||!end||start<=end);
 const query=usePagedQuery<CorrectionRequest>("/corrections/search",{keyword,status,startDate:start,endDate:end},enabled);
 const rows=query.data.items;
 const batchEligible=rows.filter(r=>r.status==="PendingAdminClose"&&!requiresRouteDecision(r));
 const load=()=>query.reload();
 useEffect(()=>{setSelected(current=>current.filter(id=>batchEligible.some(r=>r.correctionRequestId===id)))},[rows.map(r=>`${r.correctionRequestId}:${r.status}:${r.rowVersion}`).join("|")]);

 const calculateRoute=async(r:CorrectionRequest)=>{
  setRouteStates(x=>({...x,[r.correctionRequestId]:{loading:true}}));setMsg("");
  try{const result=await api<RoutePreviewResult>(`/corrections/${r.correctionRequestId}/route-preview`,{method:"POST"},120000);setRouteStates(x=>({...x,[r.correctionRequestId]:{result}}))}
  catch(e){setRouteStates(x=>({...x,[r.correctionRequestId]:{error:e instanceof Error?e.message:"Google 里程重算失敗"}}))}
 };
 const closePayload=(r:CorrectionRequest,approve:boolean,comments:string)=>{
  if(!approve||!requiresRouteDecision(r))return{approve,comments,rowVersion:r.rowVersion};
  const state=routeStates[r.correctionRequestId];
  if(state?.result?.status==="Succeeded"){
   if(!sameDistance(state.result.suggestedDistanceKm,r.proposal.approvedDistanceKm))throw new Error(`Google Maps API 重算為 ${state.result.suggestedDistanceKm??"—"} km，但更正申請核定里程為 ${r.proposal.approvedDistanceKm??"—"} km；不得標記為 ProviderSuggested。請拒絕此申請並請申請人依 Google 結果重新提出。`);
   return{approve,comments,rowVersion:r.rowVersion,distanceDecisionSource:"ProviderSuggested",routeCalculationAttemptId:state.result.routeCalculationAttemptId};
  }
  if(isDistanceMismatch(state))throw new Error(state?.result?.errorMessage||"Google Maps API 已成功取得不同里程，不能改用人工備援；請拒絕並依 Google 結果重新提出。");
  if(state?.result||state?.error){
   if(!hasManualFallback(r))throw new Error("Google Maps API 無法取得可用里程，但此更正申請沒有填寫人工備援里程；請拒絕並請申請人補上人工備援里程後重新提出。");
   return{approve,comments,rowVersion:r.rowVersion,distanceDecisionSource:"ManualFallback",routeCalculationAttemptId:null};
  }
  throw new Error("此更正涉及核定里程或路線異動；請先執行 Google Maps API 重算。只有 Google 無法取得可用里程時才可使用人工備援。");
 };
 const close=async(r:CorrectionRequest,approve:boolean)=>{
  const comments=window.prompt(approve?"管理者結案說明（選填）":"拒絕原因")||"";setMsg("");
  let payload:unknown;try{payload=closePayload(r,approve,comments)}catch(e){return setMsg(e instanceof Error?e.message:"結案條件不足")}
  const route=routeStates[r.correctionRequestId];
  const manualFallback=approve&&requiresRouteDecision(r)&&hasManualFallback(r)&&(route?.error||route?.result?.status!=="Succeeded")&&!isDistanceMismatch(route);
  if(manualFallback&&!window.confirm("Google Maps API 已無法取得可用里程；確認以更正申請中的人工備援里程結案？"))return;
  setBusy(true);try{await api(`/corrections/${r.correctionRequestId}/admin-close`,{method:"POST",body:JSON.stringify(payload)});setMsg(approve?"更正已結案並建立新 Snapshot。":"更正申請已拒絕。");setRouteStates(x=>{const n={...x};delete n[r.correctionRequestId];return n});await load()}catch(e){setMsg(e instanceof Error?e.message:"操作失敗")}finally{setBusy(false)}
 };
 const batchClose=async()=>{
  const targets=batchEligible.filter(r=>selected.includes(r.correctionRequestId));if(!targets.length)return setMsg("請先勾選不涉及里程／路線 decision 的待結案申請。");
  const fresh=await api<CorrectionRequest[]>("/corrections?status=PendingAdminClose");const latest=new Map(fresh.map(r=>[r.correctionRequestId,r]));
  const stale=targets.find(r=>!latest.has(r.correctionRequestId)||latest.get(r.correctionRequestId)?.rowVersion!==r.rowVersion||requiresRouteDecision(latest.get(r.correctionRequestId)!));
  if(stale){setMsg("選取案件的狀態或內容已變更，請重新載入後再選取。");await load();return}
  if(!window.confirm(`確認批次結案 ${targets.length} 筆一般更正？涉及里程或路線 decision 的案件不會進入批次。`))return;
  const comments=window.prompt("批次結案說明（選填）")||"";setBusy(true);let success=0;
  try{for(const target of targets){const current=latest.get(target.correctionRequestId)!;await api(`/corrections/${target.correctionRequestId}/admin-close`,{method:"POST",body:JSON.stringify({approve:true,comments,rowVersion:current.rowVersion})});success++}setSelected([]);setMsg(`批次結案完成：${success} 筆成功。`);await load()}
  catch(e){setMsg(`批次處理在第 ${success+1} 筆停止；已完成 ${success} 筆。原因：${e instanceof Error?e.message:"操作失敗"}`);await load()}finally{setBusy(false)}
 };
 const allSelected=batchEligible.length>0&&batchEligible.every(r=>selected.includes(r.correctionRequestId));
 return <div className="card">
  <div className="section-title"><div><h2>更正流程</h2><div className="sub">一般更正可批次結案；核定里程或實際路線異動必須單筆先做 Google 重算。Google 真正失敗且申請已有人工備援里程時才可人工備援。原核准 Snapshot 永久保留。</div></div><button className="btn small ok" disabled={busy||!selected.length} onClick={()=>void batchClose()}>批次結案（{selected.length}）</button></div>
  {msg&&<div className="note" style={{marginBottom:10}}>{msg}</div>}
  <div className="grid cols-2"><label>更正搜尋<input value={keyword} onChange={e=>setKeyword(e.target.value)} placeholder="更正編號、行程、工號、姓名、小組、中心／據點、地點、專案、拜訪形式、原因、審核說明或異動值"/></label><label>更正狀態<select value={status} onChange={e=>{setStatus(e.target.value);setSelected([])}}><option value="">全部狀態</option><option value="PendingAdminClose">待管理者結案</option><option value="PendingLeaderReview">待小組長審核</option><option value="Closed">已結案</option><option value="Rejected">已拒絕</option></select></label></div>
  <DateFilters label="申請日期" start={start} end={end} onChange={(s,e)=>{setStart(s);setEnd(e)}}/><div className="actions" style={{marginBottom:10}}><button className="btn small outline" onClick={()=>{setKeyword("");setStatus("");setStart("");setEnd("");setSelected([])}}>清除條件</button></div>{!enabled&&<div className="note">請設定查詢條件，或選擇待審狀態。</div>}{query.error&&<div className="note danger-note">{query.error}</div>}
  <div className="table-wrap"><table><thead><tr><th><input type="checkbox" aria-label="全選可批次結案更正" checked={allSelected} disabled={!batchEligible.length} onChange={e=>setSelected(e.target.checked?batchEligible.map(r=>r.correctionRequestId):[])}/></th><th>申請日</th><th>Trip</th><th>外訪員</th><th>原因</th><th>差異</th><th>里程治理</th><th>狀態</th><th>操作</th></tr></thead><tbody>{rows.map(r=>{const needsDecision=requiresRouteDecision(r),route=routeStates[r.correctionRequestId],fallbackReady=hasManualFallback(r);return <tr key={r.correctionRequestId}><td><input type="checkbox" disabled={r.status!=="PendingAdminClose"||needsDecision} checked={selected.includes(r.correctionRequestId)} onChange={e=>setSelected(x=>e.target.checked?[...x,r.correctionRequestId]:x.filter(id=>id!==r.correctionRequestId))}/></td><td>{r.requestedAt.slice(0,10)}</td><td>{r.tripNo}</td><td>{r.visitorName}</td><td>{r.reason}</td><td>{r.changes.length?<div className="correction-change-list">{r.changes.map((c,i)=><div key={i}>{correctionChangeText(c)}</div>)}</div>:"—"}</td><td>{!needsDecision?"不涉及里程／路線 decision":route?.loading?"Google 計算中…":route?.result?.status==="Succeeded"?<span className="pill ok">Google Maps API {route.result.suggestedDistanceKm} km</span>:isDistanceMismatch(route)?<span className="pill danger">Google 結果與申請里程不同，需退回重提</span>:route?.result?<span className={fallbackReady?"pill warn":"pill danger"}>{fallbackReady?"Google 無可用結果 → 可人工備援":"Google 無可用結果，但未提供人工備援 → 需退回"}</span>:route?.error?<span className={fallbackReady?"pill warn":"pill danger"}>{fallbackReady?"Google 呼叫失敗 → 可人工備援":"Google 呼叫失敗，但未提供人工備援 → 需退回"}</span>:<span className="pill warn">需先 Google 重算</span>}</td><td>{r.status}</td><td>{r.status==="PendingAdminClose"&&<div className="actions">{needsDecision&&<button className="btn small secondary" disabled={busy||route?.loading} onClick={()=>void calculateRoute(r)}>Google 重算里程</button>}<button className="btn small ok" disabled={busy||route?.loading} onClick={()=>void close(r,true)}>結案</button><button className="btn small danger" disabled={busy} onClick={()=>void close(r,false)}>拒絕</button></div>}</td></tr>})}</tbody></table></div>
  <Pagination {...query.data} page={query.page} pageSize={query.pageSize} busy={query.loading||busy} onPage={query.setPage} onPageSize={query.setPageSize}/>
 </div>;
}
