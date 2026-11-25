import {useEffect,useRef,useState} from "react";
import {api,apiDownload} from "../api";
import {useAuth} from "../auth";
import type {ManagedLocation} from "../types";
import ScopedNotes from "../components/ScopedLocationNotes";
import {leaderScopedTeamOptions,mayEditScopedDraft,mayMaintainLeaderPublishedLowRisk,scopedLocationNoteTeams,mayRequestPublishedChange,validTaiwanTaxId,firstTaxIdPayload} from "../location-ownership-ui";
import {OPTIONC_CREATE_LOCATION_UI_ENABLED,OPTIONC_DIRECT_LOWRISK_UI_ENABLED,OPTIONC_APPROVAL_UI_ENABLED} from "../optionc-approval-flags";
import LocationMaintenanceModal from "../components/LocationMaintenanceModal";
import {differences,safeFields,statusLabel,type OptionCRequest} from "../optionc-approval-ui";
import LeaderLocationReviewPage from "./LeaderLocationReviewPage";

export type ScopedLocationForm={teamId:string;locationName:string;city:string;district:string;address:string;plusCode:string;taxId:string;masterNote:string};
type Form=ScopedLocationForm;
const empty=(teamId=""):Form=>({teamId,locationName:"",city:"",district:"",address:"",plusCode:"",taxId:"",masterNote:""});

export function ScopedLocationFields({role,form,teams,busy,editing,onChange}:{role:"visitor"|"leader";form:Form;teams:{teamId:number;teamName:string;isPrimary:boolean}[];busy:boolean;editing:boolean;onChange:(key:keyof Form,value:string)=>void}){
 return <div className="grid cols-2" data-role={role}><div className="field"><label>所屬小組</label><select aria-label="所屬小組" value={form.teamId} disabled={busy||editing} onChange={e=>onChange("teamId",e.target.value)}><option value="">請選擇</option>{teams.map(t=><option key={t.teamId} value={t.teamId}>{t.teamName}</option>)}</select></div><div className="field"><label>地點名稱</label><input aria-label="地點名稱" value={form.locationName} onChange={e=>onChange("locationName",e.target.value)}/></div><div className="field"><label>縣市</label><input aria-label="縣市" value={form.city} onChange={e=>onChange("city",e.target.value)}/></div><div className="field"><label>鄉鎮區</label><input aria-label="鄉鎮區" value={form.district} onChange={e=>onChange("district",e.target.value)}/></div><div className="field span-2"><label>地址（或 Plus Code）</label><input aria-label="地址（或 Plus Code）" value={form.address} onChange={e=>onChange("address",e.target.value)}/></div><div className="field"><label>Plus Code</label><input aria-label="Plus Code" value={form.plusCode} onChange={e=>onChange("plusCode",e.target.value)}/></div><div className="field"><label>統一編號（選填）</label><input aria-label="統一編號（選填）" maxLength={20} value={form.taxId} onChange={e=>onChange("taxId",e.target.value)}/></div><div className="field span-2"><label>地點備註（選填）</label><textarea aria-label="主檔備註（選填）" maxLength={1000} value={form.masterNote} onChange={e=>onChange("masterNote",e.target.value)}/></div></div>;
}

