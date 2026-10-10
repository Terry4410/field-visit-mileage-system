import{useEffect,useState}from"react";
import{Link}from"react-router-dom";
import{api,apiDownload}from"../api";
import{correctionChangeText}from"../correction-ui";
import{formatTripTime,hasLeaderTimeOverlap,leaderOverlapConfirmMessage,leaderOverlapWarningText}from"../leader-overlap";
import type{BackgroundJob,CorrectionRequest,DashboardSummary,ImportPreview,ManagedLocation,Team,Trip}from"../types";
import{km,money,monthStart,todayTaipei}from"../v160";
import{tripRouteSummary}from"../components/TripRouteDisplay";
import{hasMinimumVisitStops}from"../trip-submit-rules";

type Props={section:'dashboard'|'review'|'locations'};
export default function LeaderPage({section}:Props){const[msg,setMsg]=useState(''),[busy,setBusy]=useState(false);useEffect(()=>setMsg(''),[section]);if(section==='dashboard')return <Dashboard setMsg={setMsg} msg={msg}/>;if(section==='locations')return <Locations setMsg={setMsg} msg={msg} busy={busy} setBusy={setBusy}/>;return <Review setMsg={setMsg} msg={msg} busy={busy} setBusy={setBusy}/>}
function Stat({label,value,hint}:{label:string;value:string|number;hint?:string}){return <div className="card stat"><div className="label">{label}</div><div className="value">{value}</div>{hint&&<div className="hint">{hint}</div>}</div>}
function Dashboard({msg,setMsg}:{msg:string;setMsg:(v:string)=>void}){const[d,setD]=useState<DashboardSummary|null>(null);useEffect(()=>{api<DashboardSummary>('/dashboard').then(setD).catch(e=>setMsg(e.message))},[]);return <><div className="grid cols-5 dashboard-cards"><Stat label="本月行程" value={d?.thisMonthTrips??'—'}/><Stat label="待核准" value={d?.pendingApproval??'—'}/><Stat label="已核准" value={d?.approved??'—'}/><Stat label="待確認地點" value={d?.pendingLocations??'—'}/><Stat label="待審更正" value={d?.pendingCorrections??'—'}/></div>{msg&&<div className="note">{msg}</div>}</>}

