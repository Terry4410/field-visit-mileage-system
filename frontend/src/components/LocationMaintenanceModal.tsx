import{useEffect,useMemo,useState}from"react";
import{api}from"../api";
import{useAuth}from"../auth";
import type{LocationMaintenance,LocationOfficialSite,MasterDataRow,Team}from"../types";
import{Link}from"react-router-dom";
import{todayTaipei}from"../v160";

type Props={locationId:number;teamId?:number;onClose:()=>void;onChanged?:()=>void};

const SUSPECTED="疑似重複，待管理者人工覆核";
export const eligibleOfficialCentersForDate=(rows:MasterDataRow[],date:string)=>
  rows.filter(x=>x.isActive!==false&&x.effectiveFrom<=date&&(!x.effectiveTo||date<=x.effectiveTo));

export default function LocationMaintenanceModal({locationId,teamId,onClose,onChanged}:Props){
  const{user}=useAuth();
  const isAdmin=!!user?.roles.some(r=>r.toLowerCase()==="admin");
  const[data,setData]=useState<LocationMaintenance|null>(null);
  const[teams,setTeams]=useState<Team[]>([]);
  const[activeTeamId,setActiveTeamId]=useState<number|undefined>(teamId);
  const[city,setCity]=useState(""),[district,setDistrict]=useState(""),[address,setAddress]=useState(""),[plus,setPlus]=useState(""),[taxId,setTaxId]=useState(""),[masterNote,setMasterNote]=useState("");
  const[note,setNote]=useState(""),[noteReason,setNoteReason]=useState("");
  const[msg,setMsg]=useState(""),[busy,setBusy]=useState(false);
  const[centers,setCenters]=useState<MasterDataRow[]>([]);
  const[officialSite,setOfficialSite]=useState<LocationOfficialSite|null>(null);
  const[officialChecked,setOfficialChecked]=useState(false);
  const[officialCenterCode,setOfficialCenterCode]=useState("");
  const[officialSiteName,setOfficialSiteName]=useState("");
  const[officialFrom,setOfficialFrom]=useState(todayTaipei());

  const load=async()=>{
    const suffix=activeTeamId?`?teamId=${activeTeamId}`:"";
    const row=await api<LocationMaintenance>(`/locations/${locationId}/maintenance${suffix}`);
    setData(row);setCity(row.city||"");setDistrict(row.district||"");setAddress(row.address||"");setPlus(row.plusCode||"");setTaxId(row.taxId||"");setMasterNote(row.masterNote||"");
    if(!activeTeamId&&row.teamId)setActiveTeamId(row.teamId);
    if(isAdmin){
      try{
        const[resultCenters,resultOfficial]=await Promise.all([
          api<MasterDataRow[]>("/admin/master-data/centers"),
          api<LocationOfficialSite>(`/admin/master-data/location-official-site/${locationId}`)
        ]);
        setCenters(resultCenters);
        setOfficialSite(resultOfficial);
        setOfficialChecked(resultOfficial.isOfficialSite);
        setOfficialCenterCode(resultOfficial.centerCode||"");
        setOfficialSiteName(resultOfficial.siteName||row.locationName);
        setOfficialFrom(resultOfficial.effectiveFrom||todayTaipei());
      }catch(e){
        setMsg(e instanceof Error?e.message:"官方據點狀態載入失敗");
      }
    }
  };

  useEffect(()=>{void load().catch(e=>setMsg(e instanceof Error?e.message:"地點資料載入失敗"))},[locationId,activeTeamId]);
  useEffect(()=>{if(isAdmin&&!activeTeamId)api<Team[]>("/teams").then(setTeams).catch(()=>{})},[isAdmin,activeTeamId]);

  const effectiveTeamId=activeTeamId||data?.teamId||user?.teamId;
  const canAddNote=!!effectiveTeamId;
  const eligibleOfficialCenters=eligibleOfficialCentersForDate(
    centers,
    officialFrom
  );

  const createOfficialSite=async()=>{
    if(!data)return;
    if(!officialCenterCode)return setMsg("請選擇所屬就業中心。");
    if(!officialSiteName.trim())return setMsg("請輸入官方據點名稱。");
    setBusy(true);setMsg("");
    try{
      const row=await api<LocationOfficialSite>("/admin/master-data/location-official-site",{
        method:"POST",
        body:JSON.stringify({
          locationId:data.locationId,
          centerCode:officialCenterCode,
          siteName:officialSiteName.trim(),
          effectiveFrom:officialFrom
        })
      });
      setOfficialSite(row);
      setOfficialChecked(true);
      setOfficialCenterCode(row.centerCode||officialCenterCode);
      setOfficialSiteName(row.siteName||officialSiteName);
      setOfficialFrom(row.effectiveFrom||officialFrom);
      setMsg("此地點已設為官方據點；Site Code 已由系統自動產生，可直接供行程起點／終點使用。");
      onChanged?.();
    }catch(e){setMsg(e instanceof Error?e.message:"官方據點建立失敗")}
    finally{setBusy(false)}
  };

  const save=async()=>{
    if(!data)return;setBusy(true);setMsg("");
    try{
      const row=await api<LocationMaintenance>(`/locations/${locationId}/maintenance`,{method:"PUT",body:JSON.stringify({
        city:city.trim()||null,district:district.trim()||null,address:address.trim()||null,plusCode:plus.trim()||null,
        taxId:taxId.trim()||null,masterNote:masterNote.trim()||null,rowVersion:data.rowVersion
      })});
      setData(row);
      setMsg(row.duplicateReason===SUSPECTED?"地點資料已更新，系統已自動標記疑似重複；請至「疑似重複覆核」分頁處理。":"地點資料已更新；地址/Plus Code 異動時會重新進入座標解析流程。");
      onChanged?.();
    }catch(e){setMsg(e instanceof Error?e.message:"地點資料更新失敗")}finally{setBusy(false)}
  };

  const addNote=async()=>{
    if(!effectiveTeamId)return setMsg("請先選擇備註所屬小組。");
    if(!note.trim())return setMsg("請輸入備註內容。");
    setBusy(true);setMsg("");
    try{
      const row=await api<LocationMaintenance>(`/locations/${locationId}/notes`,{method:"POST",body:JSON.stringify({teamId:effectiveTeamId,note:note.trim(),changeReason:noteReason.trim()||null})});
      setData(row);setNote("");setNoteReason("");setMsg("備註已新增並保留作者與時間歷史。");onChanged?.();
    }catch(e){setMsg(e instanceof Error?e.message:"備註新增失敗")}finally{setBusy(false)}
  };

  const audits=useMemo(()=>data?.addressAudit||[],[data]);
  if(!data)return <div className="modal"><div className="modal-panel" role="dialog" aria-modal="true"><button className="btn small outline modal-close" onClick={onClose}>關閉</button><h3>地點資料維護</h3>{msg?<div className="note danger-note">{msg}</div>:<div className="note">載入中…</div>}</div></div>;

  return <div className="modal"><div className="modal-panel" role="dialog" aria-modal="true" aria-label="地點資料維護">
    <button className="btn small outline modal-close" disabled={busy} onClick={onClose}>關閉</button>
    <h3>地點資料維護｜{data.locationName}</h3>
    <div className="sub">{data.locationCode||"—"}{data.teamName?`｜${data.teamName}`:"｜全組織"}</div>
    {data.duplicateReason===SUSPECTED&&<div className="note danger-note" style={{marginTop:10}}>系統已自動標記疑似重複；完成管理者人工覆核前不可發布為正式可用地點。</div>}
    {msg&&<div className="note" style={{marginTop:10}}>{msg}</div>}

    <div className="grid cols-2" style={{marginTop:14}}>
      <div className="field"><label>縣市</label><input value={city} disabled={!isAdmin} onChange={e=>setCity(e.target.value)}/></div><div className="field"><label>鄉鎮／區</label><input value={district} disabled={!isAdmin} onChange={e=>setDistrict(e.target.value)}/></div>
      <div className="field span-2"><label>地址</label><input value={address} disabled={!isAdmin} onChange={e=>setAddress(e.target.value)}/></div><div className="field"><label>Plus Code</label><input value={plus} disabled={!isAdmin} onChange={e=>setPlus(e.target.value)}/></div>
      <div className="field"><label>統一編號</label><input value={taxId} disabled={!isAdmin} onChange={e=>setTaxId(e.target.value)} maxLength={20}/></div><div className="field span-2"><label>主檔備註</label><textarea value={masterNote} disabled={!isAdmin} onChange={e=>setMasterNote(e.target.value)} maxLength={1000}/></div>
    </div>
    {isAdmin?<div className="actions"><button className="btn ok" disabled={busy} onClick={()=>void save()}>儲存地點資料</button></div>:<div className="note">正式主檔不開放直接修改。請至地點管理修改本人或授權小組的待審核草稿。</div>}

    {isAdmin&&<><hr/>
      <div className="section-title"><div><h4>官方據點</h4><div className="sub">Location-first：地點完成解析／發布後，只需在這裡指定所屬中心；Site Code 由系統自動產生。</div></div></div>
      {officialSite===null?<div className="note">官方據點狀態載入中…</div>:
       officialSite.isOfficialSite?
        <div className="note ok-note">
          <strong>✓ 此地點已是官方據點</strong>
          <div style={{marginTop:6}}>{officialSite.centerName||officialSite.centerCode||"—"}／{officialSite.siteName||data.locationName}</div>
          <div className="muted" style={{marginTop:4}}>系統代碼：{officialSite.siteCode||"—"}｜有效期間：{officialSite.effectiveFrom||"—"}～{officialSite.effectiveTo||"無期限"}｜{officialSite.isActive===false?"停用":"啟用"}</div>
          <div className="muted" style={{marginTop:4}}>若要停用、搬遷或調整歷史有效期間，請切換「官方據點進階維護」子分頁。</div>
        </div>
       :<>
        <label className="check-row"><input type="checkbox" checked={officialChecked} onChange={e=>{setOfficialChecked(e.target.checked);if(e.target.checked&&!officialSiteName)setOfficialSiteName(data.locationName)}}/>此地點為官方據點</label>
        {officialChecked&&<>
          <div className="grid cols-2" style={{marginTop:12}}>
            <div className="field"><label>所屬就業中心</label><select value={officialCenterCode} onChange={e=>setOfficialCenterCode(e.target.value)}><option value="">請選擇</option>{eligibleOfficialCenters.map(x=><option key={x.id} value={x.key}>{x.detail||x.key}</option>)}</select></div>
            <div className="field"><label>官方據點名稱</label><input value={officialSiteName} onChange={e=>setOfficialSiteName(e.target.value)} placeholder={data.locationName}/></div>
            <div className="field"><label>生效日</label><input type="date" value={officialFrom} onChange={e=>setOfficialFrom(e.target.value)}/></div>
            <div className="field"><label>Site Code</label><input value="系統自動產生" disabled/></div>
          </div>
          <button className="btn secondary" disabled={busy||!officialCenterCode||!officialSiteName.trim()} onClick={()=>void createOfficialSite()}>設為官方據點</button>
        </>}
       </>}
    </>}

    <hr/><h4>地點備註</h4>
    {!effectiveTeamId&&isAdmin&&<div className="field"><label>備註所屬小組</label><select value={activeTeamId||""} onChange={e=>setActiveTeamId(e.target.value?Number(e.target.value):undefined)}><option value="">請選擇</option>{teams.map(t=><option key={t.teamId} value={t.teamId}>{t.teamName}</option>)}</select></div>}
    <div className="field"><label>新增備註</label><textarea value={note} onChange={e=>setNote(e.target.value)} maxLength={1000}/></div><div className="field"><label>異動原因（選填）</label><input value={noteReason} onChange={e=>setNoteReason(e.target.value)}/></div>
    <button className="btn secondary" disabled={busy||!canAddNote} onClick={()=>void addNote()}>新增備註</button>
    <div className="route-list" style={{marginTop:12}}>{data.notes.map(n=><div className="route-item" key={n.historyId}><div><strong>{n.teamName}｜{n.changedBy}</strong><div className="sub">{new Date(n.changedAt).toLocaleString("zh-TW")}｜{n.action}{n.changeReason?`｜${n.changeReason}`:""}{n.sourceLocationId&&n.sourceLocationId!==data.locationId?`｜原始地點：${n.sourceLocationName||n.sourceLocationId}`:""}</div><div>{n.note}</div></div></div>)}{!data.notes.length&&<div className="empty compact-empty">尚無備註歷史。</div>}</div>

    <hr/><h4>地址／主檔異動歷史</h4>
    <div className="route-list">{audits.map(a=><div className="route-item" key={a.auditLogId}><div><strong>{a.changedBy||"系統"}｜{a.action}</strong><div className="sub">{new Date(a.changedAt).toLocaleString("zh-TW")}{a.sourceLocationId&&a.sourceLocationId!==data.locationId?`｜原始地點：${a.sourceLocationName||a.sourceLocationId}`:""}</div><details><summary>查看異動內容</summary><pre style={{whiteSpace:"pre-wrap"}}>{a.oldValues||"—"}{"\n→\n"}{a.newValues||"—"}</pre></details></div></div>)}{!audits.length&&<div className="empty compact-empty">尚無地址／主檔異動歷史。</div>}</div>

    {isAdmin&&data.duplicateReason===SUSPECTED&&<><hr/><div className="section-title"><div><h4>疑似重複人工覆核</h4><div className="sub">此地點已被標記為疑似重複。覆核、沿用與合併統一在「疑似重複覆核」分頁處理，避免同一責任有兩個維護入口。</div></div><Link className="btn small secondary" to="/admin/locations/duplicates" onClick={onClose}>前往疑似重複覆核</Link></div></>}
  </div></div>;
}
