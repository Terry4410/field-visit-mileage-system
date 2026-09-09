import{useEffect,useState}from"react";
import{api}from"../api";
import type{PagedResult,V180PersonRow}from"../types";
import{usePagedQuery}from"../use-query";
import{Pagination}from"../components/QueryControls";
import{todayTaipei}from"../v160";
import{v180AccessErrorMessage,v180RoleCodes}from"../v180-people-ui";
import type{V180CenterAdmin,V180CenterLifecycleDetail,V180TeamAdmin,V180TeamCenterAssignment,V180TeamLifecycleDetail}from"../v180-team-center-ui";
import{lifecycleEndDate,lifecycleStatus,v180LifecycleErrorMessage}from"../v180-team-center-ui";

export default function TeamManagementPage(){
 const today=todayTaipei();
 const[busy,setBusy]=useState(false),[msg,setMsg]=useState("");
 const[selectedTeamId,setSelectedTeamId]=useState<number|null>(null),[selectedTeam,setSelectedTeam]=useState<V180TeamAdmin|null>(null);
 const[keyword,setKeyword]=useState(""),[includeInactiveTeams,setIncludeInactiveTeams]=useState(true);
 const[centerKeyword,setCenterKeyword]=useState(""),[includeInactiveCenters,setIncludeInactiveCenters]=useState(true);
 const[personKeyword,setPersonKeyword]=useState(""),[onlyMembers,setOnlyMembers]=useState(false);

 const teamQuery=usePagedQuery<V180TeamAdmin>("/admin/v180/teams",{keyword,includeInactive:includeInactiveTeams});
 const centerQuery=usePagedQuery<V180CenterAdmin>("/admin/v180/centers",{keyword:centerKeyword,includeInactive:includeInactiveCenters});
 const peopleQuery=usePagedQuery<V180PersonRow>("/admin/v180/people",{keyword:personKeyword,teamId:onlyMembers?selectedTeamId:undefined,internalOnly:true,includeInactive:false});
 const teams=teamQuery.data.items,centers=centerQuery.data.items,users=peopleQuery.data.items;

 const[editTeam,setEditTeam]=useState<V180TeamLifecycleDetail|null>(null),[teamCode,setTeamCode]=useState(""),[teamName,setTeamName]=useState(""),[teamFrom,setTeamFrom]=useState(today),[teamTo,setTeamTo]=useState(""),[teamActive,setTeamActive]=useState(true),[teamNotes,setTeamNotes]=useState("");
 const[editCenter,setEditCenter]=useState<V180CenterLifecycleDetail|null>(null),[centerCode,setCenterCode]=useState(""),[centerName,setCenterName]=useState(""),[centerFrom,setCenterFrom]=useState(today),[centerTo,setCenterTo]=useState(""),[centerActive,setCenterActive]=useState(true),[centerNotes,setCenterNotes]=useState("");
 const[centerOptions,setCenterOptions]=useState<V180CenterAdmin[]>([]);
 const[assignments,setAssignments]=useState<V180TeamCenterAssignment[]>([]),[assignmentBusy,setAssignmentBusy]=useState(false);
 const[editAssignment,setEditAssignment]=useState<V180TeamCenterAssignment|null>(null),[assignmentCenterId,setAssignmentCenterId]=useState<number|null>(null),[assignmentFrom,setAssignmentFrom]=useState(today),[assignmentTo,setAssignmentTo]=useState(""),[assignmentReason,setAssignmentReason]=useState("");

 const loadCenterOptions=async()=>{
  try{const result=await api<PagedResult<V180CenterAdmin>>("/admin/v180/centers?includeInactive=true&page=1&pageSize=100");setCenterOptions(result.items)}catch(e){setMsg(v180LifecycleErrorMessage(e,"中心選項載入失敗"))}
 };
 const loadAssignments=async(teamId=selectedTeamId)=>{
  if(!teamId){setAssignments([]);return}
  setAssignmentBusy(true);
  try{setAssignments(await api<V180TeamCenterAssignment[]>(`/admin/v180/team-center-assignments?teamId=${teamId}&includeHistory=true`))}
  catch(e){setMsg(v180LifecycleErrorMessage(e,"Team-Center 關聯載入失敗"))}finally{setAssignmentBusy(false)}
 };
 const load=async()=>{teamQuery.reload();centerQuery.reload();peopleQuery.reload();await loadCenterOptions();await loadAssignments()};

 useEffect(()=>{void loadCenterOptions()},[]);
 useEffect(()=>{void loadAssignments(selectedTeamId)},[selectedTeamId]);
 useEffect(()=>{
  if(selectedTeamId===null&&teams.length){setSelectedTeamId(teams[0].teamId);setSelectedTeam(teams[0]);return}
  if(selectedTeamId!==null){const latest=teams.find(t=>t.teamId===selectedTeamId);if(latest)setSelectedTeam(latest)}
 },[teams,selectedTeamId]);

 const selectTeam=(team:V180TeamAdmin|null)=>{setSelectedTeamId(team?.teamId??null);setSelectedTeam(team);peopleQuery.setPage(1);setEditAssignment(null)};
 const resetTeam=()=>{setEditTeam(null);setTeamCode("");setTeamName("");setTeamFrom(today);setTeamTo("");setTeamActive(true);setTeamNotes("")};
 const openTeam=async(t:V180TeamAdmin)=>{
  setBusy(true);setMsg("");
  try{const d=await api<V180TeamLifecycleDetail>(`/admin/v180/teams/${t.teamId}`);setEditTeam(d);setTeamCode(d.code);setTeamName(d.name);setTeamFrom(d.effectiveFrom||today);setTeamTo(d.effectiveTo||"");setTeamActive(d.isActive);setTeamNotes(d.notes||"")}
  catch(e){setMsg(v180LifecycleErrorMessage(e,"小組資料載入失敗"))}finally{setBusy(false)}
 };
 const saveTeam=async()=>{
  if(!teamCode.trim()||!teamName.trim())return setMsg("小組代碼與小組名稱必填。");
  if(teamTo&&teamTo<teamFrom)return setMsg("小組結束日不得早於開始日。");
  if(!teamActive&&!teamTo)return setMsg("停用小組必須填寫結束日。");
  setBusy(true);setMsg("");
  try{
   const payload={code:teamCode.trim(),name:teamName.trim(),effectiveFrom:teamFrom,effectiveTo:teamTo||null,notes:teamNotes.trim()||null,isActive:teamActive};
   if(editTeam)await api(`/admin/v180/teams/${editTeam.teamId}`,{method:"PUT",body:JSON.stringify({...payload,version:editTeam.version})});
   else await api("/admin/v180/teams",{method:"POST",body:JSON.stringify(payload)});
   setMsg(editTeam?"小組生命週期已更新。":"小組已新增。");resetTeam();await load();
  }catch(e){setMsg(v180LifecycleErrorMessage(e))}finally{setBusy(false)}
 };
 const deactivateTeam=async(t:V180TeamAdmin)=>{
  if(!window.confirm(`確定停用小組「${t.name}」？歷史成員、行程與 Snapshot 不會刪除。`))return;
  const effectiveTo=lifecycleEndDate(t.effectiveFrom,today);
  setBusy(true);setMsg("");
  try{await api(`/admin/v180/teams/${t.teamId}/deactivate`,{method:"POST",body:JSON.stringify({effectiveTo,version:t.version})});setMsg("小組已停用；歷史資料保留。");if(selectedTeamId===t.teamId)setSelectedTeam({...t,isActive:false,effectiveTo});await load()}
  catch(e){setMsg(v180LifecycleErrorMessage(e,"停用失敗"))}finally{setBusy(false)}
 };

 const resetCenter=()=>{setEditCenter(null);setCenterCode("");setCenterName("");setCenterFrom(today);setCenterTo("");setCenterActive(true);setCenterNotes("")};
 const openCenter=async(c:V180CenterAdmin)=>{
  setBusy(true);setMsg("");
  try{const d=await api<V180CenterLifecycleDetail>(`/admin/v180/centers/${c.centerId}`);setEditCenter(d);setCenterCode(d.code);setCenterName(d.name);setCenterFrom(d.effectiveFrom);setCenterTo(d.effectiveTo||"");setCenterActive(d.isActive);setCenterNotes(d.notes||"")}
  catch(e){setMsg(v180LifecycleErrorMessage(e,"中心資料載入失敗"))}finally{setBusy(false)}
 };
 const saveCenter=async()=>{
  if(!centerCode.trim()||!centerName.trim())return setMsg("中心代碼與中心名稱必填。");
  if(centerTo&&centerTo<centerFrom)return setMsg("中心結束日不得早於開始日。");
  if(!centerActive&&!centerTo)return setMsg("停用中心必須填寫結束日。");
  setBusy(true);setMsg("");
  try{
   const payload={code:centerCode.trim(),name:centerName.trim(),effectiveFrom:centerFrom,effectiveTo:centerTo||null,notes:centerNotes.trim()||null,isActive:centerActive};
   if(editCenter)await api(`/admin/v180/centers/${editCenter.centerId}`,{method:"PUT",body:JSON.stringify({...payload,version:editCenter.version})});
   else await api("/admin/v180/centers",{method:"POST",body:JSON.stringify(payload)});
   setMsg(editCenter?"中心生命週期已更新。":"中心已新增。");resetCenter();await load();
  }catch(e){setMsg(v180LifecycleErrorMessage(e))}finally{setBusy(false)}
 };
 const deactivateCenter=async(c:V180CenterAdmin)=>{
  if(!window.confirm(`確定停用中心「${c.name}」？必須先結束仍生效或未來的 Team-Center 關聯。`))return;
  const effectiveTo=lifecycleEndDate(c.effectiveFrom,today);
  setBusy(true);setMsg("");
  try{await api(`/admin/v180/centers/${c.centerId}/deactivate`,{method:"POST",body:JSON.stringify({effectiveTo,version:c.version})});setMsg("中心已停用；歷史資料保留。");await load()}
  catch(e){setMsg(v180LifecycleErrorMessage(e,"停用失敗"))}finally{setBusy(false)}
 };

 const resetAssignment=()=>{setEditAssignment(null);setAssignmentCenterId(null);setAssignmentFrom(today);setAssignmentTo("");setAssignmentReason("")};
 const openAssignment=(a:V180TeamCenterAssignment)=>{setEditAssignment(a);setAssignmentCenterId(a.centerId);setAssignmentFrom(a.effectiveFrom);setAssignmentTo(a.effectiveTo||"");setAssignmentReason(a.changeReason||"")};
 const saveAssignment=async()=>{
  if(!selectedTeamId)return setMsg("請先選擇小組。");
  if(!assignmentCenterId)return setMsg("請選擇中心。");
  if(assignmentTo&&assignmentTo<assignmentFrom)return setMsg("Team-Center 結束日不得早於開始日。");
  setBusy(true);setMsg("");
  try{
   if(editAssignment)await api(`/admin/v180/team-center-assignments/${editAssignment.teamCenterAssignmentId}`,{method:"PUT",body:JSON.stringify({centerId:assignmentCenterId,effectiveFrom:assignmentFrom,effectiveTo:assignmentTo||null,changeReason:assignmentReason.trim()||null,version:editAssignment.version})});
   else await api("/admin/v180/team-center-assignments",{method:"POST",body:JSON.stringify({teamId:selectedTeamId,centerId:assignmentCenterId,effectiveFrom:assignmentFrom,effectiveTo:assignmentTo||null,changeReason:assignmentReason.trim()||null})});
   setMsg(editAssignment?"Team-Center 關聯已更新。":"Team-Center 關聯已新增。");resetAssignment();await loadAssignments(selectedTeamId);teamQuery.reload();
  }catch(e){setMsg(v180LifecycleErrorMessage(e))}finally{setBusy(false)}
 };
 const endAssignment=async(a:V180TeamCenterAssignment)=>{
  if(!window.confirm(`確定結束 ${a.centerCode} ${a.centerName} 的 Team-Center 關聯？`))return;
  const effectiveTo=lifecycleEndDate(a.effectiveFrom,today);
  setBusy(true);setMsg("");
  try{await api(`/admin/v180/team-center-assignments/${a.teamCenterAssignmentId}/end`,{method:"POST",body:JSON.stringify({effectiveTo,version:a.version})});setMsg("Team-Center 關聯已結束；歷史保留。");await loadAssignments(selectedTeamId);teamQuery.reload()}
  catch(e){setMsg(v180LifecycleErrorMessage(e,"關聯結束失敗"))}finally{setBusy(false)}
 };

 const normalizeTeams=(u:V180PersonRow)=>u.teamMemberships.map(s=>({teamId:s.teamId,isPrimary:s.isPrimary}));
 const saveAccess=async(u:V180PersonRow,teamMemberships:Array<{teamId:number;isPrimary:boolean}>)=>{
  if(u.adminEnabled===null||u.adminEnabled===undefined)throw new Error("此任職資料沒有可安全解析的登入帳號，無法修改小組成員權限。");
  await api(`/admin/v180/people/${u.employmentId}/access`,{method:"PUT",body:JSON.stringify({roles:v180RoleCodes(u),teamMemberships,adminEnabled:u.adminEnabled,changeEffectiveFrom:today,confirmRetroactive:false,version:u.version})});
 };
 const toggleMember=async(u:V180PersonRow,checked:boolean)=>{
  if(!selectedTeam)return;
  const current=normalizeTeams(u);let next=current;
  if(checked){
   if(!selectedTeam.isActive)return setMsg("停用中的小組不可新增成員，請先重新啟用小組。");
   if(current.some(s=>s.teamId===selectedTeam.teamId))return;
   next=[...current,{teamId:selectedTeam.teamId,isPrimary:current.length===0}];
  }else{
   const removed=current.find(s=>s.teamId===selectedTeam.teamId);next=current.filter(s=>s.teamId!==selectedTeam.teamId);
   if(removed?.isPrimary&&next.length>0)next=next.map((s,i)=>({...s,isPrimary:i===0}));
   const requiresTeam=v180RoleCodes(u).some(r=>["visitor","leader"].includes(r));
   if(requiresTeam&&next.length===0)return setMsg(`${u.displayName} 具有「外訪員／小組長」角色且只有此小組；請先在人員與權限調整角色，或先加入另一個小組。`);
  }
  setBusy(true);setMsg("");
  try{await saveAccess(u,next);setMsg(`${u.displayName} 的小組成員設定已更新；畫面已重新載入最新版本。該使用者需重新登入取得最新權限。`);await load()}
  catch(e){setMsg(v180AccessErrorMessage(e,"成員更新失敗"))}finally{setBusy(false)}
 };
 const setPrimary=async(u:V180PersonRow)=>{
  if(!selectedTeam)return;
  const current=normalizeTeams(u);if(!current.some(s=>s.teamId===selectedTeam.teamId))return setMsg("請先將此人加入小組。");
  const next=current.map(s=>({...s,isPrimary:s.teamId===selectedTeam.teamId}));
  setBusy(true);setMsg("");
  try{await saveAccess(u,next);setMsg(`${u.displayName} 的主要小組已更新；畫面已重新載入最新版本。該使用者需重新登入取得最新權限。`);await load()}
  catch(e){setMsg(v180AccessErrorMessage(e,"主要小組更新失敗"))}finally{setBusy(false)}
 };

 return <>
  {msg&&<div className="note" style={{marginBottom:14}}>{msg}</div>}
  <div className="grid cols-2">
   <div className="card">
    <div className="section-title"><div><h2>{editTeam?"修改小組":"新增小組"}</h2><div className="sub">v1.8 lifecycle：新增、修改、停用均保留歷史，更新採 RowVersion。</div></div>{editTeam&&<button className="btn small outline" onClick={resetTeam}>取消修改</button>}</div>
    <div className="grid cols-2"><label>小組代碼<input value={teamCode} onChange={e=>setTeamCode(e.target.value.toUpperCase())}/></label><label>小組名稱<input value={teamName} onChange={e=>setTeamName(e.target.value)}/></label><label>開始日<input type="date" value={teamFrom} onChange={e=>setTeamFrom(e.target.value)}/></label><label>結束日<input type="date" value={teamTo} onChange={e=>setTeamTo(e.target.value)}/></label></div>
    <label className="check-row"><input type="checkbox" checked={teamActive} onChange={e=>{setTeamActive(e.target.checked);if(e.target.checked)setTeamTo("")}}/>啟用小組</label>
    <label>備註<textarea value={teamNotes} onChange={e=>setTeamNotes(e.target.value)}/></label>
    <div className="actions" style={{marginTop:14}}><button className="btn" disabled={busy} onClick={()=>void saveTeam()}>{editTeam?"儲存修改":"新增小組"}</button></div>
   </div>
   <div className="card">
    <div className="section-title"><div><h2>小組主檔</h2><div className="sub">直接使用 v1.8 Team lifecycle API；不再透過 legacy Team write route。</div></div></div>
    <div className="grid cols-2"><label>小組搜尋<input value={keyword} onChange={e=>setKeyword(e.target.value)} placeholder="小組代碼或名稱"/></label><label className="check-row"><input type="checkbox" checked={includeInactiveTeams} onChange={e=>setIncludeInactiveTeams(e.target.checked)}/>包含停用／非目前有效</label></div>
    {teamQuery.error&&<div role="alert" className="note danger-note">{teamQuery.error}</div>}
    <div className="table-wrap"><table><thead><tr><th>代碼</th><th>名稱</th><th>生命週期</th><th>中心</th><th>成員</th><th>操作</th></tr></thead><tbody>{teams.map(t=><tr key={t.teamId} className={t.isActive?"":"team-inactive"}><td>{t.code}</td><td>{t.name}</td><td>{t.effectiveFrom?lifecycleStatus(t.effectiveFrom,t.effectiveTo,today):(t.isActive?"生效中":"已結束")}</td><td>{t.centerCode?`${t.centerCode} ${t.centerName||""}`:"—"}</td><td>{t.memberCount}</td><td><div className="actions"><button className="btn small secondary" onClick={()=>void openTeam(t)}>維護</button>{t.isActive&&<button className="btn small danger" disabled={busy} onClick={()=>void deactivateTeam(t)}>停用</button>}<button className="btn small outline" onClick={()=>selectTeam(t)}>成員／中心</button></div></td></tr>)}</tbody></table></div>
    <Pagination {...teamQuery.data} page={teamQuery.page} pageSize={teamQuery.pageSize} busy={teamQuery.loading} onPage={teamQuery.setPage} onPageSize={teamQuery.setPageSize}/>
   </div>
  </div>

  <div className="grid cols-2" style={{marginTop:18}}>
   <div className="card">
    <div className="section-title"><div><h2>{editCenter?"修改中心":"新增中心"}</h2><div className="sub">Center 為 Team 與後續 Deployment Site 的上層 lifecycle 主檔。</div></div>{editCenter&&<button className="btn small outline" onClick={resetCenter}>取消修改</button>}</div>
    <div className="grid cols-2"><label>中心代碼<input value={centerCode} onChange={e=>setCenterCode(e.target.value.toUpperCase())}/></label><label>中心名稱<input value={centerName} onChange={e=>setCenterName(e.target.value)}/></label><label>開始日<input type="date" value={centerFrom} onChange={e=>setCenterFrom(e.target.value)}/></label><label>結束日<input type="date" value={centerTo} onChange={e=>setCenterTo(e.target.value)}/></label></div>
    <label className="check-row"><input type="checkbox" checked={centerActive} onChange={e=>{setCenterActive(e.target.checked);if(e.target.checked)setCenterTo("")}}/>啟用中心</label>
    <label>備註<textarea value={centerNotes} onChange={e=>setCenterNotes(e.target.value)}/></label>
    <div className="actions" style={{marginTop:14}}><button className="btn" disabled={busy} onClick={()=>void saveCenter()}>{editCenter?"儲存修改":"新增中心"}</button></div>
   </div>
   <div className="card">
    <div className="section-title"><div><h2>中心主檔</h2><div className="sub">停用前必須先結束仍生效或未來的 Team-Center 關聯。</div></div></div>
    <div className="grid cols-2"><label>中心搜尋<input value={centerKeyword} onChange={e=>setCenterKeyword(e.target.value)} placeholder="中心代碼或名稱"/></label><label className="check-row"><input type="checkbox" checked={includeInactiveCenters} onChange={e=>setIncludeInactiveCenters(e.target.checked)}/>包含停用／非目前有效</label></div>
    {centerQuery.error&&<div role="alert" className="note danger-note">{centerQuery.error}</div>}
    <div className="table-wrap"><table><thead><tr><th>代碼</th><th>名稱</th><th>生命週期</th><th>操作</th></tr></thead><tbody>{centers.map(c=><tr key={c.centerId} className={c.isActive?"":"team-inactive"}><td>{c.code}</td><td>{c.name}</td><td>{lifecycleStatus(c.effectiveFrom,c.effectiveTo,today)}</td><td><div className="actions"><button className="btn small secondary" onClick={()=>void openCenter(c)}>維護</button>{c.isActive&&<button className="btn small danger" disabled={busy} onClick={()=>void deactivateCenter(c)}>停用</button>}</div></td></tr>)}</tbody></table></div>
    <Pagination {...centerQuery.data} page={centerQuery.page} pageSize={centerQuery.pageSize} busy={centerQuery.loading} onPage={centerQuery.setPage} onPageSize={centerQuery.setPageSize}/>
   </div>
  </div>

  <div className="card" style={{marginTop:18}}>
   <div className="section-title"><div><h2>Team-Center 關聯{selectedTeam?`｜${selectedTeam.code} ${selectedTeam.name}`:""}</h2><div className="sub">同一 Team 的有效期間不可重疊；關聯期間必須完整位於 Team 與 Center lifecycle 內。</div></div><select value={selectedTeamId??""} onChange={e=>{const id=e.target.value?Number(e.target.value):null;selectTeam(id===null?null:(teams.find(t=>t.teamId===id)||(selectedTeam?.teamId===id?selectedTeam:null)))}}><option value="">選擇小組</option>{selectedTeam&&!teams.some(t=>t.teamId===selectedTeam.teamId)&&<option value={selectedTeam.teamId}>{selectedTeam.code}｜{selectedTeam.name}</option>}{teams.map(t=><option key={t.teamId} value={t.teamId}>{t.code}｜{t.name}{t.isActive?"":"（停用）"}</option>)}</select></div>
   {!selectedTeam?<div className="empty">請先選擇小組。</div>:<div className="grid cols-2">
    <div><h4>{editAssignment?"修改 Team-Center 關聯":"新增 Team-Center 關聯"}</h4><label>中心<select value={assignmentCenterId??""} onChange={e=>setAssignmentCenterId(e.target.value?Number(e.target.value):null)}><option value="">選擇中心</option>{centerOptions.map(c=><option key={c.centerId} value={c.centerId}>{c.code}｜{c.name}{c.isActive?"":"（停用）"}</option>)}</select></label><div className="grid cols-2"><label>開始日<input type="date" value={assignmentFrom} onChange={e=>setAssignmentFrom(e.target.value)}/></label><label>結束日<input type="date" value={assignmentTo} onChange={e=>setAssignmentTo(e.target.value)}/></label></div><label>異動原因<textarea value={assignmentReason} onChange={e=>setAssignmentReason(e.target.value)}/></label><div className="actions"><button className="btn" disabled={busy||!selectedTeam.isActive} onClick={()=>void saveAssignment()}>{editAssignment?"儲存關聯修改":"新增關聯"}</button>{editAssignment&&<button className="btn outline" onClick={resetAssignment}>取消修改</button>}</div></div>
    <div><h4>關聯歷史／排程</h4>{assignmentBusy?<div className="sub">載入中…</div>:<div className="table-wrap"><table><thead><tr><th>中心</th><th>開始</th><th>結束</th><th>狀態</th><th>操作</th></tr></thead><tbody>{assignments.map(a=><tr key={a.teamCenterAssignmentId}><td>{a.centerCode} {a.centerName}</td><td>{a.effectiveFrom}</td><td>{a.effectiveTo||"—"}</td><td>{lifecycleStatus(a.effectiveFrom,a.effectiveTo,today)}</td><td><div className="actions"><button className="btn small secondary" onClick={()=>openAssignment(a)}>維護</button>{(!a.effectiveTo||a.effectiveTo>=today)&&<button className="btn small danger" disabled={busy} onClick={()=>void endAssignment(a)}>結束</button>}</div></td></tr>)}</tbody></table></div>}</div>
   </div>}
  </div>

  <div className="card" style={{marginTop:18}}>
   <div className="section-title"><div><h2>小組成員維護{selectedTeam?`｜${selectedTeam.code} ${selectedTeam.name}`:""}</h2><div className="sub">v1.8：以 Employment 與有效 TeamMembership 為唯一成員權限來源；儲存採版本控制並同步相容投影。</div></div></div>
   {!selectedTeam?<div className="empty">請先在上方選擇小組。</div>:<><div className="sub" style={{marginBottom:10}}>僅顯示 Internal Employment；External Supervisor 依 Data Scope 管理，不可加入一般小組。★ 代表主要小組。</div><div className="grid cols-2"><label>成員搜尋<input value={personKeyword} placeholder="工號、姓名或 Email" onChange={e=>setPersonKeyword(e.target.value)}/></label><label className="check-row"><input type="checkbox" checked={onlyMembers} onChange={e=>setOnlyMembers(e.target.checked)}/>僅顯示此小組成員</label></div>{peopleQuery.error&&<div role="alert" className="note danger-note">{peopleQuery.error}</div>}<div className="table-wrap"><table><thead><tr><th>員編</th><th>姓名</th><th>角色</th><th>{selectedTeam.code} {selectedTeam.name}成員</th><th>是否主要小組</th><th>所屬小組</th></tr></thead><tbody>{users.map(u=>{const scope=u.teamMemberships.find(s=>s.teamId===selectedTeam.teamId);const accountReady=u.adminEnabled!==null&&u.adminEnabled!==undefined;return <tr key={u.employmentId}><td>{u.employeeNo||`Employment ${u.employmentId}`}</td><td>{u.displayName}</td><td>{v180RoleCodes(u).join("、")||"—"}</td><td><label className="check-row"><input type="checkbox" checked={!!scope} disabled={busy||!accountReady||(!selectedTeam.isActive&&!scope)} onChange={e=>void toggleMember(u,e.target.checked)}/>{!accountReady?"無登入帳號":scope?"已加入此小組":"加入此小組"}</label></td><td>{scope?<label className="check-row"><input type="radio" name={`primary-${u.employmentId}`} checked={scope.isPrimary} disabled={busy||!accountReady} onChange={()=>void setPrimary(u)}/>{scope.isPrimary?"主要小組":"設為主要"}</label>:"—"}</td><td>{u.teamMemberships.map(s=>`${s.name}${s.isPrimary?" ★":""}`).join("、")||"—"}</td></tr>})}</tbody></table></div><Pagination {...peopleQuery.data} page={peopleQuery.page} pageSize={peopleQuery.pageSize} busy={peopleQuery.loading} onPage={peopleQuery.setPage} onPageSize={peopleQuery.setPageSize}/></>}
  </div>
 </>;
}
