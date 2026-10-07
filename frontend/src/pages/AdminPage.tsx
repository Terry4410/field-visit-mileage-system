import{useEffect,useMemo,useState}from"react";
import{api}from"../api";
import LocationMaintenanceModal from "../components/LocationMaintenanceModal";
import OfficialSiteMaintenance from "../components/OfficialSiteMaintenance";
import AdminImportPanel from "../components/AdminImportPanel";
import LocationAdminTabs from "../components/LocationAdminTabs";
import type{BackgroundJob,DashboardSummary,ManagedLocation,MileageRate,MileageRateDeleteImpact,Team,VisitType,VisitTypeDeleteImpact}from"../types";
import{money,todayTaipei}from"../v160";

import { usePagedQuery } from '../use-query';

type Props={section:'dashboard'|'locations'|'official-sites'|'visit-types'|'rates'};

type ManagedLocationPage={
 items:ManagedLocation[];
 page:number;
 pageSize:number;
 totalCount:number;
 totalPages:number;
};

type ManagedLocationDeleteImpact={
 locationId:number;
 locationCode:string;
 locationName:string;
 canDelete:boolean;
 tripReferenceCount:number;
 projectReferenceCount:number;
 favoriteReferenceCount:number;
 approvalHistoryCount:number;
 governmentMatchCount:number;
 reason?:string|null;
};

export default function AdminPage({section}:Props){
 const[msg,setMsg]=useState(''),[busy,setBusy]=useState(false);useEffect(()=>setMsg(''),[section]);
 if(section==='dashboard')return <Dashboard msg={msg} setMsg={setMsg}/>;
 if(section==='locations')return <><LocationAdminTabs/><Locations busy={busy} setBusy={setBusy} msg={msg} setMsg={setMsg}/></>;
 if(section==='official-sites')return <><LocationAdminTabs/><OfficialSiteMaintenance/></>;
 if(section==='visit-types')return <VisitTypes busy={busy} setBusy={setBusy} msg={msg} setMsg={setMsg}/>;
 if(section==='rates')return <Rates busy={busy} setBusy={setBusy} msg={msg} setMsg={setMsg}/>;
 return null;
}

function Dashboard({msg,setMsg}:{msg:string;setMsg:(v:string)=>void}){
 const[d,setD]=useState<DashboardSummary|null>(null);useEffect(()=>{api<DashboardSummary>('/dashboard').then(setD).catch(e=>setMsg(e.message))},[]);
 return <><div className="grid cols-5 dashboard-cards"><Stat label="本月行程" value={d?.thisMonthTrips??'—'}/><Stat label="待核准" value={d?.pendingApproval??'—'}/><Stat label="已核准" value={d?.approved??'—'}/><Stat label="待確認地點" value={d?.pendingLocations??'—'}/><Stat label="待處理更正" value={d?.pendingCorrections??'—'} hint={d?.currentRatePerKm!=null?`目前費率 ${money(d.currentRatePerKm)}/km`:undefined}/></div>{msg&&<div className="note">{msg}</div>}</>
}
function Stat({label,value,hint}:{label:string;value:string|number;hint?:string}){return <div className="card stat"><div className="label">{label}</div><div className="value">{value}</div>{hint&&<div className="hint">{hint}</div>}</div>}

