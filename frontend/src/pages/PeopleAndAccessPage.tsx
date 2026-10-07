import{useState}from"react";
import{NavLink}from"react-router-dom";
import{api}from"../api";
import type{V170PeopleRow}from"../types";
import{usePagedQuery}from"../use-query";
import{Pagination}from"../components/QueryControls";
import{todayTaipei}from"../v160";
import EmploymentMaintenanceModal from"../components/EmploymentMaintenanceModal";
import PeopleManagementBulkPanel from"../components/PeopleManagementBulkPanel";

type Props={mode?:"list"|"bulk"};
const roleOptions=[{code:"visitor",label:"外訪員"},{code:"leader",label:"小組長"},{code:"admin",label:"管理者"}];

function Tabs(){return <div className="actions" style={{marginBottom:14}}><NavLink end to="/admin/users" className={({isActive})=>`btn small ${isActive?"":"outline"}`}>人員與權限</NavLink><NavLink to="/admin/users/bulk" className={({isActive})=>`btn small ${isActive?"":"outline"}`}>人事 Excel 批次維護</NavLink></div>}

export default function PeopleAndAccessPage({mode="list"}:Props){
 if(mode==="bulk")return <><Tabs/><PeopleManagementBulkPanel title="人事主檔 Excel 批次維護" description="與單筆人事維護對齊：工號、姓名、Email、入職日、離職日及多期間在職狀態；不修改角色或小組歸屬。" templateUrl="/admin/people/personnel-bulk/template.xlsx" previewUrl="/admin/people/personnel-bulk/preview" confirmUrl="/admin/people/personnel-bulk/confirm" templateFilename="人事主檔批次維護.xlsx"/></>;
 return <PeopleList/>;
}

function PeopleList(){
 const[keyword,setKeyword]=useState(""),[msg,setMsg]=useState(""),[busy,setBusy]=useState(false),[access,setAccess]=useState<V170PeopleRow|null>(null),[roles,setRoles]=useState<string[]>([]),[enabled,setEnabled]=useState(true),[employmentUserId,setEmploymentUserId]=useState<number|null>(null);
 const query=usePagedQuery<V170PeopleRow>("/admin/people",{userType:"Internal",keyword,sort:"code_asc"});
 const openAccess=(u:V170PeopleRow)=>{setAccess(u);setRoles([...u.roles]);setEnabled(u.adminEnabled);setMsg("")};
 const toggleRole=(code:string,checked:boolean)=>setRoles(x=>checked?[...new Set([...x,code])]:x.filter(r=>r!==code));
 const saveAccess=async()=>{
  if(!access)return;if(roles.length===0)return setMsg("至少保留一個角色。");
  setBusy(true);setMsg("");
  try{await api(`/admin/people/internal-users/${access.userId}/roles`,{method:"PUT",body:JSON.stringify({roles,adminEnabled:enabled,effectiveFrom:todayTaipei()})});setAccess(null);setMsg("角色／帳號狀態已更新；小組歸屬完全未變更。若要調整小組，請到「小組與成員」。");query.reload()}
  catch(e){setMsg(e instanceof Error?e.message:"角色／帳號更新失敗")}finally{setBusy(false)}
 };
 return <>
  <Tabs/>
  <div className="card">
   <div className="section-title"><div><h2>人員與權限</h2><div className="sub">此頁只列內部人員：維護人事單筆資料、帳號與角色；小組歸屬統一到「小組與成員」。督導屬 External Supervisor，依 Data Scope 管理，不混入內部人員小組。</div></div></div>
   <div className="field"><label>搜尋人員</label><input value={keyword} onChange={e=>setKeyword(e.target.value)} placeholder="工號、姓名或 Email"/></div>
   {msg&&<div className="note" style={{marginBottom:10}}>{msg}</div>}{query.error&&<div className="note danger-note">{query.error}</div>}
   <div className="table-wrap"><table><thead><tr><th>工號</th><th>姓名</th><th>Email</th><th>角色</th><th>小組歸屬（唯讀）</th><th>人事狀態</th><th>帳號</th><th>操作</th></tr></thead><tbody>{query.data.items.map(u=><tr key={u.userId}><td>{u.employeeNo||u.userCode}</td><td>{u.displayName}</td><td>{u.email||"—"}</td><td>{u.roles.join("、")||"—"}</td><td>{u.teamAssignments.map(s=>`${s.teamName}${s.isPrimary?" ★":""}`).join("、")||"—"}</td><td>{u.employmentStatus||"—"}</td><td>{u.adminEnabled?"啟用":"停用"}</td><td><div className="actions"><button className="btn small secondary" onClick={()=>setEmploymentUserId(u.userId)}>人事資料</button><button className="btn small outline" onClick={()=>openAccess(u)}>角色／帳號</button></div></td></tr>)}</tbody></table></div>
   <Pagination {...query.data} page={query.page} pageSize={query.pageSize} busy={query.loading||busy} onPage={query.setPage} onPageSize={query.setPageSize}/>
  </div>
  {employmentUserId!==null&&<EmploymentMaintenanceModal userId={employmentUserId} onClose={()=>setEmploymentUserId(null)} onChanged={()=>query.reload()}/>} 
  {access&&<div className="modal" onMouseDown={e=>{if(e.target===e.currentTarget&&!busy)setAccess(null)}}><div className="modal-panel" role="dialog" aria-modal="true"><button className="btn small outline modal-close" disabled={busy} onClick={()=>setAccess(null)}>關閉</button><h3>角色／帳號｜{access.displayName}</h3><div className="note">小組歸屬：{access.teamAssignments.map(s=>`${s.teamName}${s.isPrimary?" ★":""}`).join("、")||"尚未設定"}<br/>這裡只會改角色與帳號狀態；小組歸屬請至「小組與成員」。</div><h4>角色</h4>{roleOptions.map(r=><label className="check-row" key={r.code}><input type="checkbox" checked={roles.includes(r.code)} onChange={e=>toggleRole(r.code,e.target.checked)}/>{r.label}</label>)}<hr/><label className="check-row"><input type="checkbox" checked={enabled} onChange={e=>setEnabled(e.target.checked)}/>帳號啟用</label><div className="actions" style={{marginTop:14}}><button className="btn ok" disabled={busy} onClick={()=>void saveAccess()}>儲存角色／帳號</button><button className="btn outline" disabled={busy} onClick={()=>setAccess(null)}>取消</button></div></div></div>}
 </>;
}
