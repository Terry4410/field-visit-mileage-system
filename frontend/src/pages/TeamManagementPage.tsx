import{useEffect,useState}from"react";
import{NavLink}from"react-router-dom";
import{api}from"../api";
import type{ManagedTeam,TeamDeleteImpact,V170PeopleRow}from"../types";
import{usePagedQuery}from"../use-query";
import{Pagination}from"../components/QueryControls";
import{todayTaipei}from"../v160";
import PeopleManagementBulkPanel from"../components/PeopleManagementBulkPanel";

type Props={mode?:"teams"|"members"|"bulk"};
type TeamAssignment={teamId:number;isPrimary:boolean};
type BatchAddResult={addedCount:number;noChangeCount:number};

const roleLabel:Record<string,string>={visitor:"外訪員",leader:"小組長",admin:"管理者"};
function primarySiteLabel(u:V170PeopleRow){if(!u.primaryDeploymentSiteName)return"—";return `${u.primaryCenterName?`${u.primaryCenterName}／`:""}${u.primaryDeploymentSiteName}`}

function Tabs(){return <div className="actions" style={{marginBottom:14}}>
 <NavLink end to="/admin/teams" className={({isActive})=>`btn small ${isActive?"":"outline"}`}>小組設定</NavLink>
 <NavLink to="/admin/teams/members" className={({isActive})=>`btn small ${isActive?"":"outline"}`}>成員配置</NavLink>
 <NavLink to="/admin/teams/bulk" className={({isActive})=>`btn small ${isActive?"":"outline"}`}>Excel 批次維護</NavLink>
 </div>}

export default function TeamManagementPage({mode="teams"}:Props){
 if(mode==="bulk")return <><Tabs/><PeopleManagementBulkPanel title="小組成員 Excel 批次維護" description="只維護管理小組歸屬、主要小組與有效期間；不修改角色、人事資料或就業中心／派駐據點。" templateUrl="/admin/people/team-membership-bulk/template.xlsx" previewUrl="/admin/people/team-membership-bulk/preview" confirmUrl="/admin/people/team-membership-bulk/confirm" templateFilename="小組成員批次維護.xlsx"/></>;
 if(mode==="members")return <MemberConfiguration/>;
 return <TeamSettings/>;
}