function Locations({busy,setBusy,msg,setMsg}:{busy:boolean;setBusy:(v:boolean)=>void;msg:string;setMsg:(v:string)=>void}){
 const[teams,setTeams]=useState<Team[]>([]),[edit,setEdit]=useState<ManagedLocation|null>(null),[maintain,setMaintain]=useState<ManagedLocation|null>(null);
 const[teamId,setTeamId]=useState(''),[name,setName]=useState(''),[city,setCity]=useState(''),[district,setDistrict]=useState(''),[address,setAddress]=useState(''),[plus,setPlus]=useState('');
 const[q,setQ]=useState(''),[filterTeam,setFilterTeam]=useState(''),[filterCity,setFilterCity]=useState(''),[filterDistrict,setFilterDistrict]=useState(''),[filterGeocode,setFilterGeocode]=useState(''),[filterActive,setFilterActive]=useState('');

 const[selected,setSelected]=useState<number[]>([]),[allFiltered,setAllFiltered]=useState(false),[job,setJob]=useState<BackgroundJob|null>(null);

 const query=usePagedQuery<ManagedLocation>('/managed-locations/search',{q,teamId:filterTeam,city:filterCity,district:filterDistrict,geocodingStatus:filterGeocode,isActive:filterActive});
 const rows=query.data.items, page=query.page, pageSize=query.pageSize, totalCount=query.data.totalCount, totalPages=query.data.totalPages;
 const setPage=(value:number|((page:number)=>number))=>query.setPage(typeof value==='function'?value(page):value);
 const setPageSize=query.setPageSize;
 const load=()=>{query.reload()};
 useEffect(()=>{api<Team[]>('/teams').then(setTeams).catch(e=>setMsg(e.message))},[]);

 const clearSelection=()=>{setSelected([]);setAllFiltered(false)};
 useEffect(()=>{clearSelection()},[page,pageSize,q,filterTeam,filterCity,filterDistrict,filterGeocode,filterActive]);

 const reset=()=>{setEdit(null);setTeamId('');setName('');setCity('');setDistrict('');setAddress('');setPlus('')};
 const open=(l:ManagedLocation)=>{setEdit(l);setTeamId(l.teamId?String(l.teamId):'');setName(l.locationName);setCity(l.city||'');setDistrict(l.district||'');setAddress(l.address||'');setPlus(l.plusCode||'')};
 const save=async()=>{setBusy(true);try{const body={teamId:teamId?Number(teamId):null,locationName:name,locationType:'Customer',city:city||null,district:district||null,address:address||null,plusCode:plus||null,isActive:edit?.isActive??false,rowVersion:edit?.rowVersion||null};const saved=edit?await api<ManagedLocation>(`/managed-locations/${edit.locationId}`,{method:'PUT',body:JSON.stringify(body)}):await api<ManagedLocation>('/managed-locations',{method:'POST',body:JSON.stringify(body)});setMsg(saved.duplicateReason==='疑似重複，待管理者人工覆核'?'地點已儲存並自動標記疑似重複；請先人工覆核，再進行解析／發布。':edit?'地點已修改，需重新解析/發布。':'地點已新增，需解析/發布後才會成為正式地點。');reset();await load()}catch(e){setMsg(e instanceof Error?e.message:'儲存失敗')}finally{setBusy(false)}};

 const pageIds=rows.map(x=>x.locationId);
 const pageAllSelected=pageIds.length>0&&pageIds.every(id=>selected.includes(id));
 const togglePage=(checked:boolean)=>setSelected(current=>checked?Array.from(new Set([...current,...pageIds])):current.filter(id=>!pageIds.includes(id)));

 const geocode=async()=>{
   if(!allFiltered&&!selected.length)return setMsg('請先勾選地點，或選取全部符合目前條件的地點。');
   setBusy(true);
   try{
     const body=allFiltered?{mode:'Filtered',q:q.trim()||null,teamId:filterTeam?Number(filterTeam):null,city:filterCity.trim()||null,district:filterDistrict.trim()||null,geocodingStatus:filterGeocode||null,isActive:filterActive===''?null:filterActive==='true'}:{mode:'Selected',locationIds:selected};
     const j=await api<BackgroundJob>('/jobs/geocoding',{method:'POST',body:JSON.stringify(body)});
     setJob(j);setMsg(`已建立背景工作 ${j.backgroundJobId}`);setTimeout(()=>void poll(j.backgroundJobId),1000);
   }catch(e){setMsg(e instanceof Error?e.message:'建立工作失敗')}finally{setBusy(false)}
 };

 const deactivate=async(l:ManagedLocation)=>{if(!window.confirm(`確定停用地點「${l.locationName}」？歷史行程 Snapshot 不受影響。`))return;setBusy(true);try{await api(`/managed-locations/${l.locationId}`,{method:'DELETE'});setMsg('地點已停用。');await load()}catch(e){setMsg(e instanceof Error?e.message:'停用失敗')}finally{setBusy(false)}};

 const permanentDelete=async(l:ManagedLocation)=>{
   setBusy(true);
   try{
     const impact=await api<ManagedLocationDeleteImpact>(`/managed-locations/${l.locationId}/delete-impact`);
     if(!impact.canDelete){window.alert(impact.reason||'此地點已有歷史或關聯資料，只能停用。');return}
     if(!window.confirm(`確定永久刪除地點「${l.locationName}」？

此地點目前沒有行程、專案、常用地點、核准歷史或政府主檔關聯。
刪除後無法復原。`))return;
     await api(`/managed-locations/${l.locationId}/permanent`,{method:'DELETE'});
     setMsg(`地點「${l.locationName}」已永久刪除。`);clearSelection();await load();
   }catch(e){setMsg(e instanceof Error?e.message:'刪除失敗')}finally{setBusy(false)}
 };

 const promote=async(l:ManagedLocation)=>{if(!window.confirm(`確定將「${l.locationName}」轉為正式地點？

轉換後可加入專案固定地點；既有歷史行程不會被修改。`))return;setBusy(true);try{await api(`/locations/${l.locationId}/promote`,{method:'POST',body:JSON.stringify({rowVersion:l.rowVersion})});setMsg(`地點「${l.locationName}」已轉為正式地點。`);await load()}catch(e){setMsg(e instanceof Error?e.message:'轉為正式地點失敗')}finally{setBusy(false)}};
 const poll=async(id:string)=>{const j=await api<BackgroundJob>(`/jobs/${id}`);setJob(j);if(['Waiting','Processing'].includes(j.status))setTimeout(()=>void poll(id),1500);else{await load();clearSelection()}};

 return <><div className="grid cols-2"><div className="card"><div className="section-title"><h2>{edit?'修改地點':'新增地點'}</h2>{edit&&<button className="btn small outline" onClick={reset}>取消修改</button>}</div><div className="grid cols-2"><div className="field"><label>小組</label><select value={teamId} onChange={e=>setTeamId(e.target.value)}><option value="">全組織</option>{teams.map(t=><option key={t.teamId} value={t.teamId}>{t.teamName}</option>)}</select></div><div className="field"><label>地點名稱</label><input value={name} onChange={e=>setName(e.target.value)}/></div><div className="field"><label>縣市</label><input value={city} onChange={e=>setCity(e.target.value)}/></div><div className="field"><label>鄉鎮區</label><input value={district} onChange={e=>setDistrict(e.target.value)}/></div><div className="field span-2"><label>地址</label><input value={address} onChange={e=>setAddress(e.target.value)}/></div><div className="field span-2"><label>Plus Code</label><input value={plus} onChange={e=>setPlus(e.target.value)}/></div></div><button className="btn" disabled={busy} onClick={()=>void save()}>{edit?'儲存修改':'新增地點'}</button></div><div className="card"><AdminImportPanel type="locations" onDone={load}/></div></div>{msg&&<div className="note" style={{marginTop:14}}>{msg}</div>}{query.error&&<div role="alert" className="note danger-note">{query.error}</div>}{job&&<div className="note">背景工作：{job.status}｜成功 {job.successCount}／失敗 {job.failedCount}／總計 {job.totalCount}</div>}

 <div className="card" style={{marginTop:18}}><div className="section-title"><div><h2>地點主檔</h2><div className="sub">大量資料採伺服器端搜尋與分頁；解析可選本頁或全部符合目前條件的地點。</div></div><button className="btn ok" onClick={()=>void geocode()} disabled={busy||query.loading||(!allFiltered&&!selected.length)}>批次解析／發布</button></div>
 <div className="grid cols-2">
  <div className="field"><label>搜尋</label><input value={q} placeholder="代碼／名稱／地址／Plus Code／統編／備註" onChange={e=>{setPage(1);setQ(e.target.value)}}/></div>
  <div className="field"><label>小組</label><select value={filterTeam} onChange={e=>{setPage(1);setFilterTeam(e.target.value)}}><option value="">全部</option>{teams.map(t=><option key={t.teamId} value={t.teamId}>{t.teamName}</option>)}</select></div>
  <div className="field"><label>縣市</label><input value={filterCity} onChange={e=>{setPage(1);setFilterCity(e.target.value)}}/></div>
  <div className="field"><label>鄉鎮區</label><input value={filterDistrict} onChange={e=>{setPage(1);setFilterDistrict(e.target.value)}}/></div>
  <div className="field"><label>解析狀態</label><select value={filterGeocode} onChange={e=>{setPage(1);setFilterGeocode(e.target.value)}}><option value="">全部</option><option value="NeedsProcessing">待處理（Pending/Failed）</option><option value="Pending">Pending</option><option value="Completed">Completed</option><option value="Failed">Failed</option></select></div>
  <div className="field"><label>啟用狀態</label><select value={filterActive} onChange={e=>{setPage(1);setFilterActive(e.target.value)}}><option value="">全部</option><option value="true">啟用</option><option value="false">停用／未發布</option></select></div>
 </div>
 <div className="actions" style={{marginBottom:10}}>
  <label className="check-row">每頁<select value={pageSize} onChange={e=>{setPage(1);setPageSize(Number(e.target.value))}}><option value={20}>20</option><option value={50}>50</option><option value={100}>100</option></select></label>
  <span className="sub">共 {totalCount} 筆</span>
 </div>
 {pageAllSelected&&totalCount>rows.length&&!allFiltered&&<div className="note">已選取本頁 {rows.length} 筆。<button className="btn small outline" style={{marginLeft:8}} onClick={()=>{setAllFiltered(true);setSelected([])}}>選取全部 {totalCount} 筆符合目前條件</button></div>}
 {allFiltered&&<div className="note ok-note">已選取全部 {totalCount} 筆符合目前條件的地點；執行解析時只會處理待解析／失敗／待核准資料。<button className="btn small outline" style={{marginLeft:8}} onClick={clearSelection}>清除選取</button></div>}
 <div className="table-wrap"><table><thead><tr><th><input type="checkbox" checked={pageAllSelected&&!allFiltered} onChange={e=>{setAllFiltered(false);togglePage(e.target.checked)}}/></th><th>地點代碼</th><th>地點</th><th>類型</th><th>小組</th><th>地址</th><th>解析</th><th>狀態</th><th>操作</th></tr></thead><tbody>{rows.map(l=>{const suspected=l.duplicateOfLocationId==null&&l.duplicateReason==='疑似重複，待管理者人工覆核';const canPromote=l.isTemporary&&l.isActive&&l.approvalStatus==='Approved'&&l.geocodingStatus==='Completed'&&!suspected;return <tr key={l.locationId}><td><input type="checkbox" checked={!allFiltered&&selected.includes(l.locationId)} disabled={allFiltered} onChange={e=>setSelected(x=>e.target.checked?[...x,l.locationId]:x.filter(id=>id!==l.locationId))}/></td><td>{l.locationCode}</td><td>{l.locationName}</td><td>{l.isTemporary?<span className="pill warn">臨時</span>:<span className="pill ok">正式</span>}</td><td>{l.teamName||'全組織'}</td><td>{l.address||l.plusCode||'—'}</td><td>{l.geocodingStatus}</td><td>{suspected?<span className="pill warn">疑似重複待覆核</span>:l.duplicateOfLocationId?<span className="pill">已合併 → {l.duplicateOfLocationId}</span>:l.isActive?'啟用':l.approvalStatus}</td><td><div className="actions"><button className="btn small secondary" onClick={()=>open(l)}>修改</button><button className="btn small outline" onClick={()=>setMaintain(l)}>資料／官方標記</button>{canPromote&&<button className="btn small ok" disabled={busy} onClick={()=>void promote(l)}>轉正式</button>}{l.isActive&&<button className="btn small outline" disabled={busy} onClick={()=>void deactivate(l)}>停用</button>}<button className="btn small outline" disabled={busy} onClick={()=>void permanentDelete(l)}>刪除</button></div></td></tr>})}</tbody></table></div>
 <div className="actions" style={{justifyContent:'space-between',marginTop:12}}><span className="sub">{totalCount===0?'0 筆':`${(page-1)*pageSize+1}–${Math.min(page*pageSize,totalCount)} / 共 ${totalCount} 筆`}</span><div className="actions"><button className="btn small outline" disabled={page<=1} onClick={()=>setPage(p=>Math.max(1,p-1))}>上一頁</button><span className="sub">第 {page} / {Math.max(totalPages,1)} 頁</span><button className="btn small outline" disabled={page>=totalPages} onClick={()=>setPage(p=>p+1)}>下一頁</button></div></div></div>
 {maintain&&<LocationMaintenanceModal locationId={maintain.locationId} teamId={maintain.teamId} onClose={()=>setMaintain(null)} onChanged={()=>void load()}/>}
 </>
}


