import{useEffect,useMemo,useState}from"react";
import{api,apiDownload}from"../api";
import{correctionChangeText}from"../correction-ui";
import ProjectLocationManager from "../components/ProjectLocationManager";
import PeopleBulkPanel from "../components/PeopleBulkPanel";
import type{AdminUserAccess,BackgroundJob,CorrectionRequest,DashboardSummary,ImportConfirmResult,ImportPreview,ManagedLocation,MileageRate,Project,ProjectLocationCount,Team,VisitType}from"../types";
import{money,todayTaipei}from"../v160";

type Props={section:'dashboard'|'users'|'locations'|'projects'|'rates'|'corrections'};
const roleLabels:Record<string,string>={visitor:'外訪員',leader:'小組長',admin:'管理者',supervisor:'督導'};

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
 if(section==='users')return <Users busy={busy} setBusy={setBusy} msg={msg} setMsg={setMsg}/>;
 if(section==='locations')return <Locations busy={busy} setBusy={setBusy} msg={msg} setMsg={setMsg}/>;
 if(section==='projects')return <Projects busy={busy} setBusy={setBusy} msg={msg} setMsg={setMsg}/>;
 if(section==='rates')return <Rates busy={busy} setBusy={setBusy} msg={msg} setMsg={setMsg}/>;
 return <Corrections busy={busy} setBusy={setBusy} msg={msg} setMsg={setMsg}/>;
}

function Dashboard({msg,setMsg}:{msg:string;setMsg:(v:string)=>void}){
 const[d,setD]=useState<DashboardSummary|null>(null);useEffect(()=>{api<DashboardSummary>('/dashboard').then(setD).catch(e=>setMsg(e.message))},[]);
 return <><div className="grid cols-5 dashboard-cards"><Stat label="本月行程" value={d?.thisMonthTrips??'—'}/><Stat label="待核准" value={d?.pendingApproval??'—'}/><Stat label="已核准" value={d?.approved??'—'}/><Stat label="待確認地點" value={d?.pendingLocations??'—'}/><Stat label="待處理更正" value={d?.pendingCorrections??'—'} hint={d?.currentRatePerKm!=null?`目前費率 ${money(d.currentRatePerKm)}/km`:undefined}/></div>{msg&&<div className="note">{msg}</div>}</>
}
function Stat({label,value,hint}:{label:string;value:string|number;hint?:string}){return <div className="card stat"><div className="label">{label}</div><div className="value">{value}</div>{hint&&<div className="hint">{hint}</div>}</div>}

