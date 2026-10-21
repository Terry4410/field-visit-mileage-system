import{useState}from"react";
import{api,apiDownload}from"../api";
import type{ImportConfirmResult,ImportPreview}from"../types";
import{OPTIONC_EXCEL_CREATE_UI_ENABLED,OPTIONC_EXCEL_UPDATE_UI_ENABLED}from"../optionc-approval-flags";
import{mayShowLegacyExcelConfirm,maySubmitCanonicalExcelCreates,maySubmitCanonicalExcelUpdates,type ExcelCanonicalCreateResult,type ExcelCanonicalRowLink}from"../optionc-excel-import-ui";

export function OptionCExcelActions({type,preview,busy,onCreate,onUpdate}:{type:"locations"|"projects";preview:ImportPreview;busy:boolean;onCreate:()=>void;onUpdate:()=>void}) {
 return <>{type==="locations"&&OPTIONC_EXCEL_CREATE_UI_ENABLED&&maySubmitCanonicalExcelCreates(preview)&&<button className="btn secondary" disabled={busy} onClick={onCreate}>提交 Excel 新增地點獨立審查（不發布）</button>}{type==="locations"&&OPTIONC_EXCEL_UPDATE_UI_ENABLED&&maySubmitCanonicalExcelUpdates(preview)&&<button className="btn secondary" disabled={busy} onClick={onUpdate}>提交 Excel 已發布地點更新審查（不直接覆寫）</button>}</>;
}

