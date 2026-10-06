import{useEffect,useMemo,useState}from"react";
import{api}from"../api";
import{useAuth}from"../auth";
import type{LocationDuplicateCandidate,LocationMaintenance,LocationMergeMaster,LocationMergeMasterSelection,LocationMergePreview,LocationOfficialSite,MasterDataRow,Team}from"../types";
import{todayTaipei}from"../v160";

type Props={locationId:number;teamId?:number;onClose:()=>void;onChanged?:()=>void};

const SUSPECTED="疑似重複，待管理者人工覆核";
export const eligibleOfficialCentersForDate=(rows:MasterDataRow[],date:string)=>
  rows.filter(x=>x.isActive!==false&&x.effectiveFrom<=date&&(!x.effectiveTo||date<=x.effectiveTo));

const shown=(value:unknown)=>value===null||value===undefined||value===""?"—":String(value);
const selection=(m:LocationMergeMaster):LocationMergeMasterSelection=>({
  locationName:m.locationName,locationType:m.locationType,teamId:m.teamId??null,
  city:m.city??null,district:m.district??null,address:m.address??null,plusCode:m.plusCode??null,
  taxId:m.taxId??null,masterNote:m.masterNote??null
});

function MergeChoice({label,source,survivor,selected,onSource,onSurvivor}:{label:string;source:unknown;survivor:unknown;selected:unknown;onSource:()=>void;onSurvivor:()=>void}){
  return <tr><td><strong>{label}</strong></td><td>{shown(source)}<div><button className="btn small outline" onClick={onSource}>採來源</button></div></td><td>{shown(survivor)}<div><button className="btn small outline" onClick={onSurvivor}>採保留</button></div></td><td>{shown(selected)}</td></tr>;
}

