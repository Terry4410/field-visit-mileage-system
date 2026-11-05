import {useEffect,useState} from "react";
import {api} from "../api";
import {useAuth} from "../auth";
import type {ManagedLocation} from "../types";

type Form={teamId:string;locationName:string;city:string;district:string;address:string;plusCode:string;taxId:string;masterNote:string};
const empty:Form={teamId:"",locationName:"",city:"",district:"",address:"",plusCode:"",taxId:"",masterNote:""};
// R9's eight-field Customer draft subset; no Option C 011–020 routes/flags.
export default function LeaderDraftLocationPage(){
 const {user}=useAuth();const [form,setForm]=useState<Form>({...empty});
 const [rows,setRows]=useState<ManagedLocation[]>([]),[edit,setEdit]=useState<ManagedLocation|null>(null);
 const [busy,setBusy]=useState(false),[message,setMessage]=useState(""),[error,setError]=useState("");
 const teams=user?.teamScopes?.length?user.teamScopes:user?.teamId?[{teamId:user.teamId,teamName:user.teamName||String(user.teamId),isPrimary:true}]:[];
 const load=async()=>setRows(await api<ManagedLocation[]>("/managed-locations?includeInactive=true"));
 useEffect(()=>{void load().catch(e=>setError(String(e)))},[]);
 const set=(key:keyof Form,value:string)=>setForm(f=>({...f,[key]:value}));
 const reset=()=>{setEdit(null);setForm({...empty})};
 const eligible=(row:ManagedLocation)=>row.locationType==="Customer"&&!row.isTemporary&&!row.isActive&&row.approvalStatus==="Pending"&&!row.duplicateOfLocationId&&teams.some(t=>t.teamId===row.teamId);
 const open=(row:ManagedLocation)=>{if(!eligible(row))return;setEdit(row);setForm({teamId:String(row.teamId||""),locationName:row.locationName,city:row.city||"",district:row.district||"",address:row.address||"",plusCode:row.plusCode||"",taxId:row.taxId||"",masterNote:row.masterNote||""});setError("");setMessage("")};
 const save=async()=>{if(!form.teamId||!form.locationName.trim()||(!form.address.trim()&&!form.plusCode.trim())){setError("請選擇小組、輸入地點名稱與地址或 Plus Code。");return}if(edit&&!edit.rowVersion){setError("缺少 RowVersion，請重新整理。");return}setBusy(true);setError("");setMessage("");try{await api(edit?`/managed-locations/${edit.locationId}`:"/managed-locations",{method:edit?"PUT":"POST",body:JSON.stringify({...form,teamId:Number(form.teamId),locationType:"Customer",isActive:false,rowVersion:edit?.rowVersion||null})});reset();setMessage("已儲存 Pending／未啟用 Customer 草稿；尚未核准或發布。");await load()}catch(e){setError(`${String(e)}；結果不明時請重新整理確認，不要直接重送。`)}finally{setBusy(false)}};
 return <div className="card"><h2>小組長地點草稿維護</h2><div className="note">本波為部分 Leader 功能：新增及授權小組草稿維護。已發布地點、LowRisk、HighRisk、Excel 送審及合併尚未開放。草稿不得用於正式行程／里程。</div>
 <div className="grid cols-2"><div className="field"><label>所屬小組</label><select aria-label="所屬小組" disabled={busy||!!edit} value={form.teamId} onChange={e=>set("teamId",e.target.value)}><option value="">請選擇</option>{teams.map(t=><option key={t.teamId} value={t.teamId}>{t.teamName}</option>)}</select></div>{([["locationName","地點名稱"],["city","縣市"],["district","鄉鎮區"],["address","地址（或 Plus Code）"],["plusCode","Plus Code"],["taxId","統一編號（選填）"],["masterNote","主檔備註（選填）"]] as [keyof Form,string][]).map(([key,label])=><div className="field" key={key}><label>{label}</label>{key==="masterNote"?<textarea aria-label={label} maxLength={1000} disabled={busy} value={form[key]} onChange={e=>set(key,e.target.value)}/>:<input aria-label={label} maxLength={key==="taxId"?20:undefined} disabled={busy} value={form[key]} onChange={e=>set(key,e.target.value)}/>}</div>)}</div>
 <div className="actions"><button className="btn ok" disabled={busy||!teams.length} onClick={()=>void save()}>{edit?"儲存草稿修改（待審核）":"新增地點（待審核）"}</button><button className="btn secondary" disabled={busy} onClick={reset}>取消修改</button><button className="btn secondary" disabled={busy} onClick={()=>void load().catch(e=>setError(String(e)))}>重新整理</button></div>{message&&<div role="status" className="note ok-note">{message}</div>}{error&&<div role="alert" className="note danger-note">{error}</div>}
 <div className="table-wrap"><table><thead><tr><th>地點</th><th>小組</th><th>地址</th><th>狀態</th><th>操作</th></tr></thead><tbody>{rows.map(r=><tr key={r.locationId}><td>{r.locationName}</td><td>{r.teamName}</td><td>{r.address||r.plusCode}</td><td>{r.approvalStatus}／{r.isActive?"啟用":"未啟用"}</td><td>{eligible(r)?<button className="btn small outline" disabled={busy} onClick={()=>open(r)}>修改草稿</button>:"此波不可修改"}</td></tr>)}</tbody></table></div></div>;
}
