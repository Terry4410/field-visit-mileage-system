import {useCallback,useEffect,useState} from "react";
import {api} from "../api";
import {useAuth} from "../auth";
import {OPTIONC_LEADER_REVIEW_UI_ENABLED} from "../optionc-approval-flags";
import {mayLeaderReview,safeFields} from "../optionc-approval-ui";

type Review={requestPublicId:string;entityId:string;teamId:number|null;operationCode:string;riskCode:string;requestStatus:string;leaderStatus:string;requestedByUserId:number;submittedAt:string;proposedJson:string;rowVersion:string;reason?:string};

export default function LeaderLocationReviewPage(){
 const {user}=useAuth();
 const [rows,setRows]=useState<Review[]>([]),[selected,setSelected]=useState(""),[reason,setReason]=useState(""),[busy,setBusy]=useState(false),[message,setMessage]=useState(""),[error,setError]=useState("");
 const load=useCallback(async()=>{if(!OPTIONC_LEADER_REVIEW_UI_ENABLED)return;setRows(await api<Review[]>("/optionc-candidate/leader/review-queue"));},[]);
 useEffect(()=>{if(OPTIONC_LEADER_REVIEW_UI_ENABLED)void load().catch(e=>setError(String(e)));},[load]);
 if(!OPTIONC_LEADER_REVIEW_UI_ENABLED)return <div role="status">地點初審尚未授權啟用。</div>;
 if(!user?.roles.some(role=>role.toLowerCase()==="leader"))return <div role="alert">沒有有效的小組長權限。</div>;
 const row=rows.find(x=>x.requestPublicId===selected);
 const eligible=!!row&&mayLeaderReview(row,user.userId);
 const decide=async(approve:boolean)=>{
  if(!row||!eligible||busy||!reason.trim()||reason.trim().length>1000)return;
  setError("");setMessage("");setBusy(true);
  try{
   await api(`/optionc-candidate/requests/${encodeURIComponent(row.requestPublicId)}/leader-review`,{
    method:"POST",body:JSON.stringify({requestRowVersion:row.rowVersion,
     decisionKey:crypto.randomUUID(),reason:reason.trim(),approve})
   });
   setMessage(approve?"獨立小組長初審完成；高風險案件仍需 Admin 複審。":"已拒絕申請，未發布地點。");
   setSelected("");setReason("");await load();
  }catch(e){setError(`${String(e)}；若結果不明，請重新查詢，不要直接重送。`);}
  finally{setBusy(false);}
 };
 return <div className="card"><h2>地點變更獨立初審</h2><p>只有有效任命／代理且非申請人的小組長可以審核；以伺服器即時權限為準。</p>
  {message&&<div role="status" className="note ok-note">{message}</div>}
  {error&&<div role="alert" className="note danger-note">{error}</div>}
  <button className="btn secondary" disabled={busy} onClick={()=>void load().catch(e=>setError(String(e)))}>重新整理</button>
  <div className="table-wrap"><table><thead><tr><th>地點 ID</th><th>申請者</th><th>風險</th><th>初審</th><th>檢視</th></tr></thead><tbody>{rows.map(r=><tr key={r.requestPublicId}><td>{r.entityId}</td><td>{r.requestedByUserId}</td><td>{r.riskCode}</td><td>{r.leaderStatus}</td><td><button className="btn small secondary" disabled={busy} onClick={()=>setSelected(r.requestPublicId)}>查看</button></td></tr>)}</tbody></table></div>
  {row&&<section><h3>申請內容</h3><div className="table-wrap"><table><tbody>{Object.entries(safeFields(row.proposedJson)).map(([key,value])=><tr key={key}><th>{key}</th><td>{value||"—"}</td></tr>)}</tbody></table></div>
   {!eligible&&<div role="alert">申請人不得自審，或申請狀態已變更。</div>}
   <label>初審理由</label><textarea value={reason} maxLength={1000} disabled={!eligible||busy} onChange={e=>setReason(e.target.value)}/>
   <div className="actions"><button className="btn secondary" disabled={!eligible||busy||!reason.trim()} onClick={()=>void decide(false)}>拒絕</button><button className="btn ok" disabled={!eligible||busy||!reason.trim()} onClick={()=>void decide(true)}>獨立初審通過</button></div></section>}
 </div>;
}
