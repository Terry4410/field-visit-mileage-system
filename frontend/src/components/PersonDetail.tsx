import {useEffect,useState} from "react";
import {api} from "../api";
import type {V170PeopleRow,V170PersonDetail} from "../types";
import {employmentStatusLabel} from "../employment-status";
import {businessAccessLabel,completenessWarnings} from "../people-completeness";
import ReadOnlyDetail from "./ReadOnlyDetail";

const roleLabels:Record<string,string>={visitor:"外訪員",leader:"小組長",admin:"管理者",supervisor:"督導"};
type DetailRole={roleCode:string;isCurrent:boolean};
type DetailTeam={teamId:number;teamCode:string;teamName:string;isPrimary:boolean;isCurrent:boolean};
export function PersonDetailButton({row,field,onOpen}:{row:V170PeopleRow;field:"code"|"name";onOpen:(row:V170PeopleRow)=>void}){
 return <button type="button" className="btn small outline" aria-label={`查看人員明細 ${row.employeeNo||row.userCode} ${row.displayName}`} onClick={()=>onOpen(row)}>{field==="code"?row.employeeNo||row.userCode:row.displayName}</button>;
}

export function resolvePersonSummary(row:V170PeopleRow,detail:V170PersonDetail):V170PeopleRow{
 if(row.userId!==detail.userId||row.userCode!==detail.userCode)throw new Error("人員明細與查詢結果不一致，請重新查詢。");
 const roles=(detail.roleAssignments as DetailRole[]).filter(x=>x.isCurrent).map(x=>x.roleCode);
 const teamAssignments=(detail.teamAssignments as DetailTeam[]).filter(x=>x.isCurrent).map(({teamId,teamCode,teamName,isPrimary})=>({teamId,teamCode,teamName,isPrimary}));
 return {...row,employeeNo:detail.employeeNo,displayName:detail.displayName,email:detail.email,
  employmentStatus:detail.employmentStatus,adminEnabled:detail.adminEnabled,actualAccess:detail.actualAccess,
  hireDate:detail.hireDate,terminationDate:detail.terminationDate,roles,teamAssignments};
}

export function PersonDetailView({row,detail,onClose}:{row:V170PeopleRow;detail:V170PersonDetail;onClose:()=>void}){
 const summary=resolvePersonSummary(row,detail);
 const site=[summary.primaryCenterName,summary.primaryDeploymentSiteName||summary.primaryDeploymentSiteCode].filter(Boolean).join("／");
 return <ReadOnlyDetail title={"人員明細｜"+summary.displayName} onClose={onClose} fields={[
  ["工號",summary.employeeNo||summary.userCode],["姓名",summary.displayName],["Email",summary.email||"—"],
  ["目前人事狀態",employmentStatusLabel(summary.employmentStatus)],["入職日",summary.hireDate||"—"],["離職日",summary.terminationDate||"—"],
  ["角色",summary.roles.map(r=>roleLabels[r]||r).join("、")||"未設定"],["業務授權",businessAccessLabel(summary)],
  ["主要管理小組",summary.teamAssignments.filter(x=>x.isPrimary).map(x=>x.teamName).join("、")||"—"],
  ["其他管理小組",summary.teamAssignments.filter(x=>!x.isPrimary).map(x=>x.teamName).join("、")||"—"],
  ["主要派駐據點（清單查詢結果）",site||"—"],["資料完整性",completenessWarnings(summary).join("；")||"完整"]
 ]}>
  <div className="note">此視窗僅供查看；人事、角色及小組歸屬請從各自原有維護入口處理。主要派駐據點取自目前清單，如有異動請重新查詢。</div>
  <h4>人事狀態歷史</h4>
  {detail.employmentPeriods.length?<div className="table-wrap"><table><thead><tr><th>狀態</th><th>開始日</th><th>結束日</th></tr></thead><tbody>
  {[...detail.employmentPeriods].sort((a,b)=>b.effectiveFrom.localeCompare(a.effectiveFrom)).map(x=><tr key={x.userEmploymentPeriodId}><td>{employmentStatusLabel(x.employmentStatus)}{x.isCurrent?"（目前）":""}</td><td>{x.effectiveFrom}</td><td>{x.effectiveTo||"無期限"}</td></tr>)}
  </tbody></table></div>:<div className="empty">無人事狀態期間。</div>}
 </ReadOnlyDetail>;
}

export default function PersonDetail({row,onClose}:{row:V170PeopleRow;onClose:()=>void}){
 const[detail,setDetail]=useState<V170PersonDetail|null>(null);
 const[error,setError]=useState("");
 useEffect(()=>{
  let cancelled=false;
  setDetail(null);setError("");
  api<V170PersonDetail>(`/admin/people/${row.userId}`).then(result=>{
   if(cancelled)return;
   resolvePersonSummary(row,result);setDetail(result);
  }).catch(e=>{if(!cancelled)setError(e instanceof Error?e.message:"人員明細載入失敗")});
  return()=>{cancelled=true};
 },[row]);
 if(detail)return <PersonDetailView row={row} detail={detail} onClose={onClose}/>;
 return <ReadOnlyDetail title="人員明細" onClose={onClose} fields={[]}><div role={error?"alert":"status"} className={error?"note danger-note":"note"}>{error?"無法查看人員明細："+error:"載入中…"}</div></ReadOnlyDetail>;
}
