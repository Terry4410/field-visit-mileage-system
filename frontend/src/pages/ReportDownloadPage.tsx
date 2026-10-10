import{useEffect,useMemo,useState}from"react";
import{api,apiDownload}from"../api";
import{qs}from"../v160";
import type{ManagedTeam}from"../types";

type ReportKind="personnel"|"personnel-full"|"roles"|"teams"|"members"|"locations"|"centers"|"deployment-sites"|"locations-official"|"projects"|"visit-types";
const reports:{kind:ReportKind;title:string;description:string}[]=[
 {kind:"personnel",title:"人事總覽",description:"目前狀態，依人員一人一列"},
 {kind:"personnel-full",title:"人事完整履歷",description:"含留停、人事狀態、角色、小組、派駐及資料權限有效期間"},
 {kind:"roles",title:"角色與登入",description:"目前角色、登入資格與小組"},
 {kind:"teams",title:"小組主檔",description:"小組設定及人數"},
 {kind:"members",title:"小組成員配置",description:"指定小組的成員現況，不代替人事完整履歷"},
 {kind:"locations",title:"地點主檔",description:"地點基本資料、統一編號及備註"},
 {kind:"centers",title:"就業中心",description:"中心代碼、名稱及有效期間"},
 {kind:"deployment-sites",title:"官方據點",description:"據點、所屬中心、Location 與有效期間"},
 {kind:"locations-official",title:"地點與官方據點整合",description:"多工作表含據點搬遷與小組對應歷史"},
 {kind:"projects",title:"專案管理",description:"專案主檔"},
 {kind:"visit-types",title:"拜訪型式",description:"拜訪型式代碼、名稱與排序"}
];
const fields:{group:string;list:{key:string;label:string}[]}[]=[
 {group:"基本資料",list:[{key:"employeeNo",label:"工號"},{key:"name",label:"姓名"},{key:"email",label:"Email"},{key:"userType",label:"人員類型"}]},
 {group:"人事狀態",list:[{key:"employmentStatus",label:"目前人事狀態"},{key:"hireDate",label:"入職日"},{key:"terminationDate",label:"離職日"}]},
 {group:"小組與派駐",list:[{key:"primaryTeam",label:"主要小組"},{key:"otherTeams",label:"其他小組"},{key:"primarySite",label:"主要派駐據點"},{key:"primaryCenter",label:"就業中心"}]},
 {group:"角色與登入",list:[{key:"roles",label:"目前角色"},{key:"login",label:"實際登入"},{key:"authorizationFrom",label:"授權起日"},{key:"authorizationTo",label:"授權迄日"}]}
];
const allColumns=fields.flatMap(g=>g.list.map(x=>x.key));
export default function ReportDownloadPage(){
 const[kind,setKind]=useState<ReportKind>("personnel-full");
 const[keyword,setKeyword]=useState("");
 const[teamId,setTeamId]=useState("");
 const[employmentStatus,setEmploymentStatus]=useState("");
 const[siteStatus,setSiteStatus]=useState("");
 const[includeHistory,setIncludeHistory]=useState(true);
 const[selected,setSelected]=useState<string[]>(allColumns);
 const[teams,setTeams]=useState<ManagedTeam[]>([]);
 const[busy,setBusy]=useState(false);
 const[msg,setMsg]=useState("");
 useEffect(()=>{api<ManagedTeam[]>("/admin/teams?includeInactive=true").then(setTeams).catch(()=>setTeams([]))},[]);
 const isPersonnel=kind==="personnel-full";
 const needsTeam=kind==="members";
 const info=reports.find(x=>x.kind===kind)!;
 const selectedTeam=teams.find(x=>String(x.teamId)===teamId);
 const filenamePreview="FieldVisit_"+info.title+"_"+(needsTeam?selectedTeam?.teamCode||"小組":teamId?"指定小組":"全組織")+"_YYYYMMDD_HHmmss.xlsx";
 const request=useMemo(()=>{
  const base:Record<string,string|number|boolean|undefined>={};
  if(kind==="personnel"||kind==="personnel-full"||kind==="roles"||kind==="members"){
   base.userType="Internal";base.keyword=keyword.trim()||undefined;
   base.employmentStatus=employmentStatus||undefined;
   base.teamId=teamId?Number(teamId):undefined;
   base.sort="code_asc";
   if(isPersonnel){
    base.columns=selected.join(",");
    base.includeHistory=includeHistory;
   }
  }else if(kind==="locations"||kind==="locations-official"){
   base.q=keyword.trim()||undefined;base.teamId=teamId?Number(teamId):undefined;
  }else if(kind==="centers"||kind==="deployment-sites"){
   base.keyword=keyword.trim()||undefined;base.status=siteStatus||undefined;
  }else if(kind==="teams"||kind==="projects"){
   base.keyword=keyword.trim()||undefined;base.teamId=teamId?Number(teamId):undefined;
  }
  return base;
 },[kind,keyword,employmentStatus,teamId,siteStatus,selected,includeHistory,isPersonnel]);
 const download=async()=>{
  if(busy||isPersonnel&&selected.length===0||needsTeam&&!teamId)return;
  setBusy(true);setMsg("");
  try{
   const suffix=qs(request);
   await apiDownload("/admin/query-exports/"+kind+".xlsx"+(suffix?"?"+suffix:""),
     "FieldVisit_"+info.title+".xlsx",180000);
   setMsg("已送出下載；請確認瀏覽器的 Excel 檔案。");
  }catch(e){setMsg(e instanceof Error?e.message:"報表下載失敗。")}
  finally{setBusy(false)}
 };
 return <div className="card">
  <div className="section-title"><div><h2>報表下載中心</h2><div className="sub">先選報表用途，再依需求選欄位或下載歷史；只匯出目前管理員有權查詢的資料。Excel 匯出不會修改主檔。</div></div></div>
  <div className="grid cols-2">
   <label>報表種類<select value={kind} onChange={e=>{setKind(e.target.value as ReportKind);setMsg("")}}>{reports.map(r=><option key={r.kind} value={r.kind}>{r.title}</option>)}</select></label>
   <label>關鍵字<input value={keyword} onChange={e=>setKeyword(e.target.value)} placeholder="姓名、代碼或名稱"/></label>
   {(kind==="members"||kind==="personnel"||kind==="personnel-full"||kind==="roles"||kind==="locations"||kind==="locations-official"||kind==="teams"||kind==="projects")&&
    <label>小組{needsTeam?"（必選）":"（選填）"}<select value={teamId} onChange={e=>setTeamId(e.target.value)}><option value="">{needsTeam?"請選擇小組":"全部"}</option>{teams.map(t=><option key={t.teamId} value={t.teamId}>{t.teamCode}｜{t.teamName}</option>)}</select></label>}
   {(kind==="personnel"||kind==="personnel-full"||kind==="roles"||kind==="members")&&<label>目前人事狀態<select value={employmentStatus} onChange={e=>setEmploymentStatus(e.target.value)}><option value="">全部</option><option value="Active">在職</option><option value="Leave">留停</option><option value="Terminated">離職</option><option value="PreHire">尚未到職</option></select></label>}
   {(kind==="centers"||kind==="deployment-sites")&&<label>據點狀態<select value={siteStatus} onChange={e=>setSiteStatus(e.target.value)}><option value="">全部</option><option>有效</option><option>未生效</option><option>已失效</option><option>停用</option></select></label>}
  </div>
  <div className="note" style={{marginTop:12}}><strong>{info.title}</strong>｜{info.description}<div className="sub">目前快速下載仍保留在各維護頁面；小組成員報表與人事完整履歷採不同資料列定義，不會混用。</div></div>
  {isPersonnel&&<>
   <div className="section-title" style={{marginTop:18}}><div><h3>選擇總覽欄位</h3><div className="sub">歷史期間另列工作表，不會與人員總覽做一對多展開。</div></div><div className="actions"><button className="btn small outline" onClick={()=>setSelected(allColumns)}>全選</button><button className="btn small outline" onClick={()=>setSelected(["employeeNo","name","employmentStatus","primaryTeam","primarySite"])}>簡要欄位</button></div></div>
   {fields.map(group=><div key={group.group} style={{marginTop:10}}><strong>{group.group}</strong><div className="grid cols-3" style={{marginTop:5}}>{group.list.map(f=><label key={f.key} className="check-row"><input type="checkbox" checked={selected.includes(f.key)} onChange={e=>setSelected(old=>e.target.checked?[...old,f.key]:old.filter(x=>x!==f.key))}/>{f.label}</label>)}</div></div>)}
   <label className="check-row" style={{marginTop:16}}><input type="checkbox" checked={includeHistory} onChange={e=>setIncludeHistory(e.target.checked)}/>加入人事狀態、留停、角色、小組、派駐與權限歷史工作表</label>
  </>}
  <div className="note" style={{marginTop:18}}>檔名示例：<code>{filenamePreview}</code><div className="sub">上限依後端匯出規則控管；Excel 全部儲存為文字，避免代碼前導零消失或公式被執行。</div></div>
  <div className="actions" style={{marginTop:14}}><button className="btn ok" disabled={busy||isPersonnel&&selected.length===0||needsTeam&&!teamId} onClick={()=>void download()}>{busy?"正在產生 Excel…":"下載 Excel 報表"}</button></div>
  {msg&&<div role="status" className="note" style={{marginTop:12}}>{msg}</div>}
 </div>;
}
