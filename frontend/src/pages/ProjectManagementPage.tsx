import{useEffect,useState}from"react";
import{api}from"../api";
import type{ManagedTeam,Project,ProjectDeleteImpact}from"../types";
import{usePagedQuery}from"../use-query";
import{DateFilters,Pagination}from"../components/QueryControls";
import{projectStatus}from"../query-ux";
import{todayTaipei}from"../v160";
import ProjectLocationManager from"../components/ProjectLocationManager";
import AdminImportPanel from"../components/AdminImportPanel";
import ProjectAdminTabs from"../components/ProjectAdminTabs";

type Props={mode?:"list"|"bulk"};
type ProjectRow=Project&{locationCount:number};

export default function ProjectManagementPage({mode="list"}:Props){
 if(mode==="bulk")return <><ProjectAdminTabs/><div className="card"><div className="section-title"><div><h2>專案 Excel 批次維護</h2><div className="sub">大量新增或更新專案主檔；固定地點仍在個別專案的「固定地點」分頁維護。</div></div></div><AdminImportPanel type="projects" onDone={()=>{}}/></div></>;
 return <ProjectList/>;
}

function ProjectList(){
 const[teams,setTeams]=useState<ManagedTeam[]>([]),[keyword,setKeyword]=useState(""),[filterTeam,setFilterTeam]=useState(""),[filterStatus,setFilterStatus]=useState(""),[filterStart,setFilterStart]=useState(""),[filterEnd,setFilterEnd]=useState(""),[msg,setMsg]=useState(""),[busy,setBusy]=useState(false),[modalOpen,setModalOpen]=useState(false),[edit,setEdit]=useState<Project|null>(null),[detailTab,setDetailTab]=useState<"basic"|"locations">("basic"),[code,setCode]=useState(""),[name,setName]=useState(""),[teamId,setTeamId]=useState(""),[locationMode,setLocationMode]=useState("List"),[start,setStart]=useState(todayTaipei()),[end,setEnd]=useState(""),[noEnd,setNoEnd]=useState(true),[desc,setDesc]=useState("");
 const datesValid=!filterStart||!filterEnd||filterStart<=filterEnd;
 const query=usePagedQuery<ProjectRow>("/admin/projects/search",{keyword,teamId:filterTeam||undefined,status:filterStatus||undefined,startDate:filterStart||undefined,endDate:filterEnd||undefined},datesValid);
 useEffect(()=>{api<ManagedTeam[]>("/admin/teams?includeInactive=true").then(setTeams).catch(e=>setMsg(e instanceof Error?e.message:"小組清單載入失敗"))},[]);
 const resetForm=()=>{setEdit(null);setDetailTab("basic");setCode("");setName("");setTeamId("");setLocationMode("List");setStart(todayTaipei());setEnd("");setNoEnd(true);setDesc("")};
 const openNew=()=>{resetForm();setMsg("");setModalOpen(true)};
 const openEdit=(p:Project)=>{setEdit(p);setDetailTab("basic");setCode(p.projectCode);setName(p.projectName);setTeamId(p.teamId?String(p.teamId):"");setLocationMode(p.locationMode);setStart(p.startDate||todayTaipei());setEnd(p.endDate||"");setNoEnd(!p.endDate);setDesc(p.description||"");setMsg("");setModalOpen(true)};
 const close=()=>{if(!busy){setModalOpen(false);resetForm()}};
 const save=async()=>{if(!code.trim()||!name.trim())return setMsg("專案代碼與專案名稱必填。");if(!noEnd&&!end)return setMsg("取消「無期限」後，結束日期必填。");if(!noEnd&&start&&end&&start>end)return setMsg("結束日期不可早於開始日期。");setBusy(true);setMsg("");try{const body={teamId:teamId?Number(teamId):null,projectCode:code.trim(),projectName:name.trim(),description:desc.trim()||null,locationMode,startDate:start||null,endDate:noEnd?null:(end||null),isActive:edit?.isActive??true};if(edit)await api(`/projects/${edit.projectId}`,{method:"PUT",body:JSON.stringify(body)});else await api("/projects",{method:"POST",body:JSON.stringify(body)});setMsg(edit?"專案已更新。":"專案已新增。");setModalOpen(false);resetForm();query.reload()}catch(e){setMsg(e instanceof Error?e.message:"專案儲存失敗")}finally{setBusy(false)}};
 const deactivate=async(p:Project)=>{if(!window.confirm(`確定停用專案「${p.projectName}」？歷史 Snapshot 不受影響。`))return;setBusy(true);setMsg("");try{await api(`/projects/${p.projectId}`,{method:"DELETE"});setMsg("專案已停用。");query.reload()}catch(e){setMsg(e instanceof Error?e.message:"停用失敗")}finally{setBusy(false)}};
 const deleteProject=async(p:Project)=>{setBusy(true);setMsg("");try{const impact=await api<ProjectDeleteImpact>(`/admin/projects/${p.projectId}/delete-impact`);if(!impact.canDelete){setMsg(impact.reason||"此專案已有歷史使用，只能停用。");return}if(!window.confirm(`永久刪除專案「${p.projectName}」？\n\n固定地點設定會一併移除；此動作無法復原。`))return;await api(`/admin/projects/${p.projectId}/permanent`,{method:"DELETE"});setMsg(`專案「${p.projectName}」已永久刪除。`);if(edit?.projectId===p.projectId){setModalOpen(false);resetForm()}query.reload()}catch(e){setMsg(e instanceof Error?e.message:"專案刪除失敗")}finally{setBusy(false)}};
 const persistedAllowsLocations=!!edit&&edit.locationMode==="List";
 const selectableTeams=teams.filter(t=>t.isActive||String(t.teamId)===teamId);
 return <>
  <ProjectAdminTabs/>
  <div className="card">
   <div className="section-title"><div><h2>專案清單</h2><div className="sub">預設從清單進入；新增或維護時才開啟專案詳細資料。</div></div><button className="btn" onClick={openNew}>＋新增專案</button></div>
   {msg&&<div className="note" style={{marginBottom:10}}>{msg}</div>}
   <div className="grid cols-3"><label>專案搜尋<input value={keyword} placeholder="專案代碼或名稱" onChange={e=>setKeyword(e.target.value)}/></label><label>歸屬小組<select value={filterTeam} onChange={e=>setFilterTeam(e.target.value)}><option value="">全部</option>{teams.map(t=><option key={t.teamId} value={t.teamId}>{t.teamName}{t.isActive?"":"（停用）"}</option>)}</select></label><label>專案狀態<select value={filterStatus} onChange={e=>setFilterStatus(e.target.value)}><option value="">全部</option><option value="NotStarted">未開始</option><option value="InProgress">進行中</option><option value="Ended">已結束</option><option value="Inactive">停用</option></select></label></div>
   <DateFilters label="專案期間" start={filterStart} end={filterEnd} onChange={(s,e)=>{setFilterStart(s);setFilterEnd(e)}}/>
   {!datesValid&&<div className="note danger-note">專案期間迄日不可早於起日。</div>}{query.error&&<div role="alert" className="note danger-note">{query.error}</div>}
   <div className="table-wrap"><table><thead><tr><th>專案</th><th>歸屬小組</th><th>有效期間</th><th>地點規則</th><th>固定地點</th><th>狀態</th><th>操作</th></tr></thead><tbody>{query.data.items.map(p=>{const teamName=p.teamId?teams.find(t=>t.teamId===p.teamId)?.teamName||`未知小組 #${p.teamId}`:"全組織";return <tr key={p.projectId}><td><strong>{p.projectCode}</strong><div>{p.projectName}</div>{p.description&&<div className="sub">{p.description}</div>}</td><td>{teamName}</td><td>{p.startDate||"不限"}～{p.endDate||"無期限"}</td><td>{p.locationMode==="List"?"專案清單優先":"臨時維護優先"}</td><td>{p.locationMode==="List"?`${p.locationCount??0} 筆`:"—"}</td><td>{p.isActive?<span className="pill ok">{projectStatus(p,todayTaipei())}</span>:<span className="pill warn">停用</span>}</td><td><div className="actions"><button className="btn small secondary" onClick={()=>openEdit(p)}>維護專案</button>{p.isActive&&<button className="btn small outline" disabled={busy} onClick={()=>void deactivate(p)}>停用</button>}<button className="btn small danger" disabled={busy} onClick={()=>void deleteProject(p)}>刪除</button></div></td></tr>})}</tbody></table></div>
   <Pagination {...query.data} page={query.page} pageSize={query.pageSize} busy={query.loading||busy} onPage={query.setPage} onPageSize={query.setPageSize}/>
  </div>
  {modalOpen&&<div className="modal" onMouseDown={e=>{if(e.target===e.currentTarget)close()}}><div className="modal-panel" role="dialog" aria-modal="true"><button className="btn small outline modal-close" disabled={busy} onClick={close}>關閉</button><h3>{edit?`專案詳細｜${edit.projectName}`:"新增專案"}</h3>{edit&&<div className="actions" style={{marginBottom:12}}><button className={`btn small ${detailTab==="basic"?"":"outline"}`} onClick={()=>setDetailTab("basic")}>基本資料</button>{persistedAllowsLocations&&<button className={`btn small ${detailTab==="locations"?"":"outline"}`} onClick={()=>setDetailTab("locations")}>固定地點</button>}</div>}
   {detailTab==="basic"?<><div className="grid cols-2"><label>專案代碼<input value={code} onChange={e=>setCode(e.target.value)}/></label><label>專案名稱<input value={name} onChange={e=>setName(e.target.value)}/></label><label>歸屬小組<select value={teamId} onChange={e=>setTeamId(e.target.value)}><option value="">全組織</option>{selectableTeams.map(t=><option key={t.teamId} value={t.teamId} disabled={!t.isActive}>{t.teamName}{t.isActive?"":"（停用）"}</option>)}</select></label><label>地點方式<select value={locationMode} onChange={e=>setLocationMode(e.target.value)}><option value="List">專案清單優先</option><option value="SelfMaintained">臨時維護優先</option></select></label><label>開始日期<input type="date" value={start} onChange={e=>setStart(e.target.value)}/></label><label>結束日期<input type="date" value={end} disabled={noEnd} onChange={e=>setEnd(e.target.value)}/></label><label className="check-row span-2"><input type="checkbox" checked={noEnd} onChange={e=>{setNoEnd(e.target.checked);if(e.target.checked)setEnd("")}}/>無期限</label><label className="span-2">說明<input value={desc} onChange={e=>setDesc(e.target.value)}/></label></div>{edit&&edit.locationMode!==locationMode&&<div className="note">地點方式變更後，請先儲存基本資料；重新開啟專案後，系統會依新的方式決定是否顯示「固定地點」。</div>}<div className="actions" style={{marginTop:14}}><button className="btn ok" disabled={busy} onClick={()=>void save()}>{edit?"儲存基本資料":"新增專案"}</button><button className="btn outline" disabled={busy} onClick={close}>取消</button></div></>:edit&&persistedAllowsLocations?<ProjectLocationManager projectId={edit.projectId} projectName={edit.projectName}/>:<div className="note">此專案不是「專案清單優先」，不需要維護固定地點。</div>}</div></div>}
 </>;
}
