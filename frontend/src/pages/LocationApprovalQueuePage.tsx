import {useCallback,useEffect,useState} from "react";
import {api} from "../api";
import {useAuth} from "../auth";
import {OPTIONC_APPROVAL_UI_ENABLED} from "../optionc-approval-flags";
import {adminDecisionPath,differences,mayReview,reviewPayload,statusLabel,type OptionCRequest} from "../optionc-approval-ui";
const label:Record<string,string>={LocationName:"地點名稱",City:"縣市",District:"區",Address:"地址",PlusCode:"Plus Code",TaxId:"統編",MasterNote:"備註"};
type Stage={requestPublicId:string;stageStatus:string;geocodeStatus:string;duplicateReviewStatus:string;stageRowVersion:string};

export default function LocationApprovalQueuePage(){
 const {user}=useAuth();
 const [rows,setRows]=useState<OptionCRequest[]>([]),[selected,setSelected]=useState(""),[stage,setStage]=useState<Stage|null>(null);
 const [reason,setReason]=useState(""),[latitude,setLatitude]=useState(""),[longitude,setLongitude]=useState("");
 const [error,setError]=useState(""),[message,setMessage]=useState(""),[busy,setBusy]=useState(false);
 const load=useCallback(async()=>{if(OPTIONC_APPROVAL_UI_ENABLED)setRows(await api<OptionCRequest[]>("/optionc-candidate/requests/queue"));},[]);
 const loadStage=useCallback(async(id:string)=>{
  if(!OPTIONC_APPROVAL_UI_ENABLED)return;
  setStage(await api<Stage>(`/optionc-candidate/requests/${encodeURIComponent(id)}/stage`));
 },[]);
 useEffect(()=>{if(OPTIONC_APPROVAL_UI_ENABLED)void load().catch(e=>setError(String(e)));},[load]);
 if(!OPTIONC_APPROVAL_UI_ENABLED)return <div role="status">地點變更核准尚未授權啟用。</div>;
 if(!user?.roles.some(r=>r.toLowerCase()==="admin"))return <div role="alert">沒有管理員權限。</div>;
 const row=rows.find(x=>x.requestPublicId===selected);
 const eligible=!!row&&mayReview(row,user.userId)&&
  (row.operationCode==="CreateLocation"||row.operationCode==="UpdatePublishedLocation");
 const isCreate=row?.operationCode==="CreateLocation";
 const evidenceReason=reason.trim().length>0&&reason.trim().length<=1000;
 const select=(id:string)=>{setSelected(id);setStage(null);setReason("");setLatitude("");setLongitude("");setError("");void loadStage(id).catch(e=>setError(`Stage 尚不可檢視（可能仍待 Leader 初審或權限不足）：${String(e)}`));};
 const evidence=async(kind:"geocode"|"duplicate",cleared=true)=>{
  if(!row||!eligible||!stage||!evidenceReason||busy)return;
  const lat=Number(latitude),lon=Number(longitude);
  if(kind==="geocode"&&(!latitude.trim()||!longitude.trim()||!Number.isFinite(lat)||!Number.isFinite(lon)||lat< -90||lat>90||lon< -180||lon>180)){
   setError("請提供有效緯度（-90～90）及經度（-180～180）。");return;
  }
  const body=kind==="geocode"
    ?{stageRowVersion:stage.stageRowVersion,succeeded:true,latitude:lat,longitude:lon,reason:reason.trim(),evidenceKey:crypto.randomUUID()}
    :{stageRowVersion:stage.stageRowVersion,cleared,reason:reason.trim(),evidenceKey:crypto.randomUUID()};
  setBusy(true);setError("");setMessage("");
  try{
   const updated=await api<Stage>(`/optionc-candidate/requests/${encodeURIComponent(row.requestPublicId)}/${kind==="geocode"?"geocode-evidence":"duplicate-evidence"}`,{method:"POST",body:JSON.stringify(body)});
   setStage(updated);setMessage("證據已記錄於 canonical audit，尚未發布地點。");
  }catch(e){setError(`${String(e)}；結果不明時請重新讀取 Stage，不可直接重送。`);}
  finally{setBusy(false);}
 };
 const decide=async(action:"approve"|"reject")=>{
  if(!row||!eligible||busy||!evidenceReason)return;
  if(action==="approve"&&isCreate&&stage?.stageStatus!=="ReadyForPublish"){
   setError("新增地點尚未完成 Geocode、Duplicate 與獨立 Leader 初審，不能發布。");return;
  }
  let data,endpoint;
  try{data=reviewPayload(row,reason,crypto.randomUUID());endpoint=adminDecisionPath(row,action);}
  catch(e){setError(String(e));return;}
  setBusy(true);setError("");setMessage("");
  try{
   await api(`/optionc-candidate/requests/${encodeURIComponent(row.requestPublicId)}/${endpoint}`,{method:"POST",body:JSON.stringify(data)});
   setMessage(action==="approve"?"已提交獨立核准與受控發布。":"已完成拒絕。");
   setSelected("");setStage(null);setReason("");await load();
  }catch(e){setError(`${String(e)}；如提交結果不明請先查核 Request/audit，勿直接重送。`);}
  finally{setBusy(false);}
 };
 return <div className="card"><h2>地點變更獨立 Admin 複審</h2><p>僅處理即時授權的案件；CreateLocation 必須先獨立 Leader 初審、核對地理及重複證據後才可發布。</p>
  {message&&<div role="status" className="note ok-note">{message}</div>}
  {error&&<div role="alert" className="note danger-note">{error}</div>}
  <button className="btn secondary" disabled={busy} onClick={()=>void load().catch(e=>setError(String(e)))}>重新整理佇列</button>
  <div className="table-wrap"><table><thead><tr><th>Location</th><th>作業</th><th>申請者</th><th>狀態</th><th>操作</th></tr></thead><tbody>{rows.map(r=><tr key={r.requestPublicId}><td>{r.entityId}</td><td>{r.operationCode||"未知"}</td><td>{r.requestedByUserId}</td><td>{statusLabel(r.status)}</td><td><button className="btn small secondary" disabled={busy} onClick={()=>select(r.requestPublicId)}>審查</button></td></tr>)}</tbody></table></div>
  {row&&<section><h3>差異內容</h3><div className="table-wrap"><table><thead><tr><th>欄位</th><th>原值</th><th>申請值</th></tr></thead><tbody>{differences(row).map(x=><tr key={x.field}><td>{label[x.field]}</td><td>{x.before||"—"}</td><td>{x.after||"—"}</td></tr>)}</tbody></table></div>
   {!eligible&&<div role="alert">本人申請、作業代碼未知或狀態無效，不允許複審。</div>}
   <p>Stage：{stage?.stageStatus||"未授權／未載入"}｜Geocode：{stage?.geocodeStatus||"—"}｜Duplicate：{stage?.duplicateReviewStatus||"—"}</p>
   <button className="btn small secondary" disabled={busy||!eligible} onClick={()=>void loadStage(row.requestPublicId).catch(e=>setError(String(e)))}>重新讀取 Stage</button>
   <label>審查及證據理由</label><textarea value={reason} maxLength={1000} disabled={busy||!eligible} onChange={e=>setReason(e.target.value)}/>
   {isCreate&&stage?.stageStatus==="PendingGeocode"&&<div className="grid cols-2"><div className="field"><label>緯度</label><input value={latitude} onChange={e=>setLatitude(e.target.value)}/></div><div className="field"><label>經度</label><input value={longitude} onChange={e=>setLongitude(e.target.value)}/></div></div>}
   {isCreate&&<div className="actions"><button className="btn secondary" disabled={!eligible||busy||!evidenceReason||stage?.stageStatus!=="PendingGeocode"} onClick={()=>void evidence("geocode")}>記錄 Geocode 證據</button><button className="btn secondary" disabled={!eligible||busy||!evidenceReason||stage?.stageStatus!=="PendingDuplicateReview"} onClick={()=>void evidence("duplicate",true)}>確認非重複</button><button className="btn secondary" disabled={!eligible||busy||!evidenceReason||stage?.stageStatus!=="PendingDuplicateReview"} onClick={()=>void evidence("duplicate",false)}>標記疑似重複</button></div>}
   <div className="actions"><button className="btn secondary" disabled={!eligible||busy||!evidenceReason} onClick={()=>void decide("reject")}>拒絕</button><button className="btn ok" disabled={!eligible||busy||!evidenceReason||(isCreate&&stage?.stageStatus!=="ReadyForPublish")} onClick={()=>void decide("approve")}>{isCreate?"獨立核准並發布新地點":"獨立核准"}</button></div>
  </section>}
 </div>;
}