function Users({busy,setBusy,msg,setMsg}:{busy:boolean;setBusy:(v:boolean)=>void;msg:string;setMsg:(v:string)=>void}){
 const[rows,setRows]=useState<AdminUserAccess[]>([]),[teams,setTeams]=useState<Team[]>([]),[edit,setEdit]=useState<AdminUserAccess|null>(null),[active,setActive]=useState(true),[roles,setRoles]=useState<string[]>([]),[scopes,setScopes]=useState<Array<{teamId:number;isPrimary:boolean}>>([]);
 const load=()=>Promise.all([api<AdminUserAccess[]>('/admin/users'),api<Team[]>('/teams')]).then(([u,t])=>{setRows(u);setTeams(t)}).catch(e=>setMsg(e.message));useEffect(()=>{void load()},[]);
 const open=(u:AdminUserAccess)=>{setEdit(u);setActive(u.isActive);setRoles([...u.roles]);setScopes(u.teamScopes.map(s=>({teamId:s.teamId,isPrimary:s.isPrimary})))};
 const toggleRole=(r:string)=>setRoles(x=>x.includes(r)?x.filter(v=>v!==r):[...x,r]);
 const toggleTeam=(id:number)=>setScopes(x=>x.some(s=>s.teamId===id)?x.filter(s=>s.teamId!==id):[...x,{teamId:id,isPrimary:x.length===0}]);
 const primary=(id:number)=>setScopes(x=>x.map(s=>({...s,isPrimary:s.teamId===id})));
 const save=async()=>{if(!edit)return;setBusy(true);setMsg('');try{const u=await api<AdminUserAccess>(`/admin/users/${edit.userId}/access`,{method:'PUT',body:JSON.stringify({isActive:active,roles,teamScopes:scopes})});setEdit(u);setMsg('人員角色與小組授權已更新；該使用者需重新登入取得最新權限。');await load()}catch(e){setMsg(e instanceof Error?e.message:'儲存失敗')}finally{setBusy(false)}};
 return <><PeopleBulkPanel onConfirmed={load}/><div className="card" style={{marginTop:18}}><div className="section-title"><div><h2>人員與權限</h2><div className="sub">同一人可有多角色；小組長可授權多個小組，並指定一個主要小組。</div></div></div>{msg&&<div className="note">{msg}</div>}<div className="table-wrap"><table><thead><tr><th>員編</th><th>姓名</th><th>角色</th><th>小組範圍</th><th>狀態</th><th>操作</th></tr></thead><tbody>{rows.map(u=><tr key={u.userId}><td>{u.employeeNo}</td><td>{u.displayName}</td><td>{u.roles.map(r=>roleLabels[r]||r).join('、')||'—'}</td><td>{u.teamScopes.map(s=>`${s.teamName}${s.isPrimary?' ★':''}`).join('、')||'—'}</td><td>{u.isActive?'啟用':'停用'}</td><td><button className="btn small secondary" onClick={()=>open(u)}>維護</button></td></tr>)}</tbody></table></div></div>
 {edit&&<div className="modal"><div className="modal-panel"><h3>人員權限｜{edit.displayName}</h3><label className="check-row"><input type="checkbox" checked={active} onChange={e=>setActive(e.target.checked)}/>帳號啟用</label><h4>角色</h4><div className="checkbox-grid">{Object.entries(roleLabels).map(([r,n])=><label className="check-row" key={r}><input type="checkbox" checked={roles.includes(r)} onChange={()=>toggleRole(r)}/>{n}</label>)}</div><h4>小組授權</h4><div className="scope-list">{teams.map(t=>{const s=scopes.find(x=>x.teamId===t.teamId);return <div className="scope-row" key={t.teamId}><label className="check-row"><input type="checkbox" checked={!!s} onChange={()=>toggleTeam(t.teamId)}/>{t.teamName}</label>{s&&<label className="check-row"><input type="radio" name="primary-team" checked={s.isPrimary} onChange={()=>primary(t.teamId)}/>主要小組</label>}</div>})}</div><div className="modal-sticky-actions"><button className="btn secondary" onClick={()=>setEdit(null)}>取消</button><button className="btn ok" disabled={busy} onClick={()=>void save()}>儲存</button></div></div></div>}</>
}

function ImportPanel({type,onDone}:{type:'locations'|'projects';onDone:()=>void}){
 const[file,setFile]=useState<File|null>(null),[preview,setPreview]=useState<ImportPreview|null>(null),[result,setResult]=useState<ImportConfirmResult|null>(null),[msg,setMsg]=useState(''),[busy,setBusy]=useState(false);
 const template=()=>apiDownload(`/imports/${type}/template`,type==='locations'?'地點主檔匯入範例.xlsx':'專案主檔匯入範例.xlsx');
 const doPreview=async()=>{if(!file)return setMsg('請先選擇 .xlsx、.xls 或 .csv 檔案。');setBusy(true);setMsg('');try{const form=new FormData();form.append('file',file);setPreview(await api<ImportPreview>(`/imports/${type}/preview`,{method:'POST',body:form},120000));setResult(null)}catch(e){setMsg(e instanceof Error?e.message:'預覽失敗')}finally{setBusy(false)}};
 const confirm=async()=>{if(!preview)return;setBusy(true);try{const r=await api<ImportConfirmResult>(`/imports/${preview.importBatchId}/confirm`,{method:'POST'},120000);setResult(r);setMsg('匯入完成。');onDone()}catch(e){setMsg(e instanceof Error?e.message:'匯入失敗')}finally{setBusy(false)}};
 return <div className="import-panel"><div className="section-title"><strong>批次匯入</strong><button className="btn small outline" onClick={()=>void template()}>下載 Excel 範例</button></div><div className="actions"><input type="file" accept=".xlsx,.xls,.csv" onChange={e=>{setFile(e.target.files?.[0]||null);setPreview(null)}}/><button className="btn secondary" disabled={busy} onClick={()=>void doPreview()}>預覽與驗證</button></div>{msg&&<div className="note">{msg}</div>}{preview&&<><div className={`note ${preview.errorCount?'danger-note':'ok-note'}`}>共 {preview.totalCount} 列｜可匯入 {preview.validCount}｜錯誤 {preview.errorCount}{preview.totalCount>100?'｜畫面僅顯示前 100 筆預覽，系統已驗證全部資料':''}</div><div className="table-wrap compact-table"><table><thead><tr><th>列</th><th>類型</th><th>動作</th><th>Key</th><th>結果</th></tr></thead><tbody>{preview.items.slice(0,100).map((x,i)=><tr key={i}><td>{x.rowNumber}</td><td>{x.entityType}</td><td>{x.action}</td><td>{x.displayKey}</td><td>{x.errorMessage||'OK'}</td></tr>)}</tbody></table></div><div className="actions">{preview.errorCount>0&&<button className="btn outline" onClick={()=>void apiDownload(`/imports/${preview.importBatchId}/errors.xlsx`,'匯入錯誤.xlsx')}>下載錯誤 Excel</button>}<button className="btn ok" disabled={preview.errorCount>0||busy} onClick={()=>void confirm()}>確認匯入</button></div></>}{result&&<div className="note ok-note">新增 {result.created}／更新 {result.updated}／無變更 {result.unchanged}／失敗 {result.failed}</div>}</div>
}