function Review({msg,setMsg,busy,setBusy}:{msg:string;setMsg:(v:string)=>void;busy:boolean;setBusy:(v:boolean)=>void}){
 const[rows,setRows]=useState<Trip[]>([]);
 const[selected,setSelected]=useState<number[]>([]);
 const[approvalSelected,setApprovalSelected]=useState<number[]>([]);
 const[reviewKm,setReviewKm]=useState<Record<number,string>>({});
 const[mode,setMode]=useState('AllPending');
 const[start,setStart]=useState(monthStart());
 const[end,setEnd]=useState(todayTaipei());
 const[job,setJob]=useState<BackgroundJob|null>(null);
 const[corrections,setCorrections]=useState<CorrectionRequest[]>([]);

 const selectionEnabled=mode==='Selected';
 const calculableRows=rows.filter(t=>hasMinimumVisitStops(t.stops.length)&&t.status!=='PendingApproval');
 const calculableIds=calculableRows.map(t=>t.visitTripId);
 const approvalRows=rows.filter(t=>t.status==='PendingApproval');
 const approvalIds=approvalRows.map(t=>t.visitTripId);
 const allCalculableSelected=calculableIds.length>0&&calculableIds.every(id=>selected.includes(id));
 const someCalculableSelected=calculableIds.some(id=>selected.includes(id));
 const allApprovalSelected=approvalIds.length>0&&approvalIds.every(id=>approvalSelected.includes(id));
 const someApprovalSelected=approvalIds.some(id=>approvalSelected.includes(id));

 const toggleAllCalculable=(checked:boolean)=>setSelected(current=>checked?Array.from(new Set([...current,...calculableIds])):current.filter(id=>!calculableIds.includes(id)));
 const toggleAllApproval=(checked:boolean)=>setApprovalSelected(checked?approvalIds:[]);

 const load=()=>Promise.all([
   api<Trip[]>('/leader/review-queue'),
   api<CorrectionRequest[]>('/corrections?status=PendingLeaderReview')
 ]).then(([t,c])=>{
   setRows(t);setCorrections(c);
   setReviewKm(Object.fromEntries(t.map(x=>[x.visitTripId,String(x.approvedDistanceKm??x.systemDistanceKm??x.claimedDistanceKm??'')])));
   setApprovalSelected(current=>current.filter(id=>t.some(x=>x.visitTripId===id&&x.status==='PendingApproval')));
 }).catch(e=>setMsg(e.message));
 useEffect(()=>{void load()},[]);

 const enqueue=async()=>{
   if(mode==='Selected'&&!selected.length)return setMsg('請先勾選要計算的行程。');
   setBusy(true);
   try{
     const j=await api<BackgroundJob>('/jobs/mileage',{method:'POST',body:JSON.stringify({mode,startDate:mode==='DateRange'?start:null,endDate:mode==='DateRange'?end:null,selectedTripIds:mode==='Selected'?selected:null})});
     setJob(j);setMsg(`已建立 Google Maps 里程背景工作 ${j.backgroundJobId}`);setTimeout(()=>void poll(j.backgroundJobId),800);
   }catch(e){setMsg(e instanceof Error?e.message:'建立里程工作失敗')}finally{setBusy(false)}
 };
 const poll=async(id:string)=>{
   const j=await api<BackgroundJob>(`/jobs/${id}`);setJob(j);
   if(['Waiting','Processing'].includes(j.status))setTimeout(()=>void poll(id),1200);
   else{await load();setSelected([])}
 };

 const approvalPayload=(t:Trip)=>{
   const value=Number(reviewKm[t.visitTripId]||t.systemDistanceKm||t.claimedDistanceKm);
   if(!Number.isFinite(value)||value<=0)throw new Error(`${t.tripNo}：請填寫大於 0 的有效核定里程。`);
   const google=t.mileageSource==='GoogleMapsAPI'&&!!t.routeCalculationAttemptId&&!!t.systemDistanceKm;
   if(google){
     const adjusted=Math.abs(value-Number(t.systemDistanceKm))>0.000001;
     return{visitTripId:t.visitTripId,approvedDistanceKm:value,rowVersion:t.rowVersion,distanceDecisionSource:adjusted?'LeaderAdjusted':'ProviderSuggested',routeCalculationAttemptId:t.routeCalculationAttemptId};
   }
   if(!(t.claimedDistanceKm&&t.claimedDistanceKm>0))
     throw new Error(`${t.tripNo}：Google Maps API 無可用里程，且沒有人工備援里程。`);
   return{visitTripId:t.visitTripId,approvedDistanceKm:value,rowVersion:t.rowVersion,distanceDecisionSource:'ManualFallback',routeCalculationAttemptId:null};
 };

 const approve=async(t:Trip)=>{
   let payload;
   try{payload=approvalPayload(t)}catch(e){return setMsg(e instanceof Error?e.message:'核定資料不完整')}
   const overlapMessage=leaderOverlapConfirmMessage(t,rows);
   if(overlapMessage&&!window.confirm(overlapMessage))return;
   const comments=overlapMessage?(t.timeOverlapConfirmed?'時間重疊；外訪員已確認，小組長確認仍核准。':'時間重疊；小組長確認仍核准。'):null;
   setBusy(true);
   try{
     await api(`/trips/${t.visitTripId}/approve`,{method:'POST',body:JSON.stringify({...payload,comments})});
     setMsg(`已核准 ${t.tripNo}`);await load();
   }catch(e){setMsg(e instanceof Error?e.message:'核准失敗')}finally{setBusy(false)}
 };

 const batchApprove=async()=>{
   const targets=approvalRows.filter(t=>approvalSelected.includes(t.visitTripId));
   if(!targets.length)return setMsg('請先勾選要批次核准的行程。');
   let items;
   try{items=targets.map(approvalPayload)}catch(e){return setMsg(e instanceof Error?e.message:'批次核定資料不完整')}
   const overlapCount=targets.filter(t=>hasLeaderTimeOverlap(t,rows)).length;
   if(overlapCount>0&&!window.confirm(`選取行程中有 ${overlapCount} 筆存在時間重疊提醒，是否仍要批次核准？`))return;
   if(!window.confirm(`確認批次核准 ${targets.length} 筆行程？`))return;
   setBusy(true);
   try{
     const result=await api<{success:number;failed:number;errors:string[]}>('/trips/batch-approve',{method:'POST',body:JSON.stringify({items})});
     setMsg(result.failed?`批次核准完成：成功 ${result.success}、失敗 ${result.failed}。 ${result.errors.join('；')}`:`批次核准完成：${result.success} 筆成功。`);
     setApprovalSelected([]);await load();
   }catch(e){setMsg(e instanceof Error?e.message:'批次核准失敗')}finally{setBusy(false)}
 };

 const ret=async(t:Trip)=>{
   const reason=window.prompt('請輸入退回原因');if(!reason)return;
   setBusy(true);try{await api(`/trips/${t.visitTripId}/return`,{method:'POST',body:JSON.stringify({reason,rowVersion:t.rowVersion})});setMsg(`已退回 ${t.tripNo}`);await load()}catch(e){setMsg(e instanceof Error?e.message:'退回失敗')}finally{setBusy(false)}
 };

 const reviewCorrection=async(r:CorrectionRequest,approve:boolean)=>{
   const comments=window.prompt(approve?'審核說明（選填）':'拒絕原因')||'';
   setBusy(true);try{await api(`/corrections/${r.correctionRequestId}/leader-review`,{method:'POST',body:JSON.stringify({approve,comments,rowVersion:r.rowVersion})});setMsg(approve?'更正申請已通過；若涉及財務將送管理者結案。':'更正申請已拒絕。');await load()}catch(e){setMsg(e instanceof Error?e.message:'更正審核失敗')}finally{setBusy(false)}
 };

 const sourceText=(t:Trip)=>t.mileageSource==='GoogleMapsAPI'?'Google Maps API':t.mileageSource==='ManualFallback'?'人工備援':t.status==='Returned'?'需補人工備援':'待 Google Maps 計算';

 return <>
  <div className="card">
   <div className="section-title"><div><h2>批次 Google Maps 里程</h2><div className="sub">依派駐中心 → 拜訪順序 → 返回中心取得 Google Maps API 路線；API 失敗時才使用人工備援。</div></div></div>
   <div className="actions">
    <select value={mode} onChange={e=>{const next=e.target.value;setMode(next);if(next!=='Selected')setSelected([])}}>
     <option value="AllPending">全部未計算</option><option value="DateRange">指定日期區間</option><option value="Selected">勾選指定行程</option>
    </select>
    {mode==='DateRange'&&<><input type="date" value={start} onChange={e=>setStart(e.target.value)}/><input type="date" value={end} onChange={e=>setEnd(e.target.value)}/></>}
    {selectionEnabled&&<span className="pill">已選 {selected.length} 筆</span>}
    <button className="btn" disabled={busy} onClick={()=>void enqueue()}>建立 Google Maps 計算工作</button>
   </div>
   {job&&<div className="note">工作狀態：{job.status}｜總計 {job.totalCount}｜成功 {job.successCount}｜失敗 {job.failedCount}</div>}
   {msg&&<div className="note">{msg}</div>}
  </div>

  <div className="card" style={{marginTop:18}}>
   <div className="section-title"><h2>行程審核</h2><div className="actions"><span className="pill">{rows.length} 筆</span><button className="btn small ok" disabled={busy||!approvalSelected.length} onClick={()=>void batchApprove()}>批次核准（{approvalSelected.length}）</button></div></div>
   <div className="table-wrap"><table>
    <thead><tr>
     <th><input type="checkbox" aria-label="全選可計算行程" title="全選可計算行程" checked={allCalculableSelected} disabled={!selectionEnabled||!calculableRows.length} ref={el=>{if(el)el.indeterminate=selectionEnabled&&someCalculableSelected&&!allCalculableSelected}} onChange={e=>toggleAllCalculable(e.target.checked)}/></th>
     <th><input type="checkbox" aria-label="全選待核准行程" title="全選待核准行程" checked={allApprovalSelected} disabled={!approvalRows.length} ref={el=>{if(el)el.indeterminate=someApprovalSelected&&!allApprovalSelected}} onChange={e=>toggleAllApproval(e.target.checked)}/></th>
     <th>日期</th><th>時間</th><th>外訪員</th><th>小組</th><th>路線</th><th>人工備援</th><th>Google Maps API</th><th>小組長核定</th><th>里程來源</th><th>操作</th>
    </tr></thead>
    <tbody>{rows.map(t=>{
     const noMileage=!hasMinimumVisitStops(t.stops.length);const overlap=hasLeaderTimeOverlap(t,rows);const overlapText=leaderOverlapWarningText(t,rows);
     return <tr key={t.visitTripId}>
      <td><input type="checkbox" disabled={!selectionEnabled||noMileage||t.status==='PendingApproval'} title={!selectionEnabled?'請先選擇「勾選指定行程」':noMileage?'此行程至少需要 1 個拜訪地點':t.status==='PendingApproval'?'此行程已完成路線處理':'選取待計算行程'} checked={selected.includes(t.visitTripId)} onChange={e=>setSelected(x=>e.target.checked?[...x,t.visitTripId]:x.filter(id=>id!==t.visitTripId))}/></td>
      <td><input type="checkbox" disabled={t.status!=='PendingApproval'} checked={approvalSelected.includes(t.visitTripId)} onChange={e=>setApprovalSelected(x=>e.target.checked?[...x,t.visitTripId]:x.filter(id=>id!==t.visitTripId))}/></td>
      <td>{t.visitDate}</td><td><div>{formatTripTime(t)}</div>{overlap&&overlapText&&<div style={{marginTop:4,fontSize:12,fontWeight:700,color:'#b45309',whiteSpace:'nowrap'}}>{overlapText}</div>}</td>
      <td>{t.visitorName}</td><td>{t.teamName||'—'}</td><td>{tripRouteSummary(t)}</td>
      <td>{km(t.claimedDistanceKm)}</td><td>{noMileage?'N/A':km(t.systemDistanceKm)}</td>
      <td>{noMileage?<span className="pill">N/A</span>:<input className="mileage-input" type="number" step="0.1" value={reviewKm[t.visitTripId]||''} onChange={e=>setReviewKm(x=>({...x,[t.visitTripId]:e.target.value}))}/>}</td>
      <td>{noMileage?<span className="pill">N/A</span>:<><strong>{sourceText(t)}</strong>{t.mileageSource==='GoogleMapsAPI'&&<div className="muted">Google 路線可直接核准；若調整里程會記錄 LeaderAdjusted。</div>}{t.mileageSource==='ManualFallback'&&<div className="muted">Google 無可用結果，本次使用人工備援並保留治理紀錄。</div>}</>}</td>
      <td>{t.status==='PendingApproval'?<div className="actions"><button className="btn small ok" onClick={()=>void approve(t)} disabled={busy}>核准</button><button className="btn small danger" onClick={()=>void ret(t)} disabled={busy}>退回</button></div>:<span className="muted">{t.status==='Returned'?'已退回補人工備援':'待里程計算'}</span>}</td>
     </tr>
    })}</tbody>
   </table></div>
  </div>

  <div className="card" style={{marginTop:18}}>
   <div className="section-title"><div><h2>更正申請審核</h2><div className="sub">非財務更正可由小組長完成；核定里程/補助變更會再送管理者結案。</div></div><span className="pill">{corrections.length}</span></div>
   <div className="table-wrap"><table><thead><tr><th>Trip</th><th>外訪員</th><th>小組</th><th>原因</th><th>差異</th><th>操作</th></tr></thead>
   <tbody>{corrections.map(r=><tr key={r.correctionRequestId}><td>{r.tripNo}</td><td>{r.visitorName}</td><td>{r.teamName||'—'}</td><td>{r.reason}</td><td>{r.changes.length?<div className="correction-change-list">{r.changes.map((c,i)=><div key={i}>{correctionChangeText(c)}</div>)}</div>:'—'}</td><td><div className="actions"><button className="btn small ok" onClick={()=>void reviewCorrection(r,true)}>通過</button><button className="btn small danger" onClick={()=>void reviewCorrection(r,false)}>拒絕</button></div></td></tr>)}</tbody>
   </table></div>
  </div>
 </>;
}

