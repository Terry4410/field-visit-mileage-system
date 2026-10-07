import{useState}from"react";
import{api,apiDownload}from"../api";

type PreviewItem={rowNumber:number;sheet:string;key:string;action:string;status:string;errorMessage?:string};
type Preview={totalCount:number;validCount:number;errorCount:number;items:PreviewItem[]};
type Result={appliedCount:number;noChangeCount:number;failedCount:number;errors:string[]};

type Props={
 title:string;
 description:string;
 templateUrl:string;
 previewUrl:string;
 confirmUrl:string;
 templateFilename:string;
 onConfirmed?:()=>void;
};

export default function PeopleManagementBulkPanel(props:Props){
 const[file,setFile]=useState<File|null>(null),[preview,setPreview]=useState<Preview|null>(null),[result,setResult]=useState<Result|null>(null),[msg,setMsg]=useState(""),[busy,setBusy]=useState(false);
 const upload=async(url:string)=>{
  if(!file)throw new Error("請先選擇 Excel 檔案。");
  const form=new FormData();form.append("file",file);
  return await api<any>(url,{method:"POST",body:form},120000);
 };
 const doPreview=async()=>{setBusy(true);setMsg("");setResult(null);try{setPreview(await upload(props.previewUrl))}catch(e){setMsg(e instanceof Error?e.message:"預覽失敗")}finally{setBusy(false)}};
 const confirm=async()=>{
  if(!preview||preview.errorCount>0)return setMsg("預覽仍有錯誤，請修正 Excel 後重新預覽。");
  if(!window.confirm(`確認套用 ${preview.validCount} 筆有效資料？系統只會修改本區說明的資料範圍。`))return;
  setBusy(true);setMsg("");try{const r=await upload(props.confirmUrl) as Result;setResult(r);setPreview(null);setMsg(`批次處理完成：套用 ${r.appliedCount}、無異動 ${r.noChangeCount}、失敗 ${r.failedCount}。`);props.onConfirmed?.()}catch(e){setMsg(e instanceof Error?e.message:"確認匯入失敗")}finally{setBusy(false)}
 };
 return <div className="card">
  <div className="section-title"><div><h2>{props.title}</h2><div className="sub">{props.description}</div></div><button className="btn small outline" disabled={busy} onClick={()=>void apiDownload(props.templateUrl,props.templateFilename)}>下載 Excel 範本</button></div>
  <div className="field"><label>Excel 檔案</label><input type="file" accept=".xlsx" onChange={e=>{setFile(e.target.files?.[0]||null);setPreview(null);setResult(null);setMsg("")}}/></div>
  <div className="actions"><button className="btn secondary" disabled={busy||!file} onClick={()=>void doPreview()}>{busy?"處理中…":"預覽檢核"}</button>{preview&&<button className="btn ok" disabled={busy||preview.errorCount>0} onClick={()=>void confirm()}>確認套用</button>}</div>
  {msg&&<div className={`note ${result&&result.failedCount===0?"ok-note":""}`} style={{marginTop:10}}>{msg}</div>}
  {preview&&<><div className="grid cols-3" style={{marginTop:14}}><div className="card stat"><div className="label">總筆數</div><div className="value">{preview.totalCount}</div></div><div className="card stat"><div className="label">有效</div><div className="value">{preview.validCount}</div></div><div className="card stat"><div className="label">錯誤</div><div className="value">{preview.errorCount}</div></div></div><div className="table-wrap"><table><thead><tr><th>列</th><th>Sheet</th><th>Key</th><th>動作</th><th>狀態</th><th>訊息</th></tr></thead><tbody>{preview.items.map((x,i)=><tr key={`${x.sheet}-${x.rowNumber}-${i}`}><td>{x.rowNumber}</td><td>{x.sheet}</td><td>{x.key}</td><td>{x.action}</td><td>{x.status}</td><td>{x.errorMessage||"—"}</td></tr>)}</tbody></table></div></>}
  {result&&result.errors.length>0&&<div className="note danger-note" style={{marginTop:10}}>{result.errors.join("；")}</div>}
 </div>;
}