function Locations({busy,setBusy,msg,setMsg}:{busy:boolean;setBusy:(v:boolean)=>void;msg:string;setMsg:(v:string)=>void}){
 const[rows,setRows]=useState<ManagedLocation[]>([]),[teams,setTeams]=useState<Team[]>([]),[edit,setEdit]=useState<ManagedLocation|null>(null);
 const[teamId,setTeamId]=useState(''),[name,setName]=useState(''),[city,setCity]=useState(''),[district,setDistrict]=useState(''),[address,setAddress]=useState(''),[plus,setPlus]=useState('');
 const[q,setQ]=useState(''),[filterTeam,setFilterTeam]=useState(''),[filterCity,setFilterCity]=useState(''),[filterDistrict,setFilterDistrict]=useState(''),[filterGeocode,setFilterGeocode]=useState(''),[filterActive,setFilterActive]=useState('');
 const[page,setPage]=useState(1),[pageSize,setPageSize]=useState(50),[totalCount,setTotalCount]=useState(0),[totalPages,setTotalPages]=useState(0);
 const[selected,setSelected]=useState<number[]>([]),[allFiltered,setAllFiltered]=useState(false),[job,setJob]=useState<BackgroundJob|null>(null);

 const params=()=>{
   const p=new URLSearchParams({page:String(page),pageSize:String(pageSize)});
   if(q.trim())p.set('q',q.trim());
   if(filterTeam)p.set('teamId',filterTeam);
   if(filterCity.trim())p.set('city',filterCity.trim());
   if(filterDistrict.trim())p.set('district',filterDistrict.trim());
   if(filterGeocode)p.set('geocodingStatus',filterGeocode);
   if(filterActive)p.set('isActive',filterActive);
   return p;
 };
 const load=()=>Promise.all([api<ManagedLocationPage>(`/managed-locations/search?${params().toString()}`),api<Team[]>('/teams')]).then(([result,t])=>{setRows(result.items);setTotalCount(result.totalCount);setTotalPages(result.totalPages);setTeams(t)}).catch(e=>setMsg(e.message));
 useEffect(()=>{void load()},[page,pageSize,q,filterTeam,filterCity,filterDistrict,filterGeocode,filterActive]);

 const clearSelection=()=>{setSelected([]);setAllFiltered(false)};
 useEffect(()=>{clearSelection()},[page,pageSize,q,filterTeam,filterCity,filterDistrict,filterGeocode,filterActive]);

 const reset=()=>{setEdit(null);setTeamId('');setName('');setCity('');setDistrict('');setAddress('');setPlus('')};
 const open=(l:ManagedLocation)=>{setEdit(l);setTeamId(l.teamId?String(l.teamId):'');setName(l.locationName);setCity(l.city||'');setDistrict(l.district||'');setAddress(l.address||'');setPlus(l.plusCode||'')};
 const save=async()=>{setBusy(true);try{const body={teamId:teamId?Number(teamId):null,locationName:name,locationType:'Customer',city:city||null,district:district||null,address:address||null,plusCode:plus||null,isActive:edit?.isActive??false,rowVersion:edit?.rowVersion||null};if(edit)await api(`/managed-locations/${edit.locationId}`,{method:'PUT',body:JSON.stringify(body)});else await api('/managed-locations',{method:'POST',body:JSON.stringify(body)});setMsg(edit?'地點已修改，需重新解析/發布。':'地點已新增，需解析/發布後才會成為正式地點。');reset();await load()}catch(e){setMsg(e instanceof Error?e.message:'儲存失敗')}finally{setBusy(false)}};

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

 return <><div className="grid cols-2"><div className="card"><div className="section-title"><h2>{edit?'修改地點':'新增地點'}</h2>{edit&&<button className="btn small outline" onClick={reset}>取消修改</button>}</div><div className="grid cols-2"><div className="field"><label>小組</label><select value={teamId} onChange={e=>setTeamId(e.target.value)}><option value="">全組織</option>{teams.map(t=><option key={t.teamId} value={t.teamId}>{t.teamName}</option>)}</select></div><div className="field"><label>地點名稱</label><input value={name} onChange={e=>setName(e.target.value)}/></div><div className="field"><label>縣市</label><input value={city} onChange={e=>setCity(e.target.value)}/></div><div className="field"><label>鄉鎮區</label><input value={district} onChange={e=>setDistrict(e.target.value)}/></div><div className="field span-2"><label>地址</label><input value={address} onChange={e=>setAddress(e.target.value)}/></div><div className="field span-2"><label>Plus Code</label><input value={plus} onChange={e=>setPlus(e.target.value)}/></div></div><button className="btn" disabled={busy} onClick={()=>void save()}>{edit?'儲存修改':'新增地點'}</button></div><div className="card"><ImportPanel type="locations" onDone={load}/></div></div>{msg&&<div className="note" style={{marginTop:14}}>{msg}</div>}{job&&<div className="note">背景工作：{job.status}｜成功 {job.successCount}／失敗 {job.failedCount}／總計 {job.totalCount}</div>}

 <div className="card" style={{marginTop:18}}><div className="section-title"><div><h2>地點主檔</h2><div className="sub">大量資料採伺服器端搜尋與分頁；解析可選本頁或全部符合目前條件的地點。</div></div><button className="btn ok" onClick={()=>void geocode()} disabled={busy||(!allFiltered&&!selected.length)}>批次解析／發布</button></div>
 <div className="grid cols-2">
  <div className="field"><label>搜尋</label><input value={q} placeholder="代碼／名稱／地址" onChange={e=>{setPage(1);setQ(e.target.value)}}/></div>
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
 <div className="table-wrap"><table><thead><tr><th><input type="checkbox" checked={pageAllSelected&&!allFiltered} onChange={e=>{setAllFiltered(false);togglePage(e.target.checked)}}/></th><th>地點代碼</th><th>地點</th><th>類型</th><th>小組</th><th>地址</th><th>解析</th><th>狀態</th><th>操作</th></tr></thead><tbody>{rows.map(l=>{const canPromote=l.isTemporary&&l.isActive&&l.approvalStatus==='Approved'&&l.geocodingStatus==='Completed';return <tr key={l.locationId}><td><input type="checkbox" checked={!allFiltered&&selected.includes(l.locationId)} disabled={allFiltered} onChange={e=>setSelected(x=>e.target.checked?[...x,l.locationId]:x.filter(id=>id!==l.locationId))}/></td><td>{l.locationCode}</td><td>{l.locationName}</td><td>{l.isTemporary?<span className="pill warn">臨時</span>:<span className="pill ok">正式</span>}</td><td>{l.teamName||'全組織'}</td><td>{l.address||l.plusCode||'—'}</td><td>{l.geocodingStatus}</td><td>{l.isActive?'啟用':l.approvalStatus}</td><td><div className="actions"><button className="btn small secondary" onClick={()=>open(l)}>修改</button>{canPromote&&<button className="btn small ok" disabled={busy} onClick={()=>void promote(l)}>轉正式</button>}{l.isActive&&<button className="btn small outline" disabled={busy} onClick={()=>void deactivate(l)}>停用</button>}<button className="btn small outline" disabled={busy} onClick={()=>void permanentDelete(l)}>刪除</button></div></td></tr>})}</tbody></table></div>
 <div className="actions" style={{justifyContent:'space-between',marginTop:12}}><span className="sub">{totalCount===0?'0 筆':`${(page-1)*pageSize+1}–${Math.min(page*pageSize,totalCount)} / 共 ${totalCount} 筆`}</span><div className="actions"><button className="btn small outline" disabled={page<=1} onClick={()=>setPage(p=>Math.max(1,p-1))}>上一頁</button><span className="sub">第 {page} / {Math.max(totalPages,1)} 頁</span><button className="btn small outline" disabled={page>=totalPages} onClick={()=>setPage(p=>p+1)}>下一頁</button></div></div></div></>
}