export default function AdminImportPanel({type,onDone,reviewOnly=false}:{type:"locations"|"projects";onDone:()=>void;reviewOnly?:boolean}){
 const[file,setFile]=useState<File|null>(null),[preview,setPreview]=useState<ImportPreview|null>(null),[result,setResult]=useState<ImportConfirmResult|null>(null),[msg,setMsg]=useState(""),[busy,setBusy]=useState(false);
 const[review,setReview]=useState<ExcelCanonicalCreateResult|null>(null),[links,setLinks]=useState<ExcelCanonicalRowLink[]>([]);
 const template=()=>apiDownload(`/imports/${type}/template`,type==="locations"?"地點主檔匯入範例.xlsx":"專案主檔匯入範例.xlsx");
 const doPreview=async()=>{if(!file)return setMsg("請先選擇 .xlsx、.xls 或 .csv 檔案。");setBusy(true);setMsg("");try{const form=new FormData();form.append("file",file);setPreview(await api<ImportPreview>(`/imports/${type}/preview`,{method:"POST",body:form},120000));setResult(null);setReview(null);setLinks([])}catch(e){setMsg(e instanceof Error?e.message:"預覽失敗")}finally{setBusy(false)}};
 const refreshLinks=async()=>{
  if(!preview)return;
  setLinks(await api<ExcelCanonicalRowLink[]>(
   `/optionc-candidate/imports/${encodeURIComponent(preview.importBatchId)}/request-links`,
   {method:"GET"},120000));
 };
 const submitForReview=async()=>{
  if(!preview||!OPTIONC_EXCEL_CREATE_UI_ENABLED||!maySubmitCanonicalExcelCreates(preview))return;
  setBusy(true);setMsg("");
  try{
   const reviewed=await api<ExcelCanonicalCreateResult>(
    `/optionc-candidate/imports/${encodeURIComponent(preview.importBatchId)}/submit-creates-for-review`,
    {method:"POST"},120000);
   setReview(reviewed);
   await refreshLinks();
   setMsg(`已提交 ${reviewed.submitted} 筆地點建立審查；先前提交 ${reviewed.alreadySubmitted} 筆。尚未核准或發布。`);
  }catch(e){try{await refreshLinks()}catch{/* Fail closed; do not infer success. */}setMsg(`${e instanceof Error?e.message:"提交審查失敗"}；已嘗試重新查詢已提交的每列申請，請確認後再重試。`)}
  finally{setBusy(false)}
 };
 const submitUpdatesForReview=async()=>{
  if(!preview||!OPTIONC_EXCEL_UPDATE_UI_ENABLED||!maySubmitCanonicalExcelUpdates(preview))return;
  setBusy(true);setMsg("");
  try{
   const reviewed=await api<ExcelCanonicalCreateResult>(
    `/optionc-candidate/imports/${encodeURIComponent(preview.importBatchId)}/submit-updates-for-review`,
    {method:"POST"},120000);
   setReview(reviewed);
   await refreshLinks();
   setMsg(`已提交 ${reviewed.submitted} 筆已發布地點更新審查；重試恢復 ${reviewed.alreadySubmitted} 筆。尚未發布。`);
  }catch(e){try{await refreshLinks()}catch{/* Keep original failure visible. */}setMsg(`${e instanceof Error?e.message:"提交更新審查失敗"}；已嘗試重新查詢已提交的每列申請，請確認後再重試。`)}
  finally{setBusy(false)}
 };
 const confirm=async()=>{if(!preview)return;setBusy(true);try{const r=await api<ImportConfirmResult>(`/imports/${preview.importBatchId}/confirm`,{method:"POST"},120000);setResult(r);setMsg("匯入完成。");onDone()}catch(e){setMsg(e instanceof Error?e.message:"匯入失敗")}finally{setBusy(false)}};
 return <div className="import-panel"><div className="section-title"><strong>{reviewOnly?"地點 Excel Canonical 送審":"批次匯入"}</strong><button className="btn small outline" onClick={()=>void template()}>下載 Excel 範例</button></div>{reviewOnly&&<div className="note">Excel Create／已發布地點 Update 僅建立獨立審查申請，不會直接覆寫或發布；混合動作與不完整預覽會拒絕送審。</div>}<div className="actions"><input type="file" accept=".xlsx,.xls,.csv" onChange={e=>{setFile(e.target.files?.[0]||null);setPreview(null)}}/><button className="btn secondary" disabled={busy} onClick={()=>void doPreview()}>預覽與驗證</button></div>{msg&&<div className="note">{msg}</div>}{preview&&<><div className={`note ${preview.errorCount?"danger-note":"ok-note"}`}>共 {preview.totalCount} 列｜可匯入 {preview.validCount}｜錯誤 {preview.errorCount}{preview.totalCount>100?"｜畫面僅顯示前 100 筆預覽，系統已驗證全部資料":""}</div><div className="table-wrap compact-table"><table><thead><tr><th>列</th><th>類型</th><th>動作</th><th>Key</th><th>結果</th></tr></thead><tbody>{preview.items.slice(0,100).map((x,i)=><tr key={i}><td>{x.rowNumber}</td><td>{x.entityType}</td><td>{x.action}</td><td>{x.displayKey}</td><td>{x.errorMessage||"OK"}</td></tr>)}</tbody></table></div><div className="actions">{preview.errorCount>0&&<button className="btn outline" onClick={()=>void apiDownload(`/imports/${preview.importBatchId}/errors.xlsx`,"匯入錯誤.xlsx")}>下載錯誤 Excel</button>}{mayShowLegacyExcelConfirm(reviewOnly)&&<button className="btn ok" disabled={preview.errorCount>0||busy} onClick={()=>void confirm()}>確認匯入</button>}<OptionCExcelActions type={type} preview={preview} busy={busy} onCreate={()=>void submitForReview()} onUpdate={()=>void submitUpdatesForReview()}/></div></>}{result&&<div className="note ok-note">新增 {result.created}／更新 {result.updated}／無變更 {result.unchanged}／失敗 {result.failed}</div>}{review&&<div className="note ok-note"><strong>已送交 canonical 審查，尚未核准或發布：</strong>申請 {review.requestPublicIds.map(id=>id.slice(0,8)).join("、")}。所有異動須獨立 Leader 初審；高風險另需 Admin 最終核准。</div>}{links.length>0&&<div className="table-wrap compact-table"><table><thead><tr><th>Excel 列</th><th>申請 ID</th><th>操作</th><th>風險</th><th>申請狀態</th></tr></thead><tbody>{links.map(x=><tr key={x.importBatchItemId}><td>{x.rowNumber}</td><td>{x.requestPublicId.slice(0,8)}</td><td>{x.operationCode}</td><td>{x.riskCode}</td><td>{x.requestStatus}</td></tr>)}</tbody></table><button className="btn small outline" disabled={busy} onClick={()=>void refreshLinks()}>重新查詢每列申請狀態</button></div>}</div>
}
