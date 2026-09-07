import{useEffect,useState}from"react";
import{useNavigate}from"react-router-dom";
import{api,apiDownload}from"../api";
import{useAuth}from"../auth";
import{isPendingCorrection}from"../correction-ui";
import type{CorrectionDraft,CorrectionProposal,PagedResult,Project,Team,Trip,TripQueryRow,UserOption,VisitType}from"../types";
import{km,money,monthStart,qs,timeRange,todayTaipei}from"../v160";

import { usePagedQuery } from '../use-query';
import { DateFilters, Pagination } from '../components/QueryControls';

type Props={title?:string;allowCorrection?:boolean};
const statuses=[['','全部狀態'],['Draft','草稿'],['Submitted','已送出'],['RoutePending','待計算里程'],['PendingApproval','待核准'],['Approved','已核准'],['Returned','已退回']];

export default function UnifiedQueryPage({title="行程查詢",allowCorrection=false}:Props){
 const{user}=useAuth();const navigate=useNavigate();const today=todayTaipei();
 const[start,setStart]=useState(monthStart(today)),[end,setEnd]=useState(today),[teamId,setTeamId]=useState(''),[visitorId,setVisitorId]=useState(''),[locationKeyword,setLocationKeyword]=useState(''),[projectId,setProjectId]=useState(''),[visitTypeId,setVisitTypeId]=useState(''),[status,setStatus]=useState(''),[includeCancelled,setIncludeCancelled]=useState(false);
 const[teams,setTeams]=useState<Team[]>([]),[visitors,setVisitors]=useState<UserOption[]>([]),[projects,setProjects]=useState<Project[]>([]),[visitTypes,setVisitTypes]=useState<VisitType[]>([]),[msg,setMsg]=useState(''),[busy,setBusy]=useState(false),[detail,setDetail]=useState<TripQueryRow|null>(null),[correction,setCorrection]=useState<CorrectionDraft|null>(null),[reason,setReason]=useState(''),[correctionError,setCorrectionError]=useState('');
 const activeRole=(sessionStorage.getItem('fieldvisit_active_role')||user?.roles[0]||'visitor').toLowerCase();
 const canCrossTeam=['leader','admin','supervisor'].includes(activeRole);
 const canIncludeCancelled=activeRole==='admin';

 useEffect(()=>{Promise.all([api<Team[]>('/teams'),api<UserOption[]>('/query/visitors'),api<Project[]>('/projects'),api<VisitType[]>('/visit-types')]).then(([t,u,p,v])=>{setTeams(t);setVisitors(u);setProjects(p);setVisitTypes(v)}).catch(e=>setMsg(e.message))},[]);
 const filters={startDate:start,endDate:end,teamId:teamId||undefined,visitorId:visitorId||undefined,keyword:locationKeyword||undefined,projectId:projectId||undefined,visitTypeId:visitTypeId||undefined,status:status||undefined,includeCancelled:canIncludeCancelled?includeCancelled:false};
 const query=usePagedQuery<TripQueryRow>('/query/trips',filters,!!start&&!!end&&start<=end);
 const rows=query.data, page=query.page, pageSize=query.pageSize;
 const applied=query.appliedFilters;
 const load=async(p=page,overridePageSize=pageSize)=>{if(overridePageSize!==pageSize)query.setPageSize(overridePageSize);query.setPage(p);query.reload()};
 const appliedDescription=applied?`${String(applied.startDate)}～${String(applied.endDate)}｜${rows.totalCount} 筆`:'查詢條件變更中';
 const download=async(format:'xlsx'|'pdf')=>{if(!applied)return setMsg('請先執行查詢，再下載目前查詢結果。');setBusy(true);setMsg('');try{await apiDownload(`/query/trips/export.${format}?${qs({...applied,page:1,pageSize:100})}`,`外訪行程查詢.${format}`)}catch(e){setMsg(e instanceof Error?e.message:'下載失敗')}finally{setBusy(false)}};
 const startCorrection=async(row:TripQueryRow)=>{if(isPendingCorrection(row.correctionStatus))return;setMsg('');setCorrectionError('');try{setCorrection(await api<CorrectionDraft>(`/corrections/draft/${row.visitTripId}`));setReason('')}catch(e){setMsg(e instanceof Error?e.message:'無法開啟更正申請')}};
 const updateProposal=(patch:Partial<CorrectionProposal>)=>setCorrection(c=>c?{...c,proposal:{...c.proposal,...patch}}:c);
 const updateStop=(index:number,patch:Partial<CorrectionProposal['stops'][number]>)=>setCorrection(c=>c?{...c,proposal:{...c.proposal,stops:c.proposal.stops.map((s,i)=>i===index?{...s,...patch}:s)}}:c);
 const submitCorrection=async()=>{if(!correction)return;if(!reason.trim()){setCorrectionError('請填寫更正原因。');return}setBusy(true);setCorrectionError('');try{await api('/corrections',{method:'POST',body:JSON.stringify({visitTripId:correction.visitTripId,reason:reason.trim(),proposal:correction.proposal})});setCorrection(null);setCorrectionError('');await load(page);setMsg('更正申請已送出，等待小組長審核。')}catch(e){setCorrectionError(e instanceof Error?e.message:'更正申請失敗')}finally{setBusy(false)}};
 const deleteDraft=async(row:TripQueryRow)=>{
  if(activeRole!=='visitor'||row.status!=='Draft')return;
  const confirmed=window.confirm(`確定要刪除草稿 ${row.tripNo}？\n\n刪除後不可復原；若草稿使用的臨時地點沒有被其他有效行程使用，系統也會一併取消該臨時地點。`);
  if(!confirmed)return;
  setBusy(true);setMsg('');
  try{
   const latest=await api<Trip>(`/trips/${row.visitTripId}`);
   if(latest.status!=='Draft')throw new Error('此行程已不是草稿，請重新整理後再操作。');
   await api<void>(`/trips/${row.visitTripId}`,{method:'DELETE',headers:{'If-Match':latest.rowVersion}});
   if(detail?.visitTripId===row.visitTripId)setDetail(null);
   await load(page);
   setMsg(`草稿 ${row.tripNo} 已刪除。`);
  }catch(e){setMsg(e instanceof Error?e.message:'刪除草稿失敗')}
  finally{setBusy(false)}
 };

 return <>
  <div className="card">
   <div className="section-title"><div><h2>{title}</h2><div className="sub">同一套查詢條件同時套用畫面、Excel 與 PDF；下載包含全部符合資料，不受分頁限制。</div></div><div className="actions"><button className="btn secondary" onClick={()=>void download('xlsx')} disabled={busy||query.loading}>下載 Excel</button><button className="btn secondary" onClick={()=>void download('pdf')} disabled={busy||query.loading}>下載 PDF</button></div></div>
   <DateFilters start={start} end={end} onChange={(s,e)=>{setStart(s);setEnd(e)}} />
   <div className="grid cols-4 query-grid">
    {canCrossTeam&&<div className="field"><label>小組</label><select value={teamId} onChange={e=>{setTeamId(e.target.value);setVisitorId('')}}><option value="">全部授權小組</option>{teams.map(t=><option key={t.teamId} value={t.teamId}>{t.teamName}</option>)}</select></div>}
    {canCrossTeam&&<div className="field"><label>外訪員</label><select value={visitorId} onChange={e=>setVisitorId(e.target.value)}><option value="">全部外訪員</option>{visitors.filter(v=>!teamId||String(v.teamId)===teamId).map(v=><option key={v.userId} value={v.userId}>{v.displayName}｜{v.employeeNo}</option>)}</select></div>}
    <div className="field"><label>關鍵字</label><input value={locationKeyword} onChange={e=>setLocationKeyword(e.target.value)} placeholder="工號、姓名、地點、專案或行程編號"/></div>
    <div className="field"><label>專案</label><select value={projectId} onChange={e=>setProjectId(e.target.value)}><option value="">全部專案</option>{projects.map(p=><option key={p.projectId} value={p.projectId}>{p.projectName}</option>)}</select></div>
    <div className="field"><label>拜訪形式</label><select value={visitTypeId} onChange={e=>setVisitTypeId(e.target.value)}><option value="">全部拜訪形式</option>{visitTypes.map(v=><option key={v.visitTypeId} value={v.visitTypeId}>{v.visitTypeName}</option>)}</select></div>
    <div className="field"><label>狀態</label><select value={status} onChange={e=>setStatus(e.target.value)}>{statuses.map(([v,n])=><option key={v} value={v}>{n}</option>)}</select></div>
   </div>
   <div className="actions query-actions"><button className="btn" onClick={()=>void load(1)} disabled={busy||query.loading}>查詢</button><button className="btn outline" onClick={()=>{setTeamId('');setVisitorId('');setLocationKeyword('');setProjectId('');setVisitTypeId('');setStatus('');setIncludeCancelled(false);setStart(monthStart(today));setEnd(today)}}>清除條件</button>{canIncludeCancelled&&<label className="check-row"><input type="checkbox" checked={includeCancelled} onChange={e=>setIncludeCancelled(e.target.checked)}/>包含已作廢</label>}</div>
   {msg&&<div className="note" style={{marginTop:12}}>{msg}</div>}
   {query.error&&<div role="alert" className="note danger-note">{query.error}</div>}
   {query.loading&&<div role="status">查詢中…</div>}
   <div className="query-result-head"><strong>查詢結果</strong><span>{appliedDescription}</span></div>
   <div className="table-wrap"><table><thead><tr><th>日期</th><th>時間</th><th>外訪員</th><th>小組</th><th>專案</th><th>路線</th><th>自算</th><th>系統</th><th>核定</th><th>費率</th><th>補助</th><th>狀態</th><th>操作</th></tr></thead><tbody>{rows.items.map(r=><tr key={r.visitTripId}><td>{r.visitDate}</td><td>{timeRange(r.startTime,r.endTime)}</td><td>{r.visitorName}</td><td>{r.teamName||'—'}</td><td>{r.projectNames||'—'}</td><td>{r.route}</td><td>{km(r.claimedDistanceKm)}</td><td>{km(r.systemDistanceKm)}</td><td>{km(r.approvedDistanceKm)}</td><td>{money(r.ratePerKmSnapshot)}</td><td>{money(r.subsidyAmount)}</td><td><span className={`pill ${r.status==='Approved'?'ok':r.status==='Returned'?'danger':'warn'}`}>{r.statusName}</span>{r.snapshotVersion>0&&<div className="muted">Snapshot V{r.snapshotVersion}</div>}{r.status==='Returned'&&r.returnReason&&<div className="muted"><strong>退回原因：</strong>{r.returnReason}</div>}</td><td><div className="actions">{allowCorrection&&(r.status==='Draft'||r.status==='Returned')&&<button className="btn small secondary" onClick={()=>navigate(`/?edit=${r.visitTripId}`)}>{r.status==='Returned'?'修改後重送':'繼續編輯／送出'}</button>}{allowCorrection&&activeRole==='visitor'&&r.status==='Draft'&&<button className="btn small danger" onClick={()=>void deleteDraft(r)} disabled={busy||query.loading}>刪除草稿</button>}<button className="btn small outline" onClick={()=>setDetail(r)}>查看</button>{allowCorrection&&r.status==='Approved'&&(isPendingCorrection(r.correctionStatus)?<button className="btn small secondary" disabled title="此行程已有待處理的更正申請">更正審核中</button>:<button className="btn small secondary" onClick={()=>void startCorrection(r)}>申請更正</button>)}</div></td></tr>)}</tbody></table></div>
   {!rows.items.length&&<div className="empty">查無符合條件的資料。</div>}
   <Pagination {...rows} page={page} pageSize={pageSize} busy={busy||query.loading} onPage={query.setPage} onPageSize={query.setPageSize}/>

  </div>

  {detail&&<div className="modal" onMouseDown={e=>{if(e.target===e.currentTarget)setDetail(null)}}><div className="modal-panel"><h3>{detail.tripNo}</h3><div className="note">{detail.visitDate}｜{timeRange(detail.startTime,detail.endTime)}｜{detail.visitorName}｜{detail.teamName||'—'}｜{detail.statusName}</div>{detail.status==='Returned'&&detail.returnReason&&<div className="note danger-note" style={{marginTop:10}}><strong>主管退回原因：</strong>{detail.returnReason}</div>}<p><strong>路線：</strong>{detail.route}</p><div className="route-list">{detail.stops.map(s=><div className="route-item" key={s.stopSequence}><div className="route-index">{s.stopSequence}</div><div><div className="route-name">{s.locationCode?`${s.locationCode}｜`:''}{s.locationName}</div><div className="route-address">{s.address||'—'}</div><div className="muted">{s.projectName||'非專案'}｜{s.visitTypeName||'未指定拜訪形式'}｜目的：{s.visitPurpose||'未填'}</div></div></div>)}</div><p><strong>里程：</strong>自算 {km(detail.claimedDistanceKm)}／系統 {km(detail.systemDistanceKm)}／核定 {km(detail.approvedDistanceKm)}<br/><strong>補助：</strong>{money(detail.ratePerKmSnapshot)}／km，合計 {money(detail.subsidyAmount)}</p><div className="actions">{allowCorrection&&(detail.status==='Draft'||detail.status==='Returned')&&<button className="btn ok" onClick={()=>{const id=detail.visitTripId;setDetail(null);navigate(`/?edit=${id}`)}}>{detail.status==='Returned'?'修改後重送':'繼續編輯／送出'}</button>}{allowCorrection&&activeRole==='visitor'&&detail.status==='Draft'&&<button className="btn danger" onClick={()=>void deleteDraft(detail)} disabled={busy||query.loading}>刪除草稿</button>}<button className="btn" onClick={()=>setDetail(null)}>關閉</button></div></div></div>}

  {correction&&<div className="modal"><div className="modal-panel correction-modal" role="dialog" aria-modal="true" aria-label="申請更正"><button className="btn small outline modal-close" aria-label="關閉更正視窗" disabled={busy} onClick={()=>{setCorrection(null);setCorrectionError('')}}>關閉</button><h3>申請更正｜{correction.tripNo}</h3><div className="note">更正不會覆寫原核准資料；核准後會建立新的 Snapshot 版本。</div>{correctionError&&<div className="note danger-note" style={{marginTop:10}}><strong>無法送出：</strong>{correctionError}</div>}<div className="field"><label>更正原因</label><textarea value={reason} onChange={e=>setReason(e.target.value)} placeholder="請說明錯誤原因與需要更正的內容"/></div><div className="grid cols-3"><div className="field"><label>日期</label><input type="date" value={correction.proposal.visitDate} onChange={e=>updateProposal({visitDate:e.target.value})}/></div><div className="field"><label>開始時間</label><input type="time" value={(correction.proposal.startTime||'').slice(0,5)} onChange={e=>updateProposal({startTime:e.target.value?`${e.target.value}:00`:undefined})}/></div><div className="field"><label>結束時間</label><input type="time" value={(correction.proposal.endTime||'').slice(0,5)} onChange={e=>updateProposal({endTime:e.target.value?`${e.target.value}:00`:undefined})}/></div><div className="field"><label>外訪員自算里程</label><input type="number" step="0.1" value={correction.proposal.claimedDistanceKm??''} onChange={e=>updateProposal({claimedDistanceKm:e.target.value?Number(e.target.value):undefined})}/></div><div className="field"><label>申請更正核定里程</label><input type="number" step="0.1" value={correction.proposal.approvedDistanceKm??''} onChange={e=>updateProposal({approvedDistanceKm:e.target.value?Number(e.target.value):undefined})}/></div></div><div className="field"><label>備註</label><textarea value={correction.proposal.notes||''} onChange={e=>updateProposal({notes:e.target.value})}/></div><h4>拜訪地點</h4>{correction.proposal.stops.map((s,i)=><div className="correction-stop" key={i}><strong>#{s.stopSequence}</strong><div className="grid cols-2"><div className="field"><label>地點名稱</label><input value={s.locationName} onChange={e=>updateStop(i,{locationName:e.target.value})}/></div><div className="field"><label>地址</label><input value={s.address||''} onChange={e=>updateStop(i,{address:e.target.value})}/></div><div className="field"><label>專案名稱</label><input value={s.projectName||''} onChange={e=>updateStop(i,{projectName:e.target.value})}/></div><div className="field"><label>拜訪形式</label><input value={s.visitTypeName||''} onChange={e=>updateStop(i,{visitTypeName:e.target.value})}/></div><div className="field span-2"><label>行程目的</label><input value={s.visitPurpose||''} onChange={e=>updateStop(i,{visitPurpose:e.target.value})}/></div></div></div>)}<div className="modal-sticky-actions"><button className="btn secondary" onClick={()=>{setCorrection(null);setCorrectionError('')}}>取消</button><button className="btn ok" onClick={()=>void submitCorrection()} disabled={busy||query.loading}>送出更正申請</button></div></div></div>}
 </>
}