function Projects({busy,setBusy,msg,setMsg}:{busy:boolean;setBusy:(v:boolean)=>void;msg:string;setMsg:(v:string)=>void}){
 const[projects,setProjects]=useState<Project[]>([]),[types,setTypes]=useState<VisitType[]>([]),[teams,setTeams]=useState<Team[]>([]),[locationCounts,setLocationCounts]=useState<Record<number,number>>({}),[edit,setEdit]=useState<Project|null>(null),[code,setCode]=useState(''),[name,setName]=useState(''),[teamId,setTeamId]=useState(''),[mode,setMode]=useState('List'),[start,setStart]=useState(todayTaipei()),[end,setEnd]=useState(''),[desc,setDesc]=useState('');
 const[typeEdit,setTypeEdit]=useState<VisitType|null>(null),[typeCode,setTypeCode]=useState(''),[typeName,setTypeName]=useState(''),[typeDesc,setTypeDesc]=useState(''),[sort,setSort]=useState('10');
 const load=()=>Promise.all([api<Project[]>('/projects'),api<VisitType[]>('/visit-types'),api<Team[]>('/teams'),api<ProjectLocationCount[]>('/admin/projects/location-counts')]).then(([p,v,t,c])=>{setProjects(p);setTypes(v);setTeams(t);setLocationCounts(Object.fromEntries(c.map(x=>[x.projectId,x.count])));if(!typeEdit)setSort(String(Math.max(0,...v.map(x=>x.sortOrder))+10))}).catch(e=>setMsg(e.message));useEffect(()=>{void load()},[]);
 const reset=()=>{setEdit(null);setCode('');setName('');setTeamId('');setMode('List');setStart(todayTaipei());setEnd('');setDesc('')};
 const open=(p:Project)=>{setEdit(p);setCode(p.projectCode);setName(p.projectName);setTeamId(p.teamId?String(p.teamId):'');setMode(p.locationMode);setStart(p.startDate||todayTaipei());setEnd(p.endDate||'');setDesc(p.description||'')};
 const save=async()=>{setBusy(true);try{const body={teamId:teamId?Number(teamId):null,projectCode:code,projectName:name,description:desc||null,locationMode:mode,startDate:start||null,endDate:end||null,isActive:true};if(edit)await api(`/projects/${edit.projectId}`,{method:'PUT',body:JSON.stringify(body)});else await api('/projects',{method:'POST',body:JSON.stringify(body)});setMsg(edit?'專案已修改。':'專案已新增。');reset();await load()}catch(e){setMsg(e instanceof Error?e.message:'儲存失敗')}finally{setBusy(false)}};
 const deactivateProject=async(p:Project)=>{if(!window.confirm(`確定停用專案「${p.projectName}」？歷史 Snapshot 不受影響。`))return;setBusy(true);try{await api(`/projects/${p.projectId}`,{method:'DELETE'});setMsg('專案已停用。');if(edit?.projectId===p.projectId)reset();await load()}catch(e){setMsg(e instanceof Error?e.message:'停用失敗')}finally{setBusy(false)}};
 const resetType=()=>{setTypeEdit(null);setTypeCode('');setTypeName('');setTypeDesc('');setSort(String(Math.max(0,...types.map(x=>x.sortOrder))+10))};
 const openType=(v:VisitType)=>{setTypeEdit(v);setTypeCode(v.visitTypeCode);setTypeName(v.visitTypeName);setTypeDesc(v.description||'');setSort(String(v.sortOrder))};
 const saveType=async()=>{setBusy(true);try{const body={visitTypeCode:typeCode,visitTypeName:typeName,description:typeDesc||null,sortOrder:Number(sort)||0,isActive:true};if(typeEdit)await api(`/visit-types/${typeEdit.visitTypeId}`,{method:'PUT',body:JSON.stringify(body)});else await api('/visit-types',{method:'POST',body:JSON.stringify(body)});setMsg(typeEdit?'拜訪形式已修改。':'拜訪形式已新增。');resetType();await load()}catch(e){setMsg(e instanceof Error?e.message:'儲存失敗')}finally{setBusy(false)}};
 const deactivateType=async(v:VisitType)=>{if(!window.confirm(`確定停用拜訪形式「${v.visitTypeName}」？歷史 Snapshot 不受影響。`))return;setBusy(true);try{await api(`/visit-types/${v.visitTypeId}`,{method:'DELETE'});setMsg('拜訪形式已停用。');if(typeEdit?.visitTypeId===v.visitTypeId)resetType();await load()}catch(e){setMsg(e instanceof Error?e.message:'停用失敗')}finally{setBusy(false)}};
 return <><div className="grid cols-2"><div className="card"><div className="section-title"><h2>專案主檔</h2>{edit&&<button className="btn small outline" onClick={reset}>取消修改</button>}</div><div className="grid cols-2"><div className="field"><label>專案代碼</label><input value={code} onChange={e=>setCode(e.target.value)}/></div><div className="field"><label>專案名稱</label><input value={name} onChange={e=>setName(e.target.value)}/></div><div className="field"><label>歸屬小組</label><select value={teamId} onChange={e=>setTeamId(e.target.value)}><option value="">全組織</option>{teams.map(t=><option key={t.teamId} value={t.teamId}>{t.teamName}</option>)}</select></div><div className="field"><label>預設地點方式</label><select value={mode} onChange={e=>setMode(e.target.value)}><option value="List">專案清單優先</option><option value="SelfMaintained">臨時維護優先</option></select></div><div className="field"><label>開始日期</label><input type="date" value={start} onChange={e=>setStart(e.target.value)}/></div><div className="field"><label>結束日期</label><input type="date" value={end} onChange={e=>setEnd(e.target.value)}/></div><div className="field span-2"><label>說明</label><input value={desc} onChange={e=>setDesc(e.target.value)}/></div></div><button className="btn" disabled={busy} onClick={()=>void save()}>{edit?'儲存專案':'新增專案'}</button>{edit&&mode==='List'&&<ProjectLocationManager projectId={edit.projectId} projectName={edit.projectName}/>}<div className="table-wrap"><table><thead><tr><th>專案</th><th>歸屬小組</th><th>有效期間</th><th>地點規則</th><th>固定地點</th><th>狀態</th><th>操作</th></tr></thead><tbody>{projects.map(p=>{const teamName=p.teamId?teams.find(t=>t.teamId===p.teamId)?.teamName||`Team ${p.teamId}`:'全組織';const period=`${p.startDate||'不限'}～${p.endDate||'無期限'}`;return <tr key={p.projectId}><td><strong>{p.projectCode}</strong><div>{p.projectName}</div>{p.description&&<div className="sub">{p.description}</div>}</td><td>{teamName}</td><td>{period}</td><td>{p.locationMode==='List'?'專案清單優先':'臨時維護優先'}</td><td>{p.locationMode==='List'?`${locationCounts[p.projectId]??0} 筆`:'—'}</td><td>{p.isActive?<span className="pill ok">啟用</span>:<span className="pill warn">停用</span>}</td><td><div className="actions"><button className="btn small secondary" onClick={()=>open(p)}>修改</button>{p.isActive&&<button className="btn small outline" disabled={busy} onClick={()=>void deactivateProject(p)}>停用</button>}</div></td></tr>})}</tbody></table></div></div><div className="card"><ImportPanel type="projects" onDone={load}/><hr/><div className="section-title"><h2>拜訪形式</h2>{typeEdit&&<button className="btn small outline" onClick={resetType}>取消修改</button>}</div><div className="grid cols-2"><div className="field"><label>代碼</label><input value={typeCode} onChange={e=>setTypeCode(e.target.value)}/></div><div className="field"><label>名稱</label><input value={typeName} onChange={e=>setTypeName(e.target.value)}/></div><div className="field"><label>顯示順序</label><input type="number" value={sort} onChange={e=>setSort(e.target.value)}/></div><div className="field"><label>說明</label><input value={typeDesc} onChange={e=>setTypeDesc(e.target.value)}/></div></div><button className="btn" onClick={()=>void saveType()} disabled={busy}>{typeEdit?'儲存拜訪形式':'新增拜訪形式'}</button><div className="route-list">{types.map(v=><div className="route-item" key={v.visitTypeId}><div className="route-index">{v.sortOrder}</div><div><div className="route-name">{v.visitTypeName}</div><div className="route-address">{v.visitTypeCode}{!v.isActive?'｜停用':''}</div></div><div className="actions"><button className="btn small secondary" onClick={()=>openType(v)}>修改</button>{v.isActive&&<button className="btn small outline" disabled={busy} onClick={()=>void deactivateType(v)}>停用</button>}</div></div>)}</div></div></div>{msg&&<div className="note">{msg}</div>}</>
}

