export type BatchCorrection = {correctionRequestId:number;tripNo:string;status:string;rowVersion:string;changes:readonly unknown[]};
export async function reviewSelectedCorrections<T extends BatchCorrection>(
 rows:readonly T[],selected:readonly number[],approve:boolean,comment:string,
 send:(row:T,approve:boolean,comment:string)=>Promise<unknown>
){
 const note=comment.trim(),ids=new Set(selected);
 if(!ids.size)throw new Error("請先選取更正申請。");
 if(!approve&&!note)throw new Error("不核准原因必填。");
 const targets=rows.filter(r=>ids.has(r.correctionRequestId));
 if(targets.length!==ids.size||new Set(targets.map(r=>r.correctionRequestId)).size!==ids.size||targets.some(r=>r.status!=="PendingLeaderReview"||!r.rowVersion||approve&&!r.changes.length))throw new Error("選取案件狀態已變更、缺少版本或差異明細，請重新整理。");
 let success=0;const unconfirmed:string[]=[];
 for(const row of targets){
  try{await send(row,approve,note);success++;}
  catch(e){unconfirmed.push(row.tripNo+"："+(e instanceof Error?e.message:"結果未確認"));break;}
 }
 return {success,unconfirmed,notRun:targets.length-success-unconfirmed.length};
}
