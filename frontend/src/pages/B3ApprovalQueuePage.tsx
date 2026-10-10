import{useEffect,useState}from'react';
import{api}from'../api';
type Item={requestPublicId:string;entityId:string;teamId:number|null;
riskCode:string;status:string;requestedByUserId:number;submittedAt:string;
beforeJson:string|null;proposedJson:string;rowVersion:string};
export default function B3ApprovalQueuePage(){
 const[rows,setRows]=useState<Item[]>([]),[error,setError]=useState(''),[busy,setBusy]=useState(false);
 const load=async()=>setRows(await api<Item[]>('/change-requests/admin/pending'));
 useEffect(()=>{void load().catch(e=>setError(e instanceof Error?e.message:'載入失敗'))},[]);
 const reject=async(x:Item)=>{
  const reason=window.prompt('請輸入駁回原因（必填）');
  if(!reason?.trim())return;
  setBusy(true);setError('');
  try{await api('/change-requests/admin/'+x.requestPublicId+'/reject',
   {method:'POST',body:JSON.stringify({requestRowVersion:x.rowVersion,
    decisionKey:crypto.randomUUID(),reason:reason.trim()})});await load()}
  catch(e){setError(e instanceof Error?e.message:'駁回失敗')}
  finally{setBusy(false)}
 };
 return <div className="card"><h2>B3｜異動審核（受控候選）</h2>
  <div className="note">完整核准／套用仍被安全鎖定；未完成 Schema、Owner 管理權限及原子性驗證前不得啟用。</div>
  {error&&<div role="alert" className="note danger-note">{error}</div>}
  <div className="table-wrap"><table><thead><tr><th>申請</th><th>地點</th><th>小組</th><th>時間</th><th>操作</th></tr></thead>
  <tbody>{rows.map(x=><tr key={x.requestPublicId}><td>{x.requestPublicId.slice(0,8)}</td>
    <td>{x.entityId}</td><td>{x.teamId??'—'}</td><td>{x.submittedAt}</td>
    <td><details><summary>修改前後</summary><pre>{x.beforeJson}</pre><pre>{x.proposedJson}</pre></details>
      <button className="btn small secondary" disabled={busy} onClick={()=>void reject(x)}>駁回</button>
      <button className="btn small" disabled title="核准執行尚未授權">核准（停用）</button>
    </td></tr>)}</tbody></table></div>
    {!rows.length&&<div className="empty compact-empty">目前無待審申請。</div>}
 </div>
}
