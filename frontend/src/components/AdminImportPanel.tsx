import{useState}from"react";
import{api,apiDownload}from"../api";
import type{ImportConfirmResult,ImportPreview}from"../types";
import{OPTIONC_EXCEL_CREATE_UI_ENABLED}from"../optionc-approval-flags";
import{maySubmitCanonicalExcelCreates,type ExcelCanonicalCreateResult}from"../optionc-excel-import-ui";

export default function AdminImportPanel({type,onDone}:{type:"locations"|"projects";onDone:()=>void}){
 const[file,setFile]=useState<File|null>(null),[preview,setPreview]=useState<ImportPreview|null>(null),[result,setResult]=useState<ImportConfirmResult|null>(null),[msg,setMsg]=useState(""),[busy,setBusy]=useState(false);
 const[review,setReview]=useState<ExcelCanonicalCreateResult|null>(null);
 const template=()=>apiDownload(`/imports/${type}/template`,type==="locations"?"地點主檔匯入範例.xlsx":"專案主檔匯入範例.xlsx");
 const doPreview=async()=>{if(!file)return setMsg("請先選擇 .xlsx、.xls 或 .csv 檔案。");setBusy(true);setMsg("");try{const form=new FormData();form.append("file",file);setPreview(await api<ImportPreview>(`/imports/${type}/preview`,{method:"POST",body:form},120000));setResult(null);setReview(null)}catch(e){setMsg(e instanceof Error?e.message:"預覽失敗")}finally{setBusy(false)}};
 const submitForReview=async()=>{
  if(!preview||!OPTIONC_EXCEL_CREATE_UI_ENABLED||!maySubmitCanonicalExcelCreates(preview))return;
  setBusy(true);setMsg("");
  try{
   const reviewed=await api<ExcelCanonicalCreateResult>(
    `/optionc-candidate/imports/${encodeURIComponent(preview.importBatchId)}/submit-creates-for-review`,
    {method:"POST"},120000);
   setReview(reviewed);
   setMsg(`已提交 ${reviewed.submitted} 筆地點建立審查；先前提交 ${reviewed.alreadySubmitted} 筆。尚未核准或發布。`);
  }catch(e){setMsg(`${e instanceof Error?e.message:"提交審查失敗"}；若結果不明，請先查詢申請紀錄。`)}
  finally{setBusy(false)}
 };
 const confirm=async()=>{if(!preview)return;setBusy(true);try{const r=await api<ImportConfirmResult>(`/imports/${preview.importBatchId}/confirm`,{method:"POST"},120000);setResult(r);setMsg("匯入完成。");onDone()}catch(e){setMsg(e instanceof Error?e.message:"匯入失敗")}finally{setBusy(false)}};
 return <div className="import-panel"><div className="section-title"><strong>批次匯入</strong><button className="btn small outline" onClick={()=>void template()}>下載 Excel 範例</button></div><div className="actions"><input type="file" accept=".xlsx,.xls,.csv" onChange={e=>{setFile(e.target.files?.[0]||null);setPreview(null)}}/><button className="btn secondary" disabled={busy} onClick={()=>void doPreview()}>預覽與驗證</button></div>{msg&&<div className="note">{msg}</div>}{preview&&<><div className={`note ${preview.errorCount?"danger-note":"ok-note"}`}>共 {preview.totalCount} 列｜可匯入 {preview.validCount}｜錯誤 {preview.errorCount}{preview.totalCount>100?"｜畫面僅顯示前 100 筆預覽，系統已驗證全部資料":""}</div><div className="table-wrap compact-table"><table><thead><tr><th>列</th><th>類型</th><th>動作</th><th>Key</th><th>結果</th></tr></thead><tbody>{preview.items.slice(0,100).map((x,i)=><tr key={i}><td>{x.rowNumber}</td><td>{x.entityType}</td><td>{x.action}</td><td>{x.displayKey}</td><td>{x.errorMessage||"OK"}</td></tr>)}</tbody></table></div><div className="actions">{preview.errorCount>0&&<button className="btn outline" onClick={()=>void apiDownload(`/imports/${preview.importBatchId}/errors.xlsx`,"匯入錯誤.xlsx")}>下載錯誤 Excel</button>}<button className="btn ok" disabled={preview.errorCount>0||busy} onClick={()=>void confirm()}>確認匯入</button>{type==="locations"&&OPTIONC_EXCEL_CREATE_UI_ENABLED&&maySubmitCanonicalExcelCreates(preview)&&<button className="btn secondary" disabled={busy} onClick={()=>void submitForReview()}>提交 Excel 新增地點獨立審查（不發布）</button>}</div></>}{result&&<div className="note ok-note">新增 {result.created}／更新 {result.updated}／無變更 {result.unchanged}／失敗 {result.failed}</div>}{review&&<div className="note ok-note"><strong>已送交 canonical 審查，尚未核准或發布：</strong>申請 {review.requestPublicIds.map(id=>id.slice(0,8)).join("、")}。高風險須獨立 Leader 初審及 Admin 複審。</div>}</div>
}
