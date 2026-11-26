import {useCallback,useEffect,useState} from "react";
import {api} from "../api";
import {useAuth} from "../auth";
import {OPTIONC_LEADER_REVIEW_UI_ENABLED} from "../optionc-approval-flags";
import {mayLeaderReview,differences,lowRiskReviewStage,safeFields,type ReviewStage} from "../optionc-approval-ui";

const label:Record<string,string>={LocationName:"地點名稱",City:"縣市",District:"鄉鎮區",Address:"地址",PlusCode:"Plus Code",TaxId:"統一編號",MasterNote:"主檔備註"};

type Review={requestPublicId:string;entityId:string;teamId:number|null;operationCode:string;riskCode:string;requestStatus:string;leaderStatus:string;requestedByUserId:number;applicantName?:string|null;submittedAt:string;beforeJson:string|null;proposedJson:string;rowVersion:string;reason?:string};
const locationName=(r:Review)=>safeFields(r.proposedJson).LocationName||safeFields(r.beforeJson).LocationName||"地點資料";
const operation=(r:Review)=>r.operationCode==="CreateLocation"?"新增地點":"正式地點異動";

export default function LeaderLocationReviewPage(){
 const {user}=useAuth();
 const [rows,setRows]=useState<Review[]>([]),[selected,setSelected]=useState(""),[reason,setReason]=useState(""),[busy,setBusy]=useState(false),[message,setMessage]=useState(""),[error,setError]=useState("");
 const [stage,setStage]=useState<ReviewStage|null>(null);
 const [query,setQuery]=useState(""),[typeFilter,setTypeFilter]=useState("all");
 const [start,setStart]=useState(""),[end,setEnd]=useState(""),[teamFilter,setTeamFilter]=useState("");
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
  if(!row||!eligible||busy||!reason.trim()||reason.trim().length>100)return;
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
  if(!row||!low||busy||!reason.trim()||reason.trim().length>100)return;
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
  <div className="grid cols-3"><label>申請日期起日<input type="date" value={start} onChange={e=>setStart(e.target.value)}/></label><label>申請日期迄日<input type="date" value={end} onChange={e=>setEnd(e.target.value)}/></label><label>小組<select value={teamFilter} onChange={e=>setTeamFilter(e.target.value)}><option value="">全部授權小組</option>{[...new Set(rows.map(r=>r.teamId).filter(id=>id!=null))].map(id=><option key={id} value={id!}>{user.teamScopes?.find(t=>t.teamId===id)?.teamName||`授權小組 ${id}`}</option>)}</select></label></div>{start&&end&&start>end&&<div role="alert" className="note danger-note">迄日不可早於起日。</div>}
  <div className="actions"><input aria-label="搜尋待審地點" placeholder="搜尋地點或申請人" value={query} onChange={e=>setQuery(e.target.value)}/><select aria-label="申請類型" value={typeFilter} onChange={e=>setTypeFilter(e.target.value)}><option value="all">全部</option><option value="CreateLocation">新增地點</option><option value="UpdatePublishedLocation">地點異動</option></select></div>
  <div className="table-wrap"><table><thead><tr><th>地點名稱</th><th>申請者</th><th>日期</th><th>類型</th><th>小組</th><th>審核階段</th><th>操作</th></tr></thead><tbody>{rows.filter(r=>{const day=new Date(r.submittedAt).toLocaleDateString("sv-SE",{timeZone:"Asia/Taipei"});return (!start||!end||start<=end)&&(!start||day>=start)&&(!end||day<=end)&&(!teamFilter||String(r.teamId)===teamFilter)&&(typeFilter==="all"||r.operationCode===typeFilter)&&`${locationName(r)} ${r.applicantName||""}`.toLowerCase().includes(query.toLowerCase())}).map(r=><tr key={r.requestPublicId}><td>{locationName(r)}</td><td>{r.applicantName||"申請人資料待查"}</td><td>{new Date(r.submittedAt).toLocaleDateString("zh-TW",{timeZone:"Asia/Taipei"})}</td><td>{operation(r)}</td><td>{user.teamScopes?.find(t=>t.teamId===r.teamId)?.teamName||"授權小組"}</td><td>{r.leaderStatus==="Approved"?"初審通過，待後續發布":"待初審"}／{r.riskCode==="High"?"需管理者複審":"一般異動"}</td><td><button className="btn small secondary" disabled={busy} onClick={()=>{setSelected(r.requestPublicId);setStage(null);setReason("");setError("");if(r.riskCode==="Low"&&r.leaderStatus==="Approved")void loadStage(r.requestPublicId).catch(e=>setError(String(e)));}}>查看</button></td></tr>)}</tbody></table></div>
  {row&&<section><h3>申請內容</h3><div className="table-wrap"><table><thead><tr><th>欄位</th><th>原資料</th><th>申請資料</th></tr></thead><tbody>{differences(row).map(d=><tr key={d.field}><th>{label[d.field]}</th><td>{d.before||"—"}</td><td>{d.after||"—"}</td></tr>)}</tbody></table></div>
   {!eligible&&!isContinuation&&<div role="alert">申請人不得自審，或申請狀態已變更。</div>}
   <div className="field"><label htmlFor="leader-location-review-reason">{isContinuation?"審核及確認理由":"審核意見"}（必填）</label><textarea id="leader-location-review-reason" rows={5} value={reason} maxLength={100} disabled={(!eligible&&!isContinuation)||busy} onChange={e=>setReason(e.target.value)}/><div className="hint">{reason.length} / 100 字</div></div>
   {!isContinuation&&<div className="actions"><button className="btn secondary" disabled={!eligible||busy||!reason.trim()} onClick={()=>void decide(false)}>拒絕</button><button className="btn ok" disabled={!eligible||busy||!reason.trim()} onClick={()=>void decide(true)}>初審通過</button></div>}
   {isContinuation&&<><p>低風險異動：完成重複地點確認後，由原初審小組長發布，不需管理者複審。</p><button className="btn small secondary" disabled={busy} onClick={()=>void loadStage(row.requestPublicId).catch(e=>setError(String(e)))}>重新讀取審核狀態</button><div className="actions"><button className="btn secondary" disabled={busy||!reason.trim()||low?.stageStatus!=="PendingDuplicateReview"} onClick={()=>void continueLow("duplicate")}>確認非重複地點</button><button className="btn ok" disabled={busy||!reason.trim()||low?.stageStatus!=="ReadyForPublish"||low?.duplicateReviewStatus!=="Cleared"} onClick={()=>void continueLow("publish")}>核准並發布</button></div></>}</section>}
 </div>;
}
