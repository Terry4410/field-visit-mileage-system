import{useEffect,useMemo,useState}from"react";
import{api}from"../api";
import{usePagedQuery}from"../use-query";
import type{ManagedLocation}from"../types";
import{todayTaipei}from"../v160";

type MasterRow={
 id:number;key:string;parentKey?:string|null;detail?:string|null;
 effectiveFrom:string;effectiveTo?:string|null;isActive?:boolean|null;
 rowVersion?:string|null;referenceKey?:string|null;
};

type Props={mode?:"centers"|"sites"};

export function isEligibleOfficialLocation(
 row:Pick<ManagedLocation,"isActive"|"isTemporary"|"approvalStatus">
){
 return row.isActive&&!row.isTemporary&&row.approvalStatus==="Approved";
}

function lifecycleLabel(row:MasterRow){
 const today=todayTaipei();
 if(row.isActive===false)return"停用";
 if(row.effectiveFrom>today)return"未生效";
 if(row.effectiveTo&&row.effectiveTo<today)return"已失效";
 return"有效";
}

export default function OfficialSiteMaintenance({mode="sites"}:Props){
 const[centers,setCenters]=useState<MasterRow[]>([]);
 const[sites,setSites]=useState<MasterRow[]>([]);
 const[msg,setMsg]=useState("");
 const[busy,setBusy]=useState(false);

 const[centerEdit,setCenterEdit]=useState<MasterRow|null>(null);
 const[centerCode,setCenterCode]=useState("");
 const[centerName,setCenterName]=useState("");
 const[centerFrom,setCenterFrom]=useState(todayTaipei());
 const[centerTo,setCenterTo]=useState("");
 const[centerNoEnd,setCenterNoEnd]=useState(true);
 const[centerActive,setCenterActive]=useState(true);
 const[centerKeyword,setCenterKeyword]=useState("");
 const[centerStatus,setCenterStatus]=useState("");

 const[siteEdit,setSiteEdit]=useState<MasterRow|null>(null);
 const[siteCenterCode,setSiteCenterCode]=useState("");
 const[siteCode,setSiteCode]=useState("");
 const[siteName,setSiteName]=useState("");
 const[siteLocationCode,setSiteLocationCode]=useState("");
 const[siteLocationLabel,setSiteLocationLabel]=useState("");
 const[siteFrom,setSiteFrom]=useState(todayTaipei());
 const[siteTo,setSiteTo]=useState("");
 const[siteNoEnd,setSiteNoEnd]=useState(true);
 const[siteActive,setSiteActive]=useState(true);
 const[locationSearch,setLocationSearch]=useState("");
 const[siteKeyword,setSiteKeyword]=useState("");
 const[siteCenterFilter,setSiteCenterFilter]=useState("");
 const[siteStatus,setSiteStatus]=useState("");

 const[relocationSite,setRelocationSite]=useState<MasterRow|null>(null);
 const[relocationSearch,setRelocationSearch]=useState("");
 const[relocationLocationCode,setRelocationLocationCode]=useState("");
 const[relocationLocationLabel,setRelocationLocationLabel]=useState("");
 const[relocationDate,setRelocationDate]=useState(todayTaipei());
 const[relocationReason,setRelocationReason]=useState("");

 const locationQuery=usePagedQuery<ManagedLocation>(
  "/managed-locations/search",
  {q:locationSearch,isActive:true},
  !!locationSearch.trim()&&!siteEdit
 );
 const relocationQuery=usePagedQuery<ManagedLocation>(
  "/managed-locations/search",
  {q:relocationSearch,isActive:true},
  !!relocationSearch.trim()&&!!relocationSite
 );
 const officialLocations=useMemo(
  ()=>locationQuery.data.items.filter(isEligibleOfficialLocation),
  [locationQuery.data.items]
 );
 const relocationLocations=useMemo(
  ()=>relocationQuery.data.items.filter(isEligibleOfficialLocation),
  [relocationQuery.data.items]
 );

 const load=async()=>{
  const result=await Promise.all([
   api<MasterRow[]>("/admin/master-data/centers"),
   api<MasterRow[]>("/admin/master-data/deployment-sites")
  ]);
  setCenters([...result[0]].sort((a,b)=>a.key.localeCompare(b.key)));
  setSites([...result[1]].sort((a,b)=>(a.parentKey||"").localeCompare(b.parentKey||"")||a.key.localeCompare(b.key)));
 };
 useEffect(()=>{void load().catch(e=>setMsg(e instanceof Error?e.message:"官方據點主檔載入失敗"))},[]);

 const resetCenter=()=>{
  setCenterEdit(null);setCenterCode("");setCenterName("");
  setCenterFrom(todayTaipei());setCenterTo("");setCenterNoEnd(true);setCenterActive(true);
 };
 const openCenter=(row:MasterRow)=>{
  setCenterEdit(row);setCenterCode(row.key);setCenterName(row.detail||"");
  setCenterFrom(row.effectiveFrom);setCenterTo(row.effectiveTo||"");
  setCenterNoEnd(!row.effectiveTo);setCenterActive(row.isActive!==false);setMsg("");
 };
 const saveCenter=async()=>{
  if(!centerCode.trim()||!centerName.trim())return setMsg("請輸入 Center Code 與中心名稱。");
  if(!centerNoEnd&&centerTo&&centerTo<centerFrom)return setMsg("中心失效日不可早於生效日。");
  setBusy(true);setMsg("");
  try{
   await api(centerEdit?"/admin/master-data/centers/"+centerEdit.id:"/admin/master-data/centers",{
    method:centerEdit?"PUT":"POST",
    body:JSON.stringify({
     centerCode:centerCode.trim(),centerName:centerName.trim(),
     effectiveFrom:centerFrom,effectiveTo:centerNoEnd?null:(centerTo||null),isActive:centerActive,
     rowVersion:centerEdit?.rowVersion||null
    })
   });
   setMsg(centerEdit?"就業中心已更新。":"就業中心已新增。");
   resetCenter();await load();
  }catch(e){setMsg(e instanceof Error?e.message:"就業中心儲存失敗")}
  finally{setBusy(false)}
 };

 const resetSite=()=>{
  setSiteEdit(null);setSiteCenterCode("");setSiteCode("");setSiteName("");
  setSiteLocationCode("");setSiteLocationLabel("");setSiteFrom(todayTaipei());
  setSiteTo("");setSiteNoEnd(true);setSiteActive(true);setLocationSearch("");
 };
 const openSite=(row:MasterRow)=>{
  setSiteEdit(row);setSiteCenterCode(row.parentKey||"");setSiteCode(row.key);
  setSiteName(row.detail||"");setSiteLocationCode(row.referenceKey||"");
  setSiteLocationLabel(row.referenceKey||"");setSiteFrom(row.effectiveFrom);
  setSiteTo(row.effectiveTo||"");setSiteNoEnd(!row.effectiveTo);setSiteActive(row.isActive!==false);
  setLocationSearch("");setMsg("");
 };
 const chooseLocation=(row:ManagedLocation)=>{
  setSiteLocationCode(row.locationCode);
  setSiteLocationLabel(row.locationName+(row.address?"｜"+row.address:""));
  setLocationSearch("");
 };
 const saveSite=async()=>{
  if(!siteCenterCode||!siteCode.trim()||!siteName.trim())return setMsg("請選擇所屬中心，並輸入 Site Code 與據點名稱。");
  if(!siteLocationCode)return setMsg(siteEdit?"此據點無法判定完整 Location coverage，請使用據點搬遷／修復流程。":"請先搜尋並選擇一個已核准、已啟用的正式 Location。");
  if(!siteNoEnd&&siteTo&&siteTo<siteFrom)return setMsg("據點失效日不可早於生效日。");
  setBusy(true);setMsg("");
  try{
   await api(siteEdit?"/admin/master-data/deployment-sites/"+siteEdit.id:"/admin/master-data/deployment-sites",{
    method:siteEdit?"PUT":"POST",
    body:JSON.stringify({
     centerCode:siteCenterCode,siteCode:siteCode.trim(),siteName:siteName.trim(),
     locationCode:siteLocationCode,effectiveFrom:siteFrom,effectiveTo:siteNoEnd?null:(siteTo||null),
     isActive:siteActive,rowVersion:siteEdit?.rowVersion||null
    })
   });
   setMsg(siteEdit?"官方據點已更新。":"官方據點已新增；已可供行程起點／終點使用。");
   resetSite();await load();
  }catch(e){setMsg(e instanceof Error?e.message:"官方據點儲存失敗")}
  finally{setBusy(false)}
 };

 const openRelocation=(row:MasterRow)=>{
  setRelocationSite(row);setRelocationSearch("");setRelocationLocationCode("");
  setRelocationLocationLabel("");setRelocationDate(todayTaipei());setRelocationReason("");setMsg("");
 };
 const chooseRelocationLocation=(row:ManagedLocation)=>{
  setRelocationLocationCode(row.locationCode);
  setRelocationLocationLabel(row.locationName+(row.address?"｜"+row.address:""));
  setRelocationSearch("");
 };
 const relocate=async()=>{
  if(!relocationSite||!relocationLocationCode||!relocationDate||!relocationReason.trim())
   return setMsg("據點搬遷必須選擇新正式 Location、搬遷生效日並填寫原因。");
  if(!window.confirm(`確認將「${relocationSite.detail||relocationSite.key}」自 ${relocationDate} 起搬遷到 ${relocationLocationCode}？\n\n舊 Location 關聯會在前一日結束，歷史行程與 Snapshot 不會被改寫。`))return;
  setBusy(true);setMsg("");
  try{
   await api(`/admin/master-data/deployment-sites/${relocationSite.id}/relocate`,{
    method:"POST",
    body:JSON.stringify({
     locationCode:relocationLocationCode,
     effectiveFrom:relocationDate,
     changeReason:relocationReason.trim(),
     siteRowVersion:relocationSite.rowVersion||null
    })
   });
   setMsg("據點搬遷已建立新的 Location 有效期間；舊關聯歷史已保留。");
   setRelocationSite(null);await load();
  }catch(e){setMsg(e instanceof Error?e.message:"據點搬遷失敗")}
  finally{setBusy(false)}
 };

 const filteredCenters=centers.filter(row=>{
  const k=centerKeyword.trim().toLowerCase();
  const matches=!k||row.key.toLowerCase().includes(k)||(row.detail||"").toLowerCase().includes(k);
  return matches&&(!centerStatus||lifecycleLabel(row)===centerStatus);
 });
 const filteredSites=sites.filter(row=>{
  const k=siteKeyword.trim().toLowerCase();
  const center=centers.find(c=>c.key===row.parentKey);
  const hay=[row.key,row.detail||"",row.referenceKey||"",row.parentKey||"",center?.detail||""].join(" ").toLowerCase();
  return (!k||hay.includes(k))
   &&(!siteCenterFilter||row.parentKey===siteCenterFilter)
   &&(!siteStatus||lifecycleLabel(row)===siteStatus);
 });

 if(mode==="centers")return <div className="grid cols-2" style={{marginTop:18}}>
  <div className="card">
   <div className="section-title"><div><h2>{centerEdit?"修改就業中心":"新增就業中心"}</h2><div className="sub">Center 是官方據點的上層主檔；失效日空白以「無期限」維護。</div></div>{centerEdit&&<button className="btn small outline" onClick={resetCenter}>取消修改</button>}</div>
   {msg&&<div className="note" style={{marginBottom:14}}>{msg}</div>}
   <div className="grid cols-2">
    <label>Center Code<input value={centerCode} onChange={e=>setCenterCode(e.target.value)} placeholder="例如 C-CHANGHUA"/></label>
    <label>中心名稱<input value={centerName} onChange={e=>setCenterName(e.target.value)} placeholder="例如 彰化中心"/></label>
    <label>生效日<input type="date" value={centerFrom} onChange={e=>setCenterFrom(e.target.value)}/></label>
    <label>失效日<input type="date" value={centerTo} disabled={centerNoEnd} onChange={e=>setCenterTo(e.target.value)}/></label>
   </div>
   <label className="check-row"><input type="checkbox" checked={centerNoEnd} onChange={e=>{setCenterNoEnd(e.target.checked);if(e.target.checked)setCenterTo("")}}/>無期限</label>
   <label className="check-row"><input type="checkbox" checked={centerActive} onChange={e=>setCenterActive(e.target.checked)}/>啟用</label>
   <button className="btn" disabled={busy} onClick={()=>void saveCenter()}>{centerEdit?"儲存中心修改":"新增就業中心"}</button>
  </div>
  <div className="card">
   <div className="section-title"><div><h2>就業中心清單</h2><div className="sub">狀態依啟用旗標與有效期間衍生為未生效／有效／已失效／停用。</div></div></div>
   <div className="grid cols-2"><label>關鍵字<input value={centerKeyword} onChange={e=>setCenterKeyword(e.target.value)} placeholder="Center Code 或中心名稱"/></label><label>狀態<select value={centerStatus} onChange={e=>setCenterStatus(e.target.value)}><option value="">全部</option><option>未生效</option><option>有效</option><option>已失效</option><option>停用</option></select></label></div>
   <div className="table-wrap"><table><thead><tr><th>Code</th><th>名稱</th><th>有效期間</th><th>狀態</th><th>操作</th></tr></thead><tbody>
    {filteredCenters.map(x=><tr key={x.id}><td>{x.key}</td><td>{x.detail||"—"}</td><td>{x.effectiveFrom}～{x.effectiveTo||"無期限"}</td><td>{lifecycleLabel(x)}</td><td><button className="btn small outline" disabled={busy} onClick={()=>openCenter(x)}>修改</button></td></tr>)}
    {!filteredCenters.length&&<tr><td colSpan={5}>查無符合條件的就業中心。</td></tr>}
   </tbody></table></div>
  </div>
 </div>;

 return <div style={{marginTop:18}}>
  <div className="card">
   <div className="section-title"><div><h2>官方據點</h2><div className="sub">Deployment Site 必須綁定正式 Location；搬遷以新的 Location 有效期間處理，不覆寫歷史。</div></div></div>
   {msg&&<div className="note" style={{marginBottom:14}}>{msg}</div>}
  </div>
  <div className="grid cols-2" style={{marginTop:18}}>
   <div className="card">
    <div className="section-title"><div><h2>{siteEdit?"修改官方據點":"新增官方據點"}</h2><div className="sub">既有據點 Location 不在一般修改中直接更換；請使用清單的「據點搬遷」。</div></div>{siteEdit&&<button className="btn small outline" onClick={resetSite}>取消修改</button>}</div>
    <div className="grid cols-2">
     <label>所屬就業中心<select value={siteCenterCode} onChange={e=>setSiteCenterCode(e.target.value)}><option value="">請選擇</option>{centers.map(x=><option key={x.id} value={x.key}>{x.key}｜{x.detail||""}{lifecycleLabel(x)!=="有效"?`（${lifecycleLabel(x)}）`:""}</option>)}</select></label>
     <label>Site Code<input value={siteCode} onChange={e=>setSiteCode(e.target.value)} placeholder="例如 S-CHANGHUA"/></label>
     <label className="span-2">據點名稱<input value={siteName} onChange={e=>setSiteName(e.target.value)} placeholder="例如 彰化就業中心"/></label>
     <label>生效日<input type="date" value={siteFrom} onChange={e=>setSiteFrom(e.target.value)}/></label>
     <label>失效日<input type="date" value={siteTo} disabled={siteNoEnd} onChange={e=>setSiteTo(e.target.value)}/></label>
    </div>
    <label className="check-row"><input type="checkbox" checked={siteNoEnd} onChange={e=>{setSiteNoEnd(e.target.checked);if(e.target.checked)setSiteTo("")}}/>無期限</label>
    {siteEdit?<div className="field"><label>正式 Location</label><input value={siteLocationCode||"無完整 Location coverage"} disabled/><div className="muted">需要更換 Location 時請使用「據點搬遷」。</div></div>:<>
     <div className="field"><label>搜尋正式 Location</label><input value={locationSearch} onChange={e=>setLocationSearch(e.target.value)} placeholder="地點代碼、名稱或地址"/></div>
     {siteLocationCode&&<div className="note ok-note" style={{marginBottom:12}}>已選擇：{siteLocationCode}｜{siteLocationLabel}</div>}
     {!!locationSearch.trim()&&<div className="existing-location-results" style={{marginBottom:14}}>
      {officialLocations.map(x=><button type="button" className="existing-location-choice" key={x.locationId} onClick={()=>chooseLocation(x)}><span>○</span><span><strong>{x.locationName}</strong><small>{x.locationCode}｜{x.address||x.plusCode||"—"}</small></span></button>)}
      {!locationQuery.loading&&!officialLocations.length&&<div className="empty compact-empty">查無已核准、已啟用的正式 Location。</div>}
     </div>}
    </>}
    <label className="check-row"><input type="checkbox" checked={siteActive} onChange={e=>setSiteActive(e.target.checked)}/>啟用</label>
    <button className="btn" disabled={busy||!!(siteEdit&&!siteLocationCode)} onClick={()=>void saveSite()}>{siteEdit?"儲存據點修改":"新增官方據點"}</button>
   </div>
   <div className="card">
    <div className="section-title"><div><h2>官方據點清單</h2><div className="sub">可依就業中心或關鍵字搜尋 Site Code、據點名稱、Location Code 與中心名稱。</div></div></div>
    <div className="grid cols-2"><label>關鍵字<input value={siteKeyword} onChange={e=>setSiteKeyword(e.target.value)} placeholder="Site Code／據點／Location／就業中心"/></label><label>就業中心<select value={siteCenterFilter} onChange={e=>setSiteCenterFilter(e.target.value)}><option value="">全部</option>{centers.map(x=><option key={x.id} value={x.key}>{x.key}｜{x.detail||""}</option>)}</select></label><label>狀態<select value={siteStatus} onChange={e=>setSiteStatus(e.target.value)}><option value="">全部</option><option>未生效</option><option>有效</option><option>已失效</option><option>停用</option></select></label></div>
    <div className="table-wrap"><table><thead><tr><th>中心</th><th>Site Code</th><th>據點</th><th>Location</th><th>有效期間</th><th>狀態</th><th>操作</th></tr></thead><tbody>
     {filteredSites.map(x=><tr key={x.id}><td>{centers.find(c=>c.key===x.parentKey)?.detail||x.parentKey||"—"}</td><td>{x.key}</td><td>{x.detail||"—"}</td><td>{x.referenceKey||"需修復 Location coverage"}</td><td>{x.effectiveFrom}～{x.effectiveTo||"無期限"}</td><td>{lifecycleLabel(x)}</td><td><div className="actions"><button className="btn small outline" disabled={busy} onClick={()=>openSite(x)}>修改</button><button className="btn small secondary" disabled={busy||lifecycleLabel(x)==="停用"} onClick={()=>openRelocation(x)}>據點搬遷</button></div></td></tr>)}
     {!filteredSites.length&&<tr><td colSpan={7}>查無符合條件的官方據點。</td></tr>}
    </tbody></table></div>
   </div>
  </div>
  {relocationSite&&<div className="modal" onMouseDown={e=>{if(e.target===e.currentTarget&&!busy)setRelocationSite(null)}}><div className="modal-panel" role="dialog" aria-modal="true"><button className="btn small outline modal-close" disabled={busy} onClick={()=>setRelocationSite(null)}>關閉</button><h3>據點搬遷｜{relocationSite.detail||relocationSite.key}</h3><div className="note">這不是直接改寫舊 Location。系統會關閉舊關聯期間，再建立新期間，歷史 Snapshot 保留。</div><label>搬遷生效日<input type="date" value={relocationDate} onChange={e=>setRelocationDate(e.target.value)}/></label><label>搬遷原因<input value={relocationReason} onChange={e=>setRelocationReason(e.target.value)} placeholder="例如辦公室搬遷"/></label><label>搜尋新正式 Location<input value={relocationSearch} onChange={e=>setRelocationSearch(e.target.value)} placeholder="地點代碼、名稱或地址"/></label>{relocationLocationCode&&<div className="note ok-note">已選擇：{relocationLocationCode}｜{relocationLocationLabel}</div>}{!!relocationSearch.trim()&&<div className="existing-location-results">{relocationLocations.map(x=><button type="button" className="existing-location-choice" key={x.locationId} onClick={()=>chooseRelocationLocation(x)}><span>○</span><span><strong>{x.locationName}</strong><small>{x.locationCode}｜{x.address||x.plusCode||"—"}</small></span></button>)}{!relocationQuery.loading&&!relocationLocations.length&&<div className="empty compact-empty">查無符合條件的正式 Location。</div>}</div>}<div className="actions" style={{marginTop:14}}><button className="btn ok" disabled={busy||!relocationLocationCode||!relocationDate||!relocationReason.trim()} onClick={()=>void relocate()}>確認據點搬遷</button><button className="btn outline" disabled={busy} onClick={()=>setRelocationSite(null)}>取消</button></div></div></div>}
 </div>;
}
