import{useEffect,useState}from"react";
import type{CorrectionRequest}from"../types";
import{usePagedQuery}from"../use-query";
import{ALL_CORRECTION_STATUSES,correctionSearchEnabled,correctionStatusLabel,correctionRequestDate}from"../correction-ui";
import{apiDownload}from"../api";
import{qs}from"../v160";
import{DateFilters,Pagination}from"./QueryControls";
import DetailLink from"./DetailLink";
import CorrectionDetail from"./CorrectionDetail";
export type HistoryTrip={visitTripId:number;tripNo:string};
export function exactHistoryRows(rows:readonly CorrectionRequest[],trip?:HistoryTrip|null){
 return trip?rows.filter(r=>r.visitTripId===trip.visitTripId&&r.tripNo===trip.tripNo):rows;
}
export default function CorrectionHistory({initialTrip,active=true}:{initialTrip?:HistoryTrip|null;active?:boolean}){
 const[trip,setTrip]=useState(initialTrip||null),[keyword,setKeyword]=useState(initialTrip?.tripNo||""),[status,setStatus]=useState(ALL_CORRECTION_STATUSES),[start,setStart]=useState(""),[end,setEnd]=useState(""),[detail,setDetail]=useState<CorrectionRequest|null>(null),[busy,setBusy]=useState(false),[message,setMessage]=useState("");
 useEffect(()=>{if(initialTrip){setTrip(initialTrip);setKeyword(initialTrip.tripNo);setStatus(ALL_CORRECTION_STATUSES);setStart("");setEnd("");setDetail(null);setMessage("")}},[initialTrip]);
 const enabled=active&&correctionSearchEnabled(start,end);
 const query=usePagedQuery<CorrectionRequest>("/corrections/search",{keyword:trip?undefined:keyword,status,startDate:start,endDate:end,visitTripId:trip?.visitTripId,tripNo:trip?.tripNo},enabled);
 const rows=exactHistoryRows(query.data.items,trip);
 const download=async(format:"xlsx"|"pdf")=>{
  if(!query.appliedFilters||query.loading||query.error||busy)return;
  setBusy(true);setMessage("");
  try{await apiDownload("/corrections/export."+format+"?"+qs(query.appliedFilters),"更正歷史."+format,180000)}
  catch(e){setMessage(e instanceof Error?e.message:"下載失敗")}finally{setBusy(false)}
 };
 return <div className="card">
 <div className="section-title"><div><h2>更正歷史</h2><div className="sub">依登入角色的授權範圍查詢更正申請、原核准版本、結果版本與處理紀錄；下載包含全部符合查詢條件的資料，不受分頁限制。</div></div><div className="actions"><button className="btn secondary" disabled={busy||query.loading||!!query.error||!query.appliedFilters} onClick={()=>void download("xlsx")}>下載 Excel</button><button className="btn secondary" disabled={busy||query.loading||!!query.error||!query.appliedFilters} onClick={()=>void download("pdf")}>下載 PDF</button></div></div>
 {trip&&<div className="note">目前限定行程：{trip.tripNo}。<button className="btn small outline" onClick={()=>{setTrip(null);setKeyword("");setStatus(ALL_CORRECTION_STATUSES)}}>查詢其他更正</button></div>}
 <div className="grid cols-2"><label>關鍵字<input disabled={!!trip} value={keyword} onChange={e=>setKeyword(e.target.value)} placeholder="行程編號、工號、姓名、更正原因或差異"/></label><label>更正狀態<select value={status} onChange={e=>setStatus(e.target.value)}><option value={ALL_CORRECTION_STATUSES}>全部狀態</option>{["PendingLeaderReview","PendingAdminClose","Closed","Rejected"].map(s=><option key={s} value={s}>{correctionStatusLabel(s)}</option>)}</select></label></div>
 <DateFilters label="申請日期" start={start} end={end} onChange={(s,e)=>{setStart(s);setEnd(e)}}/>
 <div className="actions"><button className="btn small" disabled={!enabled||query.loading||busy} onClick={()=>{query.setPage(1);query.reload()}}>查詢</button><button className="btn small outline" onClick={()=>{setTrip(null);setKeyword("");setStatus(ALL_CORRECTION_STATUSES);setStart("");setEnd("")}}>清除條件</button></div>
 {message&&<div role="alert" className="note danger-note">{message}</div>}
 {query.loading&&<div role="status">查詢中…</div>}{query.error&&<div role="alert" className="note danger-note">{query.error}</div>}
 <div className="query-result-head"><strong>查詢結果</strong><span>{query.appliedFilters?`${query.data.totalCount} 筆`:"查詢條件變更中"}</span></div>
 <div className="table-wrap"><table><thead><tr><th>申請日</th><th>行程編號</th><th>外訪員</th><th>更正原因</th><th>狀態</th><th>原核准版本</th><th>結果版本</th></tr></thead><tbody>{rows.map(r=><tr key={r.correctionRequestId}><td>{correctionRequestDate(r.requestedAt)}</td><td><DetailLink ariaLabel={`查看更正明細 ${r.tripNo}`} onClick={()=>setDetail(r)}>{r.tripNo}</DetailLink></td><td>{r.visitorName}</td><td>{r.reason}</td><td>{correctionStatusLabel(r.status)}</td><td>V{r.baseSnapshotVersion}</td><td>{r.resultSnapshotVersion!=null?`V${r.resultSnapshotVersion}`:"—"}</td></tr>)}</tbody></table></div>
 {query.appliedFilters&&!query.loading&&!rows.length&&<div className="empty">查無符合條件的更正紀錄。</div>}
 <Pagination {...query.data} page={query.page} pageSize={query.pageSize} busy={query.loading||busy} onPage={query.setPage} onPageSize={query.setPageSize}/>
 {detail&&<CorrectionDetail row={detail} onClose={()=>setDetail(null)}/>}
 </div>
}
