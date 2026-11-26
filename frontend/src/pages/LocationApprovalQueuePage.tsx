import ReadOnlyDetail from "../components/ReadOnlyDetail";
import {useCallback,useEffect,useState} from "react";
import {api} from "../api";
import {useAuth} from "../auth";
import {OPTIONC_APPROVAL_UI_ENABLED} from "../optionc-approval-flags";
import {adminDecisionPath,differences,mayReview,reviewPayload,statusLabel,matchingHighRiskStage,mayPublishHighRisk,safeFields,type OptionCRequest,type ReviewStage} from "../optionc-approval-ui";
const label:Record<string,string>={LocationName:"地點名稱",City:"縣市",District:"區",Address:"地址",PlusCode:"Plus Code",TaxId:"統編",MasterNote:"備註"};
type Stage=ReviewStage;
const name=(r:OptionCRequest)=>safeFields(r.proposedJson).LocationName||safeFields(r.beforeJson).LocationName||"地點資料";
const stageLabel:Record<string,string>={PendingGeocode:"待座標確認",PendingDuplicateReview:"待重複地點檢查",ReadyForPublish:"可發布",Pending:"待初審",Approved:"初審通過",Succeeded:"已完成",Cleared:"已確認非重複",Blocked:"疑似重複，尚不可發布",NotRequired:"不需座標變更"};

