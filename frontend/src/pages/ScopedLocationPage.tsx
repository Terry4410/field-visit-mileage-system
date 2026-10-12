import {useEffect,useState} from "react";
import {api} from "../api";
import {useAuth} from "../auth";
import type {ManagedLocation} from "../types";
import {mayEditVisitorDraft} from "../location-ownership-ui";

type Form={teamId:string;locationName:string;city:string;district:string;address:string;plusCode:string;taxId:string;masterNote:string};
const empty=(teamId=""):Form=>({teamId,locationName:"",city:"",district:"",address:"",plusCode:"",taxId:"",masterNote:""});

export default function ScopedLocationPage(){
 const {user}=useAuth();
 const [rows,setRows]=useState<ManagedLocation[]>([]),[edit,setEdit]=useState<ManagedLocation|null>(null),[query,setQuery]=useState("");
 const [teams,setTeams]=useState<{teamId:number;teamName:string;isPrimary:boolean}[]>([]);
 const [form,setForm]=useState<Form>(()=>empty());
 const [busy,setBusy]=useState(false),[error,setError]=useState(""),[message,setMessage]=useState("");
 const load=async()=>setRows(await api<ManagedLocation[]>("/managed-locations?includeInactive=true"));
 useEffect(()=>{void Promise.all([load(),api<{teamId:number;teamName:string;isPrimary:boolean}[]>("/managed-locations/visitor-teams")]).then(([,allowed])=>setTeams(allowed)).catch(e=>setError(e instanceof Error?e.message:"讀取地點或小組失敗"));},[]);
 const set=(key:keyof Form,value:string)=>setForm(x=>({...x,[key]:value}));
 const reset=()=>{setEdit(null);setForm(empty());};
 const open=(row:ManagedLocation)=>{if(!user||!mayEditVisitorDraft(row,user.userId))return;setEdit(row);setForm({teamId:String(row.teamId||""),locationName:row.locationName,city:row.city||"",district:row.district||"",address:row.address||"",plusCode:row.plusCode||"",taxId:row.taxId||"",masterNote:row.masterNote||""});};
 const save=async()=>{
  if(!form.teamId||!form.locationName.trim()||(!form.address.trim()&&!form.plusCode.trim())){setError("請選擇小組、輸入地點名稱與地址或 Plus Code。");return;}
  setBusy(true);setError("");setMessage("");
  try{
   await api(edit?`/managed-locations/${edit.locationId}`:"/managed-locations",{method:edit?"PUT":"POST",body:JSON.stringify({teamId:Number(form.teamId),locationName:form.locationName.trim(),locationType:"Customer",city:form.city.trim()||null,district:form.district.trim()||null,address:form.address.trim()||null,plusCode:form.plusCode.trim()||null,taxId:form.taxId.trim()||null,masterNote:form.masterNote.trim()||null,isActive:false,rowVersion:edit?.rowVersion||null})});
   setMessage("已儲存 Pending／未啟用的 Customer 草稿；這不代表正式發布。");reset();await load();
  }catch(e){setError(e instanceof Error?e.message:"儲存失敗");}finally{setBusy(false);}
 };
 return <div className="card"><div className="section-title"><div><h2>地點管理</h2><div className="sub">Visitor 僅能建立並維護本人、所屬有效小組內的 Pending／未啟用 Customer 草稿。</div></div></div>
  <div className="note">正式主檔、共用／官方地點、啟用或發布，均不可由 Visitor 直接修改；發布須另行管理者流程。</div>
  <div className="grid cols-2"><div className="field"><label>所屬小組</label><select value={form.teamId} disabled={busy||!!edit} onChange={e=>set("teamId",e.target.value)}><option value="">請選擇</option>{teams.map(t=><option key={t.teamId} value={t.teamId}>{t.teamName}</option>)}</select></div><div className="field"><label>地點名稱</label><input value={form.locationName} onChange={e=>set("locationName",e.target.value)}/></div><div className="field"><label>縣市</label><input value={form.city} onChange={e=>set("city",e.target.value)}/></div><div className="field"><label>鄉鎮區</label><input value={form.district} onChange={e=>set("district",e.target.value)}/></div><div className="field span-2"><label>地址（或 Plus Code）</label><input value={form.address} onChange={e=>set("address",e.target.value)}/></div><div className="field"><label>Plus Code</label><input value={form.plusCode} onChange={e=>set("plusCode",e.target.value)}/></div><div className="field"><label>統一編號（選填）</label><input maxLength={20} value={form.taxId} onChange={e=>set("taxId",e.target.value)}/></div><div className="field span-2"><label>主檔備註（選填）</label><textarea maxLength={1000} value={form.masterNote} onChange={e=>set("masterNote",e.target.value)}/></div></div>
  <div className="actions"><button className="btn ok" disabled={busy||!teams.length} onClick={()=>void save()}>{busy?"處理中…":edit?"儲存草稿修改":"新增 Pending 草稿"}</button>{edit&&<button className="btn secondary" disabled={busy} onClick={reset}>取消</button>}</div>{message&&<div role="status" className="note ok-note">{message}</div>}{error&&<div role="alert" className="note danger-note">{error}</div>}
  <hr/><h3>可查詢的授權地點</h3><input aria-label="搜尋地點" placeholder="搜尋名稱、地址或代碼" value={query} onChange={e=>setQuery(e.target.value)}/><div className="table-wrap"><table><thead><tr><th>代碼</th><th>地點</th><th>小組</th><th>狀態</th><th>操作</th></tr></thead><tbody>{rows.filter(x=>`${x.locationCode} ${x.locationName} ${x.address||""}`.toLowerCase().includes(query.toLowerCase())).map(x=><tr key={x.locationId}><td>{x.locationCode}</td><td>{x.locationName}</td><td>{x.teamName||"—"}</td><td>{x.approvalStatus}／{x.isActive?"啟用":"未啟用"}</td><td><button className="btn small secondary" disabled={busy||!user||!mayEditVisitorDraft(x,user.userId)} onClick={()=>open(x)}>修改本人草稿</button></td></tr>)}</tbody></table></div>{!rows.length&&<div className="empty compact-empty">目前沒有符合權限的地點。</div>}
 </div>;
}
