import{useEffect,useState}from"react";
import{api}from"../api";
import SmartLocationPicker from"../components/SmartLocationPicker";
import type{SmartLocationItem}from"../types";
import{
 TEAM_LOCATION_NOTE_TEAMS_API,
 teamLocationNoteClearPath,
 teamLocationNoteCreatePath,
 teamLocationNoteReadPath,
 teamLocationNoteStateLabel,
 teamLocationNoteUpdatePath
}from"../team-location-note";
import type{TeamLocationNote,TeamLocationNoteTeam}from"../team-location-note";

export default function TeamLocationNotesPage(){
 const[teams,setTeams]=useState<TeamLocationNoteTeam[]>([]),[teamId,setTeamId]=useState("");
 const[location,setLocation]=useState<SmartLocationItem|null>(null),[row,setRow]=useState<TeamLocationNote|null>(null);
 const[note,setNote]=useState(""),[reason,setReason]=useState(""),[msg,setMsg]=useState(""),[busy,setBusy]=useState(false);
 useEffect(()=>{api<TeamLocationNoteTeam[]>(TEAM_LOCATION_NOTE_TEAMS_API).then(x=>{setTeams(x);setTeamId(v=>v||String(x[0]?.teamId||""))}).catch(e=>setMsg(e instanceof Error?e.message:"無法取得可維護小組"))},[]);
 useEffect(()=>{setLocation(null);setRow(null);setNote("");setReason("");setMsg("")},[teamId]);
 const load=async(selected=location)=>{if(!teamId||!selected)return;setBusy(true);setMsg("");try{const current=await api<TeamLocationNote>(teamLocationNoteReadPath(Number(teamId),selected.locationId));setRow(current);setNote(current.note||"")}catch(e){setMsg(e instanceof Error?e.message:"備註載入失敗")}finally{setBusy(false)}};
 const choose=(selected:SmartLocationItem)=>{setLocation(selected);setRow(null);setNote("");setReason("");void load(selected)};
 const save=async()=>{if(!location||!teamId)return;setBusy(true);setMsg("");try{let next:TeamLocationNote;if(row?.state==="NeverExisted"||!row){next=await api<TeamLocationNote>(teamLocationNoteCreatePath(Number(teamId)),{method:"POST",body:JSON.stringify({locationId:location.locationId,note,changeReason:reason||null})})}else{if(!row.teamLocationNoteId||!row.rowVersion)throw new Error("目前備註缺少 RowVersion，請重新載入。");next=await api<TeamLocationNote>(teamLocationNoteUpdatePath(row.teamLocationNoteId),{method:"PUT",body:JSON.stringify({note,changeReason:reason||null,rowVersion:row.rowVersion})})}setRow(next);setNote(next.note||"");setReason("");setMsg(row?.state==="Cleared"?"小組地點備註已恢復。":row?.state==="Active"?"小組地點備註已更新。":"小組地點備註已建立。") }catch(e){setMsg(e instanceof Error?e.message:"儲存失敗")}finally{setBusy(false)}};
 const clear=async()=>{if(!row?.teamLocationNoteId||!row.rowVersion)return;setBusy(true);setMsg("");try{const next=await api<TeamLocationNote>(teamLocationNoteClearPath(row.teamLocationNoteId),{method:"POST",body:JSON.stringify({changeReason:reason||null,rowVersion:row.rowVersion})});setRow(next);setNote("");setReason("");setMsg("小組地點備註已清除。") }catch(e){setMsg(e instanceof Error?e.message:"清除失敗")}finally{setBusy(false)}};
 return <>
  <div className="card">
   <div className="section-title"><div><h2>小組地點備註</h2><div className="sub">備註屬於小組與地點的組合；清除後保留同一筆資料與歷程，不會刪除。</div></div></div>
   {msg&&<div className="note" role="status">{msg}</div>}
   <div className="field"><label>小組</label><select value={teamId} onChange={e=>setTeamId(e.target.value)}><option value="">請選擇</option>{teams.map(t=><option key={t.teamId} value={t.teamId}>{t.teamCode}｜{t.teamName}</option>)}</select></div>
   {teamId&&<SmartLocationPicker teamId={Number(teamId)} selectedLocationId={location?.locationId} onSelect={choose}/>} 
  </div>
  {location&&<div className="card" style={{marginTop:18}}>
   <div className="section-title"><div><h2>{location.locationName}</h2><div className="sub">{location.address||location.plusCode||"未提供地址"}</div></div><span className="pill">{row?teamLocationNoteStateLabel(row.state):"載入中"}</span></div>
   <div className="field"><label>{row?.state==="Cleared"?"恢復備註":"備註"}</label><textarea value={note} onChange={e=>setNote(e.target.value)} placeholder="輸入此小組在這個地點需要知道的工作備註"/></div>
   <div className="field"><label>異動原因 <span className="optional">選填</span></label><input value={reason} onChange={e=>setReason(e.target.value)} placeholder="例如：聯絡窗口更新"/></div>
   <div className="actions">
    <button className="btn ok" disabled={busy||!note.trim()} onClick={()=>void save()}>{row?.state==="Cleared"?"恢復備註":row?.state==="Active"?"更新備註":"建立備註"}</button>
    {row?.state==="Active"&&<button className="btn danger" disabled={busy} onClick={()=>void clear()}>清除備註</button>}
    <button className="btn secondary" disabled={busy} onClick={()=>void load()}>重新載入</button>
   </div>
  </div>}
 </>;
}
