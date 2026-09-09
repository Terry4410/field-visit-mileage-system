import{useEffect,useState}from"react";
import type{ReactNode}from"react";
import{api}from"../api";
import SmartLocationPicker from"../components/SmartLocationPicker";
import{Pagination}from"../components/QueryControls";
import type{PagedResult,SmartLocationItem,V180PersonRow}from"../types";
import{usePagedQuery}from"../use-query";
import{todayTaipei}from"../v160";
import type{V180CenterAdmin,V180TeamAdmin}from"../v180-team-center-ui";
import{
 deploymentSiteEndDate,
 deploymentSiteErrorMessage,
 deploymentSiteLifecycleStatus,
 V180_DEPLOYMENT_SITE_API
}from"../v180-deployment-site-ui";
import type{
 V180DeploymentSite,
 V180DeploymentSiteLocationAssignment,
 V180EmploymentDeploymentSiteAssignment,
 V180TeamDeploymentSiteAssignment
}from"../v180-deployment-site-ui";

export default function V180DeploymentSiteAdminPage(){
 const today=todayTaipei();
 const[msg,setMsg]=useState(""),[busy,setBusy]=useState(false);
 const[keyword,setKeyword]=useState(""),[centerFilter,setCenterFilter]=useState(""),[includeInactive,setIncludeInactive]=useState(true);
 const siteQuery=usePagedQuery<V180DeploymentSite>(V180_DEPLOYMENT_SITE_API.sites,{keyword,centerId:centerFilter||undefined,includeInactive});
 const sites=siteQuery.data.items;
 const[selectedSite,setSelectedSite]=useState<V180DeploymentSite|null>(null);
 const[centers,setCenters]=useState<V180CenterAdmin[]>([]),[teams,setTeams]=useState<V180TeamAdmin[]>([]);

 const[editSite,setEditSite]=useState<V180DeploymentSite|null>(null),[siteCenterId,setSiteCenterId]=useState<number|null>(null);
 const[siteCode,setSiteCode]=useState(""),[siteName,setSiteName]=useState(""),[siteFrom,setSiteFrom]=useState(today),[siteTo,setSiteTo]=useState(""),[siteActive,setSiteActive]=useState(true),[siteNotes,setSiteNotes]=useState("");

 const[locationRows,setLocationRows]=useState<V180DeploymentSiteLocationAssignment[]>([]);
 const[editLocation,setEditLocation]=useState<V180DeploymentSiteLocationAssignment|null>(null),[selectedLocation,setSelectedLocation]=useState<SmartLocationItem|null>(null);
 const[locationFrom,setLocationFrom]=useState(today),[locationTo,setLocationTo]=useState(""),[locationReason,setLocationReason]=useState("");

 const[teamRows,setTeamRows]=useState<V180TeamDeploymentSiteAssignment[]>([]);
 const[editTeam,setEditTeam]=useState<V180TeamDeploymentSiteAssignment|null>(null),[teamId,setTeamId]=useState<number|null>(null),[teamFrom,setTeamFrom]=useState(today),[teamTo,setTeamTo]=useState("");

 const[employmentRows,setEmploymentRows]=useState<V180EmploymentDeploymentSiteAssignment[]>([]);
 const[editEmployment,setEditEmployment]=useState<V180EmploymentDeploymentSiteAssignment|null>(null),[employmentId,setEmploymentId]=useState<number|null>(null),[employmentPrimary,setEmploymentPrimary]=useState(false),[employmentFrom,setEmploymentFrom]=useState(today),[employmentTo,setEmploymentTo]=useState("");
 const[personKeyword,setPersonKeyword]=useState("");
 const peopleQuery=usePagedQuery<V180PersonRow>("/admin/v180/people",{keyword:personKeyword,includeInactive:false});

 const loadOptions=async()=>{
  try{
   const[centerResult,teamResult]=await Promise.all([
    api<PagedResult<V180CenterAdmin>>("/admin/v180/centers?includeInactive=true&page=1&pageSize=100"),
    api<PagedResult<V180TeamAdmin>>("/admin/v180/teams?includeInactive=true&page=1&pageSize=100")
   ]);
   setCenters(centerResult.items);setTeams(teamResult.items);
  }catch(error){setMsg(deploymentSiteErrorMessage(error,"中心／小組選項載入失敗"))}
 };
 const loadHistory=async(siteId:number|null=selectedSite?.deploymentSiteId??null)=>{
  if(!siteId){setLocationRows([]);setTeamRows([]);setEmploymentRows([]);return}
  try{
   const[locations,teamAssignments,employmentAssignments]=await Promise.all([
    api<V180DeploymentSiteLocationAssignment[]>(V180_DEPLOYMENT_SITE_API.locationHistory(siteId)),
    api<V180TeamDeploymentSiteAssignment[]>(V180_DEPLOYMENT_SITE_API.teamHistory(siteId)),
    api<V180EmploymentDeploymentSiteAssignment[]>(V180_DEPLOYMENT_SITE_API.employmentHistory(siteId))
   ]);
   setLocationRows(locations);setTeamRows(teamAssignments);setEmploymentRows(employmentAssignments);
  }catch(error){setMsg(deploymentSiteErrorMessage(error,"派駐點關聯歷史載入失敗"))}
 };
 const reloadSite=async(siteId:number)=>{
  const fresh=await api<V180DeploymentSite>(V180_DEPLOYMENT_SITE_API.site(siteId));
  setSelectedSite(fresh);siteQuery.reload();await loadHistory(siteId);
  if(editSite?.deploymentSiteId===siteId)setEditSite(fresh);
 };
 useEffect(()=>{void loadOptions()},[]);
 useEffect(()=>{void loadHistory(selectedSite?.deploymentSiteId??null)},[selectedSite?.deploymentSiteId]);

 const selectSite=(site:V180DeploymentSite)=>{setSelectedSite(site);resetLocation();resetTeam();resetEmployment()};
 const resetSite=()=>{setEditSite(null);setSiteCenterId(null);setSiteCode("");setSiteName("");setSiteFrom(today);setSiteTo("");setSiteActive(true);setSiteNotes("")};
 const openSite=(site:V180DeploymentSite)=>{setEditSite(site);setSiteCenterId(site.centerId);setSiteCode(site.code);setSiteName(site.name);setSiteFrom(site.effectiveFrom);setSiteTo(site.effectiveTo||"");setSiteActive(site.isActive);setSiteNotes(site.notes||"")};
 const saveSite=async()=>{
  if(!siteCenterId||!siteCode.trim()||!siteName.trim())return setMsg("中心、派駐點代碼與名稱必填。");
  if(siteTo&&siteTo<siteFrom)return setMsg("派駐點結束日不得早於開始日。");
  if(!siteActive&&!siteTo)return setMsg("停用派駐點必須填寫結束日。");
  setBusy(true);setMsg("");
  try{
   const body={centerId:siteCenterId,code:siteCode.trim(),name:siteName.trim(),effectiveFrom:siteFrom,effectiveTo:siteTo||null,notes:siteNotes.trim()||null,isActive:siteActive};
   const result=editSite
    ?await api<V180DeploymentSite>(V180_DEPLOYMENT_SITE_API.site(editSite.deploymentSiteId),{method:"PUT",body:JSON.stringify({...body,version:editSite.version})})
    :await api<V180DeploymentSite>(V180_DEPLOYMENT_SITE_API.sites,{method:"POST",body:JSON.stringify(body)});
   setMsg(editSite?"派駐點已更新。":"派駐點已建立。");resetSite();siteQuery.reload();
   if(selectedSite?.deploymentSiteId===result.deploymentSiteId)await reloadSite(result.deploymentSiteId);
  }catch(error){setMsg(deploymentSiteErrorMessage(error,"派駐點儲存失敗"))}finally{setBusy(false)}
 };
 const deactivateSite=async(site:V180DeploymentSite)=>{
  if(!window.confirm(`確定停用派駐點「${site.name}」？既有歷史不會刪除。`))return;
  setBusy(true);setMsg("");
  try{await api(V180_DEPLOYMENT_SITE_API.deactivateSite(site.deploymentSiteId),{method:"POST",body:JSON.stringify({effectiveTo:deploymentSiteEndDate(site.effectiveFrom,today),version:site.version})});setMsg("派駐點已停用。");await reloadSite(site.deploymentSiteId)}
  catch(error){setMsg(deploymentSiteErrorMessage(error,"派駐點停用失敗"))}finally{setBusy(false)}
 };

 const resetLocation=()=>{setEditLocation(null);setSelectedLocation(null);setLocationFrom(today);setLocationTo("");setLocationReason("")};
 const openLocation=(row:V180DeploymentSiteLocationAssignment)=>{setEditLocation(row);setSelectedLocation(null);setLocationFrom(row.effectiveFrom);setLocationTo(row.effectiveTo||"");setLocationReason(row.changeReason||"")};
 const saveLocation=async()=>{
  if(!selectedSite)return;
  if(!editLocation&&!selectedLocation)return setMsg("請先選擇 Location。");
  if(locationTo&&locationTo<locationFrom)return setMsg("Location 關聯結束日不得早於開始日。");
  setBusy(true);setMsg("");
  try{
   if(editLocation)await api(V180_DEPLOYMENT_SITE_API.locationAssignment(editLocation.deploymentSiteLocationAssignmentId),{method:"PUT",body:JSON.stringify({effectiveFrom:locationFrom,effectiveTo:locationTo||null,changeReason:locationReason.trim()||null,version:editLocation.version})});
   else await api(V180_DEPLOYMENT_SITE_API.locationAssignments,{method:"POST",body:JSON.stringify({deploymentSiteId:selectedSite.deploymentSiteId,locationId:selectedLocation!.locationId,effectiveFrom:locationFrom,effectiveTo:locationTo||null,changeReason:locationReason.trim()||null})});
   setMsg(editLocation?"Location 關聯已更新。":"Location 關聯已建立。");resetLocation();await reloadSite(selectedSite.deploymentSiteId);
  }catch(error){setMsg(deploymentSiteErrorMessage(error,"Location 關聯儲存失敗"))}finally{setBusy(false)}
 };
 const endLocation=async(row:V180DeploymentSiteLocationAssignment)=>endAssignment(V180_DEPLOYMENT_SITE_API.endLocationAssignment(row.deploymentSiteLocationAssignmentId),row.effectiveFrom,row.version,"Location 關聯已結束。");

 const resetTeam=()=>{setEditTeam(null);setTeamId(null);setTeamFrom(today);setTeamTo("")};
 const openTeam=(row:V180TeamDeploymentSiteAssignment)=>{setEditTeam(row);setTeamId(row.teamId);setTeamFrom(row.effectiveFrom);setTeamTo(row.effectiveTo||"")};
 const saveTeam=async()=>{
  if(!selectedSite||!teamId)return setMsg("請先選擇 Team。");
  if(teamTo&&teamTo<teamFrom)return setMsg("Team 關聯結束日不得早於開始日。");
  setBusy(true);setMsg("");
  try{
   if(editTeam)await api(V180_DEPLOYMENT_SITE_API.teamAssignment(editTeam.teamDeploymentSiteAssignmentId),{method:"PUT",body:JSON.stringify({effectiveFrom:teamFrom,effectiveTo:teamTo||null,version:editTeam.version})});
   else await api(V180_DEPLOYMENT_SITE_API.teamAssignments,{method:"POST",body:JSON.stringify({teamId,deploymentSiteId:selectedSite.deploymentSiteId,effectiveFrom:teamFrom,effectiveTo:teamTo||null})});
   setMsg(editTeam?"Team 關聯已更新。":"Team 關聯已建立。");resetTeam();await reloadSite(selectedSite.deploymentSiteId);
  }catch(error){setMsg(deploymentSiteErrorMessage(error,"Team 關聯儲存失敗"))}finally{setBusy(false)}
 };
 const endTeam=async(row:V180TeamDeploymentSiteAssignment)=>endAssignment(V180_DEPLOYMENT_SITE_API.endTeamAssignment(row.teamDeploymentSiteAssignmentId),row.effectiveFrom,row.version,"Team 關聯已結束。");

 const resetEmployment=()=>{setEditEmployment(null);setEmploymentId(null);setEmploymentPrimary(false);setEmploymentFrom(today);setEmploymentTo("")};
 const openEmployment=(row:V180EmploymentDeploymentSiteAssignment)=>{setEditEmployment(row);setEmploymentId(row.employmentId);setEmploymentPrimary(row.isPrimary);setEmploymentFrom(row.effectiveFrom);setEmploymentTo(row.effectiveTo||"")};
 const saveEmployment=async()=>{
  if(!selectedSite||!employmentId)return setMsg("請先以 EmploymentId 選擇人員。");
  if(employmentTo&&employmentTo<employmentFrom)return setMsg("人員關聯結束日不得早於開始日。");
  setBusy(true);setMsg("");
  try{
   if(editEmployment)await api(V180_DEPLOYMENT_SITE_API.employmentAssignment(editEmployment.employmentDeploymentSiteAssignmentId),{method:"PUT",body:JSON.stringify({isPrimary:employmentPrimary,effectiveFrom:employmentFrom,effectiveTo:employmentTo||null,version:editEmployment.version})});
   else await api(V180_DEPLOYMENT_SITE_API.employmentAssignments,{method:"POST",body:JSON.stringify({employmentId,deploymentSiteId:selectedSite.deploymentSiteId,isPrimary:employmentPrimary,effectiveFrom:employmentFrom,effectiveTo:employmentTo||null})});
   setMsg(editEmployment?"人員派駐關聯已更新。":"人員派駐關聯已建立。");resetEmployment();await reloadSite(selectedSite.deploymentSiteId);
  }catch(error){setMsg(deploymentSiteErrorMessage(error,"人員派駐關聯儲存失敗"))}finally{setBusy(false)}
 };
 const endEmployment=async(row:V180EmploymentDeploymentSiteAssignment)=>endAssignment(V180_DEPLOYMENT_SITE_API.endEmploymentAssignment(row.employmentDeploymentSiteAssignmentId),row.effectiveFrom,row.version,"人員派駐關聯已結束。");

 const endAssignment=async(path:string,from:string,version:string,success:string)=>{
  if(!selectedSite)return;
  setBusy(true);setMsg("");
  try{await api(path,{method:"POST",body:JSON.stringify({effectiveTo:deploymentSiteEndDate(from,today),version})});setMsg(success);await reloadSite(selectedSite.deploymentSiteId)}
  catch(error){setMsg(deploymentSiteErrorMessage(error,"關聯結束失敗"))}finally{setBusy(false)}
 };

 return <>
  {msg&&<div className="note" style={{marginBottom:14}}>{msg}</div>}
  <div className="grid cols-2">
   <div className="card">
    <div className="section-title"><div><h2>{editSite?"修改派駐點":"新增派駐點"}</h2><div className="sub">派駐點隸屬 Center；有效期間首尾日皆包含，更新採 RowVersion。</div></div>{editSite&&<button className="btn small outline" onClick={resetSite}>取消修改</button>}</div>
    <label>中心<select value={siteCenterId??""} onChange={e=>setSiteCenterId(e.target.value?Number(e.target.value):null)}><option value="">選擇中心</option>{centers.map(center=><option key={center.centerId} value={center.centerId}>{center.code}｜{center.name}{center.isActive?"":"（停用）"}</option>)}</select></label>
    <div className="grid cols-2"><label>SiteCode<input value={siteCode} onChange={e=>setSiteCode(e.target.value.toUpperCase())}/></label><label>SiteName<input value={siteName} onChange={e=>setSiteName(e.target.value)}/></label><label>開始日<input type="date" value={siteFrom} onChange={e=>setSiteFrom(e.target.value)}/></label><label>結束日<input type="date" value={siteTo} onChange={e=>setSiteTo(e.target.value)}/></label></div>
    <label className="check-row"><input type="checkbox" checked={siteActive} onChange={e=>{setSiteActive(e.target.checked);if(e.target.checked)setSiteTo("")}}/>啟用派駐點</label>
    <label>備註<textarea value={siteNotes} onChange={e=>setSiteNotes(e.target.value)}/></label>
    <button className="btn" disabled={busy} onClick={()=>void saveSite()}>{editSite?"儲存修改":"新增派駐點"}</button>
   </div>
   <div className="card">
    <div className="section-title"><div><h2>派駐點主檔</h2><div className="sub">目前 Location 與關聯數來自 v1.8 authoritative read API。</div></div></div>
    <div className="grid cols-2"><label>搜尋<input value={keyword} onChange={e=>setKeyword(e.target.value)} placeholder="SiteCode 或名稱"/></label><label>中心<select value={centerFilter} onChange={e=>setCenterFilter(e.target.value)}><option value="">全部中心</option>{centers.map(center=><option key={center.centerId} value={center.centerId}>{center.code}｜{center.name}</option>)}</select></label></div>
    <label className="check-row"><input type="checkbox" checked={includeInactive} onChange={e=>setIncludeInactive(e.target.checked)}/>包含停用／非目前有效</label>
    {siteQuery.error&&<div role="alert" className="note danger-note">{siteQuery.error}</div>}
    <div className="table-wrap"><table><thead><tr><th>中心</th><th>代碼／名稱</th><th>有效期間</th><th>目前地點</th><th>關聯</th><th>操作</th></tr></thead><tbody>{sites.map(site=><tr key={site.deploymentSiteId} className={site.isActive?"":"team-inactive"}><td>{site.centerCode}<br/>{site.centerName}</td><td>{site.code}<br/>{site.name}</td><td>{site.effectiveFrom}～{site.effectiveTo||"—"}<br/>{deploymentSiteLifecycleStatus(site.effectiveFrom,site.effectiveTo,today)}</td><td>{site.currentLocation?`${site.currentLocation.code||""} ${site.currentLocation.name}`:"—"}</td><td>Team {site.teamAssignmentCount}／人員 {site.employmentAssignmentCount}</td><td><div className="actions"><button className="btn small secondary" onClick={()=>openSite(site)}>維護</button>{site.isActive&&<button className="btn small danger" disabled={busy} onClick={()=>void deactivateSite(site)}>停用</button>}<button className="btn small outline" onClick={()=>selectSite(site)}>關聯歷史</button></div></td></tr>)}</tbody></table></div>
    <Pagination {...siteQuery.data} page={siteQuery.page} pageSize={siteQuery.pageSize} busy={siteQuery.loading} onPage={siteQuery.setPage} onPageSize={siteQuery.setPageSize}/>
   </div>
  </div>

  <div className="card" style={{marginTop:18}}>
   <div className="section-title"><div><h2>派駐點關聯管理{selectedSite?`｜${selectedSite.code} ${selectedSite.name}`:""}</h2><div className="sub">保留完整歷史，不做 physical delete；後端負責 overlap、Organization 與 Center containment 最終判定。</div></div></div>
   {!selectedSite?<div className="empty">請先從派駐點主檔選擇「關聯歷史」。</div>:<>
    <div className="grid cols-2">
     <div><h3>Deployment Site ↔ Location</h3>{editLocation?<div className="note">目前地點：{editLocation.locationCode||""} {editLocation.locationName}（修改期間不更換地點；搬遷請結束舊關聯後新增）</div>:<><SmartLocationPicker selectedLocationId={selectedLocation?.locationId} onSelect={setSelectedLocation}/>{selectedLocation&&<div className="note ok-note">已選：{selectedLocation.locationCode||""} {selectedLocation.locationName}</div>}</>}
      <div className="grid cols-2"><label>開始日<input type="date" value={locationFrom} onChange={e=>setLocationFrom(e.target.value)}/></label><label>結束日<input type="date" value={locationTo} onChange={e=>setLocationTo(e.target.value)}/></label></div><label>異動原因<textarea value={locationReason} onChange={e=>setLocationReason(e.target.value)}/></label><div className="actions"><button className="btn" disabled={busy} onClick={()=>void saveLocation()}>{editLocation?"儲存關聯修改":"新增 Location 關聯"}</button>{editLocation&&<button className="btn outline" onClick={resetLocation}>取消修改</button>}</div>
     </div>
     <HistoryTable headers={["地點","開始","結束","狀態","操作"]}>{locationRows.map(row=><tr key={row.deploymentSiteLocationAssignmentId}><td>{row.locationCode||"—"} {row.locationName}<br/><span className="sub">{row.address||row.changeReason||""}</span></td><td>{row.effectiveFrom}</td><td>{row.effectiveTo||"—"}</td><td>{deploymentSiteLifecycleStatus(row.effectiveFrom,row.effectiveTo,today)}</td><td><div className="actions"><button className="btn small secondary" onClick={()=>openLocation(row)}>維護</button>{(!row.effectiveTo||row.effectiveTo>=today)&&<button className="btn small danger" disabled={busy} onClick={()=>void endLocation(row)}>結束</button>}</div></td></tr>)}</HistoryTable>
    </div>

    <div className="grid cols-2" style={{marginTop:18}}>
     <div><h3>Team ↔ Deployment Site</h3><label>小組<select value={teamId??""} disabled={!!editTeam} onChange={e=>setTeamId(e.target.value?Number(e.target.value):null)}><option value="">選擇小組</option>{teams.map(team=><option key={team.teamId} value={team.teamId}>{team.code}｜{team.name}{team.centerId===selectedSite.centerId?"":"（不同中心）"}</option>)}</select></label><div className="grid cols-2"><label>開始日<input type="date" value={teamFrom} onChange={e=>setTeamFrom(e.target.value)}/></label><label>結束日<input type="date" value={teamTo} onChange={e=>setTeamTo(e.target.value)}/></label></div><div className="actions"><button className="btn" disabled={busy} onClick={()=>void saveTeam()}>{editTeam?"儲存關聯修改":"新增 Team 關聯"}</button>{editTeam&&<button className="btn outline" onClick={resetTeam}>取消修改</button>}</div><div className="sub">Center containment 由後端依有效日完整驗證。</div></div>
     <HistoryTable headers={["小組","開始","結束","狀態","操作"]}>{teamRows.map(row=><tr key={row.teamDeploymentSiteAssignmentId}><td>{row.teamCode} {row.teamName}</td><td>{row.effectiveFrom}</td><td>{row.effectiveTo||"—"}</td><td>{deploymentSiteLifecycleStatus(row.effectiveFrom,row.effectiveTo,today)}</td><td><div className="actions"><button className="btn small secondary" onClick={()=>openTeam(row)}>維護</button>{(!row.effectiveTo||row.effectiveTo>=today)&&<button className="btn small danger" disabled={busy} onClick={()=>void endTeam(row)}>結束</button>}</div></td></tr>)}</HistoryTable>
    </div>

    <div className="grid cols-2" style={{marginTop:18}}>
     <div><h3>Employment ↔ Deployment Site</h3>{!editEmployment&&<label>搜尋人員<input value={personKeyword} onChange={e=>setPersonKeyword(e.target.value)} placeholder="工號、姓名或 Email"/></label>}<label>人員（EmploymentId）<select value={employmentId??""} disabled={!!editEmployment} onChange={e=>setEmploymentId(e.target.value?Number(e.target.value):null)}><option value="">選擇人員</option>{editEmployment&&!peopleQuery.data.items.some(person=>person.employmentId===editEmployment.employmentId)&&<option value={editEmployment.employmentId}>{editEmployment.employeeNo||"—"}｜{editEmployment.displayName}</option>}{peopleQuery.data.items.map(person=><option key={person.employmentId} value={person.employmentId}>{person.employeeNo||`Employment ${person.employmentId}`}｜{person.displayName}</option>)}</select></label><label className="check-row"><input type="checkbox" checked={employmentPrimary} onChange={e=>setEmploymentPrimary(e.target.checked)}/>主要派駐點（IsPrimary）</label><div className="grid cols-2"><label>開始日<input type="date" value={employmentFrom} onChange={e=>setEmploymentFrom(e.target.value)}/></label><label>結束日<input type="date" value={employmentTo} onChange={e=>setEmploymentTo(e.target.value)}/></label></div><div className="actions"><button className="btn" disabled={busy} onClick={()=>void saveEmployment()}>{editEmployment?"儲存關聯修改":"新增人員關聯"}</button>{editEmployment&&<button className="btn outline" onClick={resetEmployment}>取消修改</button>}</div>{!editEmployment&&<Pagination {...peopleQuery.data} page={peopleQuery.page} pageSize={peopleQuery.pageSize} busy={peopleQuery.loading} onPage={peopleQuery.setPage} onPageSize={peopleQuery.setPageSize}/>}</div>
     <HistoryTable headers={["人員","主要","開始","結束","狀態／操作"]}>{employmentRows.map(row=><tr key={row.employmentDeploymentSiteAssignmentId}><td>{row.employeeNo||`Employment ${row.employmentId}`}<br/>{row.displayName}</td><td>{row.isPrimary?"是":"否"}</td><td>{row.effectiveFrom}</td><td>{row.effectiveTo||"—"}</td><td>{deploymentSiteLifecycleStatus(row.effectiveFrom,row.effectiveTo,today)}<div className="actions"><button className="btn small secondary" onClick={()=>openEmployment(row)}>維護</button>{(!row.effectiveTo||row.effectiveTo>=today)&&<button className="btn small danger" disabled={busy} onClick={()=>void endEmployment(row)}>結束</button>}</div></td></tr>)}</HistoryTable>
    </div>
   </>}
  </div>
 </>;
}

function HistoryTable({headers,children}:{headers:string[];children:ReactNode}){
 return <div><h4>歷史／未來排程</h4><div className="table-wrap"><table><thead><tr>{headers.map(header=><th key={header}>{header}</th>)}</tr></thead><tbody>{children}</tbody></table></div></div>;
}
