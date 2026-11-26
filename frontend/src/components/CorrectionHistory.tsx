import{useState}from"react";
import type{CorrectionRequest}from"../types";
import{usePagedQuery}from"../use-query";
import{ALL_CORRECTION_STATUSES,correctionSearchEnabled}from"../correction-ui";
import{correctionStatusLabel}from"../correction-ui";
import{DateFilters,Pagination}from"./QueryControls";
import DetailLink from"./DetailLink";
import CorrectionDetail from"./CorrectionDetail";
export type HistoryTrip={visitTripId:number;tripNo:string};
export function exactHistoryRows(rows:readonly CorrectionRequest[],trip?:HistoryTrip|null){
 return trip?rows.filter(r=>r.visitTripId===trip.visitTripId&&r.tripNo===trip.tripNo):rows;
}
export default function CorrectionHistory({initialTrip,onBack}:{initialTrip?:HistoryTrip|null;onBack:()=>void}){
 const[trip,setTrip]=useState(initialTrip||null),[keyword,setKeyword]=useState(initialTrip?.tripNo||""),[status,setStatus]=useState(ALL_CORRECTION_STATUSES),[start,setStart]=useState(""),[end,setEnd]=useState(""),[detail,setDetail]=useState<CorrectionRequest|null>(null);
 const enabled=correctionSearchEnabled(start,end);
 const query=usePagedQuery<CorrectionRequest>("/corrections/search",{keyword,status,startDate:start,endDate:end},enabled);
 const rows=exactHistoryRows(query.data.items,trip);
 return <div className="card">
 <div className="section-title"><div><h2>更正歷史</h2><div className="sub">依登入角色的授權範圍查詢更正申請、原核准版本、結果版本與處理紀錄；一般行程查詢仍顯示最新有效核准資料。</div></div><button className="btn small outline" onClick={onBack}>返回行程查詢</button></div>
 {trip&&<div className="note">目前限定行程：{trip.tripNo}。關鍵字搜尋可能命中其他案件，本頁僅顯示該行程的精確編號及 ID；分頁數為伺服器搜尋結果。<button className="btn small outline" onClick={()=>{setTrip(null);setKeyword("");setStatus(ALL_CORRECTION_STATUSES)}}>查詢其他更正</button></div>}
 <div className="grid cols-2"><label>關鍵字<input disabled={!!trip} value={keyword} onChange={e=>setKeyword(e.target.value)} placeholder="行程編號、工號、姓名、更正原因或差異"/></label><label>更正狀態<select value={status} onChange={e=>setStatus(e.target.value)}><option value={ALL_CORRECTION_STATUSES}>全部狀態</option>{["PendingLeaderReview","PendingAdminClose","Closed","Rejected"].map(s=><option key={s} value={s}>{correctionStatusLabel(s)}</option>)}</select></label></div>
 <div className="actions">{[["PendingLeaderReview","待小組長審核"],["PendingAdminClose","待管理者結案"],["Closed","已更正"],["Rejected","更正退回"]].map(([s,label])=><button key={s} className={status===s?"btn small":"btn small outline"} onClick={()=>setStatus(s)}>{label}</button>)}</div>
 <DateFilters label="申請日期" start={start} end={end} onChange={(s,e)=>{setStart(s);setEnd(e)}}/>
 <div className="actions"><button className="btn small outline" onClick={()=>{setTrip(null);setKeyword("");setStatus(ALL_CORRECTION_STATUSES);setStart("");setEnd("")}}>清除條件</button><button className="btn small" disabled={!enabled||query.loading} onClick={()=>query.reload()}>重新查詢</button></div>
 {!enabled&&<div className="note">申請日期起日不可晚於迄日。</div>}
 {query.loading&&<div role="status">查詢中…</div>}{query.error&&<div role="alert" className="note danger-note">{query.error}</div>}
 <div className="table-wrap"><table><thead><tr><th>申請日</th><th>行程編號</th><th>外訪員</th><th>更正原因</th><th>狀態</th><th>原核准版本</th><th>結果版本</th></tr></thead><tbody>{rows.map(r=><tr key={r.correctionRequestId}><td>{r.requestedAt.slice(0,10)}</td><td><DetailLink ariaLabel={`查看更正明細 ${r.tripNo}`} onClick={()=>setDetail(r)}>{r.tripNo}</DetailLink></td><td>{r.visitorName}</td><td>{r.reason}</td><td>{correctionStatusLabel(r.status)}</td><td>V{r.baseSnapshotVersion}</td><td>{r.resultSnapshotVersion!=null?`V${r.resultSnapshotVersion}`:"—"}</td></tr>)}</tbody></table></div>
 {query.appliedFilters&&!query.loading&&!rows.length&&<div className="empty">{trip&&query.data.totalPages>1?"本頁無此行程的更正；可繼續查閱其他搜尋結果頁。":"查無符合條件的更正紀錄。"}</div>}
 <Pagination {...query.data} page={query.page} pageSize={query.pageSize} busy={query.loading} onPage={query.setPage} onPageSize={query.setPageSize}/>
 {detail&&<CorrectionDetail row={detail} onClose={()=>setDetail(null)}/>}
 </div>
}
