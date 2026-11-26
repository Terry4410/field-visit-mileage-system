import {useState} from "react";
import {api} from "../api";
import type {PagedResult,V170PeopleRow} from "../types";
import ReadOnlyDetail from "./ReadOnlyDetail";
import PersonDetail from "./PersonDetail";
const identityHeaders:Record<string,string[]>={personnel:["工號","姓名"],"personnel-full":["工號","姓名"],roles:["工號","姓名"],members:["工號","姓名"],teams:["小組代碼","小組名稱"],locations:["地點代碼","名稱"],centers:["Center Code","就業中心名稱"],"deployment-sites":["Site Code","據點名稱"],"locations-official":["Location Code","名稱"],projects:["專案代碼","專案名稱"],"visit-types":["代碼","名稱"]};
const peopleKinds=new Set(["personnel","personnel-full","roles","members"]);
export const reportDetailColumn=(kind:string,header:string)=>identityHeaders[kind]?.includes(header)??false;
export function exactReportPerson(rows:V170PeopleRow[],totalCount:number,employeeNo:string){
 if(totalCount!==rows.length||!employeeNo.trim())return null;
 const matching=rows.filter(x=>(x.employeeNo||x.userCode)===employeeNo);
 return matching.length===1?matching[0]:null;
}
export default function ReportRowDetail({kind,sheetName,headers,row,onClose}:{kind:string;sheetName:string;headers:string[];row:string[];onClose:()=>void}){
 const[person,setPerson]=useState<V170PeopleRow|null>(null),[busy,setBusy]=useState(false),[error,setError]=useState("");
 const code=headers.includes("工號")?row[headers.indexOf("工號")]:"";
 const openPerson=async()=>{
  setBusy(true);setError("");
  try{const result=await api<PagedResult<V170PeopleRow>>(`/admin/people?userType=Internal&keyword=${encodeURIComponent(code)}&page=1&pageSize=100`);const match=exactReportPerson(result.items,result.totalCount,code);if(!match){setError("無法唯一確認目前人員；請保留本筆報表內容。");return}setPerson(match)}
  catch(e){setError(e instanceof Error?e.message:"無法取得人員明細")}finally{setBusy(false)}
 };
 if(person)return <PersonDetail row={person} onClose={()=>setPerson(null)}/>;
 return <ReadOnlyDetail title={sheetName+"｜報表明細"} onClose={onClose} fields={headers.map((h,i)=>[h||"欄位 "+(i+1),row[i]||"—"] as const)}>
 <div className="note">顯示本筆已授權報表預覽的所有欄位；未選欄位及其他工作表請由報表設定或 Excel 查閱。這不是重新查詢後的主檔資料。</div>
 {peopleKinds.has(kind)&&!!code&&<button type="button" className="btn small outline" disabled={busy} onClick={()=>void openPerson()}>{busy?"查詢中…":"查看目前人員明細"}</button>}
 {error&&<div role="alert" className="note danger-note">{error}</div>}
 </ReadOnlyDetail>;
}
