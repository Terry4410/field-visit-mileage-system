import {useEffect,useMemo,useState} from "react";
import {useNavigate} from "react-router-dom";
import {api} from "../api";
import type {Trip} from "../types";
import {downloadCsv,money} from "../utils";

export default function HistoryPage(){
  const nav=useNavigate();
  const [start,setStart]=useState("2026-01-01"),[end,setEnd]=useState(new Date().toISOString().slice(0,10)),[keyword,setKeyword]=useState(""),[status,setStatus]=useState(""),[rows,setRows]=useState<Trip[]>([]),[msg,setMsg]=useState(""),[detail,setDetail]=useState<Trip|null>(null),[busy,setBusy]=useState(false);

  const load=async()=>{
    setMsg("");
    try{
      const q=new URLSearchParams({startDate:start,endDate:end});
      if(keyword)q.set("locationKeyword",keyword);
      const result=await api<Trip[]>(`/trips/history?${q}`);
      setRows(result);
    }catch(e){setMsg(e instanceof Error?e.message:"查詢失敗")}
  };
  useEffect(()=>{void load()},[]);

  const filtered=useMemo(()=>rows.filter(x=>x.status!=="Cancelled"&&(!status||x.status===status)),[rows,status]);
  const download=()=>downloadCsv("我的歷史行程.csv",filtered.map(t=>({
    日期:t.visitDate,
    出發時間:t.startTime||"",
    結束時間:t.endTime||"",
    拜訪地點:t.stops.map(s=>s.locationName).join("、"),
    拜訪順序:t.stops.map(s=>s.locationName).join(" → "),
    外訪員自算里程:t.claimedDistanceKm??"",
    系統里程:t.systemDistanceKm??"",
    小組長核定里程:t.approvedDistanceKm??"",
    每公里補助:t.ratePerKmSnapshot??"",
    補助金額:t.approvedAmount??"",
    狀態:t.statusName,
    退回原因:t.returnReason||"",
    備註:t.notes||""
  })));

  const deleteDraft=async(t:Trip)=>{
    if(t.status!=="Draft")return;
    if(!window.confirm(`確定刪除 ${t.visitDate} 的草稿行程？刪除後將不再出現在一般查詢與報表中。`))return;
    setBusy(true);setMsg("");
    try{
      await api(`/trips/${t.visitTripId}`,{method:"DELETE",headers:{"If-Match":t.rowVersion}});
      if(detail?.visitTripId===t.visitTripId)setDetail(null);
      setMsg("草稿已刪除。");
      await load();
    }catch(e){setMsg(e instanceof Error?e.message:"刪除草稿失敗")}
    finally{setBusy(false)}
  };

  return <>
    <div className="card"><div className="section-title"><div><h2>我的歷史行程</h2><div className="sub">可依日期區間、拜訪地點及狀態查詢；查詢結果可下載為 Excel。</div></div><button className="btn secondary" onClick={download}>下載查詢結果</button></div>
      <div className="grid cols-4"><div className="field"><label>開始日期</label><input type="date" value={start} onChange={e=>setStart(e.target.value)}/></div><div className="field"><label>結束日期</label><input type="date" value={end} onChange={e=>setEnd(e.target.value)}/></div><div className="field"><label>拜訪地點</label><input value={keyword} onChange={e=>setKeyword(e.target.value)} placeholder="名稱、地址或關鍵字"/></div><div className="field"><label>狀態</label><select value={status} onChange={e=>setStatus(e.target.value)}><option value="">全部狀態</option><option value="Approved">已核准</option><option value="Returned">已退回</option><option value="Submitted">已送出</option><option value="PendingApproval">待核准</option><option value="Draft">草稿</option></select></div></div>
      <div className="actions" style={{marginBottom:16}}><button className="btn" onClick={()=>void load()}>查詢</button><button className="btn outline" onClick={()=>{const e=new Date().toISOString().slice(0,10);setStart("2026-01-01");setEnd(e);setKeyword("");setStatus("");api<Trip[]>(`/trips/history?startDate=2026-01-01&endDate=${e}`).then(setRows).catch(x=>setMsg(x.message))}}>清除條件</button></div>
      {msg&&<div className={`note ${msg.includes("刪除。")?"ok-note":"danger-note"}`}>{msg}</div>}
      <div className="table-wrap"><table><thead><tr><th>日期</th><th>地點</th><th>出發</th><th>結束</th><th>自算</th><th>系統</th><th>核定</th><th>每公里補助</th><th>補助金額</th><th>狀態</th><th>操作</th></tr></thead><tbody>{filtered.map(t=><tr key={t.visitTripId}><td>{t.visitDate}</td><td>{t.stops.map(s=>s.locationName).join("、")}</td><td>{(t.startTime||"—").slice(0,5)}</td><td>{(t.endTime||"—").slice(0,5)}</td><td>{t.claimedDistanceKm??"—"}</td><td>{t.systemDistanceKm??"—"}</td><td>{t.approvedDistanceKm??"—"}</td><td>{t.ratePerKmSnapshot==null?"—":`$${money(t.ratePerKmSnapshot)}`}</td><td className="subsidy-strong">{t.approvedAmount==null?"—":`$${money(t.approvedAmount)}`}</td><td><span className={`pill ${t.status==="Approved"?"ok":t.status==="Returned"?"danger":"warn"}`}>{t.statusName}</span>{t.returnReason&&<div className="muted">{t.returnReason}</div>}</td><td><div className="actions"><button className="btn small outline" onClick={()=>setDetail(t)}>查看</button>{(t.status==="Draft"||t.status==="Returned")&&<button className="btn small secondary" onClick={()=>nav(`/?edit=${t.visitTripId}`)}>修改</button>}{t.status==="Draft"&&<button className="btn small outline" style={{color:"#b42318"}} disabled={busy} onClick={()=>void deleteDraft(t)}>刪除</button>}</div></td></tr>)}</tbody></table></div>
      {!filtered.length&&<div className="empty">查無符合條件的歷史行程。</div>}
    </div>

    {detail&&<div className="modal" onMouseDown={e=>{if(e.target===e.currentTarget)setDetail(null)}}><div className="modal-panel"><h3>歷史行程明細</h3><div className="note">{detail.visitDate}｜{(detail.startTime||"").slice(0,5)}–{(detail.endTime||"").slice(0,5)}｜{detail.statusName}</div><p><strong>拜訪路線：</strong>{detail.stops.map(s=>s.locationName).join(" → ")}</p><p><strong>外訪員自算：</strong>{detail.claimedDistanceKm??"—"} km<br/><strong>系統里程：</strong>{detail.systemDistanceKm??"—"} km<br/><strong>小組長核定：</strong>{detail.approvedDistanceKm??"—"} km</p><p><strong>每公里補助：</strong>{detail.ratePerKmSnapshot==null?"—":`$${money(detail.ratePerKmSnapshot)}`}<br/><strong>補助金額：</strong><span className="subsidy-strong">{detail.approvedAmount==null?"—":`$${money(detail.approvedAmount)}`}</span></p>{detail.notes&&<p><strong>備註：</strong>{detail.notes}</p>}<div className="actions"><button className="btn outline" onClick={()=>setDetail(null)}>關閉</button>{(detail.status==="Draft"||detail.status==="Returned")&&<button className="btn" onClick={()=>nav(`/?edit=${detail.visitTripId}`)}>修改／重送</button>}{detail.status==="Draft"&&<button className="btn outline" style={{color:"#b42318"}} disabled={busy} onClick={()=>void deleteDraft(detail)}>刪除草稿</button>}</div></div></div>}
  </>;
}
