import {useCallback,useEffect,useState} from "react";
import {api} from "../api";
import {useAuth} from "../auth";
import {OPTIONC_LEADER_REVIEW_UI_ENABLED} from "../optionc-approval-flags";
import {mayLeaderReview,differences,lowRiskReviewStage,type ReviewStage} from "../optionc-approval-ui";

const label:Record<string,string>={LocationName:"地點名稱",City:"縣市",District:"鄉鎮區",Address:"地址",PlusCode:"Plus Code",TaxId:"統一編號",MasterNote:"主檔備註"};

type Review={requestPublicId:string;entityId:string;teamId:number|null;operationCode:string;riskCode:string;requestStatus:string;leaderStatus:string;requestedByUserId:number;submittedAt:string;beforeJson:string|null;proposedJson:string;rowVersion:string;reason?:string};

export default function LeaderLocationReviewPage(){
 const {user}=useAuth();
 const [rows,setRows]=useState<Review[]>([]),[selected,setSelected]=useState(""),[reason,setReason]=useState(""),[busy,setBusy]=useState(false),[message,setMessage]=useState(""),[error,setError]=useState("");
 const [stage,setStage]=useState<ReviewStage|null>(null);
 const load=useCallback(async()=>{if(!OPTIONC_LEADER_REVIEW_UI_ENABLED)return;setRows(await api<Review[]>("/optionc-candidate/leader/review-queue"));},[]);
 useEffect(()=>{if(OPTIONC_LEADER_REVIEW_UI_ENABLED)void load().catch(e=>setError(String(e)));},[load]);
 if(!OPTIONC_LEADER_REVIEW_UI_ENABLED)return <div role="status">地點初審尚未授權啟用。</div>;
 if(!user?.roles.some(role=>role.toLowerCase()==="leader"))return <div role="alert">沒有有效的小組長權限。</div>;
 const row=rows.find(x=>x.requestPublicId===selected);
 const eligible=!!row&&mayLeaderReview(row,user.userId);
 const low=lowRiskReviewStage(row,stage,user.userId);
 const isContinuation=!!row&&row.riskCode==="Low"&&row.operationCode==="UpdatePublishedLocation"&&row.leaderStatus==="Approved"&&row.requestStatus==="Pending"&&row.requestedByUserId!==user.userId;
 const loadStage=async(id:string)=>{setStage(null);setStage(await api<ReviewStage>(`/optionc-candidate/requests/${encodeURIComponent(id)}/stage`));};
 const decide=async(approve:boolean)=>{
  if(!row||!eligible||busy||!reason.trim()||reason.trim().length>1000)return;
  setError("");setMessage("");setBusy(true);
  try{
   await api(`/optionc-candidate/requests/${encodeURIComponent(row.requestPublicId)}/leader-review`,{
    method:"POST",body:JSON.stringify({requestRowVersion:row.rowVersion,
     decisionKey:crypto.randomUUID(),reason:reason.trim(),approve})
   });
   setMessage(approve?(row.riskCode==="Low"?"初審完成，請完成重複檢查後發布。":"獨立小組長初審完成；高風險案件仍需 Admin 複審。"):"已拒絕申請，未發布地點。");
   setSelected("");setStage(null);setReason("");await load();
  }catch(e){setError(`${String(e)}；若結果不明，請重新查詢，不要直接重送。`);}
  finally{setBusy(false);}
 };
 const continueLow=async(action:"duplicate"|"publish")=>{
  if(!row||!low||busy||!reason.trim()||reason.trim().length>1000)return;
  if(action==="duplicate"&&low.stageStatus!=="PendingDuplicateReview")return;
  if(action==="publish"&&(low.stageStatus!=="ReadyForPublish"||low.duplicateReviewStatus!=="Cleared"))return;
  setBusy(true);setError("");setMessage("");
  try{
   const base=`/optionc-candidate/requests/${encodeURIComponent(row.requestPublicId)}`;
   if(action==="duplicate"){
    setStage(await api<ReviewStage>(base+"/duplicate-evidence",{method:"POST",body:JSON.stringify({stageRowVersion:low.stageRowVersion,cleared:true,reason:reason.trim(),evidenceKey:crypto.randomUUID()})}));
    setMessage("重複地點檢查已記錄，尚未發布。");
   }else{
    await api(base+"/publish-reviewed",{method:"POST",body:JSON.stringify({requestRowVersion:row.rowVersion,decisionKey:crypto.randomUUID(),reason:reason.trim()})});
    setMessage("地點異動已核准發布。");setSelected("");setStage(null);setReason("");await load();
   }
  }catch(e){setStage(null);setError(String(e)+"；請重新查詢狀態，勿直接重送。");}
  finally{setBusy(false)}
 };
 return <div className="card"><h2>地點異動審核</h2><p>只有有效任命／代理且非申請人的小組長可以審核；以伺服器即時權限為準。</p>
  {message&&<div role="status" className="note ok-note">{message}</div>}
  {error&&<div role="alert" className="note danger-note">{error}</div>}
  <button className="btn secondary" disabled={busy} onClick={()=>void load().catch(e=>setError(String(e)))}>重新整理</button>
  <div className="table-wrap"><table><thead><tr><th>地點 ID</th><th>申請者</th><th>風險</th><th>初審</th><th>檢視</th></tr></thead><tbody>{rows.map(r=><tr key={r.requestPublicId}><td>{r.entityId}</td><td>{r.requestedByUserId}</td><td>{r.riskCode}</td><td>{r.leaderStatus}</td><td><button className="btn small secondary" disabled={busy} onClick={()=>{setSelected(r.requestPublicId);setStage(null);setReason("");setError("");if(r.riskCode==="Low"&&r.leaderStatus==="Approved")void loadStage(r.requestPublicId).catch(e=>setError(String(e)));}}>查看</button></td></tr>)}</tbody></table></div>
  {row&&<section><h3>申請內容</h3><div className="table-wrap"><table><thead><tr><th>欄位</th><th>原資料</th><th>申請資料</th></tr></thead><tbody>{differences(row).map(d=><tr key={d.field}><th>{label[d.field]}</th><td>{d.before||"—"}</td><td>{d.after||"—"}</td></tr>)}</tbody></table></div>
   {!eligible&&!isContinuation&&<div role="alert">申請人不得自審，或申請狀態已變更。</div>}
   <label>初審理由</label><textarea value={reason} maxLength={1000} disabled={(!eligible&&!isContinuation)||busy} onChange={e=>setReason(e.target.value)}/>
   {!isContinuation&&<div className="actions"><button className="btn secondary" disabled={!eligible||busy||!reason.trim()} onClick={()=>void decide(false)}>拒絕</button><button className="btn ok" disabled={!eligible||busy||!reason.trim()} onClick={()=>void decide(true)}>獨立初審通過</button></div>}
   {isContinuation&&<><p>低風險異動：完成重複地點確認後，由原初審小組長發布，不需管理者複審。</p><button className="btn small secondary" disabled={busy} onClick={()=>void loadStage(row.requestPublicId).catch(e=>setError(String(e)))}>重新讀取審核狀態</button><div className="actions"><button className="btn secondary" disabled={busy||!reason.trim()||low?.stageStatus!=="PendingDuplicateReview"} onClick={()=>void continueLow("duplicate")}>確認非重複地點</button><button className="btn ok" disabled={busy||!reason.trim()||low?.stageStatus!=="ReadyForPublish"||low?.duplicateReviewStatus!=="Cleared"} onClick={()=>void continueLow("publish")}>核准並發布</button></div></>}</section>}
 </div>;
}