function VisitTypes({busy,setBusy,msg,setMsg}:{busy:boolean;setBusy:(v:boolean)=>void;msg:string;setMsg:(v:string)=>void}){
 const[types,setTypes]=useState<VisitType[]>([]),[typeEdit,setTypeEdit]=useState<VisitType|null>(null),[typeCode,setTypeCode]=useState(''),[typeName,setTypeName]=useState(''),[typeDesc,setTypeDesc]=useState('');
 const load=async()=>setTypes(await api<VisitType[]>('/visit-types'));
 useEffect(()=>{void load().catch(e=>setMsg(e instanceof Error?e.message:'拜訪形式載入失敗'))},[]);
 const moveType=async(v:VisitType,direction:'up'|'down')=>{setBusy(true);setMsg('');try{setTypes(await api<VisitType[]>(`/visit-types/${v.visitTypeId}/move`,{method:'POST',body:JSON.stringify({direction,expectedOrder:types.map(x=>({visitTypeId:x.visitTypeId,sortOrder:x.sortOrder}))})}))}catch(e){setMsg(e instanceof Error?e.message:'排序失敗');await load()}finally{setBusy(false)}};
 const resetType=()=>{setTypeEdit(null);setTypeCode('');setTypeName('');setTypeDesc('')};
 const openType=(v:VisitType)=>{setTypeEdit(v);setTypeCode(v.visitTypeCode);setTypeName(v.visitTypeName);setTypeDesc(v.description||'')};
 const saveType=async()=>{setBusy(true);setMsg('');try{const body={visitTypeCode:typeCode,visitTypeName:typeName,description:typeDesc||null,isActive:true};if(typeEdit)await api(`/visit-types/${typeEdit.visitTypeId}`,{method:'PUT',body:JSON.stringify(body)});else await api('/visit-types',{method:'POST',body:JSON.stringify(body)});setMsg(typeEdit?'拜訪形式已修改。':'拜訪形式已新增。');resetType();await load()}catch(e){setMsg(e instanceof Error?e.message:'儲存失敗')}finally{setBusy(false)}};
 const deactivateType=async(v:VisitType)=>{if(!window.confirm(`確定停用拜訪形式「${v.visitTypeName}」？歷史 Snapshot 不受影響。`))return;setBusy(true);setMsg('');try{await api(`/visit-types/${v.visitTypeId}`,{method:'DELETE'});setMsg('拜訪形式已停用。');if(typeEdit?.visitTypeId===v.visitTypeId)resetType();await load()}catch(e){setMsg(e instanceof Error?e.message:'停用失敗')}finally{setBusy(false)}};
 const deleteType=async(v:VisitType)=>{setBusy(true);setMsg('');try{const x=await api<VisitTypeDeleteImpact>(`/admin/visit-types/${v.visitTypeId}/delete-impact`);if(!x.canDelete){setMsg(x.reason||'此拜訪形式已有歷史使用，只能停用。');return}if(!window.confirm(`永久刪除拜訪形式「${v.visitTypeName}」？\n\n此動作只允許從未被行程或 Snapshot 使用的資料，且無法復原。`))return;await api(`/admin/visit-types/${v.visitTypeId}/permanent`,{method:'DELETE'});setMsg(`拜訪形式「${v.visitTypeName}」已永久刪除。`);if(typeEdit?.visitTypeId===v.visitTypeId)resetType();await load()}catch(e){setMsg(e instanceof Error?e.message:'拜訪形式刪除失敗')}finally{setBusy(false)}};
 return <><div className="card"><div className="section-title"><h2>拜訪形式</h2>{typeEdit&&<button className="btn small outline" onClick={resetType}>取消修改</button>}</div><div className="grid cols-2"><div className="field"><label>代碼</label><input value={typeCode} onChange={e=>setTypeCode(e.target.value)}/></div><div className="field"><label>名稱</label><input value={typeName} onChange={e=>setTypeName(e.target.value)}/></div><div className="field"><label>說明</label><input value={typeDesc} onChange={e=>setTypeDesc(e.target.value)}/></div></div><button className="btn" onClick={()=>void saveType()} disabled={busy}>{typeEdit?'儲存拜訪形式':'新增拜訪形式'}</button><div className="route-list">{types.map((v,index)=><div className="route-item" key={v.visitTypeId}><div className="route-index">{index+1}</div><div><div className="route-name">{v.visitTypeName}</div><div className="route-address">{v.visitTypeCode}{!v.isActive?'｜停用':''}</div></div><div className="actions"><button className="btn small outline" aria-label={`上移 ${v.visitTypeName}`} disabled={busy||index===0} onClick={()=>void moveType(v,'up')}>↑</button><button className="btn small outline" aria-label={`下移 ${v.visitTypeName}`} disabled={busy||index===types.length-1} onClick={()=>void moveType(v,'down')}>↓</button><button className="btn small secondary" onClick={()=>openType(v)}>修改</button>{v.isActive&&<button className="btn small outline" disabled={busy} onClick={()=>void deactivateType(v)}>停用</button>}<button className="btn small danger" disabled={busy} onClick={()=>void deleteType(v)}>刪除</button></div></div>)}</div></div>{msg&&<div className="note">{msg}</div>}</>
}

