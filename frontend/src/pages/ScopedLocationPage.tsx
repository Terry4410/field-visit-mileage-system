import {useEffect,useState} from "react";
import {api} from "../api";
import {useAuth} from "../auth";
import type {ManagedLocation} from "../types";
import {leaderScopedTeamOptions,mayEditScopedDraft,mayMaintainLeaderPublishedLowRisk} from "../location-ownership-ui";
import {OPTIONC_CREATE_LOCATION_UI_ENABLED,OPTIONC_DIRECT_LOWRISK_UI_ENABLED} from "../optionc-approval-flags";
import LocationMaintenanceModal from "../components/LocationMaintenanceModal";
import {statusLabel,type OptionCRequest} from "../optionc-approval-ui";

export type ScopedLocationForm={teamId:string;locationName:string;city:string;district:string;address:string;plusCode:string;taxId:string;masterNote:string};
type Form=ScopedLocationForm;
const empty=(teamId=""):Form=>({teamId,locationName:"",city:"",district:"",address:"",plusCode:"",taxId:"",masterNote:""});

export function ScopedLocationFields({role,form,teams,busy,editing,onChange}:{role:"visitor"|"leader";form:Form;teams:{teamId:number;teamName:string;isPrimary:boolean}[];busy:boolean;editing:boolean;onChange:(key:keyof Form,value:string)=>void}){
 return <div className="grid cols-2" data-role={role}><div className="field"><label>所屬小組</label><select aria-label="所屬小組" value={form.teamId} disabled={busy||editing} onChange={e=>onChange("teamId",e.target.value)}><option value="">請選擇</option>{teams.map(t=><option key={t.teamId} value={t.teamId}>{t.teamName}</option>)}</select></div><div className="field"><label>地點名稱</label><input aria-label="地點名稱" value={form.locationName} onChange={e=>onChange("locationName",e.target.value)}/></div><div className="field"><label>縣市</label><input aria-label="縣市" value={form.city} onChange={e=>onChange("city",e.target.value)}/></div><div className="field"><label>鄉鎮區</label><input aria-label="鄉鎮區" value={form.district} onChange={e=>onChange("district",e.target.value)}/></div><div className="field span-2"><label>地址（或 Plus Code）</label><input aria-label="地址（或 Plus Code）" value={form.address} onChange={e=>onChange("address",e.target.value)}/></div><div className="field"><label>Plus Code</label><input aria-label="Plus Code" value={form.plusCode} onChange={e=>onChange("plusCode",e.target.value)}/></div><div className="field"><label>統一編號（選填）</label><input aria-label="統一編號（選填）" maxLength={20} value={form.taxId} onChange={e=>onChange("taxId",e.target.value)}/></div><div className="field span-2"><label>主檔備註（選填）</label><textarea aria-label="主檔備註（選填）" maxLength={1000} value={form.masterNote} onChange={e=>onChange("masterNote",e.target.value)}/></div></div>;
}

