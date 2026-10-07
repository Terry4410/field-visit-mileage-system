import{useEffect,useState}from"react";
import{NavLink}from"react-router-dom";
import{api}from"../api";
import type{MasterDataRow,Team,V170PeopleRow}from"../types";
import{usePagedQuery}from"../use-query";
import{Pagination}from"../components/QueryControls";
import{todayTaipei}from"../v160";
import EmploymentMaintenanceModal from"../components/EmploymentMaintenanceModal";
import PeopleManagementBulkPanel from"../components/PeopleManagementBulkPanel";

type Props={mode?:"people"|"roles"|"bulk"};
const roleOptions=[{code:"visitor",label:"外訪員"},{code:"leader",label:"小組長"},{code:"admin",label:"管理者"}];
const statusOptions=[["","全部"],["Active","在職"],["Leave","留停"],["Terminated","離職"],["PreHire","未到職"]] as const;
function loginStatusLabel(u:V170PeopleRow){if(u.actualAccess)return"允許登入";switch((u.employmentStatus||"").toLowerCase()){case"leave":return"禁止登入（留停）";case"terminated":return"禁止登入（離職）";case"prehire":return"禁止登入（尚未到職）";default:return"資料不完整（缺少人事狀態）"}}
function primarySiteLabel(u:V170PeopleRow){if(!u.primaryDeploymentSiteName)return"—";return `${u.primaryCenterName?`${u.primaryCenterName}／`:""}${u.primaryDeploymentSiteName}`}

function Tabs(){return <div className="actions" style={{marginBottom:14}}>
 <NavLink end to="/admin/users" className={({isActive})=>`btn small ${isActive?"":"outline"}`}>人事資料</NavLink>
 <NavLink to="/admin/users/roles" className={({isActive})=>`btn small ${isActive?"":"outline"}`}>角色與登入</NavLink>
 <NavLink to="/admin/users/bulk" className={({isActive})=>`btn small ${isActive?"":"outline"}`}>Excel 批次維護</NavLink>
 </div>}

export default function PeopleAndAccessPage({mode="people"}:Props){
 if(mode==="bulk")return <><Tabs/><PeopleManagementBulkPanel title="人事主檔 Excel 批次維護" description="與單筆人事維護對齊：工號、姓名、Email、入職日、離職日及多期間在職狀態；不修改角色、小組歸屬或派駐據點。" templateUrl="/admin/people/personnel-bulk/template.xlsx" previewUrl="/admin/people/personnel-bulk/preview" confirmUrl="/admin/people/personnel-bulk/confirm" templateFilename="人事主檔批次維護.xlsx"/></>;
 if(mode==="roles")return <RoleLoginList/>;
 return <PersonnelList/>;
}

