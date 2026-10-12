import{useEffect,useState}from"react";
import{Link}from"react-router-dom";
import{api}from"../api";
import{useAuth}from"../auth";
import type{ManagedLocation}from"../types";
import{mayEditScopedDraft}from"../location-ownership-ui";
type Role="visitor"|"leader";
type Form={teamId:string;locationName:string;city:string;district:string;address:string;plusCode:string;taxId:string;masterNote:string};
const empty=(teamId=""):Form=>({teamId,locationName:"",city:"",district:"",address:"",plusCode:"",taxId:"",masterNote:""});
export default function ScopedLocationPage({role}:{role:Role}){
 const{user}=useAuth();
 const[leaderScopes,setLeaderScopes]=useState<{teamId:number;teamName:string;isPrimary:boolean}[]>([]);
 const leaderGrantPending=role==="leader"&&!leaderScopes.length;
 const scopes=role==="leader"?leaderScopes:(user?.teamScopes?.length?user.teamScopes:user?.teamId?[{teamId:user.teamId,teamName:user.teamName||"所屬小組",isPrimary:true}]:[]);
 const ids=scopes.map(x=>x.teamId);
 const[rows,setRows]=useState<ManagedLocation[]>([]),[edit,setEdit]=useState<ManagedLocation|null>(null);
 const[form,setForm]=useState<Form>(()=>empty(user?.teamId?String(user.teamId):""));
 const[busy,setBusy]=useState(false),[error,setError]=useState(""),[msg,setMsg]=useState("");
 const load=async()=>setRows(await api<ManagedLocation[]>("/managed-locations?includeInactive=true"));
 useEffect(()=>{void load().catch(e=>setError(e instanceof Error?e.message:"讀取地點失敗"));if(role==="leader")void api<{teamId:number;teamName:string;isPrimary:boolean}[]>("/managed-locations/leader-authorization").then(setLeaderScopes).catch(()=>setLeaderScopes([]))},[role]);
 const change=(field:keyof Form,value:string)=>setForm(f=>({...f,[field]:value}));
 const reset=()=>{setEdit(null);setForm(empty(user?.teamId?String(user.teamId):""))};
 const open=(x:ManagedLocation)=>{
  if(!user||!mayEditScopedDraft(x,role,user.userId,ids))return;
  setEdit(x);setForm({teamId:String(x.teamId||""),locationName:x.locationName,city:x.city||"",district:x.district||"",address:x.address||"",plusCode:x.plusCode||"",taxId:x.taxId||"",masterNote:x.masterNote||""});
 };
 const save=async()=>{
  if(!form.teamId||!form.locationName.trim()||(!form.address.trim()&&!form.plusCode.trim())){setError("請選擇小組、輸入地點名稱與地址或 Plus Code。");return}
  setBusy(true);setError("");setMsg("");
  try{
   const data={teamId:Number(form.teamId),locationName:form.locationName.trim(),locationType:"Customer",city:form.city.trim()||null,district:form.district.trim()||null,address:form.address.trim()||null,plusCode:form.plusCode.trim()||null,taxId:form.taxId.trim()||null,masterNote:form.masterNote.trim()||null,isActive:false,rowVersion:edit?.rowVersion||null};
   await api(edit?`/managed-locations/${edit.locationId}`:"/managed-locations",{method:edit?"PUT":"POST",body:JSON.stringify(data)});
   setMsg("已儲存待審核地點草稿；尚未正式啟用，發布須管理者核准。");reset();await load();
  }catch(e){setError(e instanceof Error?e.message:"儲存失敗")}finally{setBusy(false)}
 };
 return <div className="card">
  <div className="section-title"><div><h2>{role==="visitor"?"我的地點":"小組地點"}｜新增與維護</h2><div className="sub">外訪員僅限本人建立；小組長須有獨立核定的有效管理授權。正式主檔及共用地點不可直接修改。</div></div>{role==="leader"&&<Link className="btn small outline" to="/leader/locations">返回小組地點清單</Link>}</div>
  <div className="note">{leaderGrantPending?"小組長管理授權（VAL-B1-002）目前沒有有效、管理員核定的任命或代理；不可新增、修改或處理同組地點。若同時具外訪員角色，請切換為外訪員後僅維護本人草稿。":"已驗證有效小組長授權：可在核定小組直接儲存低風險待審核草稿；任何正式發布／高風險異動仍需管理者核准。"}</div>
  <div className="grid cols-2">
   <div className="field"><label>所屬小組</label><select value={form.teamId} disabled={busy||!!edit} onChange={e=>change("teamId",e.target.value)}><option value="">請選擇</option>{scopes.map(t=><option key={t.teamId} value={t.teamId}>{t.teamName}</option>)}</select></div>
   <div className="field"><label>地點名稱</label><input value={form.locationName} onChange={e=>change("locationName",e.target.value)}/></div>
   <div className="field"><label>縣市</label><input value={form.city} onChange={e=>change("city",e.target.value)}/></div>
   <div className="field"><label>鄉鎮區</label><input value={form.district} onChange={e=>change("district",e.target.value)}/></div>
   <div className="field span-2"><label>地址（或 Plus Code）</label><input value={form.address} onChange={e=>change("address",e.target.value)}/></div>
   <div className="field"><label>Plus Code</label><input value={form.plusCode} onChange={e=>change("plusCode",e.target.value)}/></div>
   <div className="field"><label>統一編號（選填）</label><input value={form.taxId} maxLength={20} onChange={e=>change("taxId",e.target.value)}/></div>
   <div className="field span-2"><label>主檔備註（選填）</label><textarea value={form.masterNote} maxLength={1000} onChange={e=>change("masterNote",e.target.value)}/></div>
  </div>
  <div className="actions"><button className="btn ok" disabled={busy||!ids.length||leaderGrantPending} title={leaderGrantPending?"等待 VAL-B1-002 獨立核定":""} onClick={()=>void save()}>{busy?"處理中…":edit?"儲存草稿修改":"新增待審核地點"}</button>{edit&&<button className="btn secondary" disabled={busy} onClick={reset}>取消</button>}</div>
  {msg&&<div role="status" className="note ok-note">{msg}</div>}{error&&<div role="alert" className="note danger-note">{error}</div>}
  <hr/><h3>可查詢的授權地點</h3>
  <div className="table-wrap"><table><thead><tr><th>代碼</th><th>地點</th><th>小組</th><th>狀態</th><th>操作</th></tr></thead><tbody>{rows.map(x=><tr key={x.locationId}><td>{x.locationCode}</td><td>{x.locationName}</td><td>{x.teamName||"—"}</td><td>{x.approvalStatus}/{x.geocodingStatus}</td><td><button className="btn small secondary" disabled={busy||leaderGrantPending||!user||!mayEditScopedDraft(x,role,user.userId,ids)} title={leaderGrantPending?"等待 VAL-B1-002 獨立核定":""} onClick={()=>open(x)}>修改草稿</button></td></tr>)}</tbody></table></div>
  {!rows.length&&<div className="empty compact-empty">目前沒有符合權限的地點。</div>}
 </div>;
}