function TeamSettings(){
 const[keyword,setKeyword]=useState(""),[filterActive,setFilterActive]=useState(""),[edit,setEdit]=useState<ManagedTeam|null>(null),[modalOpen,setModalOpen]=useState(false),[code,setCode]=useState(""),[name,setName]=useState(""),[active,setActive]=useState(true),[busy,setBusy]=useState(false),[msg,setMsg]=useState("");
 const query=usePagedQuery<ManagedTeam&{memberCount:number}>("/admin/teams/search",{keyword,isActive:filterActive===""?undefined:filterActive==="true"});
 const resetForm=()=>{setEdit(null);setCode("");setName("");setActive(true)};
 const openNew=()=>{resetForm();setModalOpen(true);setMsg("")};
 const openEdit=(t:ManagedTeam)=>{setEdit(t);setCode(t.teamCode);setName(t.teamName);setActive(t.isActive);setModalOpen(true);setMsg("")};
 const close=()=>{if(!busy){setModalOpen(false);resetForm()}};
 const save=async()=>{if(!code.trim()||!name.trim())return setMsg("小組代碼與小組名稱必填。");setBusy(true);setMsg("");try{const body=JSON.stringify({teamCode:code.trim().toUpperCase(),teamName:name.trim(),isActive:edit?active:true});if(edit)await api(`/admin/teams/${edit.teamId}`,{method:"PUT",body});else await api("/admin/teams",{method:"POST",body});setMsg(edit?"小組已更新。":"小組已新增。");setModalOpen(false);resetForm();query.reload()}catch(e){setMsg(e instanceof Error?e.message:"儲存失敗")}finally{setBusy(false)}};
 const deactivate=async(t:ManagedTeam)=>{if(!window.confirm(`確定停用小組「${t.teamName}」？歷史資料不會刪除。`))return;setBusy(true);setMsg("");try{await api(`/admin/teams/${t.teamId}`,{method:"DELETE"});setMsg("小組已停用。");query.reload()}catch(e){setMsg(e instanceof Error?e.message:"停用失敗")}finally{setBusy(false)}};
 const deleteTeam=async(t:ManagedTeam)=>{setBusy(true);setMsg("");try{const impact=await api<TeamDeleteImpact>(`/admin/teams/${t.teamId}/delete-impact`);if(!impact.canDelete){setMsg(impact.reason||"此小組已有歷史資料，只能停用。");return}if(!window.confirm(`永久刪除小組「${t.teamName}」？\n\n此動作只允許完全沒有歷史引用的小組，且無法復原。`))return;await api(`/admin/teams/${t.teamId}/permanent`,{method:"DELETE"});setMsg(`小組「${t.teamName}」已永久刪除。`);query.reload()}catch(e){setMsg(e instanceof Error?e.message:"小組刪除失敗")}finally{setBusy(false)}};
 return <>
  <Tabs/>
  <div className="card">
   <div className="section-title"><div><h2>小組設定</h2><div className="sub">這裡只管理小組主檔；成員歸屬請到「成員配置」。</div></div><button className="btn" onClick={openNew}>＋新增小組</button></div>
   {msg&&<div className="note" style={{marginBottom:10}}>{msg}</div>}{query.error&&<div className="note danger-note">{query.error}</div>}
   <div className="grid cols-2"><label>小組搜尋<input value={keyword} onChange={e=>setKeyword(e.target.value)} placeholder="小組代碼或名稱"/></label><label>小組狀態<select value={filterActive} onChange={e=>setFilterActive(e.target.value)}><option value="">全部</option><option value="true">啟用</option><option value="false">停用</option></select></label></div>
   <div className="table-wrap"><table><thead><tr><th>代碼</th><th>小組名稱</th><th>狀態</th><th>目前成員數</th><th>操作</th></tr></thead><tbody>{query.data.items.map(t=><tr key={t.teamId} className={t.isActive?"":"team-inactive"}><td>{t.teamCode}</td><td>{t.teamName}</td><td>{t.isActive?"啟用":"停用"}</td><td>{t.memberCount}</td><td><div className="actions"><button className="btn small secondary" onClick={()=>openEdit(t)}>維護</button>{t.isActive&&<button className="btn small outline" disabled={busy} onClick={()=>void deactivate(t)}>停用</button>}<button className="btn small danger" disabled={busy} onClick={()=>void deleteTeam(t)}>刪除</button><NavLink className="btn small outline" to="/admin/teams/members">成員配置</NavLink></div></td></tr>)}</tbody></table></div>
   <Pagination {...query.data} page={query.page} pageSize={query.pageSize} busy={query.loading||busy} onPage={query.setPage} onPageSize={query.setPageSize}/>
  </div>
  {modalOpen&&<div className="modal" onMouseDown={e=>{if(e.target===e.currentTarget)close()}}><div className="modal-panel" role="dialog" aria-modal="true"><button className="btn small outline modal-close" disabled={busy} onClick={close}>關閉</button><h3>{edit?"修改小組":"新增小組"}</h3><div className="field"><label>小組代碼</label><input value={code} onChange={e=>setCode(e.target.value.toUpperCase())} placeholder="例如 TEAM-001"/></div><div className="field"><label>小組名稱</label><input value={name} onChange={e=>setName(e.target.value)} placeholder="例如 北區第一組"/></div>{edit&&<label className="check-row"><input type="checkbox" checked={active} onChange={e=>setActive(e.target.checked)}/>啟用小組</label>}<div className="actions" style={{marginTop:14}}><button className="btn ok" disabled={busy} onClick={()=>void save()}>{edit?"儲存修改":"新增小組"}</button><button className="btn outline" disabled={busy} onClick={close}>取消</button></div></div></div>}
 </>;
}