function PersonnelList(){
 const[keyword,setKeyword]=useState(""),[employmentStatus,setEmploymentStatus]=useState(""),[hireFrom,setHireFrom]=useState(""),[hireTo,setHireTo]=useState(""),[terminationFrom,setTerminationFrom]=useState(""),[terminationTo,setTerminationTo]=useState(""),[historyStatus,setHistoryStatus]=useState(""),[historyFrom,setHistoryFrom]=useState(""),[historyTo,setHistoryTo]=useState(""),[primarySiteId,setPrimarySiteId]=useState(""),[teamId,setTeamId]=useState(""),[dataIssue,setDataIssue]=useState(""),[advanced,setAdvanced]=useState(false),[employmentUserId,setEmploymentUserId]=useState<number|null>(null),[teams,setTeams]=useState<Team[]>([]),[sites,setSites]=useState<MasterDataRow[]>([]),[centers,setCenters]=useState<MasterDataRow[]>([]),[lookupError,setLookupError]=useState("");
 const validDates=(!hireFrom||!hireTo||hireFrom<=hireTo)&&(!terminationFrom||!terminationTo||terminationFrom<=terminationTo)&&(!historyFrom||!historyTo||historyFrom<=historyTo);
 const query=usePagedQuery<V170PeopleRow>("/admin/people",{userType:"Internal",keyword,employmentStatus:employmentStatus||undefined,hireFrom:hireFrom||undefined,hireTo:hireTo||undefined,terminationFrom:terminationFrom||undefined,terminationTo:terminationTo||undefined,historicalEmploymentStatus:historyStatus||undefined,employmentStatusFrom:historyStatus&&historyFrom?historyFrom:undefined,employmentStatusTo:historyStatus&&historyTo?historyTo:undefined,primaryDeploymentSiteId:primarySiteId?Number(primarySiteId):undefined,teamId:teamId?Number(teamId):undefined,dataIssue:dataIssue||undefined,sort:"code_asc"},validDates);
 useEffect(()=>{Promise.all([api<Team[]>("/teams"),api<MasterDataRow[]>("/admin/master-data/deployment-sites"),api<MasterDataRow[]>("/admin/master-data/centers")]).then(([t,s,c])=>{setTeams(t);setSites(s);setCenters(c)}).catch(e=>setLookupError(e instanceof Error?e.message:"查詢條件載入失敗"))},[]);
 const centerName=(code?:string)=>centers.find(c=>c.key===code)?.detail||code||"";
 const setQuick=(status:string,issue="")=>{setEmploymentStatus(status);setDataIssue(issue)};
 const clearAdvanced=()=>{setHireFrom("");setHireTo("");setTerminationFrom("");setTerminationTo("");setHistoryStatus("");setHistoryFrom("");setHistoryTo("");setPrimarySiteId("");setTeamId("");setDataIssue("")};
 return <>
  <Tabs/>
  <div className="card">
   <div className="section-title"><div><h2>人事資料</h2><div className="sub">此頁只維護人事事實與就業中心／派駐據點；管理小組僅供查詢參考，請至「小組與成員」維護。</div></div></div>
   <div className="field"><label>搜尋人員</label><input value={keyword} onChange={e=>setKeyword(e.target.value)} placeholder="工號、姓名或 Email"/></div>
   <div className="quick-filters" style={{marginBottom:10}}>
    {statusOptions.map(([value,label])=><button type="button" key={value||"all"} className={`btn small ${employmentStatus===value&&!dataIssue?"":"outline"}`} onClick={()=>setQuick(value)}>{label}</button>)}
    <button type="button" className={`btn small ${dataIssue==="MissingEmploymentStatus"?"":"outline"}`} onClick={()=>setQuick("","MissingEmploymentStatus")}>缺人事狀態</button>
    <button type="button" className="btn small outline" onClick={()=>setAdvanced(x=>!x)}>{advanced?"收合進階篩選":"進階篩選"}</button>
   </div>
   {advanced&&<div className="card" style={{marginBottom:14}}>
    <div className="grid cols-2">
     <label>入職日起<input type="date" value={hireFrom} onChange={e=>setHireFrom(e.target.value)}/></label>
     <label>入職日迄<input type="date" value={hireTo} onChange={e=>setHireTo(e.target.value)}/></label>
     <label>離職日起<input type="date" value={terminationFrom} onChange={e=>setTerminationFrom(e.target.value)}/></label>
     <label>離職日迄<input type="date" value={terminationTo} onChange={e=>setTerminationTo(e.target.value)}/></label>
     <label>曾處於人事狀態<select value={historyStatus} onChange={e=>setHistoryStatus(e.target.value)}><option value="">不限制</option><option value="Active">Active（在職）</option><option value="Leave">Leave（留停）</option><option value="Terminated">Terminated（離職）</option><option value="PreHire">PreHire（未到職）</option></select></label>
     <label>管理小組（唯讀條件）<select value={teamId} onChange={e=>setTeamId(e.target.value)}><option value="">全部</option>{teams.map(t=><option key={t.teamId} value={t.teamId}>{t.teamName}</option>)}</select></label>
     <label>人事狀態歷史起日<input type="date" value={historyFrom} disabled={!historyStatus} onChange={e=>setHistoryFrom(e.target.value)}/></label>
     <label>人事狀態歷史迄日<input type="date" value={historyTo} disabled={!historyStatus} onChange={e=>setHistoryTo(e.target.value)}/></label>
     <label>主要派駐據點<select value={primarySiteId} onChange={e=>setPrimarySiteId(e.target.value)}><option value="">全部</option>{sites.filter(s=>s.isActive!==false).map(s=><option key={s.id} value={s.id}>{centerName(s.parentKey)}／{s.detail||s.key}</option>)}</select></label>
     <label>資料完整性<select value={dataIssue} onChange={e=>setDataIssue(e.target.value)}><option value="">全部</option><option value="MissingEmploymentStatus">缺有效人事狀態</option><option value="MissingPrimaryDeploymentSite">缺主要派駐據點</option><option value="MissingEmail">缺 Email</option></select></label>
    </div>
    <div className="actions"><button type="button" className="btn small outline" onClick={clearAdvanced}>清除進階條件</button></div>
   </div>}
   {!validDates&&<div role="alert" className="note danger-note">日期區間的迄日不可早於起日。</div>}
   {lookupError&&<div className="note danger-note">{lookupError}</div>}
   {query.error&&<div className="note danger-note">{query.error}</div>}
   <div className="table-wrap"><table><thead><tr><th>工號</th><th>姓名</th><th>目前人事狀態</th><th>入職日</th><th>離職日</th><th>主要派駐據點</th><th>管理小組（唯讀）</th><th>操作</th></tr></thead><tbody>{query.data.items.map(u=><tr key={u.userId}><td>{u.employeeNo||u.userCode}</td><td><strong>{u.displayName}</strong><div className="sub">{u.email||"—"}</div></td><td>{u.employmentStatus||"資料不完整"}</td><td>{u.hireDate||"—"}</td><td>{u.terminationDate||"—"}</td><td>{primarySiteLabel(u)}</td><td>{u.teamAssignments.map(s=>`${s.teamName}${s.isPrimary?" ★":""}`).join("、")||"—"}</td><td><button className="btn small secondary" onClick={()=>setEmploymentUserId(u.userId)}>維護人事資料</button></td></tr>)}</tbody></table></div>
   <Pagination {...query.data} page={query.page} pageSize={query.pageSize} busy={query.loading} onPage={query.setPage} onPageSize={query.setPageSize}/>
  </div>
  {employmentUserId!==null&&<EmploymentMaintenanceModal userId={employmentUserId} onClose={()=>setEmploymentUserId(null)} onChanged={()=>query.reload()}/>}
 </>;
}