type PublishedMode="tax"|"change";
type ScopedResult={location:ManagedLocation;latestNote?:string;noteAuthor?:string;noteAt?:string};
const requestType=(r:OptionCRequest)=>r.operationCode==="CreateLocation"?"新增地點":r.operationCode==="UpdatePublishedLocation"?(differences(r).some(d=>d.field==="TaxId")?"統一編號／地點異動":"地點異動"):"其他申請";
const requestName=(r:OptionCRequest)=>safeFields(r.proposedJson).LocationName||safeFields(r.beforeJson).LocationName||"地點資料";
function PublishedLocationRequest({row,mode,onClose,onChanged}:{row:ManagedLocation;mode:PublishedMode;onClose:()=>void;onChanged:()=>void}){
 const [form,setForm]=useState({locationName:row.locationName,city:row.city||"",district:row.district||"",address:row.address||"",plusCode:row.plusCode||"",taxId:row.taxId||""});
 const [reason,setReason]=useState(""),[busy,setBusy]=useState(false),[locked,setLocked]=useState(false),[message,setMessage]=useState(""),[error,setError]=useState("");
 const fields=[["locationName","地點名稱"],["city","縣市"],["district","鄉鎮區"],["address","地址"],["plusCode","Plus Code"],["taxId","統一編號"]] as const;
 const changed=fields.some(([key])=>form[key].trim()!==(row[key]||"").trim());
 const enabled=mode==="tax"?OPTIONC_DIRECT_LOWRISK_UI_ENABLED:OPTIONC_APPROVAL_UI_ENABLED;
 const canSave=enabled&&!locked&&!busy&&(mode==="tax"?(!row.taxId?.trim()&&validTaiwanTaxId(form.taxId)):changed&&!!reason.trim()&&!!form.locationName.trim()&&!!(form.address.trim()||form.plusCode.trim()));
 const save=async()=>{
  if(!canSave)return;
  setBusy(true);setError("");setMessage("");
  try{
   if(mode==="tax"){
    await api(`/optionc-candidate/locations/${row.locationId}/low-risk-direct`,{method:"POST",body:JSON.stringify(firstTaxIdPayload(row,form.taxId,crypto.randomUUID()))});
    setMessage("統一編號已補登，保留原地點及備註資料。");
   }else{
    const proposed={locationName:form.locationName.trim(),city:form.city.trim()||null,district:form.district.trim()||null,address:form.address.trim()||null,plusCode:form.plusCode.trim()||null,taxId:form.taxId.trim()||null,masterNote:row.masterNote??null};
    await api<OptionCRequest>(`/optionc-candidate/locations/${row.locationId}/changes`,{method:"POST",body:JSON.stringify({locationId:row.locationId,expectedRowVersion:row.rowVersion,reason:reason.trim(),proposed})});
    setMessage(`地點異動已送審，可在「我的地點申請」追蹤；正式地點資料不會立即變更。`);
   }
   setLocked(true);onChanged();
  }catch(e){setLocked(true);setError((e instanceof Error?e.message:"操作失敗")+"；請關閉後重新查詢資料或申請紀錄，勿直接重送。");}
  finally{setBusy(false)}
 };
 return <div className="modal"><div className="modal-panel" role="dialog" aria-modal="true" aria-label={mode==="tax"?"首次補登統一編號":"申請地點異動"}>
 <button className="btn small outline modal-close" disabled={busy} onClick={onClose}>關閉</button>
 <h3>{mode==="tax"?"首次補登統一編號":"申請地點異動"}｜{row.locationName}</h3>
 <div className="note">{mode==="tax"?"僅能補登原本空白的統編，無須審核；已有統編的修改須提出異動申請。":"正式地點名稱、地址或既有統編的變更須經審核，核准前原資料繼續有效。"}</div>
 {error&&<div role="alert" className="note danger-note">{error}</div>}{message&&<div role="status" className="note ok-note">{message}</div>}
 <div className="grid cols-2">{fields.filter(([key])=>mode==="change"||key==="taxId").map(([key,label])=><div className="field" key={key}><label>{label}<input aria-label={label} value={form[key]} disabled={busy||locked} inputMode={mode==="tax"?"numeric":undefined} maxLength={key==="taxId"?(mode==="tax"?8:20):undefined} onChange={e=>setForm(x=>({...x,[key]:e.target.value}))}/></label></div>)}
 {mode==="change"&&<div className="field span-2"><label>異動原因<textarea maxLength={1000} disabled={busy||locked} value={reason} onChange={e=>setReason(e.target.value)}/></label></div>}</div>
 <button className="btn ok" disabled={!canSave} onClick={()=>void save()}>{mode==="tax"?"補登統一編號":"送出異動申請"}</button>
 </div></div>;
}

