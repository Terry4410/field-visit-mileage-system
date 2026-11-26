import DetailLink from "../components/DetailLink";
import{useEffect,useRef,useState}from"react";
import{api}from"../api";
import{correctionChangeText,correctionStatusLabel,withConfirmedCorrectionComment}from"../correction-ui";
import{adminBatchTargets,assertFreshTargets,canBatchClose,currentRoute,executeAdminBatch,hasManualFallback,isDistanceMismatch,requiresRouteDecision,sameDistance,type RouteState}from"../admin-correction-batch";
import type{CorrectionRequest,RoutePreviewResult}from"../types";
import{usePagedQuery}from"../use-query";
import{ALL_CORRECTION_STATUSES,correctionSearchEnabled}from"../correction-ui";
import{DateFilters,Pagination}from"../components/QueryControls";
import CorrectionDetail from "../components/CorrectionDetail";

export default function CorrectionAdminPage(){
 const[detail,setDetail]=useState<CorrectionRequest|null>(null);
 const inProgress=useRef(false);
 const[keyword,setKeyword]=useState(""),[status,setStatus]=useState("PendingAdminClose"),[start,setStart]=useState(""),[end,setEnd]=useState(""),[selected,setSelected]=useState<number[]>([]),[msg,setMsg]=useState(""),[busy,setBusy]=useState(false),[routeStates,setRouteStates]=useState<Record<number,RouteState>>({});
 const enabled=correctionSearchEnabled(start,end);
 const query=usePagedQuery<CorrectionRequest>("/corrections/search",{keyword,status,startDate:start,endDate:end},enabled);
 const rows=query.data.items;
 const selectable=rows.filter(r=>r.status==="PendingAdminClose"&&!!r.rowVersion);
 const closeTargets=adminBatchTargets(rows,selected,true,routeStates),returnTargets=adminBatchTargets(rows,selected,false,routeStates);
 const load=()=>query.reload();
 useEffect(()=>{setSelected(current=>current.filter(id=>selectable.some(r=>r.correctionRequestId===id)))},[rows.map(r=>`${r.correctionRequestId}:${r.status}:${r.rowVersion}`).join("|")]);

 const calculateRoute=async(r:CorrectionRequest)=>{
  setRouteStates(x=>({...x,[r.correctionRequestId]:{loading:true,rowVersion:r.rowVersion}}));setMsg("");
  try{const result=await api<RoutePreviewResult>(`/corrections/${r.correctionRequestId}/route-preview`,{method:"POST"},120000);setRouteStates(x=>({...x,[r.correctionRequestId]:{result,rowVersion:r.rowVersion}}))}
  catch(e){setRouteStates(x=>({...x,[r.correctionRequestId]:{error:e instanceof Error?e.message:"Google 里程重算失敗",rowVersion:r.rowVersion}}))}
 };
 const closePayload=(r:CorrectionRequest,approve:boolean,comments:string)=>{
  if(!r.rowVersion)throw new Error("缺少資料版本，請重新查詢。");
  if(approve&&!r.changes.length)throw new Error("缺少更正差異，不能結案。");
  if(!approve||!requiresRouteDecision(r))return{approve,comments,rowVersion:r.rowVersion};
  if(!r.changes.length||!r.rowVersion)throw new Error("缺少更正差異或版本，不能結案。");
  const state=currentRoute(r,routeStates[r.correctionRequestId]);
  if(state?.result?.status==="Succeeded"){
   if(!sameDistance(state.result.suggestedDistanceKm,r.proposal.approvedDistanceKm))throw new Error(`Google Maps API 重算為 ${state.result.suggestedDistanceKm??"—"} km，但更正申請核定里程為 ${r.proposal.approvedDistanceKm??"—"} km；不得標記為 ProviderSuggested。請退回此申請並請申請人依 Google 結果重新提出。`);
   return{approve,comments,rowVersion:r.rowVersion,distanceDecisionSource:"ProviderSuggested",routeCalculationAttemptId:state.result.routeCalculationAttemptId};
  }
  if(isDistanceMismatch(state))throw new Error(state?.result?.errorMessage||"Google Maps API 已成功取得不同里程，不能改用人工備援；請退回並依 Google 結果重新提出。");
  if(state?.result||state?.error){
   if(!hasManualFallback(r))throw new Error("Google Maps API 無法取得可用里程，但此更正申請沒有填寫人工備援里程；請退回並請申請人補上人工備援里程後重新提出。");
   return{approve,comments,rowVersion:r.rowVersion,distanceDecisionSource:"ManualFallback",routeCalculationAttemptId:null};
  }
  throw new Error("此更正涉及核定里程或路線異動；請先執行 Google Maps API 重算。只有 Google 無法取得可用里程時才可使用人工備援。");
 };
 const close=async(r:CorrectionRequest,approve:boolean)=>withConfirmedCorrectionComment(
  window.prompt(approve?"管理者結案說明（選填）":"退回原因（必填）"),async comments=>{
  if(busy||inProgress.current)return;
  if(!approve&&!comments.trim())return setMsg("退回原因必填。");
  setMsg("");
  let payload:unknown;try{payload=closePayload(r,approve,comments)}catch(e){return setMsg(e instanceof Error?e.message:"結案條件不足")}
  const route=currentRoute(r,routeStates[r.correctionRequestId]);
  const manualFallback=approve&&requiresRouteDecision(r)&&hasManualFallback(r)&&(route?.error||route?.result?.status!=="Succeeded")&&!isDistanceMismatch(route);
  if(manualFallback&&!window.confirm("Google Maps API 已無法取得可用里程；確認以更正申請中的人工備援里程結案？"))return;
  inProgress.current=true;setBusy(true);try{await api(`/corrections/${r.correctionRequestId}/admin-close`,{method:"POST",body:JSON.stringify(payload)});setMsg(approve?"更正已結案並建立新 Snapshot。":"更正申請已退回，請重新提出更正。");setRouteStates(x=>{const n={...x};delete n[r.correctionRequestId];return n});await load()}catch(e){setMsg(e instanceof Error?e.message:"操作失敗")}finally{inProgress.current=false;setBusy(false)}
 });
 const batchClose=async(approve:boolean)=>{
  if(busy||inProgress.current||query.loading||query.error)return;
  const targets=approve?closeTargets:returnTargets;
  if(!targets.length)return setMsg(approve?"沒有符合批次結案條件的選取案件。":"請先選取待結案案件。");
  const skipped=returnTargets.length-targets.length;
  if(!window.confirm(`確認批次${approve?"結案":"退回"} ${targets.length} 筆？${approve&&skipped>0?`另有 ${skipped} 筆不符合結案條件，不會處理。`:""}`))return;
  const comment=window.prompt(approve?"批次結案說明（選填）":"批次退回原因（必填）");
  if(comment===null)return;if(!approve&&!comment.trim())return setMsg("退回原因必填。");
  inProgress.current=true;setBusy(true);setMsg("");
  try{
   const fresh=await api<CorrectionRequest[]>("/corrections?status=PendingAdminClose");
   const verified=assertFreshTargets(targets,fresh,approve,routeStates);
   const result=await executeAdminBatch(verified,approve,comment,(r,decision,note)=>api(`/corrections/${r.correctionRequestId}/admin-close`,{method:"POST",body:JSON.stringify(closePayload(r,decision,note))}));
   setMsg(`批次${approve?"結案":"退回"}：成功 ${result.success} 筆、未確認 ${result.unconfirmed?1:0} 筆、未執行 ${result.notRun} 筆。${result.unconfirmed?"請重新查詢確認，不要直接重送："+result.unconfirmed:""}`);
  }catch(e){setMsg(e instanceof Error?e.message:"批次處理未確認，請重新查詢。")}
  finally{setSelected([]);setRouteStates({});await load();inProgress.current=false;setBusy(false)}
 };
 const allSelected=selectable.length>0&&selectable.every(r=>selected.includes(r.correctionRequestId));
 return <div className="card">
  <div className="section-title"><div><h2>更正流程</h2><div className="sub">一般更正與 Google 重算一致的案件可批次結案；未重算、不一致或人工備援案件須單筆確認或退回。所有待管理者結案案件皆可退回。原核准版本永久保留。</div></div><div className="actions"><button className="btn small ok" disabled={busy||query.loading||!!query.error||!closeTargets.length} onClick={()=>void batchClose(true)}>批次結案（{closeTargets.length}）</button><button className="btn small danger" disabled={busy||query.loading||!!query.error||!returnTargets.length} onClick={()=>void batchClose(false)}>批次退回（{returnTargets.length}）</button></div></div>
  {msg&&<div className="note" style={{marginBottom:10}}>{msg}</div>}
  <div className="grid cols-2"><label>更正搜尋<input value={keyword} onChange={e=>setKeyword(e.target.value)} placeholder="更正編號、行程、工號、姓名、小組、中心／據點、地點、專案、拜訪形式、原因、審核說明或異動值"/></label><label>更正狀態<select value={status} onChange={e=>{setStatus(e.target.value);setSelected([])}}><option value={ALL_CORRECTION_STATUSES}>全部狀態</option><option value="PendingAdminClose">待管理者結案</option><option value="PendingLeaderReview">待小組長審核</option><option value="Closed">已結案</option><option value="Rejected">已退回</option></select></label></div>
  <DateFilters label="申請日期" start={start} end={end} onChange={(s,e)=>{setStart(s);setEnd(e)}}/><div className="actions" style={{marginBottom:10}}><button className="btn small outline" onClick={()=>{setKeyword("");setStatus(ALL_CORRECTION_STATUSES);setStart("");setEnd("");setSelected([])}}>清除條件</button></div>{!enabled&&<div className="note">申請日期起日不可晚於迄日。</div>}{query.error&&<div className="note danger-note">{query.error}</div>}
  <div className="table-wrap"><table><thead><tr><th><input type="checkbox" aria-label="全選待結案更正" checked={allSelected} disabled={busy||query.loading||!selectable.length} onChange={e=>setSelected(e.target.checked?selectable.map(r=>r.correctionRequestId):[])}/></th><th>申請日</th><th>行程編號</th><th>外訪員</th><th>原因</th><th>差異</th><th>里程檢核</th><th>狀態</th><th>操作</th></tr></thead><tbody>{rows.map(r=>{const needsDecision=requiresRouteDecision(r),route=currentRoute(r,routeStates[r.correctionRequestId]),fallbackReady=hasManualFallback(r);return <tr key={r.correctionRequestId}><td><input type="checkbox" aria-label={`選取更正 ${r.tripNo}`} disabled={busy||query.loading||r.status!=="PendingAdminClose"||!r.rowVersion||!!route?.loading} checked={selected.includes(r.correctionRequestId)} onChange={e=>setSelected(x=>e.target.checked?[...x,r.correctionRequestId]:x.filter(id=>id!==r.correctionRequestId))}/></td><td>{r.requestedAt.slice(0,10)}</td><td><DetailLink ariaLabel={`查看更正明細 ${r.tripNo}`} onClick={()=>setDetail(r)}>{r.tripNo}</DetailLink></td><td>{r.visitorName}</td><td>{r.reason}</td><td>{r.changes.length?<div className="correction-change-list">{r.changes.map((c,i)=><div key={i}>{correctionChangeText(c)}</div>)}</div>:"—"}</td><td>{!needsDecision?"不涉及里程／路線異動":route?.loading?"Google 計算中…":route?.result?.status==="Succeeded"?<span className="pill ok">Google Maps API {route.result.suggestedDistanceKm} km</span>:isDistanceMismatch(route)?<span className="pill danger">Google 結果與申請里程不同，需退回重提</span>:route?.result?<span className={fallbackReady?"pill warn":"pill danger"}>{fallbackReady?"Google 無可用結果 → 可人工備援":"Google 無可用結果，但未提供人工備援 → 需退回"}</span>:route?.error?<span className={fallbackReady?"pill warn":"pill danger"}>{fallbackReady?"Google 呼叫失敗 → 可人工備援":"Google 呼叫失敗，但未提供人工備援 → 需退回"}</span>:<span className="pill warn">需先 Google 重算</span>}</td><td>{correctionStatusLabel(r.status)}</td><td>{r.status==="PendingAdminClose"&&<div className="actions">{needsDecision&&<button className="btn small secondary" disabled={busy||route?.loading} onClick={()=>void calculateRoute(r)}>Google 重算里程</button>}<button className="btn small ok" disabled={busy||query.loading||route?.loading||!r.changes.length||!r.rowVersion||(needsDecision&&(!route||isDistanceMismatch(route)||(route.result?.status==="Succeeded"&&!canBatchClose(r,route))))} onClick={()=>void close(r,true)}>結案</button><button className="btn small danger" disabled={busy} onClick={()=>void close(r,false)}>退回</button></div>}</td></tr>})}</tbody></table></div>
  {detail&&<CorrectionDetail row={detail} onClose={()=>setDetail(null)}/>}
  <Pagination {...query.data} page={query.page} pageSize={query.pageSize} busy={query.loading||busy} onPage={query.setPage} onPageSize={query.setPageSize}/>
 </div>;
}