type MileageRateImpact={effectiveFrom:string;vehicleType:string;approvedTripCount:number;firstApprovedVisitDate:string|null;lastApprovedVisitDate:string|null;requiresAcknowledgement:boolean};

function Rates({busy,setBusy,msg,setMsg}:{busy:boolean;setBusy:(v:boolean)=>void;msg:string;setMsg:(v:string)=>void}){
 const[rows,setRows]=useState<MileageRate[]>([]),[edit,setEdit]=useState<MileageRate|null>(null),[name,setName]=useState(''),[rate,setRate]=useState('2.50'),[from,setFrom]=useState(todayTaipei());
 const load=()=>api<MileageRate[]>('/mileage-rate-rules').then(setRows).catch(e=>setMsg(e.message));useEffect(()=>{void load()},[]);
 const reset=()=>{setEdit(null);setName('');setRate('2.50');setFrom(todayTaipei())};const open=(r:MileageRate)=>{setEdit(r);setName(r.ruleName);setRate(String(r.ratePerKm));setFrom(r.effectiveFrom)};
 const impact=async(effectiveFrom:string)=>api<MileageRateImpact>(`/mileage-rate-rules/impact?effectiveFrom=${encodeURIComponent(effectiveFrom)}&vehicleType=Motorcycle`);
 const impactText=(x:MileageRateImpact,verb:string)=>`此異動自 ${x.effectiveFrom} 起可能影響費率判讀；該日期之後已有 ${x.approvedTripCount} 筆已核准且具有費率快照的行程${x.firstApprovedVisitDate&&x.lastApprovedVisitDate?`（${x.firstApprovedVisitDate}～${x.lastApprovedVisitDate}）`:''}。\n\n${verb}後不會自動重算既有 Snapshot，因此可能出現同一行程日期具有不同歷史費率。若需追溯調整，應透過更正流程建立新 Snapshot。\n\n是否仍要繼續？`;
 const save=async()=>{setBusy(true);setMsg('');try{const nextRate=Number(rate);const material=!edit||edit.ratePerKm!==nextRate||edit.effectiveFrom!==from||edit.vehicleType!=='Motorcycle'||!edit.isActive;let acknowledged=false;if(material){const impactFrom=edit&&edit.effectiveFrom<from?edit.effectiveFrom:from;const x=await impact(impactFrom);if(x.requiresAcknowledgement){if(!window.confirm(impactText(x,edit?'修改費率版本':'新增費率版本')))return;acknowledged=true}}const body={ruleName:name,vehicleType:'Motorcycle',ratePerKm:nextRate,effectiveFrom:from,effectiveTo:null,isActive:true,acknowledgeHistoricalImpact:acknowledged};if(edit)await api(`/mileage-rate-rules/${edit.mileageRateRuleId}`,{method:'PUT',body:JSON.stringify(body)});else await api('/mileage-rate-rules',{method:'POST',body:JSON.stringify(body)});setMsg('費率版本已儲存；前後版本失效日期已由系統自動銜接。歷史 Snapshot 不會自動重算。');reset();await load()}catch(e){setMsg(e instanceof Error?e.message:'儲存失敗')}finally{setBusy(false)}};
 const deactivateRate=async(r:MileageRate)=>{setBusy(true);setMsg('');try{const x=await impact(r.effectiveFrom);let acknowledged=false;if(x.requiresAcknowledgement){if(!window.confirm(impactText(x,'停用此費率版本')))return;acknowledged=true}else if(!window.confirm(`確定停用 ${r.effectiveFrom} 起生效的費率版本？歷史核准費率快照不受影響。`))return;await api(`/mileage-rate-rules/${r.mileageRateRuleId}?acknowledgeHistoricalImpact=${acknowledged?'true':'false'}`,{method:'DELETE'});setMsg('費率版本已停用，剩餘有效版本日期已重新銜接；歷史 Snapshot 不會自動重算。');if(edit?.mileageRateRuleId===r.mileageRateRuleId)reset();await load()}catch(e){setMsg(e instanceof Error?e.message:'停用失敗')}finally{setBusy(false)}};
 const current=useMemo(()=>rows.filter(r=>r.isActive&&r.effectiveFrom<=todayTaipei()&&(!r.effectiveTo||r.effectiveTo>=todayTaipei())).sort((a,b)=>b.effectiveFrom.localeCompare(a.effectiveFrom))[0],[rows]);
 return <><div className="grid cols-3"><Stat label="目前每公里補助" value={money(current?.ratePerKm)}/><Stat label="版本數" value={rows.length}/><Stat label="日期規則" value="自動銜接" hint="只輸入生效日"/></div><div className="card" style={{marginTop:18}}><div className="section-title"><div><h2>{edit?'修改費率版本':'新增費率版本'}</h2><div className="sub">管理者只維護生效日期；上一版失效日由後端自動設定為新生效日前一天。</div></div>{edit&&<button className="btn small outline" onClick={reset}>取消修改</button>}</div><div className="grid cols-3"><div className="field"><label>生效日期</label><input type="date" value={from} onChange={e=>setFrom(e.target.value)}/></div><div className="field"><label>每公里補助</label><input type="number" step="0.01" min="0" value={rate} onChange={e=>setRate(e.target.value)}/></div><div className="field"><label>規則名稱／備註</label><input value={name} onChange={e=>setName(e.target.value)}/></div></div><button className="btn" disabled={busy} onClick={()=>void save()}>{edit?'儲存修改':'新增費率版本'}</button>{msg&&<div className="note">{msg}</div>}<div className="table-wrap"><table><thead><tr><th>生效日期</th><th>失效日期（系統）</th><th>每公里</th><th>規則</th><th>狀態</th><th>操作</th></tr></thead><tbody>{rows.map(r=><tr key={r.mileageRateRuleId}><td>{r.effectiveFrom}</td><td>{r.effectiveTo||'無期限'}</td><td>{money(r.ratePerKm)}</td><td>{r.ruleName}</td><td>{r.isActive?'啟用':'停用'}</td><td><div className="actions"><button className="btn small secondary" onClick={()=>open(r)}>修改</button>{r.isActive&&<button className="btn small outline" disabled={busy} onClick={()=>void deactivateRate(r)}>停用</button>}</div></td></tr>)}</tbody></table></div></div></>
}

