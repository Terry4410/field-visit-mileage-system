import {useState} from "react";
import {apiDownload} from "../api";
import {qs} from "../v160";

type FilterValue=string|number|boolean|null|undefined;
type Props={dataset:"personnel"|"roles"|"teams"|"members"|"locations"|"projects"|"visit-types";
 filters:Record<string,FilterValue>|null; label?:string; disabled?:boolean};

export default function ExportFilteredButton({dataset,filters,label="匯出查詢結果 Excel",disabled=false}:Props){
 const[busy,setBusy]=useState(false),[error,setError]=useState("");
 const download=async()=>{
  if(!filters||busy)return;
  setBusy(true);setError("");
  try{
   const suffix=qs(filters);
   await apiDownload("/admin/query-exports/"+dataset+".xlsx"+(suffix?"?"+suffix:""),
    "fieldvisit_"+dataset+".xlsx",180000);
  }catch(e){setError(e instanceof Error?e.message:"匯出失敗");}
  finally{setBusy(false)}
 };
 return <span style={{display:"inline-flex",flexDirection:"column",alignItems:"flex-start",gap:4}}>
  <button className="btn small outline" disabled={disabled||!filters||busy} onClick={()=>void download()}>{busy?"匯出中…":label}</button>
  {error&&<span role="alert" className="sub" style={{color:"var(--danger, #b91c1c)"}}>{error}</span>}
 </span>;
}
