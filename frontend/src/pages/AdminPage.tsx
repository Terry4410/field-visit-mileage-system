import {useEffect,useMemo,useState} from "react";
import {api} from "../api";
import type {MileageRate,MileageReport,Project,Team,VisitType} from "../types";
import {downloadCsv,money} from "../utils";

type Props={section:"dashboard"|"users"|"projects"|"rates"|"reports"};
const effectiveRate=(rates:MileageRate[],date:string)=>rates.filter(r=>r.isActive&&r.effectiveFrom<=date&&(!r.effectiveTo||r.effectiveTo>=date)).sort((a,b)=>b.effectiveFrom.localeCompare(a.effectiveFrom))[0];

export default function AdminPage({section}:Props){
  const today=new Date().toISOString().slice(0,10);
  const [rates,setRates]=useState<MileageRate[]>([]),[report,setReport]=useState<MileageReport[]>([]),[projects,setProjects]=useState<Project[]>([]),[types,setTypes]=useState<VisitType[]>([]),[teams,setTeams]=useState<Team[]>([]);
  const [msg,setMsg]=useState(""),[busy,setBusy]=useState(false),[start,setStart]=useState("2026-01-01"),[end,setEnd]=useState(today),[status,setStatus]=useState(""),[team,setTeam]=useState("");

  const [rateEditId,setRateEditId]=useState<number|null>(null),[rateValue,setRateValue]=useState("2.50"),[rateFrom,setRateFrom]=useState(today),[rateTo,setRateTo]=useState(""),[rateName,setRateName]=useState(""),[rateActive,setRateActive]=useState(true);
  const [projectEditId,setProjectEditId]=useState<number|null>(null),[projectCode,setProjectCode]=useState(""),[projectName,setProjectName]=useState(""),[projectDesc,setProjectDesc]=useState(""),[projectTeamId,setProjectTeamId]=useState(""),[projectMode,setProjectMode]=useState("List"),[projectStart,setProjectStart]=useState(today),[projectEnd,setProjectEnd]=useState(""),[projectActive,setProjectActive]=useState(true);
  const [typeEditId,setTypeEditId]=useState<number|null>(null),[typeCode,setTypeCode]=useState(""),[typeName,setTypeName]=useState(""),[typeDesc,setTypeDesc]=useState(""),[typeSort,setTypeSort]=useState("10"),[typeActive,setTypeActive]=useState(true);

  const load=()=>Promise.all([
    api<MileageRate[]>("/mileage-rate-rules"),
    api<MileageReport[]>(`/reports/mileage?startDate=${start}&endDate=${end}`),
    api<Project[]>("/projects"),
    api<VisitType[]>("/visit-types"),
    api<Team[]>("/teams")
  ]).then(([r,m,p,v,t])=>{setRates(r);setReport(m);setProjects(p);setTypes(v);setTeams(t)}).catch(e=>setMsg(e.message));
  useEffect(()=>{void load()},[section]);

  const current=effectiveRate(rates,today);
  const teamNames=useMemo(()=>[...new Set(report.map(x=>x.teamName).filter(Boolean) as string[])].sort(),[report]);
  const filtered=useMemo(()=>report.filter(x=>(!status||x.status===status)&&(!team||x.teamName===team)),[report,status,team]);
  const approved=report.filter(x=>x.status==="Approved");
  const reportDownload=(name:string,rows:MileageReport[]=report)=>downloadCsv(`${name}.csv`,rows.map(x=>({日期:x.visitDate,外訪員:x.visitorName,小組:x.teamName||"",路線:x.route,外訪員自算里程:x.claimedDistanceKm??"",系統里程:x.systemDistanceKm??"",小組長核定里程:x.approvedDistanceKm??"",每公里補助:x.ratePerKmSnapshot??"",補助金額:x.approvedAmount??"",狀態:x.statusName})));

  const resetRate=()=>{setRateEditId(null);setRateValue("2.50");setRateFrom(today);setRateTo("");setRateName("");setRateActive(true)};
  const editRate=(r:MileageRate)=>{setRateEditId(r.mileageRateRuleId);setRateValue(String(r.ratePerKm));setRateFrom(r.effectiveFrom);setRateTo(r.effectiveTo||"");setRateName(r.ruleName);setRateActive(r.isActive)};
  const saveRate=async()=>{
    if(Number(rateValue)<0)return setMsg("每公里補助不可小於 0。");
    if(!rateName.trim())return setMsg("請輸入規則名稱。");
    setBusy(true);setMsg("");
    try{
      const body={ruleName:rateName.trim(),vehicleType:"Motorcycle",ratePerKm:Number(rateValue),effectiveFrom:rateFrom,effectiveTo:rateTo||null,isActive:rateActive};
      if(rateEditId)await api(`/mileage-rate-rules/${rateEditId}`,{method:"PUT",body:JSON.stringify(body)});
      else await api("/mileage-rate-rules",{method:"POST",body:JSON.stringify(body)});
      setMsg(rateEditId?"費率版本已修改。":"費率版本已新增。");
      resetRate();await load();
    }catch(e){setMsg(e instanceof Error?e.message:"費率儲存失敗")}finally{setBusy(false)}
  };
  const deleteRate=async(r:MileageRate)=>{
    if(!window.confirm(`確定刪除／停用費率「${r.ruleName}」？已被歷史行程引用的費率會保留資料但停止後續套用。`))return;
    setBusy(true);setMsg("");
    try{await api(`/mileage-rate-rules/${r.mileageRateRuleId}`,{method:"DELETE"});setMsg("費率已刪除／停用。");if(rateEditId===r.mileageRateRuleId)resetRate();await load()}
    catch(e){setMsg(e instanceof Error?e.message:"費率刪除失敗")}finally{setBusy(false)}
  };

  const resetProject=()=>{setProjectEditId(null);setProjectCode("");setProjectName("");setProjectDesc("");setProjectTeamId("");setProjectMode("List");setProjectStart(today);setProjectEnd("");setProjectActive(true)};
  const editProject=(p:Project)=>{setProjectEditId(p.projectId);setProjectCode(p.projectCode);setProjectName(p.projectName);setProjectDesc(p.description||"");setProjectTeamId(p.teamId?String(p.teamId):"");setProjectMode(p.locationMode);setProjectStart(p.startDate||today);setProjectEnd(p.endDate||"");setProjectActive(p.isActive)};
  const saveProject=async()=>{
    if(!projectCode.trim()||!projectName.trim())return setMsg("專案代碼與專案名稱為必填。");
    setBusy(true);setMsg("");
    try{
      const body={teamId:projectTeamId?Number(projectTeamId):null,projectCode:projectCode.trim(),projectName:projectName.trim(),description:projectDesc.trim()||null,locationMode:projectMode,startDate:projectStart||null,endDate:projectEnd||null,isActive:projectActive};
      if(projectEditId)await api(`/projects/${projectEditId}`,{method:"PUT",body:JSON.stringify(body)});
      else await api("/projects",{method:"POST",body:JSON.stringify(body)});
      setMsg(projectEditId?"專案已修改。":"專案已新增。");resetProject();await load();
    }catch(e){setMsg(e instanceof Error?e.message:"專案儲存失敗")}finally{setBusy(false)}
  };
  const deleteProject=async(p:Project)=>{
    if(!window.confirm(`確定刪除／停用專案「${p.projectName}」？既有行程仍會保留專案歷史資料。`))return;
    setBusy(true);setMsg("");
    try{await api(`/projects/${p.projectId}`,{method:"DELETE"});setMsg("專案已刪除／停用。");if(projectEditId===p.projectId)resetProject();await load()}
    catch(e){setMsg(e instanceof Error?e.message:"專案刪除失敗")}finally{setBusy(false)}
  };

  const resetType=()=>{setTypeEditId(null);setTypeCode("");setTypeName("");setTypeDesc("");setTypeSort("10");setTypeActive(true)};
  const editType=(v:VisitType)=>{setTypeEditId(v.visitTypeId);setTypeCode(v.visitTypeCode);setTypeName(v.visitTypeName);setTypeDesc(v.description||"");setTypeSort(String(v.sortOrder));setTypeActive(v.isActive)};
  const saveType=async()=>{
    if(!typeCode.trim()||!typeName.trim())return setMsg("拜訪形式代碼與名稱為必填。");
    setBusy(true);setMsg("");
    try{
      const body={visitTypeCode:typeCode.trim(),visitTypeName:typeName.trim(),description:typeDesc.trim()||null,sortOrder:Number(typeSort)||0,isActive:typeActive};
      if(typeEditId)await api(`/visit-types/${typeEditId}`,{method:"PUT",body:JSON.stringify(body)});
      else await api("/visit-types",{method:"POST",body:JSON.stringify(body)});
      setMsg(typeEditId?"拜訪形式已修改。":"拜訪形式已新增。");resetType();await load();
    }catch(e){setMsg(e instanceof Error?e.message:"拜訪形式儲存失敗")}finally{setBusy(false)}
  };
  const deleteType=async(v:VisitType)=>{
    if(!window.confirm(`確定刪除／停用拜訪形式「${v.visitTypeName}」？既有行程仍保留歷史資料。`))return;
    setBusy(true);setMsg("");
    try{await api(`/visit-types/${v.visitTypeId}`,{method:"DELETE"});setMsg("拜訪形式已刪除／停用。");if(typeEditId===v.visitTypeId)resetType();await load()}
    catch(e){setMsg(e instanceof Error?e.message:"拜訪形式刪除失敗")}finally{setBusy(false)}
  };

  if(section==="dashboard")return <>
    <div className="grid cols-4"><div className="card stat"><div className="label">本期行程</div><div className="value">{report.length}</div><div className="hint">目前查詢資料</div></div><div className="card stat"><div className="label">已核准</div><div className="value">{approved.length}</div><div className="hint">含補助快照</div></div><div className="card stat"><div className="label">專案</div><div className="value">{projects.filter(x=>x.isActive).length}</div><div className="hint">目前啟用專案</div></div><div className="card stat"><div className="label">目前每公里補助</div><div className="value">${money(current?.ratePerKm??0)}</div><div className="hint">依今天日期適用</div></div></div>
    <div className="grid cols-2" style={{marginTop:18}}>
      <div className="card"><div className="section-title"><div><h2>補助費率管理</h2><div className="sub">可新增、修改、刪除／停用；歷史核准行程保留費率快照。</div></div><a className="btn small" href="#/admin/rates">管理費率</a></div><div className="table-wrap"><table><thead><tr><th>生效日</th><th>每公里</th><th>狀態</th></tr></thead><tbody>{rates.slice(0,3).map(r=><tr key={r.mileageRateRuleId}><td>{r.effectiveFrom}</td><td>${money(r.ratePerKm)}</td><td>{r.isActive?"啟用":"停用"}</td></tr>)}</tbody></table></div></div>
      <div className="card"><div className="section-title"><h2>目前 UAT 架構</h2><span className="pill">Schema 1.5.x</span></div><div className="route-list"><div className="route-item"><div className="route-index">1</div><div><div className="route-name">Azure SQL 真實資料</div><div className="route-address">行程、核准、主檔、補助費率與報表皆由 API 讀寫</div></div></div><div className="route-item"><div className="route-index">2</div><div><div className="route-name">無 API 使用量控制</div><div className="route-address">依需求，管理者不提供 API 配額／用量維護功能</div></div></div></div></div>
    </div>
  </>;

  if(section==="rates")return <>
    <div className="grid cols-3"><div className="card stat"><div className="label">目前每公里補助</div><div className="value">${money(current?.ratePerKm??0)}</div><div className="hint">依今天日期套用</div></div><div className="card stat"><div className="label">費率版本數</div><div className="value">{rates.length}</div><div className="hint">含停用與歷史版本</div></div><div className="card stat"><div className="label">計算原則</div><div className="value" style={{fontSize:19}}>核定里程 × 費率</div><div className="hint">依行程日期抓取生效費率</div></div></div>
    <div className="card" style={{marginTop:18}}>
      <div className="section-title"><div><h2>{rateEditId?"修改補助費率":"新增補助費率"}</h2><div className="sub">生效日版本管理；刪除採安全停用，避免破壞歷史核准快照。</div></div>{rateEditId&&<button className="btn secondary small" onClick={resetRate}>取消修改</button>}</div>
      <div className="grid cols-3">
        <div className="field"><label>生效日期</label><input type="date" value={rateFrom} onChange={e=>setRateFrom(e.target.value)}/></div>
        <div className="field"><label>失效日期（選填）</label><input type="date" value={rateTo} onChange={e=>setRateTo(e.target.value)}/></div>
        <div className="field"><label>每公里補助金額（元）</label><input type="number" min="0" step="0.01" value={rateValue} onChange={e=>setRateValue(e.target.value)}/></div>
        <div className="field span-2"><label>規則名稱／備註</label><input value={rateName} onChange={e=>setRateName(e.target.value)} placeholder="例如：2026 年機車里程補助"/></div>
        <div className="field"><label>狀態</label><select value={rateActive?"1":"0"} onChange={e=>setRateActive(e.target.value==="1")}><option value="1">啟用</option><option value="0">停用</option></select></div>
      </div>
      <div className="actions"><button className="btn" disabled={busy} onClick={()=>void saveRate()}>{rateEditId?"儲存修改":"＋新增費率版本"}</button>{rateEditId&&<button className="btn secondary" onClick={resetRate}>取消</button>}</div>
      {msg&&<div className="note" style={{marginTop:12}}>{msg}</div>}
      <div className="table-wrap" style={{marginTop:16}}><table><thead><tr><th>生效日期</th><th>失效日期</th><th>每公里補助</th><th>狀態</th><th>規則</th><th>操作</th></tr></thead><tbody>{[...rates].sort((a,b)=>b.effectiveFrom.localeCompare(a.effectiveFrom)).map(r=><tr key={r.mileageRateRuleId}><td>{r.effectiveFrom}</td><td>{r.effectiveTo||"—"}</td><td><strong>${money(r.ratePerKm)}</strong></td><td><span className={`pill ${r.isActive?"ok":"warn"}`}>{r.isActive?"啟用":"停用"}</span></td><td>{r.ruleName}</td><td><div className="actions"><button className="btn small secondary" onClick={()=>editRate(r)}>修改</button><button className="btn small outline" disabled={busy} onClick={()=>void deleteRate(r)}>刪除</button></div></td></tr>)}</tbody></table></div>
    </div>
  </>;

  if(section==="projects")return <>
    <div className="grid cols-2">
      <div className="card">
        <div className="section-title"><div><h2>專案清單</h2><div className="sub">可新增、修改、刪除／停用專案主檔。</div></div>{projectEditId&&<button className="btn secondary small" onClick={resetProject}>取消修改</button>}</div>
        <div className="grid cols-2">
          <div className="field"><label>專案代碼</label><input value={projectCode} onChange={e=>setProjectCode(e.target.value)} placeholder="例如：CARE-002"/></div>
          <div className="field"><label>專案名稱</label><input value={projectName} onChange={e=>setProjectName(e.target.value)} placeholder="專案名稱"/></div>
          <div className="field"><label>歸屬小組</label><select value={projectTeamId} onChange={e=>setProjectTeamId(e.target.value)}><option value="">全組織</option>{teams.map(t=><option key={t.teamId} value={t.teamId}>{t.teamName}</option>)}</select></div>
          <div className="field"><label>地點模式</label><select value={projectMode} onChange={e=>setProjectMode(e.target.value)}><option value="List">固定清單</option><option value="SelfMaintained">自行維護</option></select></div>
          <div className="field"><label>開始日期</label><input type="date" value={projectStart} onChange={e=>setProjectStart(e.target.value)}/></div>
          <div className="field"><label>結束日期（選填）</label><input type="date" value={projectEnd} onChange={e=>setProjectEnd(e.target.value)}/></div>
          <div className="field span-2"><label>說明</label><input value={projectDesc} onChange={e=>setProjectDesc(e.target.value)} placeholder="專案說明"/></div>
          <div className="field"><label>狀態</label><select value={projectActive?"1":"0"} onChange={e=>setProjectActive(e.target.value==="1")}><option value="1">啟用</option><option value="0">停用</option></select></div>
        </div>
        <div className="actions"><button className="btn" disabled={busy} onClick={()=>void saveProject()}>{projectEditId?"儲存專案修改":"＋新增專案"}</button>{projectEditId&&<button className="btn secondary" onClick={resetProject}>取消</button>}</div>
        <div className="table-wrap" style={{marginTop:16}}><table><thead><tr><th>專案代碼</th><th>專案名稱</th><th>小組</th><th>地點模式</th><th>狀態</th><th>操作</th></tr></thead><tbody>{projects.map(p=><tr key={p.projectId}><td>{p.projectCode}</td><td>{p.projectName}</td><td>{teams.find(t=>t.teamId===p.teamId)?.teamName||"全組織"}</td><td><span className="pill">{p.locationMode==="List"?"固定清單":"自行維護"}</span></td><td><span className={`pill ${p.isActive?"ok":"warn"}`}>{p.isActive?"啟用":"停用"}</span></td><td><div className="actions"><button className="btn small secondary" onClick={()=>editProject(p)}>修改</button><button className="btn small outline" disabled={busy} onClick={()=>void deleteProject(p)}>刪除</button></div></td></tr>)}</tbody></table></div>
      </div>

      <div className="card">
        <div className="section-title"><div><h2>拜訪形式主檔</h2><div className="sub">可新增、修改、刪除／停用；停用後不再提供新行程選擇。</div></div>{typeEditId&&<button className="btn secondary small" onClick={resetType}>取消修改</button>}</div>
        <div className="grid cols-2">
          <div className="field"><label>拜訪形式代碼</label><input value={typeCode} onChange={e=>setTypeCode(e.target.value)} placeholder="例如：DOCUMENT"/></div>
          <div className="field"><label>拜訪形式名稱</label><input value={typeName} onChange={e=>setTypeName(e.target.value)} placeholder="例如：文件送達"/></div>
          <div className="field"><label>排序</label><input type="number" value={typeSort} onChange={e=>setTypeSort(e.target.value)}/></div>
          <div className="field"><label>狀態</label><select value={typeActive?"1":"0"} onChange={e=>setTypeActive(e.target.value==="1")}><option value="1">啟用</option><option value="0">停用</option></select></div>
          <div className="field span-2"><label>說明</label><input value={typeDesc} onChange={e=>setTypeDesc(e.target.value)} placeholder="拜訪形式說明"/></div>
        </div>
        <div className="actions"><button className="btn" disabled={busy} onClick={()=>void saveType()}>{typeEditId?"儲存拜訪形式修改":"＋新增拜訪形式"}</button>{typeEditId&&<button className="btn secondary" onClick={resetType}>取消</button>}</div>
        <div className="route-list" style={{marginTop:16}}>{types.map((v,i)=><div className="route-item" key={v.visitTypeId}><div className="route-index">{i+1}</div><div><div className="route-name">{v.visitTypeName} {!v.isActive&&<span className="pill warn">停用</span>}</div><div className="route-address">{v.visitTypeCode}｜排序 {v.sortOrder}{v.description?`｜${v.description}`:""}</div></div><div className="actions"><button className="btn small secondary" onClick={()=>editType(v)}>修改</button><button className="btn small outline" disabled={busy} onClick={()=>void deleteType(v)}>刪除</button></div></div>)}</div>
      </div>
    </div>
    {msg&&<div className="note" style={{marginTop:18}}>{msg}</div>}
    <div className="note" style={{marginTop:18}}>為保留歷史行程與報表一致性，「刪除」採安全停用；已停用主檔仍會保留於管理畫面，但外訪員新增行程時不再顯示。</div>
  </>;

  if(section==="users")return <><div className="grid cols-2"><div className="card"><div className="section-title"><h2>人員與權限</h2><span className="pill">UAT</span></div><div className="note">正式環境以 Microsoft Entra ID 驗證身分；應用程式內仍保存角色、小組及資料範圍。此頁不提供 API 使用量控制。</div></div><div className="card"><div className="section-title"><h2>示範帳號</h2></div><div className="route-list">{[["visitor01","外訪員"],["visitor02","外訪員"],["leader01","小組長"],["admin01","管理者"],["gov01","督導"]].map(([a,r],i)=><div className="route-item" key={a}><div className="route-index">{i+1}</div><div><div className="route-name">{a}</div><div className="route-address">{r}｜UAT Seed</div></div></div>)}</div></div></div><div className="placeholder-box" style={{marginTop:18}}>人員批次匯入、新增帳號與角色異動將另列 Master Data API 模組；目前不以假資料模擬寫入。</div></>;

  const reportCards=[
    ["每日行程明細","外訪員、時間、路線、三種里程、補助費率與補助金額"],
    ["里程彙總報表","自算、系統、核定里程與補助金額"],
    ["地點使用分析","依路線關鍵字檢視地點使用情形，並保留補助資訊"],
    ["核准補助清單","已核准行程、費率快照與補助金額"]
  ];
  return <><div className="grid cols-3">{reportCards.map(([name,desc])=><div className="card report-card" key={name}><h2>{name}</h2><p>{desc}</p><button className="btn small secondary" onClick={()=>reportDownload(name,filtered)}>下載查詢結果</button></div>)}</div><div className="card" style={{marginTop:18}}><div className="section-title"><div><h2>報表查詢</h2><div className="sub">所有里程相關報表均顯示每公里補助與補助金額。</div></div><button className="btn" onClick={load}>查詢</button></div><div className="grid cols-4"><div className="field"><label>開始日期</label><input type="date" value={start} onChange={e=>setStart(e.target.value)}/></div><div className="field"><label>結束日期</label><input type="date" value={end} onChange={e=>setEnd(e.target.value)}/></div><div className="field"><label>小組</label><select value={team} onChange={e=>setTeam(e.target.value)}><option value="">全部</option>{teamNames.map(x=><option key={x}>{x}</option>)}</select></div><div className="field"><label>狀態</label><select value={status} onChange={e=>setStatus(e.target.value)}><option value="">全部</option><option value="Approved">已核准</option><option value="PendingApproval">待核准</option><option value="Submitted">已送出</option><option value="Returned">已退回</option></select></div></div>{msg&&<div className="note danger-note">{msg}</div>}<div className="table-wrap"><table><thead><tr><th>日期</th><th>外訪員</th><th>小組</th><th>路線</th><th>自算</th><th>系統</th><th>核定</th><th>每公里補助</th><th>補助金額</th><th>狀態</th></tr></thead><tbody>{filtered.map(x=><tr key={x.tripNo}><td>{x.visitDate}</td><td>{x.visitorName}</td><td>{x.teamName||"—"}</td><td>{x.route}</td><td>{x.claimedDistanceKm??"—"}</td><td>{x.systemDistanceKm??"—"}</td><td>{x.approvedDistanceKm??"—"}</td><td>{x.ratePerKmSnapshot==null?"—":`$${money(x.ratePerKmSnapshot)}`}</td><td className="subsidy-strong">{x.approvedAmount==null?"—":`$${money(x.approvedAmount)}`}</td><td>{x.statusName}</td></tr>)}</tbody></table></div></div></>;
}