function Corrections({busy,setBusy,msg,setMsg}:{busy:boolean;setBusy:(v:boolean)=>void;msg:string;setMsg:(v:string)=>void}){
 const[rows,setRows]=useState<CorrectionRequest[]>([]);const load=()=>api<CorrectionRequest[]>('/corrections').then(setRows).catch(e=>setMsg(e.message));useEffect(()=>{void load()},[]);
 const close=async(r:CorrectionRequest,approve:boolean)=>{const comments=window.prompt(approve?'管理者結案說明（選填）':'拒絕原因')||'';setBusy(true);try{await api(`/corrections/${r.correctionRequestId}/admin-close`,{method:'POST',body:JSON.stringify({approve,comments,rowVersion:r.rowVersion})});setMsg(approve?'更正已結案並建立新 Snapshot。':'更正申請已拒絕。');await load()}catch(e){setMsg(e instanceof Error?e.message:'操作失敗')}finally{setBusy(false)}};
 return <div className="card"><div className="section-title"><div><h2>更正流程</h2><div className="sub">財務性更正由小組長審核後，管理者結案；原核准 Snapshot 永久保留。</div></div></div>{msg&&<div className="note">{msg}</div>}<div className="table-wrap"><table><thead><tr><th>申請日</th><th>Trip</th><th>外訪員</th><th>小組</th><th>原因</th><th>差異</th><th>狀態</th><th>操作</th></tr></thead><tbody>{rows.map(r=><tr key={r.correctionRequestId}><td>{r.requestedAt.slice(0,10)}</td><td>{r.tripNo}</td><td>{r.visitorName}</td><td>{r.teamName||'—'}</td><td>{r.reason}</td><td>{r.changes.length?<div className="correction-change-list">{r.changes.map((c,i)=><div key={i}>{correctionChangeText(c)}</div>)}</div>:'—'}</td><td>{r.status}</td><td>{r.status==='PendingAdminClose'&&<div className="actions"><button className="btn small ok" disabled={busy} onClick={()=>void close(r,true)}>結案</button><button className="btn small danger" disabled={busy} onClick={()=>void close(r,false)}>拒絕</button></div>}</td></tr>)}</tbody></table></div></div>
}
