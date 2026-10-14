import { useEffect, useState } from "react";
import { OPTIONC_LEADER_REVIEW_UI_ENABLED } from "../optionc-approval-flags";

type Review = { requestPublicId:string; entityId:string; teamId:number|null; operationCode:string; riskCode:string; requestStatus:string; leaderStatus:string; requestedByUserId:number; submittedAt:string; proposedJson:string; rowVersion:string; reason?:string };

export default function LeaderLocationReviewPage(){
  const [rows,setRows]=useState<Review[]>([]);
  const [message,setMessage]=useState("");
  const load=async()=>{ if(!OPTIONC_LEADER_REVIEW_UI_ENABLED)return; const r=await fetch("/api/v1/optionc-candidate/leader/review-queue"); if(!r.ok){setMessage("目前無法取得初審佇列。");return;} setRows(await r.json()); };
  useEffect(()=>{void load()},[]);
  if(!OPTIONC_LEADER_REVIEW_UI_ENABLED)
    return <div className="panel"><h2>地點變更初審</h2><p>地點申請初審尚未授權啟用。</p></div>;
  const decide=async(row:Review,approve:boolean)=>{
    const reason=window.prompt(approve?"核准理由":"退回理由","")?.trim();
    if(!reason)return;
    const r=await fetch(`/api/v1/optionc-candidate/requests/${row.requestPublicId}/leader-review`,{method:"POST",headers:{"Content-Type":"application/json"},body:JSON.stringify({requestRowVersion:row.rowVersion,decisionKey:crypto.randomUUID(),reason,approve})});
    setMessage(r.ok?(approve?"已完成 Leader 初審；高風險案件仍須獨立 Admin 最終核准。":"已退回申請。"):"初審失敗，請重新整理後確認授權與版本。");
    if(r.ok)void load();
  };
  return <div className="panel"><h2>地點變更初審</h2>{message&&<p>{message}</p>}{rows.length===0&&<p>目前沒有待處理申請。</p>}{rows.map(row=><article key={row.requestPublicId} className="card"><strong>Location {row.entityId}</strong><span>風險：{row.riskCode}｜狀態：{row.requestStatus}/{row.leaderStatus}</span><pre>{row.proposedJson}</pre><button className="btn" onClick={()=>void decide(row,true)}>核准初審</button><button className="btn secondary" onClick={()=>void decide(row,false)}>退回</button></article>)}</div>;
}
