import{useEffect,useState}from"react";
import{api}from"../api";
import{teamLocationNoteReadPath,teamLocationNoteStateLabel}from"../team-location-note";
import type{TeamLocationNote}from"../team-location-note";

export default function TeamLocationNoteViewer({teamId,locationId}:{teamId:number;locationId:number}){
 const[row,setRow]=useState<TeamLocationNote|null>(null),[error,setError]=useState("");
 useEffect(()=>{let active=true;setRow(null);setError("");api<TeamLocationNote>(teamLocationNoteReadPath(teamId,locationId)).then(x=>{if(active)setRow(x)}).catch(e=>{if(active)setError(e instanceof Error?e.message:"小組地點備註載入失敗")});return()=>{active=false}},[teamId,locationId]);
 if(error)return <div className="note warn-note" style={{marginTop:10}}>小組地點備註：{error}</div>;
 if(!row)return <div className="note" style={{marginTop:10}}>小組地點備註：載入中…</div>;
 return <div className="note" style={{marginTop:10}}><strong>小組地點備註：</strong>{row.state==="Active"?row.note:teamLocationNoteStateLabel(row.state)}</div>;
}