function isProcessableLocation(l:ManagedLocation){
 return l.approvalStatus==='Pending'
   ||l.geocodingStatus==='Pending'
   ||l.geocodingStatus==='Failed'
}

function Locations({msg,setMsg,busy,setBusy}:{msg:string;setMsg:(v:string)=>void;busy:boolean;setBusy:(v:boolean)=>void}){
 const[rows,setRows]=useState<ManagedLocation[]>([]),[teams,setTeams]=useState<Team[]>([]),[selected,setSelected]=useState<number[]>([]),[edit,setEdit]=useState<ManagedLocation|null>(null),[name,setName]=useState(''),[teamId,setTeamId]=useState(''),[address,setAddress]=useState(''),[plus,setPlus]=useState(''),[city,setCity]=useState(''),[district,setDistrict]=useState(''),[file,setFile]=useState<File|null>(null),[preview,setPreview]=useState<ImportPreview|null>(null),[job,setJob]=useState<BackgroundJob|null>(null),[geoMode,setGeoMode]=useState('AllPending'),[geoStart,setGeoStart]=useState(monthStart()),[geoEnd,setGeoEnd]=useState(todayTaipei());
 const selectionEnabled=geoMode==='Selected';
 const processableRows=rows.filter(isProcessableLocation);
 const processableIds=processableRows.map(l=>l.locationId);
 const allProcessableSelected=
   processableIds.length>0
   &&processableIds.every(id=>selected.includes(id));
 const someProcessableSelected=
   processableIds.some(id=>selected.includes(id));

 const toggleAllProcessable=(checked:boolean)=>
   setSelected(current=>
     checked
       ?Array.from(new Set([...current,...processableIds]))
       :current.filter(id=>!processableIds.includes(id))
   );

 const load=()=>Promise.all([api<ManagedLocation[]>('/managed-locations?includeInactive=true'),api<Team[]>('/teams')]).then(([l,t])=>{setRows(l);setTeams(t)}).catch(e=>setMsg(e.message));useEffect(()=>{void load()},[]);
 const open=(l:ManagedLocation)=>{setEdit(l);setName(l.locationName);setTeamId(l.teamId?String(l.teamId):'');setAddress(l.address||'');setPlus(l.plusCode||'');setCity(l.city||'');setDistrict(l.district||'')};
 const save=async()=>{if(!edit)return;setBusy(true);try{await api(`/managed-locations/${edit.locationId}`,{method:'PUT',body:JSON.stringify({teamId:Number(teamId),locationName:name,locationType:edit.locationType,city:city||null,district:district||null,address:address||null,plusCode:plus||null,isActive:edit.isActive,rowVersion:edit.rowVersion})});setEdit(null);setMsg('地點已修改，需重新解析後發布。');await load()}catch(e){setMsg(e instanceof Error?e.message:'儲存失敗')}finally{setBusy(false)}};
 const previewImport=async()=>{if(!file)return setMsg('請選擇 .xlsx');setBusy(true);try{const f=new FormData();f.append('file',file);setPreview(await api<ImportPreview>('/imports/locations/preview',{method:'POST',body:f},120000))}catch(e){setMsg(e instanceof Error?e.message:'預覽失敗')}finally{setBusy(false)}};
 const confirm=async()=>{if(!preview)return;setBusy(true);try{await api(`/imports/${preview.importBatchId}/confirm`,{method:'POST'},120000);setPreview(null);setMsg('地點匯入完成。');await load()}catch(e){setMsg(e instanceof Error?e.message:'匯入失敗')}finally{setBusy(false)}};
 const geocode=async()=>{if(geoMode==='Selected'&&!selected.length)return setMsg('請先勾選地點。');if(geoMode==='Selected'&&selected.length>1&&!window.confirm(`即將解析並發布 ${selected.length} 筆地點。\n\n完成後符合條件的地點會變成正式可使用地點。\n\n確定繼續？`))return;setBusy(true);try{const j=await api<BackgroundJob>('/jobs/geocoding',{method:'POST',body:JSON.stringify({mode:geoMode,startDate:geoMode==='DateRange'?geoStart:null,endDate:geoMode==='DateRange'?geoEnd:null,locationIds:geoMode==='Selected'?selected:null})});setJob(j);setMsg(`已建立地點解析背景工作 ${j.backgroundJobId}`);setTimeout(()=>void poll(j.backgroundJobId),800)}catch(e){setMsg(e instanceof Error?e.message:'建立工作失敗')}finally{setBusy(false)}};
 const poll=async(id:string)=>{const j=await api<BackgroundJob>(`/jobs/${id}`);setJob(j);if(['Waiting','Processing'].includes(j.status))setTimeout(()=>void poll(id),1200);else{await load();setSelected([])}};
 return <><div className="card"><div className="section-title"><div><h2>地點管理</h2><div className="sub">只可變更有效授權小組的待審核草稿；正式主檔修改須管理者核准。</div></div><Link className="btn small ok" to="/leader/locations/new">新增地點</Link><button className="btn outline" onClick={()=>void apiDownload('/imports/locations/template','地點主檔匯入範例.xlsx')}>下載 Excel 範例</button></div><div className="actions"><input type="file" accept=".xlsx" onChange={e=>setFile(e.target.files?.[0]||null)}/><button className="btn secondary" onClick={()=>void previewImport()} disabled={busy}>預覽匯入</button></div><div className="actions geocode-actions"><select value={geoMode} onChange={e=>{const next=e.target.value;setGeoMode(next);if(next!=='Selected')setSelected([])}}><option value="AllPending">全部未上傳／待解析地點</option><option value="DateRange">依新增日期區間</option><option value="Selected">只處理勾選地點</option></select>{geoMode==='DateRange'&&<><input type="date" value={geoStart} onChange={e=>setGeoStart(e.target.value)}/><input type="date" value={geoEnd} onChange={e=>setGeoEnd(e.target.value)}/></>}{selectionEnabled&&<span className="pill">已選 {selected.length} 筆</span>}<button className="btn ok" onClick={()=>void geocode()} disabled={busy}>解析座標（不直接發布）</button></div>{preview&&<div className="note">匯入預覽：{preview.validCount} 可匯入／{preview.errorCount} 錯誤 <span className="actions">{preview.errorCount>0&&<button className="btn small outline" onClick={()=>void apiDownload(`/imports/${preview.importBatchId}/errors.xlsx`,'匯入錯誤.xlsx')}>下載錯誤 Excel</button>}<button className="btn small" disabled={preview.errorCount>0} onClick={()=>void confirm()}>確認匯入</button></span></div>}{job&&<div className="note">背景工作：{job.status}｜成功 {job.successCount}｜失敗 {job.failedCount}</div>}{msg&&<div className="note">{msg}</div>}<div className="table-wrap"><table><thead><tr><th><input type="checkbox" aria-label="全選可處理地點" title="全選可處理地點" checked={allProcessableSelected} disabled={!selectionEnabled||!processableRows.length} ref={el=>{if(el)el.indeterminate=selectionEnabled&&someProcessableSelected&&!allProcessableSelected}} onChange={e=>toggleAllProcessable(e.target.checked)}/></th><th>代碼</th><th>地點</th><th>小組</th><th>地址</th><th>狀態</th><th>操作</th></tr></thead><tbody>{rows.map(l=><tr key={l.locationId}><td><input type="checkbox" disabled={!selectionEnabled||!isProcessableLocation(l)} title={!selectionEnabled?'請先選擇「只處理勾選地點」':isProcessableLocation(l)?'選取待處理地點':'此地點已完成，不需再次解析/發布'} checked={selected.includes(l.locationId)} onChange={e=>setSelected(x=>e.target.checked?[...x,l.locationId]:x.filter(i=>i!==l.locationId))}/></td><td>{l.locationCode}</td><td>{l.locationName}</td><td>{l.teamName}</td><td>{l.address||l.plusCode||'—'}</td><td>{l.geocodingStatus}/{l.approvalStatus}</td><td><button className="btn small secondary" disabled={l.isActive||l.approvalStatus!=='Pending'} title={l.isActive?'正式主檔需管理者核准':'僅可修改待審核草稿'} onClick={()=>open(l)}>修改草稿</button></td></tr>)}</tbody></table></div></div>{edit&&<div className="modal"><div className="modal-panel"><h3>地點維護｜{edit.locationCode}</h3><div className="grid cols-2"><div className="field"><label>小組</label><select value={teamId} onChange={e=>setTeamId(e.target.value)}>{teams.map(t=><option key={t.teamId} value={t.teamId}>{t.teamName}</option>)}</select></div><div className="field"><label>地點名稱</label><input value={name} onChange={e=>setName(e.target.value)}/></div><div className="field"><label>縣市</label><input value={city} onChange={e=>setCity(e.target.value)}/></div><div className="field"><label>鄉鎮區</label><input value={district} onChange={e=>setDistrict(e.target.value)}/></div><div className="field span-2"><label>地址</label><input value={address} onChange={e=>setAddress(e.target.value)}/></div><div className="field span-2"><label>Plus Code</label><input value={plus} onChange={e=>setPlus(e.target.value)}/></div></div><div className="modal-sticky-actions"><button className="btn secondary" onClick={()=>setEdit(null)}>取消</button><button className="btn ok" onClick={()=>void save()}>儲存</button></div></div></div>}</>
}