export default function ScopedLocationPage({role="visitor"}:{role?:"visitor"|"leader"}){
 const {user}=useAuth();
 const [rows,setRows]=useState<ManagedLocation[]>([]),[edit,setEdit]=useState<ManagedLocation|null>(null),[query,setQuery]=useState("");
 const [teams,setTeams]=useState<{teamId:number;teamName:string;isPrimary:boolean}[]>([]);
 const [form,setForm]=useState<Form>(()=>empty());
 const [busy,setBusy]=useState(false),[error,setError]=useState(""),[message,setMessage]=useState("");
 const [myRequests,setMyRequests]=useState<OptionCRequest[]>([]);
 const [lowRiskLocation,setLowRiskLocation]=useState<ManagedLocation|null>(null);
 const loadOwn=async()=>{if(OPTIONC_CREATE_LOCATION_UI_ENABLED)setMyRequests(await api<OptionCRequest[]>("/optionc-candidate/requests/mine"));};
 const load=async()=>setRows(await api<ManagedLocation[]>("/managed-locations?includeInactive=true"));
 useEffect(()=>{
  // Leader options come only from authenticated granted team scopes.
  // Visitor options continue to be validated by the server's effective-assignment endpoint.
  const allowed = role==="leader"
   ? Promise.resolve(leaderScopedTeamOptions(user))
   : api<{teamId:number;teamName:string;isPrimary:boolean}[]>("/managed-locations/visitor-teams");
  void Promise.all([load(),allowed,loadOwn()]).then(([,teams])=>setTeams(teams))
   .catch(e=>setError(e instanceof Error?e.message:"讀取地點或小組失敗"));
 },[role,user]);
 const set=(key:keyof Form,value:string)=>setForm(x=>({...x,[key]:value}));
 const reset=()=>{setEdit(null);setForm(empty());};
 const canEdit=(row:ManagedLocation)=>mayEditScopedDraft(row,user,role);
 const open=(row:ManagedLocation)=>{if(!canEdit(row))return;setEdit(row);setForm({teamId:String(row.teamId||""),locationName:row.locationName,city:row.city||"",district:row.district||"",address:row.address||"",plusCode:row.plusCode||"",taxId:row.taxId||"",masterNote:row.masterNote||""});};
 const save=async()=>{
  if(!form.teamId||!form.locationName.trim()||(!form.address.trim()&&!form.plusCode.trim())){setError("請選擇小組、輸入地點名稱與地址或 Plus Code。");return;}
  if(OPTIONC_CREATE_LOCATION_UI_ENABLED&&!edit&&!form.address.trim()){setError("正式新增申請必須提供地址，Plus Code 不可取代地址。");return;}
  setBusy(true);setError("");setMessage("");
  try{
   if(OPTIONC_CREATE_LOCATION_UI_ENABLED&&!edit){
    const proposal={locationName:form.locationName.trim(),city:form.city.trim()||null,district:form.district.trim()||null,address:form.address.trim(),plusCode:form.plusCode.trim()||null,taxId:form.taxId.trim()||null,masterNote:form.masterNote.trim()||null};
    const submitted=await api<OptionCRequest>("/optionc-candidate/locations",{method:"POST",body:JSON.stringify({teamId:Number(form.teamId),reason:"新增客戶地點，請進行獨立初審與高風險複審",proposed:proposal})});
    setMessage(`新增地點已提交獨立審查（${submitted.requestPublicId.slice(0,8)}），目前不可用於行程或里程計算。`);
    reset();await Promise.all([load(),loadOwn()]);
   }else{
    await api(edit?`/managed-locations/${edit.locationId}`:"/managed-locations",{method:edit?"PUT":"POST",body:JSON.stringify({teamId:Number(form.teamId),locationName:form.locationName.trim(),locationType:"Customer",city:form.city.trim()||null,district:form.district.trim()||null,address:form.address.trim()||null,plusCode:form.plusCode.trim()||null,taxId:form.taxId.trim()||null,masterNote:form.masterNote.trim()||null,isActive:false,rowVersion:edit?.rowVersion||null})});
    setMessage("已儲存 Pending／未啟用的 Customer 草稿；這不代表正式發布。");reset();await load();
   }
  }catch(e){setError(e instanceof Error?e.message:"儲存失敗");}finally{setBusy(false);}
 };
 return <div className="card"><div className="section-title"><div><h2>地點管理</h2><div className="sub">{role==="leader"?"小組長可建立及維護授權管理小組內的待審核、未啟用客戶地點草稿。":"外訪員可建立及修改本人待審核、未啟用的客戶地點草稿。"}</div></div></div>
  <div className="note">草稿維護與已發布地點的 LowRisk 主檔備註維護分開處理。已發布地點不允許在此直接修改地址、名稱或統編。</div>
  <ScopedLocationFields role={role} form={form} teams={teams} busy={busy} editing={!!edit} onChange={set}/>
  <div className="actions"><button className="btn ok" disabled={busy||!teams.length} onClick={()=>void save()}>{busy?"處理中…":edit?"儲存草稿修改（待審核）":"新增地點（待審核）"}</button>{edit&&<button className="btn secondary" disabled={busy} onClick={reset}>取消</button>}</div>{message&&<div role="status" className="note ok-note">{message}</div>}{error&&<div role="alert" className="note danger-note">{error}</div>}
  {OPTIONC_CREATE_LOCATION_UI_ENABLED&&<section aria-label="我的地點申請"><hr/><h3>我的地點申請</h3><button className="btn small secondary" disabled={busy} onClick={()=>void loadOwn().catch(e=>setError(String(e)))}>重新整理申請</button><div className="table-wrap"><table><thead><tr><th>申請編號</th><th>地點 ID</th><th>風險</th><th>狀態</th></tr></thead><tbody>{myRequests.map(r=><tr key={r.requestPublicId}><td>{r.requestPublicId.slice(0,8)}</td><td>{r.entityId}</td><td>{r.riskCode}</td><td>{statusLabel(r.status)}</td></tr>)}</tbody></table></div></section>}
  <hr/><h3>可查詢的授權地點</h3><input aria-label="搜尋地點" placeholder="搜尋名稱、地址或代碼" value={query} onChange={e=>setQuery(e.target.value)}/><div className="table-wrap"><table><thead><tr><th>代碼</th><th>地點</th><th>小組</th><th>狀態</th><th>操作</th></tr></thead><tbody>{rows.filter(x=>`${x.locationCode} ${x.locationName} ${x.address||""}`.toLowerCase().includes(query.toLowerCase())).map(x=><tr key={x.locationId}><td>{x.locationCode}</td><td>{x.locationName}</td><td>{x.teamName||"—"}</td><td>{x.approvalStatus}／{x.isActive?"啟用":"未啟用"}</td><td>{canEdit(x)&&<button className="btn small secondary" disabled={busy} onClick={()=>open(x)}>{role==="leader"?"修改管理小組草稿":"修改本人草稿"}</button>}{role==="leader"&&mayMaintainLeaderPublishedLowRisk(x,user?.teamScopes?.map(t=>t.teamId))&&<button className="btn small outline" disabled={busy||!OPTIONC_DIRECT_LOWRISK_UI_ENABLED} title={!OPTIONC_DIRECT_LOWRISK_UI_ENABLED?"Option C LowRisk UI gate is disabled":"僅可維護主檔備註"} onClick={()=>setLowRiskLocation(x)}>LowRisk 主檔備註</button>}</td></tr>)}</tbody></table></div>{!rows.length&&<div className="empty compact-empty">目前沒有符合權限的地點。</div>}
  {lowRiskLocation&&<LocationMaintenanceModal locationId={lowRiskLocation.locationId} teamId={lowRiskLocation.teamId} lowRiskOnly onClose={()=>setLowRiskLocation(null)} onChanged={()=>void load()}/>}
 </div>;
}
