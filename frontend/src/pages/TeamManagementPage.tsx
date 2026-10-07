import{useEffect,useState}from"react";
import{api}from"../api";
import type{ManagedTeam,V170PeopleRow}from"../types";
import{usePagedQuery}from"../use-query";
import{Pagination}from"../components/QueryControls";
import{todayTaipei}from"../v160";
import PeopleManagementBulkPanel from"../components/PeopleManagementBulkPanel";

type TeamAssignment={teamId:number;isPrimary:boolean};
type BatchAddResult={addedCount:number;noChangeCount:number};

export default function TeamManagementPage(){
 const[selectedTeamId,setSelectedTeamId]=useState<number|null>(null);
 const[edit,setEdit]=useState<ManagedTeam|null>(null),[code,setCode]=useState(""),[name,setName]=useState(""),[active,setActive]=useState(true),[busy,setBusy]=useState(false),[msg,setMsg]=useState("");
 const[keyword,setKeyword]=useState(""),[filterActive,setFilterActive]=useState(""),[personKeyword,setPersonKeyword]=useState(""),[onlyMembers,setOnlyMembers]=useState(false),[selectedUserIds,setSelectedUserIds]=useState<number[]>([]);
 const teamQuery=usePagedQuery<ManagedTeam&{memberCount:number}>("/admin/teams/search",{keyword,isActive:filterActive===""?undefined:filterActive==="true"});
 const peopleQuery=usePagedQuery<V170PeopleRow>("/admin/people",{userType:"Internal",keyword:personKeyword,teamId:onlyMembers?selectedTeamId:undefined,sort:"code_asc"});
 const teams=teamQuery.data.items,users=peopleQuery.data.items;
 const[selectedTeam,setSelectedTeam]=useState<ManagedTeam|null>(null);

 useEffect(()=>{if(selectedTeamId===null&&teams.length){setSelectedTeamId(teams[0].teamId);setSelectedTeam(teams[0])}},[teams,selectedTeamId]);
 useEffect(()=>{setSelectedUserIds([])},[selectedTeamId,personKeyword,onlyMembers,peopleQuery.page]);
 const selectTeam=(id:number|null)=>{setSelectedTeamId(id);setSelectedTeam(teams.find(t=>t.teamId===id)||null);setSelectedUserIds([]);peopleQuery.setPage(1)};
 const load=async()=>{teamQuery.reload();peopleQuery.reload()};
 const reset=()=>{setEdit(null);setCode("");setName("");setActive(true)};
 const open=(t:ManagedTeam)=>{setEdit(t);setCode(t.teamCode);setName(t.teamName);setActive(t.isActive)};
 const saveTeam=async()=>{
  if(!code.trim()||!name.trim())return setMsg("小組代碼與小組名稱必填。");
  setBusy(true);setMsg("");
  try{
   const body=JSON.stringify({teamCode:code.trim(),teamName:name.trim(),isActive:active});
   if(edit)await api(`/admin/teams/${edit.teamId}`,{method:"PUT",body});else await api("/admin/teams",{method:"POST",body});
   setMsg(edit?"小組已更新。":"小組已新增。");reset();if(edit&&selectedTeamId===edit.teamId)setSelectedTeam({...edit,teamCode:code.trim(),teamName:name.trim(),isActive:active});await load();
  }catch(e){setMsg(e instanceof Error?e.message:"儲存失敗")}finally{setBusy(false)}
 };
 const deactivate=async(t:ManagedTeam)=>{
  if(!window.confirm(`確定停用小組「${t.teamName}」？歷史行程與 Snapshot 不會刪除。`))return;
  setBusy(true);setMsg("");
  try{await api(`/admin/teams/${t.teamId}`,{method:"DELETE"});setMsg("小組已停用。");if(selectedTeamId===t.teamId)setSelectedTeam({...t,isActive:false});await load()}
  catch(e){setMsg(e instanceof Error?e.message:"停用失敗")}finally{setBusy(false)}
 };

 const normalizeTeams=(u:V170PeopleRow):TeamAssignment[]=>u.teamAssignments.map(s=>({teamId:s.teamId,isPrimary:s.isPrimary}));
 const saveMemberships=async(u:V170PeopleRow,teamAssignments:TeamAssignment[])=>{
  await api(`/admin/people/internal-users/${u.userId}/team-memberships`,{
   method:"PUT",
   body:JSON.stringify({teamAssignments,effectiveFrom:todayTaipei()})
  });
 };
 const toggleMember=async(u:V170PeopleRow,checked:boolean)=>{
  if(!selectedTeam)return;
  const current=normalizeTeams(u);let next=current;
  if(checked){
   if(!selectedTeam.isActive)return setMsg("停用中的小組不可新增成員，請先重新啟用小組。");
   if(current.some(s=>s.teamId===selectedTeam.teamId))return;
   next=[...current,{teamId:selectedTeam.teamId,isPrimary:current.length===0}];
  }else{
   const removed=current.find(s=>s.teamId===selectedTeam.teamId);
   next=current.filter(s=>s.teamId!==selectedTeam.teamId);
   if(removed?.isPrimary&&next.length>0)next=next.map((s,i)=>({...s,isPrimary:i===0}));
  }
  setBusy(true);setMsg("");
  try{await saveMemberships(u,next);setMsg(`${u.displayName} 的小組歸屬已更新；角色未變更。`);await load()}
  catch(e){setMsg(e instanceof Error?e.message:"成員更新失敗")}finally{setBusy(false)}
 };
 const setPrimary=async(u:V170PeopleRow)=>{
  if(!selectedTeam)return;
  const current=normalizeTeams(u);
  if(!current.some(s=>s.teamId===selectedTeam.teamId))return setMsg("請先將此人加入小組。");
  const next=current.map(s=>({...s,isPrimary:s.teamId===selectedTeam.teamId}));
  setBusy(true);setMsg("");
  try{await saveMemberships(u,next);setMsg(`${u.displayName} 的主要小組已更新；角色未變更。`);await load()}
  catch(e){setMsg(e instanceof Error?e.message:"主要小組更新失敗")}finally{setBusy(false)}
 };

 const selectableUsers=users.filter(u=>selectedTeam&&!u.teamAssignments.some(s=>s.teamId===selectedTeam.teamId));
 const allPageSelected=selectableUsers.length>0&&selectableUsers.every(u=>selectedUserIds.includes(u.userId));
 const batchAdd=async()=>{
  if(!selectedTeam||!selectedUserIds.length)return setMsg("請先選擇要加入此小組的人員。");
  if(!selectedTeam.isActive)return setMsg("停用中的小組不可新增成員。");
  if(!window.confirm(`確認將 ${selectedUserIds.length} 位人員加入「${selectedTeam.teamName}」？既有角色不會變更。`))return;
  setBusy(true);setMsg("");
  try{
   const r=await api<BatchAddResult>(`/admin/people/team-memberships/${selectedTeam.teamId}/batch-add`,{method:"POST",body:JSON.stringify({userIds:selectedUserIds,effectiveFrom:todayTaipei()})});
   setSelectedUserIds([]);setMsg(`批次加入完成：新增 ${r.addedCount} 位、原本已在小組 ${r.noChangeCount} 位；角色未變更。`);await load();
  }catch(e){setMsg(e instanceof Error?e.message:"批次加入失敗")}finally{setBusy(false)}
 };

 return <>
  <div className="grid cols-2">
   <div className="card">
    <div className="section-title"><div><h2>{edit?"修改小組":"新增小組"}</h2><div className="sub">小組主檔採新增／修改／停用，不做硬刪除。</div></div>{edit&&<button className="btn small outline" onClick={reset}>取消修改</button>}</div>
    <div className="field"><label>小組代碼</label><input value={code} onChange={e=>setCode(e.target.value.toUpperCase())} placeholder="例如 TEAM-001"/></div>
    <div className="field"><label>小組名稱</label><input value={name} onChange={e=>setName(e.target.value)} placeholder="例如 北區第一組"/></div>
    {edit&&<label className="check-row"><input type="checkbox" checked={active} onChange={e=>setActive(e.target.checked)}/>啟用小組</label>}
    <div className="actions" style={{marginTop:14}}><button className="btn" disabled={busy} onClick={()=>void saveTeam()}>{edit?"儲存修改":"新增小組"}</button></div>
   </div>
   <div className="card">
    <div className="section-title"><div><h2>小組主檔</h2><div className="sub">停用不刪除歷史資料；成員歸屬與角色權限分開維護。</div></div></div>
    <div className="grid cols-2"><label>小組搜尋<input value={keyword} onChange={e=>setKeyword(e.target.value)} placeholder="小組代碼或名稱"/></label><label>小組狀態<select value={filterActive} onChange={e=>setFilterActive(e.target.value)}><option value="">全部</option><option value="true">啟用</option><option value="false">停用</option></select></label></div>{teamQuery.error&&<div role="alert" className="note danger-note">{teamQuery.error}</div>}<div className="table-wrap"><table><thead><tr><th>代碼</th><th>名稱</th><th>狀態</th><th>成員數</th><th>操作</th></tr></thead><tbody>{teams.map(t=><tr key={t.teamId} className={t.isActive?"":"team-inactive"}><td>{t.teamCode}</td><td>{t.teamName}</td><td>{t.isActive?"啟用":"停用"}</td><td>{t.memberCount}</td><td><div className="actions"><button className="btn small secondary" onClick={()=>open(t)}>維護</button>{t.isActive&&<button className="btn small danger" disabled={busy} onClick={()=>void deactivate(t)}>停用</button>}<button className="btn small outline" onClick={()=>selectTeam(t.teamId)}>成員</button></div></td></tr>)}</tbody></table></div><Pagination {...teamQuery.data} page={teamQuery.page} pageSize={teamQuery.pageSize} busy={teamQuery.loading} onPage={teamQuery.setPage} onPageSize={teamQuery.setPageSize}/>
   </div>
  </div>

  {msg&&<div className="note" style={{marginTop:14}}>{msg}</div>}

  <div style={{marginTop:18}}><PeopleManagementBulkPanel title="小組成員 Excel 批次維護" description="只維護小組歸屬、主要小組與有效期間；不修改角色、帳號或人事主檔。" templateUrl="/admin/people/team-membership-bulk/template.xlsx" previewUrl="/admin/people/team-membership-bulk/preview" confirmUrl="/admin/people/team-membership-bulk/confirm" templateFilename="小組成員批次維護.xlsx" onConfirmed={load}/></div>

  <div className="card" style={{marginTop:18}}>
   <div className="section-title"><div><h2>小組成員維護{selectedTeam?`｜${selectedTeam.teamCode} ${selectedTeam.teamName}`:""}</h2><div className="sub">小組歸屬是本頁唯一責任；角色請到「人員與權限」維護。系統同步 v1.7 權限投影與 v1.8 Trip Context membership。</div></div>
    <select value={selectedTeamId??""} onChange={e=>selectTeam(e.target.value?Number(e.target.value):null)}><option value="">選擇小組</option>{selectedTeam&&!teams.some(t=>t.teamId===selectedTeam.teamId)&&<option value={selectedTeam.teamId}>{selectedTeam.teamCode}｜{selectedTeam.teamName}</option>}{teams.map(t=><option key={t.teamId} value={t.teamId}>{t.teamCode}｜{t.teamName}{t.isActive?"":"（停用）"}</option>)}</select>
   </div>
   {!selectedTeam?<div className="empty">請先選擇小組。</div>:<>
    <div className="sub" style={{marginBottom:10}}>搜尋既有人員後可多選一次加入；External Supervisor 依 Data Scope 管理，不加入一般小組。★ 代表主要小組。</div>
    <div className="grid cols-2"><label>成員搜尋<input value={personKeyword} placeholder="工號、姓名或 Email" onChange={e=>setPersonKeyword(e.target.value)}/></label><label className="check-row"><input type="checkbox" checked={onlyMembers} onChange={e=>setOnlyMembers(e.target.checked)}/>僅顯示此小組成員</label></div>
    <div className="actions" style={{marginBottom:10}}><button className="btn small ok" disabled={busy||!selectedUserIds.length||!selectedTeam.isActive} onClick={()=>void batchAdd()}>批次加入選取人員（{selectedUserIds.length}）</button><span className="sub">批次加入不修改任何角色。</span></div>
    {peopleQuery.error&&<div role="alert" className="note danger-note">{peopleQuery.error}</div>}
    <div className="table-wrap"><table><thead><tr><th><input type="checkbox" aria-label="選取本頁可加入人員" checked={allPageSelected} disabled={!selectableUsers.length} onChange={e=>setSelectedUserIds(e.target.checked?selectableUsers.map(u=>u.userId):[])}/></th><th>員編</th><th>姓名</th><th>角色（唯讀）</th><th>{selectedTeam.teamCode} 成員</th><th>主要小組</th><th>目前所屬小組</th></tr></thead><tbody>{users.map(u=>{
     const membership=u.teamAssignments.find(s=>s.teamId===selectedTeam.teamId);const selectable=!membership&&selectedTeam.isActive;
     return <tr key={u.userId}><td><input type="checkbox" aria-label={`選取 ${u.displayName}`} disabled={!selectable||busy} checked={selectedUserIds.includes(u.userId)} onChange={e=>setSelectedUserIds(x=>e.target.checked?[...x,u.userId]:x.filter(id=>id!==u.userId))}/></td><td>{u.employeeNo||u.userCode}</td><td>{u.displayName}</td><td>{u.roles.join("、")||"—"}</td><td><label className="check-row"><input type="checkbox" checked={!!membership} disabled={busy||(!selectedTeam.isActive&&!membership)} onChange={e=>void toggleMember(u,e.target.checked)}/>{membership?"已加入":"加入"}</label></td><td>{membership?<label className="check-row"><input type="radio" name={`primary-${u.userId}`} checked={membership.isPrimary} disabled={busy} onChange={()=>void setPrimary(u)}/>{membership.isPrimary?"主要":"設為主要"}</label>:"—"}</td><td>{u.teamAssignments.map(s=>`${s.teamName}${s.isPrimary?" ★":""}`).join("、")||"—"}</td></tr>
    })}</tbody></table></div><Pagination {...peopleQuery.data} page={peopleQuery.page} pageSize={peopleQuery.pageSize} busy={peopleQuery.loading} onPage={peopleQuery.setPage} onPageSize={peopleQuery.setPageSize}/>
   </>}
  </div>
 </>;
}