function RoleLoginList(){
 const[keyword,setKeyword]=useState(""),[role,setRole]=useState(""),[employmentStatus,setEmploymentStatus]=useState(""),[teamId,setTeamId]=useState(""),[msg,setMsg]=useState(""),[busy,setBusy]=useState(false),[access,setAccess]=useState<V170PeopleRow|null>(null),[roles,setRoles]=useState<string[]>([]),[teams,setTeams]=useState<Team[]>([]);
 const query=usePagedQuery<V170PeopleRow>("/admin/people",{userType:"Internal",keyword,role:role||undefined,employmentStatus:employmentStatus||undefined,teamId:teamId?Number(teamId):undefined,sort:"code_asc"});
 useEffect(()=>{api<Team[]>("/teams").then(setTeams).catch(e=>setMsg(e instanceof Error?e.message:"小組清單載入失敗"))},[]);
 const openAccess=(u:V170PeopleRow)=>{setAccess(u);setRoles([...u.roles]);setMsg("")};
 const toggleRole=(code:string,checked:boolean)=>setRoles(x=>checked?[...new Set([...x,code])]:x.filter(r=>r!==code));
 const saveAccess=async()=>{if(!access)return;if(roles.length===0)return setMsg("至少保留一個角色。");setBusy(true);setMsg("");try{await api(`/admin/people/internal-users/${access.userId}/roles`,{method:"PUT",body:JSON.stringify({roles,effectiveFrom:todayTaipei()})});setAccess(null);setMsg("角色已更新；登入資格仍由人事狀態自動判斷，小組歸屬未變更。");query.reload()}catch(e){setMsg(e instanceof Error?e.message:"角色更新失敗")}finally{setBusy(false)}};
 return <>
  <Tabs/>
  <div className="card">
   <div className="section-title"><div><h2>角色與登入</h2><div className="sub">此頁只維護角色。實際登入由人事狀態有效期間自動判斷；管理小組請至「小組與成員」維護。</div></div></div>
   <div className="grid cols-2">
    <label>搜尋人員<input value={keyword} onChange={e=>setKeyword(e.target.value)} placeholder="工號、姓名或 Email"/></label>
    <label>角色<select value={role} onChange={e=>setRole(e.target.value)}><option value="">全部</option>{roleOptions.map(r=><option key={r.code} value={r.code}>{r.label}</option>)}</select></label>
    <label>目前人事狀態<select value={employmentStatus} onChange={e=>setEmploymentStatus(e.target.value)}><option value="">全部</option><option value="Active">Active</option><option value="Leave">Leave</option><option value="Terminated">Terminated</option><option value="PreHire">PreHire</option></select></label>
    <label>管理小組<select value={teamId} onChange={e=>setTeamId(e.target.value)}><option value="">全部</option>{teams.map(t=><option key={t.teamId} value={t.teamId}>{t.teamName}</option>)}</select></label>
   </div>
   {msg&&<div className="note" style={{marginBottom:10}}>{msg}</div>}{query.error&&<div className="note danger-note">{query.error}</div>}
   <div className="table-wrap"><table><thead><tr><th>工號</th><th>姓名</th><th>角色</th><th>人事狀態</th><th>實際登入</th><th>管理小組（唯讀）</th><th>操作</th></tr></thead><tbody>{query.data.items.map(u=><tr key={u.userId}><td>{u.employeeNo||u.userCode}</td><td>{u.displayName}</td><td>{u.roles.map(r=>roleOptions.find(x=>x.code===r)?.label||r).join("、")||"—"}</td><td>{u.employmentStatus||"—"}</td><td>{loginStatusLabel(u)}</td><td>{u.teamAssignments.map(s=>`${s.teamName}${s.isPrimary?" ★":""}`).join("、")||"—"}</td><td><button className="btn small secondary" onClick={()=>openAccess(u)}>維護角色</button></td></tr>)}</tbody></table></div>
   <Pagination {...query.data} page={query.page} pageSize={query.pageSize} busy={query.loading||busy} onPage={query.setPage} onPageSize={query.setPageSize}/>
  </div>
  {access&&<div className="modal" onMouseDown={e=>{if(e.target===e.currentTarget&&!busy)setAccess(null)}}><div className="modal-panel" role="dialog" aria-modal="true"><button className="btn small outline modal-close" disabled={busy} onClick={()=>setAccess(null)}>關閉</button><h3>角色與登入｜{access.displayName}</h3><div className="note">實際登入：{loginStatusLabel(access)}<br/>登入資格由人事狀態有效期間自動判斷；這裡只維護角色。<br/>管理小組：{access.teamAssignments.map(s=>`${s.teamName}${s.isPrimary?" ★":""}`).join("、")||"尚未設定"}（唯讀），如需調整請至「小組與成員」。</div><h4>角色</h4>{roleOptions.map(r=><label className="check-row" key={r.code}><input type="checkbox" checked={roles.includes(r.code)} onChange={e=>toggleRole(r.code,e.target.checked)}/>{r.label}</label>)}<div className="actions" style={{marginTop:14}}><button className="btn ok" disabled={busy} onClick={()=>void saveAccess()}>儲存角色</button><button className="btn outline" disabled={busy} onClick={()=>setAccess(null)}>取消</button></div></div></div>}
 </>;
}