function MemberConfiguration(){
 const[teams,setTeams]=useState<ManagedTeam[]>([]),[selectedTeamId,setSelectedTeamId]=useState(""),[memberKeyword,setMemberKeyword]=useState(""),[msg,setMsg]=useState(""),[busy,setBusy]=useState(false),[addOpen,setAddOpen]=useState(false),[candidateKeyword,setCandidateKeyword]=useState(""),[selectedCandidates,setSelectedCandidates]=useState<V170PeopleRow[]>([]),[adjustUser,setAdjustUser]=useState<V170PeopleRow|null>(null),[otherTeamId,setOtherTeamId]=useState("");
 const selectedTeam=teams.find(t=>t.teamId===Number(selectedTeamId));
 const memberQuery=usePagedQuery<V170PeopleRow>("/admin/people",{userType:"Internal",keyword:memberKeyword,teamId:selectedTeamId?Number(selectedTeamId):undefined,sort:"code_asc"},!!selectedTeamId);
 const candidateQuery=usePagedQuery<V170PeopleRow>("/admin/people",{userType:"Internal",keyword:candidateKeyword,employmentStatus:"Active",sort:"code_asc"},addOpen&&!!selectedTeamId);
 const loadTeams=()=>api<ManagedTeam[]>("/admin/teams?includeInactive=true").then(rows=>{setTeams(rows);if(!selectedTeamId){const first=rows.find(x=>x.isActive);if(first)setSelectedTeamId(String(first.teamId))}}).catch(e=>setMsg(e instanceof Error?e.message:"小組清單載入失敗"));
 useEffect(()=>{void loadTeams()},[]);
 useEffect(()=>{setSelectedCandidates([]);setCandidateKeyword("");setAddOpen(false);setAdjustUser(null);setOtherTeamId("");memberQuery.setPage(1)},[selectedTeamId]);
 const normalizeTeams=(u:V170PeopleRow):TeamAssignment[]=>u.teamAssignments.map(s=>({teamId:s.teamId,isPrimary:s.isPrimary}));
 const saveMemberships=async(u:V170PeopleRow,next:TeamAssignment[],success:string)=>{setBusy(true);setMsg("");try{await api(`/admin/people/internal-users/${u.userId}/team-memberships`,{method:"PUT",body:JSON.stringify({teamAssignments:next,effectiveFrom:todayTaipei()})});setMsg(success);setAdjustUser(null);setOtherTeamId("");memberQuery.reload();candidateQuery.reload()}catch(e){setMsg(e instanceof Error?e.message:"小組歸屬更新失敗")}finally{setBusy(false)}};
 const toggleCandidate=(u:V170PeopleRow,checked:boolean)=>setSelectedCandidates(rows=>checked?(rows.some(x=>x.userId===u.userId)?rows:[...rows,u]):rows.filter(x=>x.userId!==u.userId));
 const addCandidates=async()=>{if(!selectedTeam||!selectedCandidates.length)return;const noTeam=selectedCandidates.filter(u=>u.teamAssignments.length===0).length;const keepPrimary=selectedCandidates.length-noTeam;if(!window.confirm(`將 ${selectedCandidates.length} 位人員加入「${selectedTeam.teamName}」。\n\n${noTeam} 位目前沒有管理小組：系統會將本小組設為主要小組。\n${keepPrimary} 位已有主要小組：保留原主要小組。\n\n確認加入？`))return;setBusy(true);setMsg("");try{const r=await api<BatchAddResult>(`/admin/people/team-memberships/${selectedTeam.teamId}/batch-add`,{method:"POST",body:JSON.stringify({userIds:selectedCandidates.map(x=>x.userId),effectiveFrom:todayTaipei()})});setMsg(`加入完成：新增 ${r.addedCount} 位、原本已在小組 ${r.noChangeCount} 位；角色與派駐據點均未變更。`);setSelectedCandidates([]);setAddOpen(false);memberQuery.reload()}catch(e){setMsg(e instanceof Error?e.message:"加入成員失敗")}finally{setBusy(false)}};
 const setPrimary=async(u:V170PeopleRow,teamId:number)=>{const next=normalizeTeams(u).map(x=>({...x,isPrimary:x.teamId===teamId}));await saveMemberships(u,next,`${u.displayName} 的主要小組已更新。`)};
 const removeTeam=async(u:V170PeopleRow,teamId:number)=>{const current=normalizeTeams(u);const removed=current.find(x=>x.teamId===teamId);let next=current.filter(x=>x.teamId!==teamId);if(next.length===0&&u.roles.some(r=>r==="visitor"||r==="leader"))return setMsg("外訪員或小組長至少需要一個管理小組，請先加入另一個小組再移除。");if(removed?.isPrimary&&next.length>0)next=next.map((x,i)=>({...x,isPrimary:i===0}));if(!window.confirm(`確定將「${u.displayName}」移出這個管理小組？`))return;await saveMemberships(u,next,`${u.displayName} 已移出小組。`)};
 const addOther=async()=>{if(!adjustUser||!otherTeamId)return;const id=Number(otherTeamId);const current=normalizeTeams(adjustUser);if(current.some(x=>x.teamId===id))return setMsg("此人員已在該小組。");const next=[...current,{teamId:id,isPrimary:current.length===0}];const target=teams.find(t=>t.teamId===id);await saveMemberships(adjustUser,next,`${adjustUser.displayName} 已加入「${target?.teamName||"另一個小組"}」；原主要小組未變更。`)};
 const candidates=candidateQuery.data.items.filter(u=>!u.teamAssignments.some(s=>s.teamId===Number(selectedTeamId)));
 return <>
  <Tabs/>
  <div className="card">
   <div className="section-title"><div><h2>成員配置</h2><div className="sub">這裡只維護管理小組。角色與就業中心／派駐據點均為唯讀資訊，請到各自的正式維護入口調整。</div></div>{selectedTeam?.isActive&&<button className="btn" onClick={()=>{setSelectedCandidates([]);setAddOpen(true)}}>＋加入小組成員</button>}</div>
   {msg&&<div className="note" style={{marginBottom:10}}>{msg}</div>}
   <div className="grid cols-2"><label>管理小組<select value={selectedTeamId} onChange={e=>setSelectedTeamId(e.target.value)}><option value="">請選擇</option>{teams.map(t=><option key={t.teamId} value={t.teamId}>{t.teamName}{t.isActive?"":"（停用）"}</option>)}</select></label><label>搜尋目前成員<input value={memberKeyword} onChange={e=>setMemberKeyword(e.target.value)} placeholder="工號、姓名或 Email"/></label></div>
   {!selectedTeamId?<div className="empty">請先選擇管理小組。</div>:<>
    <div className="note" style={{marginBottom:10}}><strong>{selectedTeam?.teamName}</strong>｜目前只顯示此小組成員。若要新增成員，請使用右上「＋加入小組成員」。</div>
    {memberQuery.error&&<div className="note danger-note">{memberQuery.error}</div>}
    <div className="table-wrap"><table><thead><tr><th>工號</th><th>姓名</th><th>角色（唯讀）</th><th>主要派駐據點（唯讀）</th><th>主要小組</th><th>其他小組</th><th>操作</th></tr></thead><tbody>{memberQuery.data.items.map(u=>{const primary=u.teamAssignments.find(s=>s.isPrimary);const others=u.teamAssignments.filter(s=>!s.isPrimary);return <tr key={u.userId}><td>{u.employeeNo||u.userCode}</td><td>{u.displayName}</td><td>{u.roles.map(r=>roleLabel[r]||r).join("、")||"—"}</td><td>{primarySiteLabel(u)}</td><td>{primary?.teamName||"—"}{primary&&" ★"}</td><td>{others.map(s=>s.teamName).join("、")||"—"}</td><td><button className="btn small secondary" onClick={()=>{setAdjustUser(u);setOtherTeamId("")}}>調整歸屬</button></td></tr>})}</tbody></table></div>
    <Pagination {...memberQuery.data} page={memberQuery.page} pageSize={memberQuery.pageSize} busy={memberQuery.loading||busy} onPage={memberQuery.setPage} onPageSize={memberQuery.setPageSize}/>
   </>}
  </div>
  {addOpen&&selectedTeam&&<div className="modal" onMouseDown={e=>{if(e.target===e.currentTarget&&!busy)setAddOpen(false)}}><div className="modal-panel" role="dialog" aria-modal="true"><button className="btn small outline modal-close" disabled={busy} onClick={()=>setAddOpen(false)}>關閉</button><h3>加入小組成員｜{selectedTeam.teamName}</h3><div className="note">只列目前 Active 人員。角色、主要派駐據點與既有主要小組都不會被這個動作修改。</div><div className="field" style={{marginTop:12}}><label>搜尋人員</label><input value={candidateKeyword} onChange={e=>setCandidateKeyword(e.target.value)} placeholder="工號、姓名或 Email"/></div>{candidateQuery.error&&<div className="note danger-note">{candidateQuery.error}</div>}<div className="table-wrap"><table><thead><tr><th>選取</th><th>工號</th><th>姓名</th><th>主要派駐據點（唯讀）</th><th>目前管理小組</th></tr></thead><tbody>{candidates.map(u=><tr key={u.userId}><td><input type="checkbox" aria-label={`選取 ${u.displayName}`} checked={selectedCandidates.some(x=>x.userId===u.userId)} onChange={e=>toggleCandidate(u,e.target.checked)}/></td><td>{u.employeeNo||u.userCode}</td><td>{u.displayName}</td><td>{primarySiteLabel(u)}</td><td>{u.teamAssignments.map(s=>`${s.teamName}${s.isPrimary?" ★":""}`).join("、")||"尚無管理小組"}</td></tr>)}</tbody></table></div><Pagination {...candidateQuery.data} page={candidateQuery.page} pageSize={candidateQuery.pageSize} busy={candidateQuery.loading||busy} onPage={candidateQuery.setPage} onPageSize={candidateQuery.setPageSize}/><div className="actions" style={{marginTop:14}}><button className="btn ok" disabled={busy||!selectedCandidates.length} onClick={()=>void addCandidates()}>確認加入（{selectedCandidates.length}）</button><button className="btn outline" disabled={busy} onClick={()=>setAddOpen(false)}>取消</button></div></div></div>}
  {adjustUser&&<div className="modal" onMouseDown={e=>{if(e.target===e.currentTarget&&!busy)setAdjustUser(null)}}><div className="modal-panel" role="dialog" aria-modal="true"><button className="btn small outline modal-close" disabled={busy} onClick={()=>setAdjustUser(null)}>關閉</button><h3>調整管理小組｜{adjustUser.displayName}</h3><div className="note">角色：{adjustUser.roles.map(r=>roleLabel[r]||r).join("、")||"—"}（唯讀）<br/>主要派駐據點：{primarySiteLabel(adjustUser)}（唯讀）</div><h4>目前管理小組</h4><div className="route-list">{adjustUser.teamAssignments.map(s=><div className="route-item" key={s.teamId}><div><strong>{s.teamName}{s.isPrimary?" ★":""}</strong><div className="sub">{s.isPrimary?"主要小組":"其他小組"}</div></div><div className="actions">{!s.isPrimary&&<button className="btn small secondary" disabled={busy} onClick={()=>void setPrimary(adjustUser,s.teamId)}>設為主要</button>}<button className="btn small outline" disabled={busy} onClick={()=>void removeTeam(adjustUser,s.teamId)}>移出此小組</button></div></div>)}</div><h4>加入另一個小組</h4><div className="actions"><select value={otherTeamId} onChange={e=>setOtherTeamId(e.target.value)}><option value="">選擇小組</option>{teams.filter(t=>t.isActive&&!adjustUser.teamAssignments.some(s=>s.teamId===t.teamId)).map(t=><option key={t.teamId} value={t.teamId}>{t.teamName}</option>)}</select><button className="btn secondary" disabled={busy||!otherTeamId} onClick={()=>void addOther()}>加入另一個小組</button></div></div></div>}
 </>;
}