export default function ScopedLocationPage({role="visitor"}:{role?:"visitor"|"leader"}){
 const {user}=useAuth();
 const [rows,setRows]=useState<ManagedLocation[]>([]),[edit,setEdit]=useState<ManagedLocation|null>(null),[query,setQuery]=useState("");
 const [latest,setLatest]=useState<Record<number,ScopedResult>>({}),sequence=useRef(0);
 const [teams,setTeams]=useState<{teamId:number;teamName:string;isPrimary:boolean}[]>([]);
 const [form,setForm]=useState<Form>(()=>empty());
 const [busy,setBusy]=useState(false),[error,setError]=useState(""),[message,setMessage]=useState("");
 const [myRequests,setMyRequests]=useState<OptionCRequest[]>([]);
 const [requestSearch,setRequestSearch]=useState(""),[requestFilter,setRequestFilter]=useState("all"),[requestDetail,setRequestDetail]=useState<OptionCRequest|null>(null);
 const [lowRiskLocation,setLowRiskLocation]=useState<ManagedLocation|null>(null);
 const [noteLocation,setNoteLocation]=useState<ManagedLocation|null>(null);
 const [publishedRequest,setPublishedRequest]=useState<{row:ManagedLocation;mode:PublishedMode}|null>(null);
 const mayRequest=(row:ManagedLocation)=>mayRequestPublishedChange(row,user?.userId,role,teams.map(t=>t.teamId));
 const noteTeams=(row:ManagedLocation)=>scopedLocationNoteTeams(teams,row.teamId);
 const canNote=(row:ManagedLocation)=>row.isActive&&row.approvalStatus==="Approved"&&!row.isTemporary&&!row.duplicateOfLocationId&&noteTeams(row).length>0;
 const [tab,setTab]=useState<"review"|"create"|"list"|"mine">(role==="leader"?"review":"list");
 const loadOwn=async()=>{if(OPTIONC_CREATE_LOCATION_UI_ENABLED||OPTIONC_APPROVAL_UI_ENABLED)setMyRequests(await api<OptionCRequest[]>("/optionc-candidate/requests/mine"));};
 const load=async(search=query)=>{const id=++sequence.current;const result=await api<ScopedResult[]>(`/scoped-location-export/locations${search?"?q="+encodeURIComponent(search):""}`);if(id!==sequence.current)return;setRows(result.map(x=>x.location));setLatest(Object.fromEntries(result.map(x=>[x.location.locationId,x])))};
 useEffect(()=>{
  // Leader options come only from authenticated granted team scopes.
  // Visitor options continue to be validated by the server's effective-assignment endpoint.
  const allowed = role==="leader"
   ? Promise.resolve(leaderScopedTeamOptions(user))
   : api<{teamId:number;teamName:string;isPrimary:boolean}[]>("/managed-locations/visitor-teams");
  void Promise.all([allowed,loadOwn()]).then(([teams])=>setTeams(teams))
   .catch(e=>setError(e instanceof Error?e.message:"讀取地點或小組失敗"));
 },[role,user]);
 useEffect(()=>{const timer=setTimeout(()=>void load().catch(e=>setError(e instanceof Error?e.message:"地點查詢失敗")),250);return()=>{clearTimeout(timer);sequence.current++}},[query,role,user]);
 const set=(key:keyof Form,value:string)=>setForm(x=>({...x,[key]:value}));
 const reset=()=>{setEdit(null);setForm(empty());};
 const canEdit=(row:ManagedLocation)=>mayEditScopedDraft(row,user,role);
 const pendingReviewRows=rows.filter(x=>x.approvalStatus==="Pending"&&x.createdByUserId!==user?.userId);
 const visibleRows=role==="leader"&&tab==="review"?pendingReviewRows:rows;
 const shownRequests=myRequests.filter(r=>(requestFilter==="all"||requestFilter==="pending"&&r.status==="Pending"||requestFilter==="completed"&&r.status==="Applied"||requestFilter==="rejected"&&r.status==="Rejected")&&requestName(r).toLowerCase().includes(requestSearch.toLowerCase()));
 const download=async()=>{setBusy(true);setError("");try{await apiDownload(`/scoped-location-export/locations.xlsx${query?"?q="+encodeURIComponent(query):""}`,"授權地點清單.xlsx")}catch(e){setError(e instanceof Error?e.message:"下載失敗")}finally{setBusy(false)}};
 const open=(row:ManagedLocation)=>{if(!canEdit(row))return;setTab("create");setEdit(row);setForm({teamId:String(row.teamId||""),locationName:row.locationName,city:row.city||"",district:row.district||"",address:row.address||"",plusCode:row.plusCode||"",taxId:row.taxId||"",masterNote:row.masterNote||""});};
 const save=async()=>{
  if(!edit&&!OPTIONC_CREATE_LOCATION_UI_ENABLED){setError("新增地點送審尚未開放，請勿重複送出。");return;}
  if(!form.teamId||!form.locationName.trim()||(!form.address.trim()&&!form.plusCode.trim())){setError("請選擇小組、輸入地點名稱與地址或 Plus Code。");return;}
  if(OPTIONC_CREATE_LOCATION_UI_ENABLED&&!edit&&!form.address.trim()){setError("正式新增申請必須提供地址，Plus Code 不可取代地址。");return;}
  setBusy(true);setError("");setMessage("");
  try{
   if(OPTIONC_CREATE_LOCATION_UI_ENABLED&&!edit){
    const proposal={locationName:form.locationName.trim(),city:form.city.trim()||null,district:form.district.trim()||null,address:form.address.trim(),plusCode:form.plusCode.trim()||null,taxId:form.taxId.trim()||null,masterNote:form.masterNote.trim()||null};
    await api<OptionCRequest>("/optionc-candidate/locations",{method:"POST",body:JSON.stringify({teamId:Number(form.teamId),reason:"新增客戶地點，請進行獨立初審與高風險複審",proposed:proposal})});
    setMessage("新增地點已提交獨立審查，可在「我的地點申請」追蹤；目前不可用於行程或里程計算。");
    reset();setTab("mine");await Promise.all([load(),loadOwn()]);
   }else{
    await api(edit?`/managed-locations/${edit.locationId}`:"/managed-locations",{method:edit?"PUT":"POST",body:JSON.stringify({teamId:Number(form.teamId),locationName:form.locationName.trim(),locationType:"Customer",city:form.city.trim()||null,district:form.district.trim()||null,address:form.address.trim()||null,plusCode:form.plusCode.trim()||null,taxId:form.taxId.trim()||null,masterNote:form.masterNote.trim()||null,isActive:false,rowVersion:edit?.rowVersion||null})});
    setMessage("地點草稿已儲存並送審，目前為待審核且尚未啟用。");setTab("list");reset();await load();
   }
  }catch(e){setError(e instanceof Error?e.message:"儲存失敗");}finally{setBusy(false);}
 };
 return <div className="card"><div className="section-title"><div><h2>地點管理</h2><div className="sub">{role==="leader"?"查詢及維護授權小組的地點，待審地點依現行授權流程處理。":"新增拜訪地點或查詢已儲存的地點。"}</div></div></div>
  <div className="actions" role="tablist" aria-label="地點管理分類">{role==="leader"&&<button type="button" role="tab" aria-selected={tab==="review"} className={tab==="review"?"btn":"btn secondary"} disabled={busy} onClick={()=>{reset();setTab("review")}}>待審核地點</button>}<button type="button" role="tab" aria-selected={tab==="create"} className={tab==="create"?"btn":"btn secondary"} disabled={busy} onClick={()=>{reset();setTab("create")}}>新增地點</button><button type="button" role="tab" aria-selected={tab==="list"} className={tab==="list"?"btn":"btn secondary"} disabled={busy} onClick={()=>{reset();setTab("list")}}>地點清單</button><button type="button" role="tab" aria-selected={tab==="mine"} className={tab==="mine"?"btn":"btn secondary"} disabled={busy} onClick={()=>{reset();setTab("mine");void loadOwn().catch(e=>setError(String(e)))}}>我的地點申請</button></div>
  {error&&<div role="alert" className="note danger-note">{error.includes("LEADER_DRAFT_NO_CURRENT_SCOPE")?"目前沒有可用的地點管理小組授權，請管理者確認權限。":error}</div>}
  {message&&<div role="status" className="note ok-note">{message}</div>}
  {tab==="create"&&<>
  <div className="note">新增地點提交後需經獨立審查，請到「我的地點申請」查看進度。待審草稿的修改與正式地點異動依各自既有流程處理。</div>
  <ScopedLocationFields role={role} form={form} teams={teams} busy={busy} editing={!!edit} onChange={set}/>
  <div className="actions"><button className="btn ok" disabled={busy||!teams.length||(!edit&&!OPTIONC_CREATE_LOCATION_UI_ENABLED)} onClick={()=>void save()}>{busy?"處理中…":edit?(role==="leader"?"儲存草稿修改（待審核）":"修改地點"):"新增地點送審"}</button>{edit&&<button className="btn secondary" disabled={busy} onClick={reset}>取消</button>}</div><div className="hint">{!edit&&!OPTIONC_CREATE_LOCATION_UI_ENABLED?"新增地點送審尚未開放。":edit?"修改待審草稿後仍需完成既有審核。":"提交後須經獨立審核；狀態可至我的地點申請查詢。"}</div></>}
  {tab==="mine"&&<section aria-label="我的地點申請"><h3>我的地點申請</h3>
   <div className="actions"><button className="btn small secondary" disabled={busy} onClick={()=>void loadOwn().catch(e=>setError(String(e)))}>↻ 重新整理</button><input aria-label="搜尋申請地點" placeholder="搜尋地點名稱" value={requestSearch} onChange={e=>setRequestSearch(e.target.value)}/><select aria-label="申請狀態" value={requestFilter} onChange={e=>setRequestFilter(e.target.value)}><option value="all">全部</option><option value="pending">待處理</option><option value="completed">已完成</option><option value="rejected">未通過</option></select></div>
   <div className="table-wrap scoped-location-table scoped-request-table"><table><thead><tr><th>申請日期</th><th>地點名稱</th><th>申請類型</th><th>異動摘要</th><th>審核狀態</th><th>處理日期</th><th>操作</th></tr></thead><tbody>{shownRequests.map(r=><tr key={r.requestPublicId}><td>{new Date(r.submittedAt).toLocaleDateString("zh-TW",{timeZone:"Asia/Taipei"})}</td><td>{requestName(r)}</td><td>{requestType(r)}</td><td>{differences(r).map(d=>({TaxId:"統一編號",LocationName:"名稱",Address:"地址",MasterNote:"地點備註"} as Record<string,string>)[d.field]||d.field).join("、")||"新增地點"}</td><td>{statusLabel(r.status)}</td><td>{r.reviewedAt?new Date(r.reviewedAt).toLocaleDateString("zh-TW",{timeZone:"Asia/Taipei"}):"—"}</td><td><button className="btn small outline" onClick={()=>setRequestDetail(r)}>查看內容</button></td></tr>)}</tbody></table></div>{!shownRequests.length&&<div className="empty compact-empty">沒有符合條件的申請。</div>}
  </section>}
  {requestDetail&&<div className="modal"><div className="modal-panel" role="dialog" aria-modal="true" aria-label="地點申請內容"><button className="btn small outline modal-close" onClick={()=>setRequestDetail(null)}>關閉</button><h3>{requestName(requestDetail)}｜{requestType(requestDetail)}</h3><p>申請日期：{new Date(requestDetail.submittedAt).toLocaleString("zh-TW",{timeZone:"Asia/Taipei"})}；狀態：{statusLabel(requestDetail.status)}</p><div className="table-wrap"><table><thead><tr><th>欄位</th><th>原資料</th><th>申請內容</th></tr></thead><tbody>{differences(requestDetail).map(d=><tr key={d.field}><td>{d.field}</td><td>{d.before||"—"}</td><td>{d.after||"—"}</td></tr>)}</tbody></table></div></div></div>}
  {tab==="review"&&role==="leader"&&<><LeaderLocationReviewPage/><div className="note">傳統待審草稿 {pendingReviewRows.length} 筆；這類舊資料不屬於上方獨立初審佇列，不能在此直接核准。請在地點清單查看內容，由管理者依既有資料治理流程處理。</div>{pendingReviewRows.map(x=><div className="note" key={x.locationId}>{x.locationCode}｜{x.locationName}｜{x.teamName||"授權小組"}｜待審草稿 <button className="btn small outline" onClick={()=>{setQuery(x.locationCode);setTab("list")}}>在清單查看</button></div>)}</>}
  {tab==="list"&&<><div className="section-title"><h3>地點清單</h3><button className="btn small outline" disabled={busy} onClick={()=>void download()}>下載地點清單 Excel</button></div><input aria-label="搜尋地點" placeholder="搜尋名稱、地址、代碼、統編或授權備註" value={query} onChange={e=>setQuery(e.target.value)}/><div className="table-wrap scoped-location-table scoped-list-table"><table><thead><tr><th>代碼</th><th>地點／地址</th><th>統一編號</th><th>小組</th><th>最近可見拜訪備註</th><th>狀態</th><th>操作</th></tr></thead><tbody>{visibleRows.map(x=><tr key={x.locationId}><td>{x.locationCode}</td><td><strong>{x.locationName}</strong><div className="sub">{x.address||x.plusCode||"—"}</div></td><td>{x.taxId||"—"}</td><td>{x.teamName||"—"}</td><td>{latest[x.locationId]?.latestNote||"—"}<div className="sub">{latest[x.locationId]?.noteAuthor||""} {latest[x.locationId]?.noteAt?new Date(latest[x.locationId].noteAt!).toLocaleString("zh-TW",{timeZone:"Asia/Taipei"}):""}</div></td><td>{x.approvalStatus==="Pending"?"待審核":x.approvalStatus==="Approved"?"已核准":x.approvalStatus==="Rejected"?"已退回":x.approvalStatus}／{x.isActive?"啟用":"未啟用"}</td><td><div className="location-action-groups">{canEdit(x)&&<button className="btn small secondary" disabled={busy} onClick={()=>open(x)}>修改待審草稿</button>}{(canNote(x)||OPTIONC_DIRECT_LOWRISK_UI_ENABLED&&mayRequest(x))&&<details><summary>地點備註</summary><div className="sub">主檔備註：{x.masterNote||"—"}；拜訪備註依小組權限另行記錄。</div><div className="actions">{canNote(x)&&<button className="btn small outline" disabled={busy} onClick={()=>setNoteLocation(x)}>新增拜訪備註／查看歷史</button>}{OPTIONC_DIRECT_LOWRISK_UI_ENABLED&&mayRequest(x)&&<button className="btn small outline" disabled={busy} onClick={()=>setLowRiskLocation(x)}>修改主檔備註</button>}</div></details>}{mayRequest(x)&&<details><summary>地點維護</summary><div className="actions">{OPTIONC_DIRECT_LOWRISK_UI_ENABLED&&!x.taxId?.trim()&&<button className="btn small outline" disabled={busy} onClick={()=>setPublishedRequest({row:x,mode:"tax"})}>首次補登統編</button>}{OPTIONC_APPROVAL_UI_ENABLED&&<button className="btn small outline" disabled={busy} onClick={()=>setPublishedRequest({row:x,mode:"change"})}>申請正式異動</button>}</div></details>}{!canEdit(x)&&!canNote(x)&&!mayRequest(x)&&<span className="muted">{x.isTemporary?"臨時地點":"僅供查詢"}</span>}</div></td></tr>)}</tbody></table></div>{!visibleRows.length&&<div className="empty compact-empty">目前沒有符合條件的地點。</div>}</>}
  {publishedRequest&&<PublishedLocationRequest key={publishedRequest.row.locationId+"-"+publishedRequest.mode} {...publishedRequest} onClose={()=>setPublishedRequest(null)} onChanged={()=>{void Promise.all([load(),loadOwn()]).catch(e=>setError(e instanceof Error?e.message:"重新讀取失敗"));}}/>}
  {noteLocation&&<ScopedNotes key={noteLocation.locationId} row={noteLocation} teams={noteTeams(noteLocation)} onClose={()=>setNoteLocation(null)}/>}
  {lowRiskLocation&&<LocationMaintenanceModal locationId={lowRiskLocation.locationId} teamId={lowRiskLocation.teamId} lowRiskOnly onClose={()=>setLowRiskLocation(null)} onChanged={()=>void load()}/>}
 </div>;
}
