import{useCallback,useEffect,useState}from"react";
import{api}from"../api";
import{useAuth}from"../auth";
import{OPTIONC_APPROVAL_UI_ENABLED}from"../optionc-approval-flags";
import{differences,mayReview,reviewPayload,statusLabel,type OptionCRequest}from"../optionc-approval-ui";
const label:Record<string,string>={LocationName:"地點名稱",City:"縣市",District:"區",Address:"地址",PlusCode:"Plus Code",TaxId:"統編",MasterNote:"備註"};
export default function LocationApprovalQueuePage(){
 const{user}=useAuth(),[rows,setRows]=useState<OptionCRequest[]>([]),[selected,setSelected]=useState(""),[reason,setReason]=useState(""),[error,setError]=useState(""),[busy,setBusy]=useState(false);
 const load=useCallback(async()=>{if(OPTIONC_APPROVAL_UI_ENABLED)setRows(await api<OptionCRequest[]>("/optionc-candidate/requests/queue"));},[]);
 useEffect(()=>{if(OPTIONC_APPROVAL_UI_ENABLED)void load().catch(e=>setError(String(e)));},[load]);
 if(!OPTIONC_APPROVAL_UI_ENABLED)return <div role="status">地點變更核准尚未授權啟用。</div>;
 if(!user?.roles.some(x=>x.toLowerCase()==="admin"))return <div role="alert">沒有管理員權限。</div>;
 const current=rows.find(x=>x.requestPublicId===selected);
 const eligible=!!current&&mayReview(current,user.userId);
 const decide=async(action:"approve"|"reject")=>{
  if(!current||!eligible||busy)return;
  let payload;try{payload=reviewPayload(current,reason,crypto.randomUUID());}catch(e){setError(String(e));return;}
  setBusy(true);setError("");
  try{await api(`/optionc-candidate/requests/${encodeURIComponent(current.requestPublicId)}/${action}`,{method:"POST",body:JSON.stringify(payload)});
   setSelected("");setReason("");await load();
  }catch(e){setError(String(e)+"；若結果不明，請查核事件紀錄，切勿直接重送。");}finally{setBusy(false);}
 };
 return <div className="card"><h2>地點變更獨立核准</h2><p>僅顯示待審申請；不可核准本人申請。</p>
 {error&&<div role="alert" className="note danger-note">{error}</div>}
 <button className="btn secondary" disabled={busy} onClick={()=>void load().catch(e=>setError(String(e)))}>重新整理</button>
 <div className="table-wrap"><table><thead><tr><th>地點</th><th>申請者</th><th>狀態</th><th>操作</th></tr></thead><tbody>{rows.map(r=><tr key={r.requestPublicId}><td>{r.entityId}</td><td>{r.requestedByUserId}</td><td>{statusLabel(r.status)}</td><td><button className="btn small secondary" onClick={()=>setSelected(r.requestPublicId)}>查看差異</button></td></tr>)}</tbody></table></div>
 {current&&<section><h3>原資料／申請資料</h3><div className="table-wrap"><table><thead><tr><th>欄位</th><th>原值</th><th>申請值</th></tr></thead><tbody>{differences(current).map(d=><tr key={d.field}><td>{label[d.field]}</td><td>{d.before||"—"}</td><td>{d.after||"—"}</td></tr>)}</tbody></table></div>
 {!eligible&&<div role="alert">本人申請或狀態不符，禁止審核。</div>}
 <label>審核理由</label><textarea value={reason} maxLength={1000} disabled={busy||!eligible} onChange={e=>setReason(e.target.value)}/>
 <div className="actions"><button className="btn secondary" disabled={busy||!eligible||!reason.trim()} onClick={()=>void decide("reject")}>退回</button><button className="btn ok" disabled={busy||!eligible||!reason.trim()} onClick={()=>void decide("approve")}>核准</button></div></section>}
 </div>;
}
