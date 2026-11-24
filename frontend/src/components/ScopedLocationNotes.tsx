import {useEffect,useState} from "react";
import {api} from "../api";
import type {LocationMaintenance,SmartLocationItem} from "../types";

export default function ScopedNotes({row,teams,onClose}:{row:Pick<SmartLocationItem,'locationId'|'locationName'|'address'|'plusCode'>;teams:{teamId:number;teamName:string;isPrimary:boolean}[];onClose:()=>void}){
 const [teamId,setTeamId]=useState<number|undefined>(teams.find(t=>t.isPrimary)?.teamId??teams[0]?.teamId);
 const [data,setData]=useState<LocationMaintenance|null>(null);
 const [note,setNote]=useState(""),[error,setError]=useState(""),[message,setMessage]=useState(""),[busy,setBusy]=useState(false);
 useEffect(()=>{
  let active=true;setData(null);setError("");setMessage("");setNote("");
  if(!teamId){setError("目前沒有可用的備註小組，請先選擇行程所屬小組。");return;}
  if(teams.some(t=>t.teamId===teamId))void api<LocationMaintenance>(`/locations/${row.locationId}/maintenance?teamId=${teamId}`).then(value=>{if(active)setData(value)}).catch(e=>{if(active)setError(e instanceof Error?e.message:"備註讀取失敗")});
  return()=>{active=false};
 },[row.locationId,teamId]);
 const save=async()=>{
  if(busy||!data||!data.isActive||data.duplicateOfLocationId||!teamId||!teams.some(t=>t.teamId===teamId)||!note.trim()||note.trim().length>1000)return;
  setBusy(true);setError("");setMessage("");
  try{
   const next=await api<LocationMaintenance>(`/locations/${row.locationId}/notes`,{method:"POST",body:JSON.stringify({teamId,note:note.trim(),changeReason:null})});
   setData(next);setNote("");setMessage("備註已新增，無須審核。");
  }catch(e){setError((e instanceof Error?e.message:"備註儲存失敗")+"；若結果不明，請關閉後重新查看備註歷史，勿直接重送。");}finally{setBusy(false);}
 };
 return <div className="modal"><div className="modal-panel" role="dialog" aria-modal="true" aria-label="地點備註"><button className="btn small outline modal-close" disabled={busy} onClick={onClose}>關閉</button><h3>地點備註｜{row.locationName}</h3><div className="sub">{row.address||row.plusCode||"—"}</div>
 <div className="field"><label>備註所屬小組<select value={teamId??""} disabled={busy} onChange={e=>setTeamId(Number(e.target.value))}>{teams.map(t=><option key={t.teamId} value={t.teamId}>{t.teamName}</option>)}</select></label></div>
 {error&&<div role="alert" className="note danger-note">{error}</div>}{message&&<div role="status" className="note ok-note">{message}</div>}
 {data?<><div className="note">一般拜訪備註不需審核，僅供授權小組查看。請勿輸入敏感個人資料；正式地點名稱、地址及統編不會變更。</div>
 {data.masterNote&&<div><strong>主檔備註</strong><p style={{whiteSpace:"pre-wrap"}}>{data.masterNote}</p></div>}
 <div className="field"><label>新增備註<textarea value={note} disabled={busy} maxLength={1000} onChange={e=>setNote(e.target.value)} placeholder="例如：入口位置、停車資訊、拜訪注意事項"/></label></div>
 <button className="btn ok" disabled={busy||!data.isActive||!!data.duplicateOfLocationId||!note.trim()} onClick={()=>void save()}>儲存備註</button>
 <hr/><h4>備註歷史</h4><div className="route-list">{data.notes.map(n=><div key={n.historyId} className="route-item"><div><strong>{n.changedBy}</strong><div className="sub">{new Date(n.changedAt).toLocaleString("zh-TW",{timeZone:"Asia/Taipei"})}｜{n.teamName}</div><div style={{whiteSpace:"pre-wrap"}}>{n.note}</div></div></div>)}{!data.notes.length&&<div className="empty compact-empty">尚無備註。</div>}</div></>:!error&&<div className="note">載入中…</div>}
 </div></div>;
}
