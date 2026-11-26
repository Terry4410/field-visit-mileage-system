import{useEffect,useState}from"react";
import{api}from"../api";
import type{V170PersonDetail}from"../types";
import{todayTaipei}from"../v160";
import{employmentStatusLabel}from"../employment-status";

type Props={userId:number;onClose:()=>void;onChanged?:()=>void};
type MasterRow={id:number;key:string;parentKey?:string;detail?:string;effectiveFrom:string;effectiveTo?:string;isPrimary?:boolean;rowVersion?:string};

export default function EmploymentMaintenanceModal({userId,onClose,onChanged}:Props){
  const[data,setData]=useState<V170PersonDetail|null>(null);
  const[tab,setTab]=useState<"basic"|"status"|"sites">("basic");
  const[statusRows,setStatusRows]=useState<MasterRow[]>([]);
  const[statusLoadFailed,setStatusLoadFailed]=useState(false);
  const[transitionDate,setTransitionDate]=useState(todayTaipei());
  const[editingStatus,setEditingStatus]=useState<MasterRow|null>(null);
  const[advancedStatus,setAdvancedStatus]=useState(false);
  const[employeeNo,setEmployeeNo]=useState("");
  const[displayName,setDisplayName]=useState("");
  const[email,setEmail]=useState("");
  const[hireDate,setHireDate]=useState("");
  const[terminationDate,setTerminationDate]=useState("");
  const[status,setStatus]=useState("Leave");
  const[statusFrom,setStatusFrom]=useState(todayTaipei());
  const[statusTo,setStatusTo]=useState("");
  const[statusNoEnd,setStatusNoEnd]=useState(true);
  const[msg,setMsg]=useState("");
  const[busy,setBusy]=useState(false);
  const[sites,setSites]=useState<MasterRow[]>([]);
  const[assignments,setAssignments]=useState<MasterRow[]>([]);
  const[siteLoadFailed,setSiteLoadFailed]=useState(false);
  const[siteCode,setSiteCode]=useState("");
  const[sitePrimary,setSitePrimary]=useState(true);
  const[siteFrom,setSiteFrom]=useState(todayTaipei());
  const[siteTo,setSiteTo]=useState("");
  const[siteNoEnd,setSiteNoEnd]=useState(true);
  const[editingAssignment,setEditingAssignment]=useState<MasterRow|null>(null);

  const load=async()=>{
    const row=await api<V170PersonDetail>(`/admin/people/${userId}`);
    setData(row);
    setEmployeeNo(row.employeeNo||"");
    setDisplayName(row.displayName||"");
    setEmail(row.email||"");
    setHireDate(row.hireDate||"");
    setTerminationDate(row.terminationDate||"");
    const [available,allAssignments,allStatuses]=await Promise.allSettled([api<MasterRow[]>("/admin/master-data/deployment-sites"),api<MasterRow[]>("/admin/master-data/employment-sites"),api<MasterRow[]>("/admin/master-data/employment-status")]);
    setStatusLoadFailed(allStatuses.status==="rejected");
    setStatusRows(allStatuses.status==="fulfilled"?allStatuses.value.filter(x=>x.key===row.employeeNo):[]);
    if(available.status==="fulfilled")setSites(available.value);
    if(allAssignments.status==="fulfilled")setAssignments(allAssignments.value.filter(x=>x.key===row.employeeNo));
    const failures=[available.status==="rejected"?"鄉鎮台清單":"",allAssignments.status==="rejected"?"派駐資料清單":""].filter(Boolean);
    setSiteLoadFailed(failures.length>0);
    setMsg(failures.length?`${failures.join("、")}載入失敗；人事資料仍可查看，派駐設定請暫緩修改。`:"");
  };

  const saveAssignment=async(id?:number,rowVersion?:string)=>{
    if(siteLoadFailed)return setMsg("派駐資料尚未完整載入，請先重新開啟維護畫面。");
    if(!data?.employeeNo||!siteCode)return setMsg("請選擇所屬就業中心／派駐據點。");
    if(!siteNoEnd&&!siteTo)return setMsg("取消「無期限」後，派駐據點失效日必填。");
    if(!siteNoEnd&&siteTo<siteFrom)return setMsg("結束日不可早於開始日。");
    setBusy(true);setMsg("");
    try{await api(id?`/admin/master-data/employment-sites/${id}`:"/admin/master-data/employment-sites",{method:id?"PUT":"POST",body:JSON.stringify({employeeNo:data.employeeNo,siteCode,isPrimary:sitePrimary,effectiveFrom:siteFrom,effectiveTo:siteNoEnd?null:siteTo,rowVersion:rowVersion||null})});setEditingAssignment(null);await load();if(!siteLoadFailed)setMsg("所屬就業中心／派駐據點已更新。");onChanged?.()}catch(e){setMsg(e instanceof Error?e.message:"派駐據點更新失敗")}finally{setBusy(false)}
  };
  useEffect(()=>{void load().catch(e=>setMsg(`人事詳細資料載入失敗：${e instanceof Error?e.message:"請稍後再試"}`))},[userId]);

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
    if(!statusNoEnd&&!statusTo)return setMsg("取消「無期限」後，狀態結束日必填。");
    if(!statusNoEnd&&statusTo<statusFrom)return setMsg("狀態結束日不可早於開始日。");
    setBusy(true);setMsg("");
    try{
      if(editingStatus&&!window.confirm("確認更正此筆人事狀態期間？既有派駐期間仍須符合安全檢核。"))return;
      await api(editingStatus?`/admin/master-data/employment-status/${editingStatus.id}`:"/admin/master-data/employment-status",{
        method:editingStatus?"PUT":"POST",
        body:JSON.stringify({
          employeeNo:data.employeeNo,
          status,
          effectiveFrom:statusFrom,
          effectiveTo:statusNoEnd?null:statusTo,
          rowVersion:editingStatus?.rowVersion||null
        })
      });
      setEditingStatus(null);setAdvancedStatus(false);
      await load();setMsg("人事狀態期間已儲存。");onChanged?.();
    }catch(e){setMsg(e instanceof Error?e.message:"人事狀態期間新增失敗")}
    finally{setBusy(false)}
  };

  const previous=statusRows.filter(x=>x.effectiveFrom<transitionDate&&!x.effectiveTo);
  const prior=previous.length===1?previous[0]:null;
  const target=prior?.parentKey==="Active"?"Leave":prior?.parentKey==="Leave"?"Active":null;
  const transition=async()=>{
    if(!data?.employeeNo||!prior?.rowVersion||!target||statusLoadFailed||siteLoadFailed)return;
    if(transitionDate<todayTaipei())return setMsg("異動生效日不可早於今天。");
    const leave=target==="Leave";
    if(!window.confirm(leave?"確認辦理留停？生效日前一天將結束原在職及有效派駐期間；角色、小組及歷史紀錄保留。":"確認辦理復職？原角色及小組保留，派駐據點請確認有效性後從歷史資料帶入新增。"))return;
    setBusy(true);setMsg("");
    try{
      await api("/admin/master-data/employment-status",{method:"POST",body:JSON.stringify({
        employeeNo:data.employeeNo,status:target,effectiveFrom:transitionDate,effectiveTo:null,
        rowVersion:prior.rowVersion,transition:true,confirmEndSites:leave
      })});
      await load();setMsg(leave?"留停異動已儲存。":"復職異動已儲存；如需派駐，請至派駐據點標籤確認及新增。");onChanged?.();
    }catch(e){setMsg(e instanceof Error?e.message:"人事異動失敗")}
    finally{setBusy(false)}
  };

  if(!data)return <div className="modal"><div className="modal-panel"><button className="btn small outline modal-close" onClick={onClose}>關閉</button><h3>人事資料維護</h3><div className="note">{msg||"載入中…"}</div></div></div>;

  return <div className="modal"><div className="modal-panel" role="dialog" aria-modal="true" aria-label="人事資料維護">
    <button className="btn small outline modal-close" disabled={busy} onClick={onClose}>關閉</button>
    <h3>人事資料維護｜{data.displayName}</h3>
    <div className="note">工號：{data.employeeNo||"—"}｜目前人事狀態：<strong>{employmentStatusLabel(data.employmentStatus)}</strong>｜人事登入資格：<strong>{data.actualAccess?"符合":"暫停"}</strong>（業務角色另行判斷）</div>
    {msg&&<div className="note" style={{marginTop:10}}>{msg}</div>}

    <nav className="actions" aria-label="人事資料維護子功能" style={{marginTop:14}}>{([{key:"basic",label:"基本資料"},{key:"status",label:"人事狀態"},{key:"sites",label:"派駐據點"}] as const).map(x=><button type="button" key={x.key} className={`btn small ${tab===x.key?"":"outline"}`} aria-pressed={tab===x.key} disabled={busy} onClick={()=>{setTab(x.key);setMsg("")}}>{x.label}</button>)}</nav>
    {tab==="basic"&&<>
    <div className="grid cols-2" style={{marginTop:14}}>
      <div className="field"><label>工號</label><input value={employeeNo} onChange={e=>setEmployeeNo(e.target.value)}/></div>
      <div className="field"><label>姓名</label><input value={displayName} onChange={e=>setDisplayName(e.target.value)}/></div>
      <div className="field span-2"><label>Email</label><input type="email" value={email} onChange={e=>setEmail(e.target.value)}/></div>
      <div className="field"><label>入職日</label><input type="date" value={hireDate} onChange={e=>setHireDate(e.target.value)}/></div>
      <div className="field"><label>離職日</label><input type="date" value={terminationDate} onChange={e=>setTerminationDate(e.target.value)}/><div className="sub">尚未離職者，離職日請留空白。</div></div>
    </div>
    <div className="actions"><button className="btn ok" disabled={busy||!data.employmentRowVersion} onClick={()=>void save()}>儲存人事主檔</button></div>

    </>}
    {tab==="status"&&<>
    <h4>人事異動</h4>
    {statusLoadFailed&&<div className="note danger-note">人事狀態版本載入失敗，請重新開啟維護畫面。</div>}
    <div className="note">留停及復職使用生效日自動接續原期間。留停會結束有效派駐期間，復職後可從歷史據點帶入新增；離職日請於基本資料維護。</div>
    <div className="field"><label>異動生效日</label><input type="date" min={todayTaipei()} value={transitionDate} onChange={e=>setTransitionDate(e.target.value)}/></div>
    <button type="button" className="btn secondary" disabled={busy||statusLoadFailed||siteLoadFailed||!prior?.rowVersion||!target||!transitionDate||transitionDate<todayTaipei()} onClick={()=>void transition()}>{target==="Leave"?"辦理留停":target==="Active"?"辦理復職":"請確認可接續的狀態期間"}</button>
    <hr/>
    <h4>人事狀態期間</h4>
    <button type="button" className="btn small outline" disabled={busy||statusLoadFailed} onClick={()=>{setAdvancedStatus(!advancedStatus);setEditingStatus(null)}}>{advancedStatus?"收合期間維護":"進階期間維護"}</button>
    {advancedStatus&&<>
    <div className="note">在職、留停、離職及未到職採有效日期管理；目前處於留停或離職時，系統會拒絕登入。</div>
    <div className="grid cols-3" style={{marginTop:12}}>
      <div className="field"><label>狀態</label><select value={status} onChange={e=>setStatus(e.target.value)}><option value="Active">在職</option><option value="Leave">留停</option><option value="Terminated">離職</option><option value="PreHire">未到職</option></select></div>
      <div className="field"><label>開始日</label><input type="date" value={statusFrom} onChange={e=>setStatusFrom(e.target.value)}/></div>
      <div className="field"><label>結束日</label><input type="date" value={statusTo} disabled={statusNoEnd} onChange={e=>setStatusTo(e.target.value)}/><label className="check-row"><input type="checkbox" checked={statusNoEnd} onChange={e=>{setStatusNoEnd(e.target.checked);if(e.target.checked)setStatusTo("")}}/>無期限</label></div>
    </div>
    <button className="btn secondary" disabled={busy||statusLoadFailed||(!!editingStatus&&!editingStatus.rowVersion)} onClick={()=>void addStatus()}>{editingStatus?"儲存期間更正":"新增狀態期間"}</button>
    </>}

    <div className="table-wrap" style={{marginTop:12}}><table><thead><tr><th>狀態</th><th>開始日</th><th>結束日</th><th>目前</th><th>來源</th><th>操作</th></tr></thead><tbody>
      {data.employmentPeriods.map(p=><tr key={p.userEmploymentPeriodId}><td>{employmentStatusLabel(p.employmentStatus)}</td><td>{p.effectiveFrom}</td><td>{p.effectiveTo||"無期限"}</td><td>{p.isCurrent?"是":"—"}</td><td>{p.sourceType}{p.sourceReference?`｜${p.sourceReference}`:""}</td><td><button type="button" className="btn small outline" disabled={busy||statusLoadFailed||!statusRows.find(x=>x.id===p.userEmploymentPeriodId)?.rowVersion} onClick={()=>{const row=statusRows.find(x=>x.id===p.userEmploymentPeriodId);if(!row?.rowVersion)return;setEditingStatus(row);setStatus(p.employmentStatus);setStatusFrom(p.effectiveFrom);setStatusTo(p.effectiveTo||"");setStatusNoEnd(!p.effectiveTo);setAdvancedStatus(true)}}>更正期間</button></td></tr>)}
    </tbody></table></div>

    </>}
    {tab==="sites"&&<>
    <h4>所屬就業中心／派駐據點</h4>
    <div className="note">此資料決定外訪員行程起點與終點的預設值；系統會以有效期間內的主要據點自動帶入。外訪員可依本次行程實際需要調整起點或終點，調整只影響該次行程，不會修改人事主檔。</div>
    <div className="grid cols-3" style={{marginTop:12}}><div className="field"><label>就業中心／派駐據點</label><select value={siteCode} onChange={e=>setSiteCode(e.target.value)}><option value="">請選擇</option>{sites.map(s=><option key={s.id} value={s.key}>{s.parentKey?`${s.parentKey}／`:''}{s.key}｜{s.detail||''}</option>)}</select></div><div className="field"><label>生效日</label><input type="date" value={siteFrom} onChange={e=>setSiteFrom(e.target.value)}/></div><div className="field"><label>失效日</label><input type="date" value={siteTo} disabled={siteNoEnd} onChange={e=>setSiteTo(e.target.value)}/></div></div>
    <div className="effective-flags"><label className="check-row"><input type="checkbox" checked={siteNoEnd} onChange={e=>{setSiteNoEnd(e.target.checked);if(e.target.checked)setSiteTo("")}}/>無期限</label><label className="check-row"><input type="checkbox" checked={sitePrimary} onChange={e=>setSitePrimary(e.target.checked)}/>主要據點（行程起點／終點）</label></div><button className="btn secondary" disabled={busy||siteLoadFailed} onClick={()=>void saveAssignment(editingAssignment?.id,editingAssignment?.rowVersion)}>{editingAssignment?"儲存派駐據點變更":"新增派駐據點"}</button>{editingAssignment&&<button className="btn small outline" style={{marginLeft:8}} onClick={()=>setEditingAssignment(null)}>取消修改</button>}
    <div className="table-wrap" style={{marginTop:12}}><table><thead><tr><th>中心</th><th>派駐據點</th><th>有效期間</th><th>主要</th><th>操作</th></tr></thead><tbody>{assignments.map(a=><tr key={a.id}><td>{sites.find(s=>s.key===a.parentKey)?.parentKey||"—"}</td><td>{a.parentKey||"—"}</td><td>{a.effectiveFrom}～{a.effectiveTo||"無期限"}</td><td>{a.isPrimary?"是":"—"}</td><td><button className="btn small outline" disabled={busy||siteLoadFailed} onClick={()=>{setEditingAssignment(a);setSiteCode(a.parentKey||"");setSitePrimary(!!a.isPrimary);setSiteFrom(a.effectiveFrom);setSiteTo(a.effectiveTo||"");setSiteNoEnd(!a.effectiveTo)}}>修改</button>{a.effectiveTo&&a.effectiveTo<todayTaipei()&&<button type="button" className="btn small outline" disabled={busy||siteLoadFailed} onClick={()=>{setEditingAssignment(null);setSiteCode(a.parentKey||"");setSitePrimary(!!a.isPrimary);setSiteFrom(todayTaipei());setSiteTo("");setSiteNoEnd(true);setMsg("已帶入歷史據點，請確認生效日及失效日後新增。")}}>依歷史據點新增</button>}</td></tr>)}</tbody></table></div>
    </>}
  </div></div>;
}
