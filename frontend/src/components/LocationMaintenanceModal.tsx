import{useEffect,useMemo,useState}from"react";
import{api}from"../api";
import{useAuth}from"../auth";
import type{LocationDuplicateCandidate,LocationMaintenance,LocationMergePreview,Team}from"../types";

type Props={
  locationId:number;
  teamId?:number;
  onClose:()=>void;
  onChanged?:()=>void;
};

export default function LocationMaintenanceModal({locationId,teamId,onClose,onChanged}:Props){
  const{user}=useAuth();
  const isAdmin=!!user?.roles.some(r=>r.toLowerCase()==="admin");
  const[data,setData]=useState<LocationMaintenance|null>(null);
  const[teams,setTeams]=useState<Team[]>([]);
  const[activeTeamId,setActiveTeamId]=useState<number|undefined>(teamId);
  const[city,setCity]=useState("");
  const[district,setDistrict]=useState("");
  const[address,setAddress]=useState("");
  const[plus,setPlus]=useState("");
  const[taxId,setTaxId]=useState("");
  const[masterNote,setMasterNote]=useState("");
  const[note,setNote]=useState("");
  const[noteReason,setNoteReason]=useState("");
  const[duplicates,setDuplicates]=useState<LocationDuplicateCandidate[]>([]);
  const[msg,setMsg]=useState("");
  const[busy,setBusy]=useState(false);

  const load=async()=>{
    const suffix=activeTeamId?`?teamId=${activeTeamId}`:"";
    const row=await api<LocationMaintenance>(`/locations/${locationId}/maintenance${suffix}`);
    setData(row);
    setCity(row.city||"");
    setDistrict(row.district||"");
    setAddress(row.address||"");
    setPlus(row.plusCode||"");
    setTaxId(row.taxId||"");
    setMasterNote(row.masterNote||"");
    if(!activeTeamId&&row.teamId)setActiveTeamId(row.teamId);
  };

  useEffect(()=>{
    void load().catch(e=>setMsg(e instanceof Error?e.message:"地點資料載入失敗"));
  },[locationId,activeTeamId]);

  useEffect(()=>{
    if(isAdmin&&!activeTeamId){
      api<Team[]>("/teams").then(setTeams).catch(()=>{});
    }
  },[isAdmin,activeTeamId]);

  const effectiveTeamId=activeTeamId||data?.teamId||user?.teamId;
  const canAddNote=!!effectiveTeamId;

  const save=async()=>{
    if(!data)return;
    setBusy(true);setMsg("");
    try{
      const row=await api<LocationMaintenance>(`/locations/${locationId}/maintenance`,{
        method:"PUT",
        body:JSON.stringify({
          city:city.trim()||null,
          district:district.trim()||null,
          address:address.trim()||null,
          plusCode:plus.trim()||null,
          taxId:taxId.trim()||null,
          masterNote:masterNote.trim()||null,
          rowVersion:data.rowVersion
        })
      });
      setData(row);
      setMsg("地點資料已更新；地址/Plus Code 異動時會重新進入座標解析流程。");
      onChanged?.();
    }catch(e){setMsg(e instanceof Error?e.message:"地點資料更新失敗")}
    finally{setBusy(false)}
  };

  const addNote=async()=>{
    if(!effectiveTeamId)return setMsg("請先選擇備註所屬小組。");
    if(!note.trim())return setMsg("請輸入備註內容。");
    setBusy(true);setMsg("");
    try{
      const row=await api<LocationMaintenance>(`/locations/${locationId}/notes`,{
        method:"POST",
        body:JSON.stringify({teamId:effectiveTeamId,note:note.trim(),changeReason:noteReason.trim()||null})
      });
      setData(row);setNote("");setNoteReason("");setMsg("備註已新增並保留作者與時間歷史。");
      onChanged?.();
    }catch(e){setMsg(e instanceof Error?e.message:"備註新增失敗")}
    finally{setBusy(false)}
  };

  const findDuplicates=async()=>{
    setBusy(true);setMsg("");
    try{
      const rows=await api<LocationDuplicateCandidate[]>(`/locations/${locationId}/duplicate-candidates`);
      setDuplicates(rows);
      if(!rows.length)setMsg("目前沒有符合統一編號／名稱／地址／Plus Code 的疑似重複地點。");
    }catch(e){setMsg(e instanceof Error?e.message:"疑似重複查詢失敗")}
    finally{setBusy(false)}
  };

  const merge=async(candidate:LocationDuplicateCandidate)=>{
    if(!data)return;
    setBusy(true);setMsg("");
    try{
      const preview=await api<LocationMergePreview>(`/locations/${locationId}/merge-preview?survivorLocationId=${candidate.locationId}`);
      if(!preview.canMerge){
        setMsg(preview.blockingReason||"目前不可合併。");return;
      }
      const detail=`來源地點歷史行程 ${preview.tripReferenceCount}、專案引用 ${preview.projectReferenceCount}、常用地點 ${preview.favoriteReferenceCount}、備註歷史 ${preview.noteHistoryCount} 筆都會保留，不會改寫歷史 Trip/Snapshot。\n\n保留地點：${candidate.locationName}\n是否確認將目前地點標記為 duplicate 並停用？`;
      if(!window.confirm(detail))return;
      const reason=window.prompt("請輸入合併原因","疑似重複地點，經管理者確認合併")||"";
      if(!reason.trim())return;
      await api(`/locations/${locationId}/merge`,{
        method:"POST",
        body:JSON.stringify({survivorLocationId:candidate.locationId,reason:reason.trim(),sourceRowVersion:data.rowVersion,confirm:true})
      });
      setMsg("地點合併已完成；來源地點與所有歷史紀錄保留，未改寫既有 Trip/Snapshot。");
      onChanged?.();
      setTimeout(onClose,500);
    }catch(e){setMsg(e instanceof Error?e.message:"地點合併失敗")}
    finally{setBusy(false)}
  };

  const audits=useMemo(()=>data?.addressAudit||[],[data]);
  if(!data)return <div className="modal"><div className="modal-panel" role="dialog" aria-modal="true"><button className="btn small outline modal-close" onClick={onClose}>關閉</button><h3>地點資料維護</h3>{msg?<div className="note danger-note">{msg}</div>:<div className="note">載入中…</div>}</div></div>;

  return <div className="modal"><div className="modal-panel" role="dialog" aria-modal="true" aria-label="地點資料維護">
    <button className="btn small outline modal-close" disabled={busy} onClick={onClose}>關閉</button>
    <h3>地點資料維護｜{data.locationName}</h3>
    <div className="sub">{data.locationCode||"—"}{data.teamName?`｜${data.teamName}`:"｜全組織"}</div>
    {msg&&<div className="note" style={{marginTop:10}}>{msg}</div>}

    <div className="grid cols-2" style={{marginTop:14}}>
      <div className="field"><label>縣市</label><input value={city} onChange={e=>setCity(e.target.value)}/></div>
      <div className="field"><label>鄉鎮／區</label><input value={district} onChange={e=>setDistrict(e.target.value)}/></div>
      <div className="field span-2"><label>地址</label><input value={address} onChange={e=>setAddress(e.target.value)}/></div>
      <div className="field"><label>Plus Code</label><input value={plus} onChange={e=>setPlus(e.target.value)}/></div>
      <div className="field"><label>統一編號</label><input value={taxId} onChange={e=>setTaxId(e.target.value)} maxLength={20}/></div>
      <div className="field span-2"><label>主檔備註</label><textarea value={masterNote} onChange={e=>setMasterNote(e.target.value)} maxLength={1000}/></div>
    </div>
    <div className="actions"><button className="btn ok" disabled={busy} onClick={()=>void save()}>儲存地點資料</button></div>

    <hr/>
    <h4>地點備註</h4>
    {!effectiveTeamId&&isAdmin&&<div className="field"><label>備註所屬小組</label><select value={activeTeamId||""} onChange={e=>setActiveTeamId(e.target.value?Number(e.target.value):undefined)}><option value="">請選擇</option>{teams.map(t=><option key={t.teamId} value={t.teamId}>{t.teamName}</option>)}</select></div>}
    <div className="field"><label>新增備註</label><textarea value={note} onChange={e=>setNote(e.target.value)} maxLength={1000}/></div>
    <div className="field"><label>異動原因（選填）</label><input value={noteReason} onChange={e=>setNoteReason(e.target.value)}/></div>
    <button className="btn secondary" disabled={busy||!canAddNote} onClick={()=>void addNote()}>新增備註</button>
    <div className="route-list" style={{marginTop:12}}>
      {data.notes.map(n=><div className="route-item" key={n.historyId}><div><strong>{n.teamName}｜{n.changedBy}</strong><div className="sub">{new Date(n.changedAt).toLocaleString("zh-TW")}｜{n.action}{n.changeReason?`｜${n.changeReason}`:""}</div><div>{n.note}</div></div></div>)}
      {!data.notes.length&&<div className="empty compact-empty">尚無備註歷史。</div>}
    </div>

    <hr/>
    <h4>地址／主檔異動歷史</h4>
    <div className="route-list">
      {audits.map(a=><div className="route-item" key={a.auditLogId}><div><strong>{a.changedBy||"系統"}｜{a.action}</strong><div className="sub">{new Date(a.changedAt).toLocaleString("zh-TW")}</div><details><summary>查看異動內容</summary><pre style={{whiteSpace:"pre-wrap"}}>{a.oldValues||"—"}{"\n→\n"}{a.newValues||"—"}</pre></details></div></div>)}
      {!audits.length&&<div className="empty compact-empty">尚無地址／主檔異動歷史。</div>}
    </div>

    {isAdmin&&<>
      <hr/>
      <div className="section-title"><div><h4>疑似重複與合併</h4><div className="sub">只標記 duplicate 與 survivor，不改寫歷史 Trip / Snapshot。</div></div><button className="btn small outline" disabled={busy} onClick={()=>void findDuplicates()}>檢查疑似重複</button></div>
      <div className="route-list">
        {duplicates.map(d=><div className="route-item" key={d.locationId}><div><strong>{d.locationName}</strong><div className="sub">{d.locationCode||"—"}｜{d.address||d.plusCode||"—"}｜統編 {d.taxId||"—"}</div><div>命中：{d.matchReasons.join("、")}</div></div><button className="btn small danger" disabled={busy} onClick={()=>void merge(d)}>合併至此筆</button></div>)}
      </div>
    </>}
  </div></div>;
}
