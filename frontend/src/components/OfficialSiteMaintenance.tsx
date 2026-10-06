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

export function isEligibleOfficialLocation(
 row:Pick<ManagedLocation,"isActive"|"isTemporary"|"approvalStatus">
){
 return row.isActive&&!row.isTemporary&&row.approvalStatus==="Approved";
}

export default function OfficialSiteMaintenance(){
 const[centers,setCenters]=useState<MasterRow[]>([]);
 const[sites,setSites]=useState<MasterRow[]>([]);
 const[msg,setMsg]=useState("");
 const[busy,setBusy]=useState(false);

 const[centerEdit,setCenterEdit]=useState<MasterRow|null>(null);
 const[centerCode,setCenterCode]=useState("");
 const[centerName,setCenterName]=useState("");
 const[centerFrom,setCenterFrom]=useState(todayTaipei());
 const[centerTo,setCenterTo]=useState("");
 const[centerActive,setCenterActive]=useState(true);

 const[siteEdit,setSiteEdit]=useState<MasterRow|null>(null);
 const[siteCenterCode,setSiteCenterCode]=useState("");
 const[siteCode,setSiteCode]=useState("");
 const[siteName,setSiteName]=useState("");
 const[siteLocationCode,setSiteLocationCode]=useState("");
 const[siteLocationLabel,setSiteLocationLabel]=useState("");
 const[siteFrom,setSiteFrom]=useState(todayTaipei());
 const[siteTo,setSiteTo]=useState("");
 const[siteActive,setSiteActive]=useState(true);
 const[locationSearch,setLocationSearch]=useState("");

 const locationQuery=usePagedQuery<ManagedLocation>(
  "/managed-locations/search",
  {q:locationSearch,isActive:true},
  !!locationSearch.trim()&&!siteEdit
 );
 const officialLocations=useMemo(
  ()=>locationQuery.data.items.filter(isEligibleOfficialLocation),
  [locationQuery.data.items]
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
  setCenterFrom(todayTaipei());setCenterTo("");setCenterActive(true);
 };
 const openCenter=(row:MasterRow)=>{
  setCenterEdit(row);setCenterCode(row.key);setCenterName(row.detail||"");
  setCenterFrom(row.effectiveFrom);setCenterTo(row.effectiveTo||"");
  setCenterActive(row.isActive!==false);setMsg("");
 };
 const saveCenter=async()=>{
  if(!centerCode.trim()||!centerName.trim())return setMsg("請輸入 Center Code 與中心名稱。");
  if(centerTo&&centerTo<centerFrom)return setMsg("中心失效日不可早於生效日。");
  setBusy(true);setMsg("");
  try{
   await api(centerEdit?"/admin/master-data/centers/"+centerEdit.id:"/admin/master-data/centers",{
    method:centerEdit?"PUT":"POST",
    body:JSON.stringify({
     centerCode:centerCode.trim(),centerName:centerName.trim(),
     effectiveFrom:centerFrom,effectiveTo:centerTo||null,isActive:centerActive,
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
  setSiteTo("");setSiteActive(true);setLocationSearch("");
 };
 const openSite=(row:MasterRow)=>{
  setSiteEdit(row);setSiteCenterCode(row.parentKey||"");setSiteCode(row.key);
  setSiteName(row.detail||"");setSiteLocationCode(row.referenceKey||"");
  setSiteLocationLabel(row.referenceKey||"");setSiteFrom(row.effectiveFrom);
  setSiteTo(row.effectiveTo||"");setSiteActive(row.isActive!==false);
  setLocationSearch("");setMsg("");
 };
 const chooseLocation=(row:ManagedLocation)=>{
  setSiteLocationCode(row.locationCode);
  setSiteLocationLabel(row.locationName+(row.address?"｜"+row.address:""));
  setLocationSearch("");
 };
 const saveSite=async()=>{
  if(!siteCenterCode||!siteCode.trim()||!siteName.trim())return setMsg("請選擇所屬中心，並輸入 Site Code 與據點名稱。");
  if(!siteLocationCode)return setMsg(siteEdit?"此據點無法判定完整 Location coverage，請改走既有搬遷流程。":"請先搜尋並選擇一個已核准、已啟用的正式 Location。");
  if(siteTo&&siteTo<siteFrom)return setMsg("據點失效日不可早於生效日。");
  setBusy(true);setMsg("");
  try{
   await api(siteEdit?"/admin/master-data/deployment-sites/"+siteEdit.id:"/admin/master-data/deployment-sites",{
    method:siteEdit?"PUT":"POST",
    body:JSON.stringify({
     centerCode:siteCenterCode,siteCode:siteCode.trim(),siteName:siteName.trim(),
     locationCode:siteLocationCode,effectiveFrom:siteFrom,effectiveTo:siteTo||null,
     isActive:siteActive,rowVersion:siteEdit?.rowVersion||null
    })
   });
   setMsg(siteEdit?"官方據點已更新。":"官方據點已新增；已可供行程起點／終點 Picker 使用。");
   resetSite();await load();
  }catch(e){setMsg(e instanceof Error?e.message:"官方據點儲存失敗")}
  finally{setBusy(false)}
 };

 return <div style={{marginTop:18}}>
  <div className="card">
   <div className="section-title"><div>
    <h2>官方據點進階維護</h2>
    <div className="sub">一般新增請從「地點主檔 → 資料／官方據點」勾選設定；此區保留給 Center 主檔、特殊調整與歷史維護。人員歸屬只決定行程預設值。</div>
   </div></div>
   {msg&&<div className="note" style={{marginBottom:14}}>{msg}</div>}
   <div className="note">一般修改不允許直接更換既有 Deployment Site 的 Location；若據點實際搬遷，仍須走既有 relocation flow，以保留歷史期間與 Snapshot。</div>
  </div>

  <div className="grid cols-2" style={{marginTop:18}}>
   <div className="card">
    <div className="section-title"><div><h2>{centerEdit?"修改就業中心":"新增就業中心"}</h2><div className="sub">Center 是官方據點的上層主檔。</div></div>{centerEdit&&<button className="btn small outline" onClick={resetCenter}>取消修改</button>}</div>
    <div className="grid cols-2">
     <div className="field"><label>Center Code</label><input value={centerCode} onChange={e=>setCenterCode(e.target.value)} placeholder="例如 UAT-C-CHANGHUA"/></div>
     <div className="field"><label>中心名稱</label><input value={centerName} onChange={e=>setCenterName(e.target.value)} placeholder="例如 UAT 彰化中心"/></div>
     <div className="field"><label>生效日</label><input type="date" value={centerFrom} onChange={e=>setCenterFrom(e.target.value)}/></div>
     <div className="field"><label>失效日</label><input type="date" value={centerTo} onChange={e=>setCenterTo(e.target.value)}/></div>
    </div>
    <label className="check-row"><input type="checkbox" checked={centerActive} onChange={e=>setCenterActive(e.target.checked)}/>啟用</label>
    <button className="btn" disabled={busy} onClick={()=>void saveCenter()}>{centerEdit?"儲存中心修改":"新增就業中心"}</button>
   </div>

   <div className="card">
    <div className="section-title"><div><h2>就業中心清單</h2><div className="sub">縮短有效期間時，既有下層關聯會由 backend fail closed。</div></div></div>
    <div className="table-wrap"><table><thead><tr><th>Code</th><th>名稱</th><th>有效期間</th><th>狀態</th><th>操作</th></tr></thead><tbody>
     {centers.map(x=><tr key={x.id}><td>{x.key}</td><td>{x.detail||"—"}</td><td>{x.effectiveFrom}～{x.effectiveTo||"無期限"}</td><td>{x.isActive===false?"停用":"啟用"}</td><td><button className="btn small outline" disabled={busy} onClick={()=>openCenter(x)}>修改</button></td></tr>)}
     {!centers.length&&<tr><td colSpan={5}>尚無中心主檔。</td></tr>}
    </tbody></table></div>
   </div>
  </div>

  <div className="grid cols-2" style={{marginTop:18}}>
   <div className="card">
    <div className="section-title"><div><h2>{siteEdit?"進階修改官方據點":"進階新增官方據點"}</h2><div className="sub">Deployment Site 必須綁定已核准、已啟用的正式 Location。</div></div>{siteEdit&&<button className="btn small outline" onClick={resetSite}>取消修改</button>}</div>
    <div className="grid cols-2">
     <div className="field"><label>所屬中心</label><select value={siteCenterCode} onChange={e=>setSiteCenterCode(e.target.value)}><option value="">請選擇</option>{centers.map(x=><option key={x.id} value={x.key}>{x.key}｜{x.detail||""}{x.isActive===false?"（停用）":""}</option>)}</select></div>
     <div className="field"><label>Site Code</label><input value={siteCode} onChange={e=>setSiteCode(e.target.value)} placeholder="例如 UAT-S-CHANGHUA"/></div>
     <div className="field span-2"><label>據點名稱</label><input value={siteName} onChange={e=>setSiteName(e.target.value)} placeholder="例如 UAT 彰化就業中心"/></div>
     <div className="field"><label>生效日</label><input type="date" value={siteFrom} onChange={e=>setSiteFrom(e.target.value)}/></div>
     <div className="field"><label>失效日</label><input type="date" value={siteTo} onChange={e=>setSiteTo(e.target.value)}/></div>
    </div>

    {siteEdit?
     <div className="field"><label>正式 Location</label><input value={siteLocationCode||"無完整 Location coverage"} disabled/><div className="muted">既有據點 Location 不在一般修改畫面更換；搬遷需使用 relocation flow。</div></div>
     :<>
      <div className="field"><label>搜尋正式 Location</label><input value={locationSearch} onChange={e=>setLocationSearch(e.target.value)} placeholder="輸入地點代碼、名稱或地址"/></div>
      {siteLocationCode&&<div className="note ok-note" style={{marginBottom:12}}>已選擇：{siteLocationCode}｜{siteLocationLabel}</div>}
      {!!locationSearch.trim()&&<div className="existing-location-results" style={{marginBottom:14}}>
       {officialLocations.map(x=><button type="button" className="existing-location-choice" key={x.locationId} onClick={()=>chooseLocation(x)}>
        <span>○</span><span><strong>{x.locationName}</strong><small>{x.locationCode}｜{x.address||x.plusCode||"—"}</small></span>
       </button>)}
       {!locationQuery.loading&&!officialLocations.length&&<div className="empty compact-empty">查無已核准、已啟用的正式 Location。請先到上方「地點主檔」完成解析／發布。</div>}
       {locationQuery.loading&&<div className="empty compact-empty">搜尋中…</div>}
      </div>}
     </>
    }

    <label className="check-row"><input type="checkbox" checked={siteActive} onChange={e=>setSiteActive(e.target.checked)}/>啟用</label>
    <button className="btn" disabled={busy||!!(siteEdit&&!siteLocationCode)} onClick={()=>void saveSite()}>{siteEdit?"儲存據點修改":"新增官方據點"}</button>
   </div>

   <div className="card">
    <div className="section-title"><div><h2>官方據點清單</h2><div className="sub">只有有效且綁定 Approved / Active Location 的據點會出現在 Start / End Picker。</div></div></div>
    <div className="table-wrap"><table><thead><tr><th>中心</th><th>Site Code</th><th>據點</th><th>Location</th><th>有效期間</th><th>狀態</th><th>操作</th></tr></thead><tbody>
     {sites.map(x=><tr key={x.id}><td>{x.parentKey||"—"}</td><td>{x.key}</td><td>{x.detail||"—"}</td><td>{x.referenceKey||"需搬遷流程"}</td><td>{x.effectiveFrom}～{x.effectiveTo||"無期限"}</td><td>{x.isActive===false?"停用":"啟用"}</td><td><button className="btn small outline" disabled={busy} onClick={()=>openSite(x)}>修改</button></td></tr>)}
     {!sites.length&&<tr><td colSpan={7}>尚無官方據點。</td></tr>}
    </tbody></table></div>
   </div>
  </div>
 </div>;
}