type MileageRateImpact={effectiveFrom:string;vehicleType:string;approvedTripCount:number;firstApprovedVisitDate:string|null;lastApprovedVisitDate:string|null;requiresAcknowledgement:boolean};

function Rates({busy,setBusy,msg,setMsg}:{busy:boolean;setBusy:(v:boolean)=>void;msg:string;setMsg:(v:string)=>void}){
 const[rows,setRows]=useState<MileageRate[]>([]),[edit,setEdit]=useState<MileageRate|null>(null),[name,setName]=useState(''),[vehicle,setVehicle]=useState('Motorcycle'),[rate,setRate]=useState('2.50'),[from,setFrom]=useState(todayTaipei()),[to,setTo]=useState('');
 const load=()=>api<MileageRate[]>('/mileage-rate-rules').then(setRows).catch(e=>setMsg(e.message));useEffect(()=>{void load()},[]);
 const reset=()=>{setEdit(null);setName('');setVehicle('Motorcycle');setRate('2.50');setFrom(todayTaipei());setTo('')};
 const isSharedRate=(r:MileageRate)=>r.organizationId==null;
 const open=(r:MileageRate)=>{if(isSharedRate(r))return setMsg('系統共用費率為唯讀，請建立目前組織自己的費率版本。');setEdit(r);setName(r.ruleName);setVehicle(r.vehicleType||'Motorcycle');setRate(String(r.ratePerKm));setFrom(r.effectiveFrom);setTo(r.effectiveTo||'')};
 const impact=async(effectiveFrom:string,vehicleType:string)=>api<MileageRateImpact>(`/mileage-rate-rules/impact?effectiveFrom=${encodeURIComponent(effectiveFrom)}&vehicleType=${encodeURIComponent(vehicleType)}`);
 const impactText=(x:MileageRateImpact,verb:string)=>`此異動自 ${x.effectiveFrom} 起可能影響費率判讀；該日期之後已有 ${x.approvedTripCount} 筆已核准且具有費率快照的行程${x.firstApprovedVisitDate&&x.lastApprovedVisitDate?`（${x.firstApprovedVisitDate}～${x.lastApprovedVisitDate}）`:''}。\n\n${verb}後不會自動重算既有 Snapshot；若需追溯調整，應透過更正流程建立新 Snapshot。\n\n是否仍要繼續？`;
 const save=async()=>{
  if(to&&to<from)return setMsg('失效日期不可早於生效日期。');
  setBusy(true);setMsg('');
  try{
   const nextRate=Number(rate);
   const material=!edit||edit.ratePerKm!==nextRate||edit.effectiveFrom!==from||(edit.effectiveTo||'')!==to||edit.vehicleType!==vehicle||!edit.isActive;
   let acknowledged=false;
   if(material){const impactFrom=edit&&edit.effectiveFrom<from?edit.effectiveFrom:from;const x=await impact(impactFrom,vehicle);if(x.requiresAcknowledgement){if(!window.confirm(impactText(x,edit?'修改費率版本':'新增費率版本')))return;acknowledged=true}}
   const body={ruleName:name,vehicleType:vehicle,ratePerKm:nextRate,effectiveFrom:from,effectiveTo:to||null,isActive:true,acknowledgeHistoricalImpact:acknowledged};
   if(edit)await api(`/mileage-rate-rules/${edit.mileageRateRuleId}`,{method:'PUT',body:JSON.stringify(body)});else await api('/mileage-rate-rules',{method:'POST',body:JSON.stringify(body)});
   setMsg('費率版本已儲存；生效日與失效日皆由管理者明確維護，系統不改寫前後版本日期。歷史 Snapshot 不會自動重算。');reset();await load();
  }catch(e){setMsg(e instanceof Error?e.message:'儲存失敗')}finally{setBusy(false)}
 };
 const deactivateRate=async(r:MileageRate)=>{if(isSharedRate(r))return setMsg('系統共用費率為唯讀，無法由組織管理者停用。');setBusy(true);setMsg('');try{const x=await impact(r.effectiveFrom,r.vehicleType);let acknowledged=false;if(x.requiresAcknowledgement){if(!window.confirm(impactText(x,'停用此費率版本')))return;acknowledged=true}else if(!window.confirm(`確定停用 ${r.vehicleType}｜${r.effectiveFrom}～${r.effectiveTo||'無期限'} 的費率版本？歷史核准費率快照不受影響。`))return;await api(`/mileage-rate-rules/${r.mileageRateRuleId}?acknowledgeHistoricalImpact=${acknowledged?'true':'false'}`,{method:'DELETE'});setMsg('費率版本已停用；其他版本日期不會被系統自動改寫。');if(edit?.mileageRateRuleId===r.mileageRateRuleId)reset();await load()}catch(e){setMsg(e instanceof Error?e.message:'停用失敗')}finally{setBusy(false)}};
 const deleteRate=async(r:MileageRate)=>{if(isSharedRate(r))return setMsg('系統共用費率為唯讀，無法由組織管理者永久刪除。');setBusy(true);setMsg('');try{const x=await api<MileageRateDeleteImpact>(`/admin/mileage-rate-rules/${r.mileageRateRuleId}/delete-impact`);if(!x.canDelete){setMsg(x.reason||'此補助費率已有歷史使用，只能停用。');return}if(!window.confirm(`永久刪除費率版本「${r.ruleName||r.vehicleType}」？\n\n${r.vehicleType}｜${r.effectiveFrom}～${r.effectiveTo||'無期限'}\n此動作無法復原。`))return;await api(`/admin/mileage-rate-rules/${r.mileageRateRuleId}/permanent`,{method:'DELETE'});setMsg('補助費率版本已永久刪除。');if(edit?.mileageRateRuleId===r.mileageRateRuleId)reset();await load()}catch(e){setMsg(e instanceof Error?e.message:'補助費率刪除失敗')}finally{setBusy(false)}};
 const currentMoto=useMemo(()=>rows.filter(r=>r.vehicleType.toLowerCase()==='motorcycle'&&r.isActive&&r.effectiveFrom<=todayTaipei()&&(!r.effectiveTo||r.effectiveTo>=todayTaipei())).sort((a,b)=>b.effectiveFrom.localeCompare(a.effectiveFrom))[0],[rows]);
 const currentCar=useMemo(()=>rows.filter(r=>r.vehicleType.toLowerCase()==='car'&&r.isActive&&r.effectiveFrom<=todayTaipei()&&(!r.effectiveTo||r.effectiveTo>=todayTaipei())).sort((a,b)=>b.effectiveFrom.localeCompare(a.effectiveFrom))[0],[rows]);
 return <><div className="grid cols-3"><Stat label="機車目前每公里補助" value={money(currentMoto?.ratePerKm)}/><Stat label="汽車目前每公里補助" value={money(currentCar?.ratePerKm)}/><Stat label="日期規則" value="管理者明確維護" hint="同車種期間不可重疊"/></div><div className="card" style={{marginTop:18}}><div className="section-title"><div><h2>{edit?'修改費率版本':'新增費率版本'}</h2><div className="sub">交通工具、開始日、結束日與費率都由管理者明確輸入；系統只驗證同車種期間不可重疊，不自動改寫其他版本日期。</div></div>{edit&&<button className="btn small outline" onClick={reset}>取消修改</button>}</div><div className="grid cols-3"><div className="field"><label>交通工具</label><select value={vehicle} onChange={e=>setVehicle(e.target.value)}><option value="Motorcycle">機車</option><option value="Car">汽車</option></select></div><div className="field"><label>生效日期</label><input type="date" value={from} onChange={e=>setFrom(e.target.value)}/></div><div className="field"><label>失效日期</label><input type="date" value={to} onChange={e=>setTo(e.target.value)}/></div><div className="field"><label>每公里補助</label><input type="number" step="0.01" min="0" value={rate} onChange={e=>setRate(e.target.value)}/></div><div className="field"><label>規則名稱／備註</label><input value={name} onChange={e=>setName(e.target.value)}/></div></div><button className="btn" disabled={busy} onClick={()=>void save()}>{edit?'儲存修改':'新增費率版本'}</button>{msg&&<div className="note">{msg}</div>}<div className="table-wrap"><table><thead><tr><th>交通工具</th><th>生效日期</th><th>失效日期</th><th>每公里</th><th>規則</th><th>狀態</th><th>操作</th></tr></thead><tbody>{rows.map(r=><tr key={r.mileageRateRuleId}><td>{r.vehicleType.toLowerCase()==='car'?'汽車':'機車'}</td><td>{r.effectiveFrom}</td><td>{r.effectiveTo||'無期限'}</td><td>{money(r.ratePerKm)}</td><td>{r.ruleName}</td><td>{r.isActive?'啟用':'停用'}{isSharedRate(r)?'／系統共用':''}</td><td>{isSharedRate(r)?<span className="sub">系統共用（唯讀）</span>:<div className="actions"><button className="btn small secondary" onClick={()=>open(r)}>修改</button>{r.isActive&&<button className="btn small outline" disabled={busy} onClick={()=>void deactivateRate(r)}>停用</button>}<button className="btn small danger" disabled={busy} onClick={()=>void deleteRate(r)}>刪除</button></div>}</td></tr>)}</tbody></table></div></div></>
}
