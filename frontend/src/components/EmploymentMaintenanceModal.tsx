import{useEffect,useState}from"react";
import{api}from"../api";
import type{V170PersonDetail}from"../types";
import{todayTaipei}from"../v160";

type Props={userId:number;onClose:()=>void;onChanged?:()=>void};
type MasterRow={id:number;key:string;parentKey?:string;detail?:string;effectiveFrom:string;effectiveTo?:string;isPrimary?:boolean;rowVersion?:string};

export default function EmploymentMaintenanceModal({userId,onClose,onChanged}:Props){
  const[data,setData]=useState<V170PersonDetail|null>(null);
  const[employeeNo,setEmployeeNo]=useState("");
  const[displayName,setDisplayName]=useState("");
  const[email,setEmail]=useState("");
  const[hireDate,setHireDate]=useState("");
  const[terminationDate,setTerminationDate]=useState("");
  const[status,setStatus]=useState("Leave");
  const[statusFrom,setStatusFrom]=useState(todayTaipei());
  const[statusTo,setStatusTo]=useState("");
  const[msg,setMsg]=useState("");
  const[busy,setBusy]=useState(false);
  const[sites,setSites]=useState<MasterRow[]>([]);
  const[assignments,setAssignments]=useState<MasterRow[]>([]);
  const[siteCode,setSiteCode]=useState("");
  const[sitePrimary,setSitePrimary]=useState(true);
  const[siteFrom,setSiteFrom]=useState(todayTaipei());
  const[siteTo,setSiteTo]=useState("");
  const[editingAssignment,setEditingAssignment]=useState<MasterRow|null>(null);

  const load=async()=>{
    const row=await api<V170PersonDetail>(`/admin/people/${userId}`);
    setData(row);
    setEmployeeNo(row.employeeNo||"");
    setDisplayName(row.displayName||"");
    setEmail(row.email||"");
    setHireDate(row.hireDate||"");
    setTerminationDate(row.terminationDate||"");
    const [available,allAssignments]=await Promise.all([api<MasterRow[]>("/admin/master-data/deployment-sites"),api<MasterRow[]>("/admin/master-data/employment-sites")]);
    setSites(available);setAssignments(allAssignments.filter(x=>x.key===row.employeeNo));
  };

  const saveAssignment=async(id?:number,rowVersion?:string)=>{
    if(!data?.employeeNo||!siteCode)return setMsg("請選擇所屬就業中心／派駐據點。");
    if(siteTo&&siteTo<siteFrom)return setMsg("結束日不可早於開始日。");
    setBusy(true);setMsg("");
    try{await api(id?`/admin/master-data/employment-sites/${id}`:"/admin/master-data/employment-sites",{method:id?"PUT":"POST",body:JSON.stringify({employeeNo:data.employeeNo,siteCode,isPrimary:sitePrimary,effectiveFrom:siteFrom,effectiveTo:siteTo||null,rowVersion:rowVersion||null})});setMsg("所屬就業中心／派駐據點已更新。");setEditingAssignment(null);await load();onChanged?.()}catch(e){setMsg(e instanceof Error?e.message:"派駐據點更新失敗")}finally{setBusy(false)}
  };
  useEffect(()=>{void load().catch(e=>setMsg(e instanceof Error?e.message:"人事資料載入失敗"))},[userId]);

  const save=async()=>{
    if(!data?.employmentRowVersion)return setMsg("此人員沒有可維護的 v1.8 Employment。");
    setBusy(true);setMsg("");
    try{
      const row=await api<V170PersonDetail>(`/admin/people/internal-users/${userId}/employment`,{
        method:"PUT",
        body:JSON.stringify({
          employeeNo,displayName,email:email.trim()||null,
          hireDate:hireDate||null,terminationDate:terminationDate||null,
          employmentRowVersion:data.employmentRowVersion
        })
      });
      setData(row);setMsg("人事主檔已更新。");onChanged?.();
    }catch(e){setMsg(e instanceof Error?e.message:"人事主檔更新失敗")}
    finally{setBusy(false)}
  };

  const addStatus=async()=>{
    if(!data?.employeeNo)return setMsg("缺少工號，無法新增人事狀態期間。");
    if(statusTo&&statusTo<statusFrom)return setMsg("狀態結束日不可早於開始日。");
    setBusy(true);setMsg("");
    try{
      await api("/admin/master-data/employment-status",{
        method:"POST",
        body:JSON.stringify({
          employeeNo:data.employeeNo,
          status,
          effectiveFrom:statusFrom,
          effectiveTo:statusTo||null,
          rowVersion:null
        })
      });
      setMsg("人事狀態期間已新增；留停可建立多個不重疊期間。");
      await load();onChanged?.();
    }catch(e){setMsg(e instanceof Error?e.message:"人事狀態期間新增失敗")}
    finally{setBusy(false)}
  };

  if(!data)return <div className="modal"><div className="modal-panel"><button className="btn small outline modal-close" onClick={onClose}>關閉</button><h3>人事資料維護</h3><div className="note">{msg||"載入中…"}</div></div></div>;

  return <div className="modal"><div className="modal-panel" role="dialog" aria-modal="true" aria-label="人事資料維護">
    <button className="btn small outline modal-close" disabled={busy} onClick={onClose}>關閉</button>
    <h3>人事資料維護｜{data.displayName}</h3>
    <div className="note">目前人事狀態：<strong>{data.employmentStatus||"—"}</strong>｜實際登入：<strong>{data.actualAccess?"允許":"暫停"}</strong></div>
    {msg&&<div className="note" style={{marginTop:10}}>{msg}</div>}

    <div className="grid cols-2" style={{marginTop:14}}>
      <div className="field"><label>工號</label><input value={employeeNo} onChange={e=>setEmployeeNo(e.target.value)}/></div>
      <div className="field"><label>姓名</label><input value={displayName} onChange={e=>setDisplayName(e.target.value)}/></div>
      <div className="field span-2"><label>Email</label><input type="email" value={email} onChange={e=>setEmail(e.target.value)}/></div>
      <div className="field"><label>入職日</label><input type="date" value={hireDate} onChange={e=>setHireDate(e.target.value)}/></div>
      <div className="field"><label>離職日</label><input type="date" value={terminationDate} onChange={e=>setTerminationDate(e.target.value)}/></div>
    </div>
    <div className="actions"><button className="btn ok" disabled={busy||!data.employmentRowVersion} onClick={()=>void save()}>儲存人事主檔</button></div>

    <hr/>
    <h4>人事狀態期間</h4>
    <div className="note">Active／Leave／Terminated／PreHire 採有效日期管理；目前處於 Leave 或 Terminated 時，登入會在 server-side 自動拒絕。</div>
    <div className="grid cols-3" style={{marginTop:12}}>
      <div className="field"><label>狀態</label><select value={status} onChange={e=>setStatus(e.target.value)}><option value="Active">Active</option><option value="Leave">Leave（留停）</option><option value="Terminated">Terminated（離職）</option><option value="PreHire">PreHire（未到職）</option></select></div>
      <div className="field"><label>開始日</label><input type="date" value={statusFrom} onChange={e=>setStatusFrom(e.target.value)}/></div>
      <div className="field"><label>結束日</label><input type="date" value={statusTo} onChange={e=>setStatusTo(e.target.value)}/></div>
    </div>
    <button className="btn secondary" disabled={busy} onClick={()=>void addStatus()}>新增狀態期間</button>

    <div className="table-wrap" style={{marginTop:12}}><table><thead><tr><th>狀態</th><th>開始日</th><th>結束日</th><th>目前</th><th>來源</th></tr></thead><tbody>
      {data.employmentPeriods.map(p=><tr key={p.userEmploymentPeriodId}><td>{p.employmentStatus}</td><td>{p.effectiveFrom}</td><td>{p.effectiveTo||"無期限"}</td><td>{p.isCurrent?"是":"—"}</td><td>{p.sourceType}{p.sourceReference?`｜${p.sourceReference}`:""}</td></tr>)}
    </tbody></table></div>

    <hr/>
    <h4>所屬就業中心／派駐據點</h4>
    <div className="note">此資料決定外訪員每次行程的起點與終點；系統會以有效期間內的主要據點自動帶入，外訪員不可覆寫。</div>
    <div className="grid cols-3" style={{marginTop:12}}><div className="field"><label>就業中心／派駐據點</label><select value={siteCode} onChange={e=>setSiteCode(e.target.value)}><option value="">請選擇</option>{sites.map(s=><option key={s.id} value={s.key}>{s.parentKey?`${s.parentKey}／`:''}{s.key}｜{s.detail||''}</option>)}</select></div><div className="field"><label>生效日</label><input type="date" value={siteFrom} onChange={e=>setSiteFrom(e.target.value)}/></div><div className="field"><label>失效日</label><input type="date" value={siteTo} onChange={e=>setSiteTo(e.target.value)}/></div></div>
    <label className="check-row"><input type="checkbox" checked={sitePrimary} onChange={e=>setSitePrimary(e.target.checked)}/>主要據點（行程起點／終點）</label><button className="btn secondary" disabled={busy} onClick={()=>void saveAssignment(editingAssignment?.id,editingAssignment?.rowVersion)}>{editingAssignment?"儲存派駐據點變更":"新增派駐據點"}</button>{editingAssignment&&<button className="btn small outline" style={{marginLeft:8}} onClick={()=>setEditingAssignment(null)}>取消修改</button>}
    <div className="table-wrap" style={{marginTop:12}}><table><thead><tr><th>中心</th><th>派駐據點</th><th>有效期間</th><th>主要</th><th>操作</th></tr></thead><tbody>{assignments.map(a=><tr key={a.id}><td>{sites.find(s=>s.key===a.parentKey)?.parentKey||"—"}</td><td>{a.parentKey||"—"}</td><td>{a.effectiveFrom}～{a.effectiveTo||"無期限"}</td><td>{a.isPrimary?"是":"—"}</td><td><button className="btn small outline" disabled={busy} onClick={()=>{setEditingAssignment(a);setSiteCode(a.parentKey||"");setSitePrimary(!!a.isPrimary);setSiteFrom(a.effectiveFrom);setSiteTo(a.effectiveTo||"")}}>修改</button></td></tr>)}</tbody></table></div>
  </div></div>;
}
