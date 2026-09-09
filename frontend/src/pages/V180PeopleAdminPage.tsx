import{useEffect,useState}from"react";
import{api}from"../api";
import PeopleBulkPanel from"../components/PeopleBulkPanel";
import{Pagination}from"../components/QueryControls";
import type{Team,V180PersonRow}from"../types";
import{usePagedQuery}from"../use-query";
import{todayTaipei}from"../v160";
import{canEditV180InternalAccess,V180_INTERNAL_ROLE_LABELS,v180AccessErrorMessage,v180RoleCodes}from"../v180-people-ui";

const roleLabels:Record<string,string>={...V180_INTERNAL_ROLE_LABELS,supervisor:"督導"};

type TeamMembershipInput={teamId:number;isPrimary:boolean};

export default function V180PeopleAdminPage(){
 const[msg,setMsg]=useState(""),[busy,setBusy]=useState(false);
 const[teams,setTeams]=useState<Team[]>([]),[edit,setEdit]=useState<V180PersonRow|null>(null);
 const[active,setActive]=useState(true),[roles,setRoles]=useState<string[]>([]),[memberships,setMemberships]=useState<TeamMembershipInput[]>([]);
 const[keyword,setKeyword]=useState(""),[filterRole,setFilterRole]=useState(""),[filterActive,setFilterActive]=useState("");
 const query=usePagedQuery<V180PersonRow>("/admin/v180/people",{
  keyword,
  includeInactive:true,
  role:filterRole||undefined,
  adminEnabled:filterActive===""?undefined:filterActive==="true"
 });
 const rows=query.data.items;
 const load=()=>query.reload();
 useEffect(()=>{api<Team[]>("/teams").then(setTeams).catch(e=>setMsg(e instanceof Error?e.message:"小組載入失敗"))},[]);

 const open=(person:V180PersonRow)=>{
  if(!canEditV180InternalAccess(person)){
   setMsg(v180RoleCodes(person).includes("supervisor")
    ?"外部督導請使用既有的 External Supervisor 專用管理流程；此處不會以 Internal Employment 異動。"
    :"此任職資料沒有可安全解析的登入帳號，無法在此修改帳號權限。");
   return;
  }
  setEdit(person);
  setActive(person.adminEnabled===true);
  setRoles(v180RoleCodes(person).filter(role=>Object.hasOwn(V180_INTERNAL_ROLE_LABELS,role)));
  setMemberships(person.teamMemberships.map(team=>({teamId:team.teamId,isPrimary:team.isPrimary})));
  setMsg("");
 };
 const toggleRole=(role:string)=>setRoles(current=>current.includes(role)?current.filter(x=>x!==role):[...current,role]);
 const toggleTeam=(teamId:number)=>setMemberships(current=>{
  const existing=current.find(x=>x.teamId===teamId);
  if(!existing)return[...current,{teamId,isPrimary:current.length===0}];
  const next=current.filter(x=>x.teamId!==teamId);
  return existing.isPrimary&&next.length>0?next.map((x,index)=>({...x,isPrimary:index===0})):next;
 });
 const primary=(teamId:number)=>setMemberships(current=>current.map(x=>({...x,isPrimary:x.teamId===teamId})));
 const save=async()=>{
  if(!edit)return;
  if(!roles.length)return setMsg("至少需要一個 Internal 角色。");
  if(memberships.length>0&&memberships.filter(x=>x.isPrimary).length!==1)return setMsg("有小組授權時必須且只能指定一個主要小組。");
  if(memberships.length===0&&roles.some(role=>role==="visitor"||role==="leader"))return setMsg("外訪員或小組長至少需要一個小組。");
  setBusy(true);setMsg("");
  try{
   await api(`/admin/v180/people/${edit.employmentId}/access`,{
    method:"PUT",
    body:JSON.stringify({
     roles,
     teamMemberships:memberships,
     adminEnabled:active,
     changeEffectiveFrom:todayTaipei(),
     confirmRetroactive:false,
     version:edit.version
    })
   });
   const fresh=await api<V180PersonRow>(`/admin/v180/people/${edit.employmentId}`);
   setEdit(fresh);
   setActive(fresh.adminEnabled===true);
   setRoles(v180RoleCodes(fresh).filter(role=>Object.hasOwn(V180_INTERNAL_ROLE_LABELS,role)));
   setMemberships(fresh.teamMemberships.map(team=>({teamId:team.teamId,isPrimary:team.isPrimary})));
   setMsg("人員角色與小組授權已更新；畫面已重新載入最新版本。該使用者需重新登入取得最新權限。");
   await load();
  }catch(error){setMsg(v180AccessErrorMessage(error))}finally{setBusy(false)}
 };

 return <>
  <PeopleBulkPanel onConfirmed={load}/>
  <div className="card" style={{marginTop:18}}>
   <div className="section-title"><div><h2>人員與權限</h2><div className="sub">v1.8：以 Employment、有效角色與 TeamMembership 為權限主檔；寫入使用 EmploymentId 與版本控制。</div></div></div>
   {msg&&<div className="note">{msg}</div>}
   {query.error&&<div role="alert" className="note danger-note">{query.error}</div>}
   <div className="grid cols-3">
    <label>人員搜尋<input value={keyword} onChange={e=>setKeyword(e.target.value)} placeholder="工號、姓名或 Email"/></label>
    <label>角色<select value={filterRole} onChange={e=>setFilterRole(e.target.value)}><option value="">全部角色</option>{Object.entries(roleLabels).map(([role,label])=><option key={role} value={role}>{label}</option>)}</select></label>
    <label>帳號狀態<select value={filterActive} onChange={e=>setFilterActive(e.target.value)}><option value="">全部</option><option value="true">啟用</option><option value="false">停用</option></select></label>
   </div>
   <div className="table-wrap"><table><thead><tr><th>員編</th><th>姓名</th><th>任職狀態</th><th>角色</th><th>小組範圍</th><th>帳號</th><th>操作</th></tr></thead><tbody>{rows.map(person=>{
    const roleCodes=v180RoleCodes(person);
    const editable=canEditV180InternalAccess(person);
    return <tr key={person.employmentId}><td>{person.employeeNo||"—"}</td><td>{person.displayName}</td><td>{person.employmentStatus||"—"}</td><td>{roleCodes.map(role=>roleLabels[role]||role).join("、")||"—"}</td><td>{person.teamMemberships.map(team=>`${team.name}${team.isPrimary?" ★":""}`).join("、")||"—"}</td><td>{person.adminEnabled===null||person.adminEnabled===undefined?"無登入帳號":person.adminEnabled?"啟用":"停用"}</td><td>{editable?<button className="btn small secondary" onClick={()=>open(person)}>維護</button>:roleCodes.includes("supervisor")?"外部督導專用流程":"不可維護"}</td></tr>
   })}</tbody></table></div>
   <Pagination {...query.data} page={query.page} pageSize={query.pageSize} busy={query.loading} onPage={query.setPage} onPageSize={query.setPageSize}/>
  </div>
  {edit&&<div className="modal"><div className="modal-panel" role="dialog" aria-modal="true" aria-label="v1.8 人員權限"><button className="btn small outline modal-close" aria-label="關閉人員權限視窗" disabled={busy} onClick={()=>setEdit(null)}>關閉</button><h3>人員權限｜{edit.displayName}</h3><div className="sub">Employment ID：{edit.employmentId}</div><label className="check-row"><input type="checkbox" checked={active} onChange={e=>setActive(e.target.checked)}/>帳號啟用</label><h4>角色</h4><div className="checkbox-grid">{Object.entries(V180_INTERNAL_ROLE_LABELS).map(([role,label])=><label className="check-row" key={role}><input type="checkbox" checked={roles.includes(role)} onChange={()=>toggleRole(role)}/>{label}</label>)}</div><h4>小組授權</h4><div className="scope-list">{teams.map(team=>{const membership=memberships.find(x=>x.teamId===team.teamId);return <div className="scope-row" key={team.teamId}><label className="check-row"><input type="checkbox" checked={!!membership} onChange={()=>toggleTeam(team.teamId)}/>{team.teamName}</label>{membership&&<label className="check-row"><input type="radio" name="primary-team" checked={membership.isPrimary} onChange={()=>primary(team.teamId)}/>主要小組</label>}</div>})}</div><div className="modal-sticky-actions"><button className="btn secondary" onClick={()=>setEdit(null)}>取消</button><button className="btn ok" disabled={busy} onClick={()=>void save()}>儲存</button></div></div></div>}
 </>;
}