export default function LocationApprovalQueuePage(){
 const {user}=useAuth();
 const [view,setView]=useState<OptionCRequest|null>(null);
 const [rows,setRows]=useState<OptionCRequest[]>([]),[selected,setSelected]=useState(""),[stage,setStage]=useState<Stage|null>(null);
 const [reason,setReason]=useState(""),[decisionReason,setDecisionReason]=useState(""),[latitude,setLatitude]=useState(""),[longitude,setLongitude]=useState("");
 const [query,setQuery]=useState(""),[typeFilter,setTypeFilter]=useState("all");
 const [error,setError]=useState(""),[message,setMessage]=useState(""),[busy,setBusy]=useState(false);
 const load=useCallback(async()=>{if(OPTIONC_APPROVAL_UI_ENABLED)setRows(await api<OptionCRequest[]>("/optionc-candidate/requests/queue"));},[]);
 const loadStage=useCallback(async(id:string)=>{
  if(!OPTIONC_APPROVAL_UI_ENABLED)return;
  setStage(await api<Stage>(`/optionc-candidate/requests/${encodeURIComponent(id)}/stage`));
 },[]);
 useEffect(()=>{if(OPTIONC_APPROVAL_UI_ENABLED)void load().catch(e=>setError(String(e)));},[load]);
 if(!OPTIONC_APPROVAL_UI_ENABLED)return <div role="status">地點申請複審尚未授權啟用。</div>;
 if(!user?.roles.some(r=>r.toLowerCase()==="admin"))return <div role="alert">沒有管理員權限。</div>;
 const row=rows.find(x=>x.requestPublicId===selected);
 const eligible=!!row&&mayReview(row,user.userId)&&
  (row.operationCode==="CreateLocation"||row.operationCode==="UpdatePublishedLocation");
 const isCreate=row?.operationCode==="CreateLocation";
 const matchedStage=matchingHighRiskStage(row,stage);
 const publishReady=mayPublishHighRisk(row,stage);
 const evidenceReason=reason.trim().length>0&&reason.trim().length<=100;
 const select=(id:string)=>{setSelected(id);setStage(null);setReason("");setDecisionReason("");setLatitude("");setLongitude("");setError("");void loadStage(id).catch(e=>setError(`審核階段尚不可檢視（可能仍待小組長初審或權限不足）：${String(e)}`));};
 const evidence=async(kind:"geocode"|"duplicate",cleared=true)=>{
  if(!row||!eligible||!matchedStage||!evidenceReason||busy)return;
  if(kind==="geocode"&&matchedStage.stageStatus!=="PendingGeocode")return;
  if(kind==="duplicate"&&matchedStage.stageStatus!=="PendingDuplicateReview")return;
  const lat=Number(latitude),lon=Number(longitude);
  if(kind==="geocode"&&(!latitude.trim()||!longitude.trim()||!Number.isFinite(lat)||!Number.isFinite(lon)||lat< -90||lat>90||lon< -180||lon>180)){
   setError("請提供有效緯度（-90～90）及經度（-180～180）。");return;
  }
  const body=kind==="geocode"
    ?{stageRowVersion:matchedStage.stageRowVersion,succeeded:true,latitude:lat,longitude:lon,reason:reason.trim(),evidenceKey:crypto.randomUUID()}
    :{stageRowVersion:matchedStage.stageRowVersion,cleared,reason:reason.trim(),evidenceKey:crypto.randomUUID()};
  setBusy(true);setError("");setMessage("");
  try{
   const updated=await api<Stage>(`/optionc-candidate/requests/${encodeURIComponent(row.requestPublicId)}/${kind==="geocode"?"geocode-evidence":"duplicate-evidence"}`,{method:"POST",body:JSON.stringify(body)});
   setStage(updated);setMessage("證據已記錄於 canonical audit，尚未發布地點。");
  }catch(e){setError(`${String(e)}；結果不明時請重新讀取審核階段，不可直接重送。`);}
  finally{setBusy(false);}
 };
 const decide=async(action:"approve"|"reject")=>{
  if(!row||!eligible||busy||!decisionReason.trim()||decisionReason.trim().length>100)return;
  if(action==="approve"&&!publishReady){
   setError("地點尚未完成必要座標及重複檢查，或審核資料已變更，不能發布。");return;
  }
  let data,endpoint;
  try{data=reviewPayload(row,decisionReason,crypto.randomUUID());endpoint=adminDecisionPath(row,action);}
  catch(e){setError(String(e));return;}
  setBusy(true);setError("");setMessage("");
  try{
   await api(`/optionc-candidate/requests/${encodeURIComponent(row.requestPublicId)}/${endpoint}`,{method:"POST",body:JSON.stringify(data)});
   setMessage(action==="approve"?"已提交獨立核准與受控發布。":"已完成拒絕。");
   setSelected("");setStage(null);setReason("");await load();
  }catch(e){setError(`${String(e)}；如提交結果不明請先查核 Request/audit，勿直接重送。`);}
  finally{setBusy(false);}
 };
 return <div className="card"><h2>地點申請複審</h2><p>請依案件的審核進度，確認地址與座標、檢查是否重複，再完成核准。初審及必要檢查尚未完成時，不可發布正式地點。</p>
  {message&&<div role="status" className="note ok-note">{message}</div>}
  {error&&<div role="alert" className="note danger-note">{error}</div>}
  <button className="btn secondary" disabled={busy} onClick={()=>void load().catch(e=>setError(String(e)))}>重新整理佇列</button>
  <div className="actions"><input aria-label="搜尋審核地點" placeholder="搜尋地點或申請人" value={query} onChange={e=>setQuery(e.target.value)}/><select aria-label="審核申請類型" value={typeFilter} onChange={e=>setTypeFilter(e.target.value)}><option value="all">全部</option><option value="CreateLocation">新增地點</option><option value="UpdatePublishedLocation">地點異動</option></select></div>
  <div className="table-wrap"><table><thead><tr><th>地點名稱</th><th>申請類型</th><th>申請者</th><th>申請日期</th><th>審核階段</th><th>操作</th></tr></thead><tbody>{rows.filter(r=>(typeFilter==="all"||r.operationCode===typeFilter)&&`${name(r)} ${r.applicantName||""}`.toLowerCase().includes(query.toLowerCase())).map(r=><tr key={r.requestPublicId}><td><button type="button" className="btn small outline" onClick={()=>setView(r)}>{name(r)}</button></td><td>{r.operationCode==="CreateLocation"?"新增地點":"地點異動"}</td><td>{r.applicantName||"申請人資料待查"}</td><td>{new Date(r.submittedAt).toLocaleDateString("zh-TW",{timeZone:"Asia/Taipei"})}</td><td>需管理者複審／{statusLabel(r.status)}</td><td><button className="btn small secondary" disabled={busy} onClick={()=>select(r.requestPublicId)}>審查</button></td></tr>)}</tbody></table></div>
  {view&&<ReadOnlyDetail title={name(view)} onClose={()=>setView(null)} fields={[["申請人",view.applicantName],["申請日期",new Date(view.submittedAt).toLocaleString("zh-TW",{timeZone:"Asia/Taipei"})],["狀態",statusLabel(view.status)],...Object.entries(safeFields(view.proposedJson)).map(([key,value])=>[label[key]||key,value] as const)]}/>}
  {row&&<section><h3>差異內容</h3><div className="table-wrap"><table><thead><tr><th>欄位</th><th>原值</th><th>申請值</th></tr></thead><tbody>{differences(row).map(x=><tr key={x.field}><td>{label[x.field]}</td><td>{x.before||"—"}</td><td>{x.after||"—"}</td></tr>)}</tbody></table></div>
   {!eligible&&<div role="alert">本人申請、作業代碼未知或狀態無效，不允許複審。</div>}
   <p>審核階段：{stageLabel[matchedStage?.stageStatus||""]||"未載入／待初審"}｜座標：{stageLabel[matchedStage?.geocodeStatus||""]||"—"}｜重複檢查：{stageLabel[matchedStage?.duplicateReviewStatus||""]||"—"}</p>
   <button className="btn small secondary" disabled={busy||!eligible} onClick={()=>void loadStage(row.requestPublicId).catch(e=>setError(String(e)))}>重新整理審核進度</button>
   <div className="field"><label htmlFor="admin-location-review-reason">核對說明（必填，100 字）</label><textarea id="admin-location-review-reason" rows={3} value={reason} maxLength={100} disabled={busy||!eligible} onChange={e=>setReason(e.target.value)}/><div className="hint">{reason.length} / 100 字</div></div>
   {matchedStage?.stageStatus==="PendingGeocode"&&<><div className="note">可手動開啟地圖確認地址及座標，再填入下方欄位；不會自動寫入或產生座標。</div><a className="btn small outline" href={`https://www.google.com/maps/search/?api=1&query=${encodeURIComponent([safeFields(row.proposedJson).Address,safeFields(row.proposedJson).LocationName].filter(Boolean).join(" "))}`} target="_blank" rel="noopener noreferrer">開啟地圖核對</a><div className="grid cols-2"><div className="field"><label>緯度</label><input value={latitude} onChange={e=>setLatitude(e.target.value)}/></div><div className="field"><label>經度</label><input value={longitude} onChange={e=>setLongitude(e.target.value)}/></div></div></>}
   {matchedStage?.duplicateReviewStatus==="Blocked"&&<div className="note">此申請仍未發布。請核對既有地點；確認不同後才可繼續核准，若確為同一地點則不核准本申請。不要直接合併未發布的申請資料。</div>}
   {matchedStage&&<div className="actions"><button className="btn secondary" disabled={!eligible||busy||!evidenceReason||matchedStage?.stageStatus!=="PendingGeocode"} onClick={()=>void evidence("geocode")}>確認地址及座標</button><button className="btn secondary" disabled={!eligible||busy||!evidenceReason||matchedStage?.stageStatus!=="PendingDuplicateReview"} onClick={()=>void evidence("duplicate",true)}>確認為不同地點</button><button className="btn secondary" disabled={!eligible||busy||!evidenceReason||matchedStage?.stageStatus!=="PendingDuplicateReview"} onClick={()=>void evidence("duplicate",false)}>標記疑似重複</button></div>}
   <div className="field"><label>審核意見（必填，100 字）<textarea maxLength={100} value={decisionReason} disabled={busy||!eligible} onChange={e=>setDecisionReason(e.target.value)}/></label></div><div className="actions"><button className="btn secondary" disabled={!eligible||busy||!decisionReason.trim()} onClick={()=>void decide("reject")}>不核准</button><button className="btn ok" disabled={!eligible||busy||!decisionReason.trim()||!publishReady} onClick={()=>void decide("approve")}>{isCreate?"核准並發布新地點":"確認核准"}</button></div>
  </section>}
 </div>;
}