export default function LocationMaintenanceModal({locationId,teamId,onClose,onChanged}:Props){
  const{user}=useAuth();
  const isAdmin=!!user?.roles.some(r=>r.toLowerCase()==="admin");
  const[data,setData]=useState<LocationMaintenance|null>(null);
  const[teams,setTeams]=useState<Team[]>([]);
  const[activeTeamId,setActiveTeamId]=useState<number|undefined>(teamId);
  const[city,setCity]=useState(""),[district,setDistrict]=useState(""),[address,setAddress]=useState(""),[plus,setPlus]=useState(""),[taxId,setTaxId]=useState(""),[masterNote,setMasterNote]=useState("");
  const[note,setNote]=useState(""),[noteReason,setNoteReason]=useState("");
  const[duplicates,setDuplicates]=useState<LocationDuplicateCandidate[]>([]);
  const[mergePreview,setMergePreview]=useState<LocationMergePreview|null>(null);
  const[finalMaster,setFinalMaster]=useState<LocationMergeMasterSelection|null>(null);
  const[mergeReason,setMergeReason]=useState("");
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
        const[resultDuplicates,resultCenters,resultOfficial]=await Promise.all([
          api<LocationDuplicateCandidate[]>(`/locations/${locationId}/duplicate-candidates`),
          api<MasterDataRow[]>("/admin/master-data/centers"),
          api<LocationOfficialSite>(`/admin/master-data/location-official-site/${locationId}`)
        ]);
        setDuplicates(resultDuplicates);
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
      if(isAdmin)setDuplicates(await api<LocationDuplicateCandidate[]>(`/locations/${locationId}/duplicate-candidates`));
      setMsg(row.duplicateReason===SUSPECTED?"地點資料已更新，系統已自動標記疑似重複，需由管理者人工覆核。":"地點資料已更新；地址/Plus Code 異動時會重新進入座標解析流程。");
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

  const findDuplicates=async(showEmpty=true)=>{
    setBusy(true);if(showEmpty)setMsg("");
    try{
      const rows=await api<LocationDuplicateCandidate[]>(`/locations/${locationId}/duplicate-candidates`);
      setDuplicates(rows);if(showEmpty&&!rows.length)setMsg("目前沒有統一編號／名稱／地址／Plus Code 相同或近似的疑似重複地點。");
    }catch(e){setMsg(e instanceof Error?e.message:"疑似重複查詢失敗")}finally{setBusy(false)}
  };

  const confirmDistinct=async(candidate:LocationDuplicateCandidate)=>{
    if(!data)return;
    const reason=window.prompt("請輸入確認為不同地點的原因","管理者已核對，確認為不同地點")||"";
    if(!reason.trim())return;
    setBusy(true);setMsg("");
    try{
      await api(`/locations/${locationId}/duplicate-review/distinct`,{method:"POST",body:JSON.stringify({candidateLocationId:candidate.locationId,reason:reason.trim(),sourceRowVersion:data.rowVersion,confirm:true})});
      await load();setMsg("已確認為不同地點；只要兩筆主檔比對欄位未變更，系統不會重複提示這一組。");onChanged?.();
    }catch(e){setMsg(e instanceof Error?e.message:"人工覆核失敗")}finally{setBusy(false)}
  };

  const getPreview=async(candidate:LocationDuplicateCandidate)=>{
    const preview=await api<LocationMergePreview>(`/locations/${locationId}/merge-preview?survivorLocationId=${candidate.locationId}`);
    if(!preview.canMerge){setMsg(preview.blockingReason||"目前不可合併。");return null}
    if(!preview.source||!preview.survivor){setMsg("合併預覽缺少主檔比較資料，請重新整理。");return null}
    return preview;
  };

  const impact=(p:LocationMergePreview)=>`來源引用：行程 ${p.tripReferenceCount}、Snapshot ${p.snapshotReferenceCount}、專案 ${p.projectReferenceCount}、常用地點 ${p.favoriteReferenceCount}、備註歷史 ${p.noteHistoryCount}、政府主檔 ${p.governmentMatchCount}。歷史 Trip / Snapshot 不會改寫。`;

  const useExisting=async(candidate:LocationDuplicateCandidate)=>{
    if(!data)return;setBusy(true);setMsg("");
    try{
      const p=await getPreview(candidate);if(!p)return;
      if(!window.confirm(`${impact(p)}\n\n將沿用既有主檔「${p.survivor!.locationName}」，不覆寫它的主檔欄位；確認繼續？`))return;
      const reason=window.prompt("請輸入沿用既有主檔的原因","確認為同一地點，沿用既有主檔")||"";if(!reason.trim())return;
      await api(`/locations/${locationId}/merge`,{method:"POST",body:JSON.stringify({survivorLocationId:p.survivorLocationId,reason:reason.trim(),sourceRowVersion:data.rowVersion,survivorRowVersion:p.survivor!.rowVersion,confirm:true,mode:"UseExisting",finalMaster:null})});
      setMsg("已沿用既有主檔；來源地點保留為歷史 duplicate，既有 Trip / Snapshot 未改寫。");onChanged?.();setTimeout(onClose,500);
    }catch(e){setMsg(e instanceof Error?e.message:"沿用既有主檔失敗")}finally{setBusy(false)}
  };

  const openMergeFields=async(candidate:LocationDuplicateCandidate)=>{
    setBusy(true);setMsg("");
    try{const p=await getPreview(candidate);if(!p)return;setMergePreview(p);setFinalMaster(selection(p.survivor!));setMergeReason("")}
    catch(e){setMsg(e instanceof Error?e.message:"合併預覽失敗")}finally{setBusy(false)}
  };

  const choose=(key:keyof LocationMergeMasterSelection,value:LocationMergeMasterSelection[keyof LocationMergeMasterSelection])=>
    setFinalMaster(current=>current?({...current,[key]:value} as LocationMergeMasterSelection):current);

  const executeFieldMerge=async()=>{
    if(!data||!mergePreview?.source||!mergePreview.survivor||!finalMaster)return;
    if(!mergeReason.trim())return setMsg("請輸入合併原因。");
    if(!window.confirm(`${impact(mergePreview)}\n\n系統只會依你選定的欄位更新保留主檔，來源主檔與歷史證據會保留。確認執行？`))return;
    setBusy(true);setMsg("");
    try{
      await api(`/locations/${locationId}/merge`,{method:"POST",body:JSON.stringify({survivorLocationId:mergePreview.survivorLocationId,reason:mergeReason.trim(),sourceRowVersion:data.rowVersion,survivorRowVersion:mergePreview.survivor.rowVersion,confirm:true,mode:"MergeFields",finalMaster})});
      setMsg("地點合併已完成；欄位依人工選擇寫入保留主檔，歷史 Trip / Snapshot 與來源備註歷史均未改寫。");onChanged?.();setTimeout(onClose,500);
    }catch(e){setMsg(e instanceof Error?e.message:"地點合併失敗")}finally{setBusy(false)}
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
      <div className="field"><label>縣市</label><input value={city} onChange={e=>setCity(e.target.value)}/></div><div className="field"><label>鄉鎮／區</label><input value={district} onChange={e=>setDistrict(e.target.value)}/></div>
      <div className="field span-2"><label>地址</label><input value={address} onChange={e=>setAddress(e.target.value)}/></div><div className="field"><label>Plus Code</label><input value={plus} onChange={e=>setPlus(e.target.value)}/></div>
      <div className="field"><label>統一編號</label><input value={taxId} onChange={e=>setTaxId(e.target.value)} maxLength={20}/></div><div className="field span-2"><label>主檔備註</label><textarea value={masterNote} onChange={e=>setMasterNote(e.target.value)} maxLength={1000}/></div>
    </div>
    <div className="actions"><button className="btn ok" disabled={busy} onClick={()=>void save()}>儲存地點資料</button></div>

    {isAdmin&&<><hr/>
      <div className="section-title"><div><h4>官方據點</h4><div className="sub">Location-first：地點完成解析／發布後，只需在這裡指定所屬中心；Site Code 由系統自動產生。</div></div></div>
      {officialSite===null?<div className="note">官方據點狀態載入中…</div>:
       officialSite.isOfficialSite?
        <div className="note ok-note">
          <strong>✓ 此地點已是官方據點</strong>
          <div style={{marginTop:6}}>{officialSite.centerName||officialSite.centerCode||"—"}／{officialSite.siteName||data.locationName}</div>
          <div className="muted" style={{marginTop:4}}>系統代碼：{officialSite.siteCode||"—"}｜有效期間：{officialSite.effectiveFrom||"—"}～{officialSite.effectiveTo||"無期限"}｜{officialSite.isActive===false?"停用":"啟用"}</div>
          <div className="muted" style={{marginTop:4}}>若要停用、搬遷或調整歷史有效期間，請使用頁面下方「官方據點進階維護」。</div>
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

    {isAdmin&&<><hr/><div className="section-title"><div><h4>疑似重複人工覆核</h4><div className="sub">新增／修改時系統會自動比對統編、名稱、地址、Plus Code；單一欄位命中只會標記，不會自動刪除或合併。</div></div><button className="btn small outline" disabled={busy} onClick={()=>void findDuplicates()}>重新檢核</button></div>
      <div className="route-list">{duplicates.map(d=><div className="route-item" key={d.locationId}><div><strong>{d.locationName}</strong><div className="sub">{d.locationCode||"—"}｜{d.address||d.plusCode||"—"}｜統編 {d.taxId||"—"}</div><div>命中：{d.matchReasons.join("、")}</div></div><div className="actions"><button className="btn small outline" disabled={busy} onClick={()=>void confirmDistinct(d)}>確認不同</button><button className="btn small secondary" disabled={busy} onClick={()=>void useExisting(d)}>沿用此主檔</button><button className="btn small danger" disabled={busy} onClick={()=>void openMergeFields(d)}>合併／選欄位</button></div></div>)}{!duplicates.length&&<div className="empty compact-empty">目前沒有待人工覆核的疑似重複地點。</div>}</div>
      {mergePreview?.source&&mergePreview.survivor&&finalMaster&&<div className="card" style={{marginTop:14}}>
        <div className="section-title"><div><h4>合併前防呆與主檔選擇</h4><div className="sub">{impact(mergePreview)}</div></div><button className="btn small outline" onClick={()=>{setMergePreview(null);setFinalMaster(null)}}>取消</button></div>
        <div className="table-wrap"><table><thead><tr><th>欄位</th><th>來源地點</th><th>保留地點</th><th>最後主檔</th></tr></thead><tbody>
          <MergeChoice label="地點名稱" source={mergePreview.source.locationName} survivor={mergePreview.survivor.locationName} selected={finalMaster.locationName} onSource={()=>choose("locationName",mergePreview.source!.locationName)} onSurvivor={()=>choose("locationName",mergePreview.survivor!.locationName)}/>
          <MergeChoice label="類型" source={mergePreview.source.locationType} survivor={mergePreview.survivor.locationType} selected={finalMaster.locationType} onSource={()=>choose("locationType",mergePreview.source!.locationType)} onSurvivor={()=>choose("locationType",mergePreview.survivor!.locationType)}/>
          <MergeChoice label="小組" source={mergePreview.source.teamName} survivor={mergePreview.survivor.teamName} selected={finalMaster.teamId===mergePreview.source.teamId?mergePreview.source.teamName:finalMaster.teamId===mergePreview.survivor.teamId?mergePreview.survivor.teamName:finalMaster.teamId} onSource={()=>choose("teamId",mergePreview.source!.teamId??null)} onSurvivor={()=>choose("teamId",mergePreview.survivor!.teamId??null)}/>
          <MergeChoice label="縣市" source={mergePreview.source.city} survivor={mergePreview.survivor.city} selected={finalMaster.city} onSource={()=>choose("city",mergePreview.source!.city??null)} onSurvivor={()=>choose("city",mergePreview.survivor!.city??null)}/>
          <MergeChoice label="鄉鎮／區" source={mergePreview.source.district} survivor={mergePreview.survivor.district} selected={finalMaster.district} onSource={()=>choose("district",mergePreview.source!.district??null)} onSurvivor={()=>choose("district",mergePreview.survivor!.district??null)}/>
          <MergeChoice label="地址" source={mergePreview.source.address} survivor={mergePreview.survivor.address} selected={finalMaster.address} onSource={()=>choose("address",mergePreview.source!.address??null)} onSurvivor={()=>choose("address",mergePreview.survivor!.address??null)}/>
          <MergeChoice label="Plus Code" source={mergePreview.source.plusCode} survivor={mergePreview.survivor.plusCode} selected={finalMaster.plusCode} onSource={()=>choose("plusCode",mergePreview.source!.plusCode??null)} onSurvivor={()=>choose("plusCode",mergePreview.survivor!.plusCode??null)}/>
          <MergeChoice label="統一編號" source={mergePreview.source.taxId} survivor={mergePreview.survivor.taxId} selected={finalMaster.taxId} onSource={()=>choose("taxId",mergePreview.source!.taxId??null)} onSurvivor={()=>choose("taxId",mergePreview.survivor!.taxId??null)}/>
          <MergeChoice label="主檔備註" source={mergePreview.source.masterNote} survivor={mergePreview.survivor.masterNote} selected={finalMaster.masterNote} onSource={()=>choose("masterNote",mergePreview.source!.masterNote??null)} onSurvivor={()=>choose("masterNote",mergePreview.survivor!.masterNote??null)}/>
        </tbody></table></div>
        <div className="field"><label>合併原因</label><input value={mergeReason} onChange={e=>setMergeReason(e.target.value)} placeholder="必填，會寫入 AuditLog"/></div><button className="btn danger" disabled={busy||!mergeReason.trim()} onClick={()=>void executeFieldMerge()}>確認合併並套用最後主檔</button>
      </div>}
    </>}
  </div></div>;
}
