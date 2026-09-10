import { useEffect, useMemo, useState } from 'react';
import { ApiError, api, apiDownload } from '../api';
import ProjectLocationManager from '../components/ProjectLocationManager';
import { DateFilters, Pagination } from '../components/QueryControls';
import { projectStatus } from '../query-ux';
import { usePagedQuery } from '../use-query';
import { todayTaipei } from '../v160';
import type { ImportConfirmResult, ImportPreview, Project, Team, VisitType } from '../types';
import { activeVisitTypes, projectSavePayload, reorderExpectedOrder, visitTypeSavePayload } from '../db-work1-master-data';

type Props = { section: 'projects' | 'visit-types' };

type ProjectRow = Project & { locationCount: number };

export default function DbWork1MasterDataPage({ section }: Props) {
  return section === 'projects' ? <ProjectsPanel /> : <VisitTypesPanel />;
}

function ProjectsPanel() {
  const [teams, setTeams] = useState<Team[]>([]);
  const [edit, setEdit] = useState<Project | null>(null);
  const [code, setCode] = useState('');
  const [name, setName] = useState('');
  const [teamId, setTeamId] = useState('');
  const [mode, setMode] = useState('List');
  const [start, setStart] = useState(todayTaipei());
  const [end, setEnd] = useState('');
  const [desc, setDesc] = useState('');
  const [busy, setBusy] = useState(false);
  const [msg, setMsg] = useState('');
  const [keyword, setKeyword] = useState('');
  const [filterTeam, setFilterTeam] = useState('');
  const [filterStatus, setFilterStatus] = useState('');
  const [filterStart, setFilterStart] = useState('');
  const [filterEnd, setFilterEnd] = useState('');
  const query = usePagedQuery<ProjectRow>(
    '/admin/projects/search',
    { keyword, teamId: filterTeam, status: filterStatus, startDate: filterStart, endDate: filterEnd },
    !filterStart || !filterEnd || filterStart <= filterEnd
  );

  useEffect(() => {
    api<Team[]>('/teams').then(setTeams).catch(e => setMsg(e.message));
  }, []);

  const reload = () => query.reload();
  const reset = () => {
    setEdit(null);
    setCode('');
    setName('');
    setTeamId('');
    setMode('List');
    setStart(todayTaipei());
    setEnd('');
    setDesc('');
  };
  const open = (p: Project) => {
    setEdit(p);
    setCode(p.projectCode);
    setName(p.projectName);
    setTeamId(p.teamId ? String(p.teamId) : '');
    setMode(p.locationMode);
    setStart(p.startDate || todayTaipei());
    setEnd(p.endDate || '');
    setDesc(p.description || '');
  };
  const conflict = (e: unknown, action: string) => {
    if (e instanceof ApiError && e.status === 409) {
      setMsg(`${action}發生版本衝突；已重新載入最新資料，系統沒有自動重試。`);
      reset();
      reload();
      return true;
    }
    return false;
  };
  const save = async () => {
    setBusy(true);
    setMsg('');
    try {
      const body = projectSavePayload({
        teamId: teamId ? Number(teamId) : null,
        projectCode: code,
        projectName: name,
        description: desc || null,
        locationMode: mode,
        startDate: start || null,
        endDate: end || null
      }, edit);
      if (edit) await api(`/projects/${edit.projectId}`, { method: 'PUT', body: JSON.stringify(body) });
      else await api('/projects', { method: 'POST', body: JSON.stringify(body) });
      setMsg(edit ? '專案已修改。' : '專案已新增。');
      reset();
      reload();
    } catch (e) {
      if (!conflict(e, '專案儲存')) setMsg(e instanceof Error ? e.message : '儲存失敗');
    } finally {
      setBusy(false);
    }
  };
  const lifecycle = async (p: Project, action: 'deactivate' | 'reactivate') => {
    const verb = action === 'deactivate' ? '停用' : '重新啟用';
    if (!window.confirm(`確定${verb}專案「${p.projectName}」？歷史 Snapshot 不受影響。`)) return;
    setBusy(true);
    setMsg('');
    try {
      await api(`/projects/${p.projectId}/${action}`, {
        method: 'POST',
        body: JSON.stringify({ rowVersion: p.rowVersion })
      });
      setMsg(`專案已${verb}。`);
      if (edit?.projectId === p.projectId) reset();
      reload();
    } catch (e) {
      if (!conflict(e, `專案${verb}`)) setMsg(e instanceof Error ? e.message : `${verb}失敗`);
    } finally {
      setBusy(false);
    }
  };

  return <>
    <div className="grid cols-2">
      <div className="card">
        <div className="section-title"><h2>專案主檔</h2>{edit && <button className="btn small outline" onClick={reset}>取消修改</button>}</div>
        {msg && <div className="note">{msg}</div>}
        <div className="grid cols-2">
          <div className="field"><label>專案代碼</label><input value={code} onChange={e => setCode(e.target.value)} /></div>
          <div className="field"><label>專案名稱</label><input value={name} onChange={e => setName(e.target.value)} /></div>
          <div className="field"><label>歸屬小組</label><select value={teamId} onChange={e => setTeamId(e.target.value)}><option value="">全組織</option>{teams.map(t => <option key={t.teamId} value={t.teamId}>{t.teamName}</option>)}</select></div>
          <div className="field"><label>預設地點方式</label><select value={mode} onChange={e => setMode(e.target.value)}><option value="List">專案清單優先</option><option value="SelfMaintained">臨時維護優先</option></select></div>
          <div className="field"><label>開始日期</label><input type="date" value={start} onChange={e => setStart(e.target.value)} /></div>
          <div className="field"><label>結束日期</label><input type="date" value={end} onChange={e => setEnd(e.target.value)} /></div>
          <div className="field span-2"><label>說明</label><input value={desc} onChange={e => setDesc(e.target.value)} /></div>
        </div>
        <button className="btn" disabled={busy} onClick={() => void save()}>{edit ? '儲存專案' : '新增專案'}</button>
        {edit && mode === 'List' && <ProjectLocationManager projectId={edit.projectId} projectName={edit.projectName} />}
        <div className="grid cols-3" style={{ marginTop: 18 }}>
          <label>專案搜尋<input value={keyword} placeholder="專案代碼或名稱" onChange={e => setKeyword(e.target.value)} /></label>
          <label>查詢小組<select value={filterTeam} onChange={e => setFilterTeam(e.target.value)}><option value="">全部</option>{teams.map(t => <option key={t.teamId} value={t.teamId}>{t.teamName}</option>)}</select></label>
          <label>專案狀態<select value={filterStatus} onChange={e => setFilterStatus(e.target.value)}><option value="">全部狀態</option><option value="NotStarted">未開始</option><option value="InProgress">進行中</option><option value="Ended">已結束</option><option value="Inactive">停用</option></select></label>
        </div>
        <DateFilters label="專案期間" start={filterStart} end={filterEnd} onChange={(s, e) => { setFilterStart(s); setFilterEnd(e); }} />
        <div className="table-wrap"><table><thead><tr><th>專案</th><th>歸屬小組</th><th>有效期間</th><th>地點規則</th><th>固定地點</th><th>狀態</th><th>操作</th></tr></thead><tbody>{query.data.items.map(p => {
          const teamName = p.teamId ? teams.find(t => t.teamId === p.teamId)?.teamName || `Team ${p.teamId}` : '全組織';
          const period = `${p.startDate || '不限'}～${p.endDate || '無期限'}`;
          return <tr key={p.projectId}><td><strong>{p.projectCode}</strong><div>{p.projectName}</div>{p.description && <div className="sub">{p.description}</div>}</td><td>{teamName}</td><td>{period}</td><td>{p.locationMode === 'List' ? '專案清單優先' : '臨時維護優先'}</td><td>{p.locationMode === 'List' ? `${p.locationCount ?? 0} 筆` : '—'}</td><td>{p.isActive ? <span className="pill ok">{projectStatus(p, todayTaipei())}</span> : <span className="pill warn">停用</span>}</td><td><div className="actions"><button className="btn small secondary" onClick={() => open(p)}>修改</button>{p.isActive ? <button className="btn small outline" disabled={busy} onClick={() => void lifecycle(p, 'deactivate')}>停用</button> : <button className="btn small ok" disabled={busy} onClick={() => void lifecycle(p, 'reactivate')}>重新啟用</button>}</div></td></tr>;
        })}</tbody></table></div>
        <Pagination {...query.data} page={query.page} pageSize={query.pageSize} busy={query.loading} onPage={query.setPage} onPageSize={query.setPageSize} />
        {query.error && <div role="alert" className="note danger-note">{query.error}</div>}
      </div>
      <div className="card"><ProjectImportPanel onDone={reload} /></div>
    </div>
  </>;
}

function ProjectImportPanel({ onDone }: { onDone: () => void }) {
  const [file, setFile] = useState<File | null>(null);
  const [preview, setPreview] = useState<ImportPreview | null>(null);
  const [result, setResult] = useState<ImportConfirmResult | null>(null);
  const [msg, setMsg] = useState('');
  const [busy, setBusy] = useState(false);
  const template = () => apiDownload('/imports/projects/template', '專案主檔匯入範例.xlsx');
  const doPreview = async () => {
    if (!file) return setMsg('請先選擇 .xlsx、.xls 或 .csv 檔案。');
    setBusy(true); setMsg('');
    try {
      const form = new FormData(); form.append('file', file);
      setPreview(await api<ImportPreview>('/imports/projects/preview', { method: 'POST', body: form }, 120000));
      setResult(null);
    } catch (e) { setMsg(e instanceof Error ? e.message : '預覽失敗'); }
    finally { setBusy(false); }
  };
  const confirm = async () => {
    if (!preview) return;
    setBusy(true);
    try {
      const r = await api<ImportConfirmResult>(`/imports/${preview.importBatchId}/confirm`, { method: 'POST' }, 120000);
      setResult(r); setMsg('匯入完成。'); onDone();
    } catch (e) { setMsg(e instanceof Error ? e.message : '匯入失敗'); }
    finally { setBusy(false); }
  };
  return <div className="import-panel"><div className="section-title"><strong>批次匯入</strong><button className="btn small outline" onClick={() => void template()}>下載 Excel 範例</button></div><div className="actions"><input type="file" accept=".xlsx,.xls,.csv" onChange={e => { setFile(e.target.files?.[0] || null); setPreview(null); }} /><button className="btn secondary" disabled={busy} onClick={() => void doPreview()}>預覽與驗證</button></div>{msg && <div className="note">{msg}</div>}{preview && <><div className={`note ${preview.errorCount ? 'danger-note' : 'ok-note'}`}>共 {preview.totalCount} 列｜可匯入 {preview.validCount}｜錯誤 {preview.errorCount}</div><div className="actions">{preview.errorCount > 0 && <button className="btn outline" onClick={() => void apiDownload(`/imports/${preview.importBatchId}/errors.xlsx`, '匯入錯誤.xlsx')}>下載錯誤 Excel</button>}<button className="btn ok" disabled={preview.errorCount > 0 || busy} onClick={() => void confirm()}>確認匯入</button></div></>}{result && <div className="note ok-note">新增 {result.created}／更新 {result.updated}／無變更 {result.unchanged}／失敗 {result.failed}</div>}</div>;
}

function VisitTypesPanel() {
  const [rows, setRows] = useState<VisitType[]>([]);
  const [edit, setEdit] = useState<VisitType | null>(null);
  const [code, setCode] = useState('');
  const [name, setName] = useState('');
  const [desc, setDesc] = useState('');
  const [busy, setBusy] = useState(false);
  const [msg, setMsg] = useState('');
  const load = () => api<VisitType[]>('/visit-types').then(setRows).catch(e => setMsg(e.message));
  useEffect(() => { void load(); }, []);
  const active = useMemo(() => activeVisitTypes(rows), [rows]);
  const inactive = useMemo(() => rows.filter(x => !x.isActive).sort((a, b) => a.visitTypeName.localeCompare(b.visitTypeName)), [rows]);
  const reset = () => { setEdit(null); setCode(''); setName(''); setDesc(''); };
  const open = (v: VisitType) => { setEdit(v); setCode(v.visitTypeCode); setName(v.visitTypeName); setDesc(v.description || ''); };
  const conflict = (e: unknown, action: string) => {
    if (e instanceof ApiError && e.status === 409) {
      setMsg(`${action}發生版本衝突；已重新載入最新資料，系統沒有自動重試。`);
      reset();
      void load();
      return true;
    }
    return false;
  };
  const save = async () => {
    setBusy(true); setMsg('');
    try {
      const body = visitTypeSavePayload({ visitTypeCode: code, visitTypeName: name, description: desc || null }, edit);
      if (edit) await api(`/visit-types/${edit.visitTypeId}`, { method: 'PUT', body: JSON.stringify(body) });
      else await api('/visit-types', { method: 'POST', body: JSON.stringify(body) });
      setMsg(edit ? '拜訪形式已修改。' : '拜訪形式已新增。'); reset(); await load();
    } catch (e) { if (!conflict(e, '拜訪形式儲存')) setMsg(e instanceof Error ? e.message : '儲存失敗'); }
    finally { setBusy(false); }
  };
  const lifecycle = async (v: VisitType, action: 'deactivate' | 'reactivate') => {
    const verb = action === 'deactivate' ? '停用' : '重新啟用';
    if (!window.confirm(`確定${verb}拜訪形式「${v.visitTypeName}」？歷史 Snapshot 不受影響。`)) return;
    setBusy(true); setMsg('');
    try {
      await api(`/visit-types/${v.visitTypeId}/${action}`, { method: 'POST', body: JSON.stringify({ rowVersion: v.rowVersion }) });
      setMsg(`拜訪形式已${verb}。`); if (edit?.visitTypeId === v.visitTypeId) reset(); await load();
    } catch (e) { if (!conflict(e, `拜訪形式${verb}`)) { setMsg(e instanceof Error ? e.message : `${verb}失敗`); await load(); } }
    finally { setBusy(false); }
  };
  const move = async (v: VisitType, direction: 'up' | 'down') => {
    const expectedOrder = reorderExpectedOrder(rows, v.visitTypeId, direction);
    if (expectedOrder.join(',') === active.map(x => x.visitTypeId).join(',')) return;
    setBusy(true); setMsg('');
    try {
      setRows(await api<VisitType[]>('/visit-types/reorder', { method: 'POST', body: JSON.stringify({ expectedOrder }) }));
      setMsg('拜訪形式排序已更新。');
    } catch (e) {
      setMsg(e instanceof ApiError && e.status === 409
        ? '拜訪形式排序發生版本衝突；已重新載入最新資料，系統沒有自動重試。'
        : '拜訪形式清單已變更或排序驗證失敗；已重新載入最新資料，系統沒有自動重試。');
      await load();
    } finally { setBusy(false); }
  };

  return <div className="card">
    <div className="section-title"><div><h2>拜訪形式</h2><div className="sub">拜訪形式為全域主檔；排序送出完整啟用 membership，衝突時只重新載入，不自動重試。</div></div>{edit && <button className="btn small outline" onClick={reset}>取消修改</button>}</div>
    {msg && <div className="note">{msg}</div>}
    <div className="grid cols-2"><div className="field"><label>代碼</label><input value={code} onChange={e => setCode(e.target.value)} /></div><div className="field"><label>名稱</label><input value={name} onChange={e => setName(e.target.value)} /></div><div className="field span-2"><label>說明</label><input value={desc} onChange={e => setDesc(e.target.value)} /></div></div>
    <button className="btn" onClick={() => void save()} disabled={busy}>{edit ? '儲存拜訪形式' : '新增拜訪形式'}</button>
    <div className="route-list">{active.map((v, index) => <div className="route-item" key={v.visitTypeId}><div className="route-index">{index + 1}</div><div><div className="route-name">{v.visitTypeName}</div><div className="route-address">{v.visitTypeCode}</div></div><div className="actions"><button className="btn small outline" aria-label={`上移 ${v.visitTypeName}`} disabled={busy || index === 0} onClick={() => void move(v, 'up')}>↑</button><button className="btn small outline" aria-label={`下移 ${v.visitTypeName}`} disabled={busy || index === active.length - 1} onClick={() => void move(v, 'down')}>↓</button><button className="btn small secondary" onClick={() => open(v)}>修改</button><button className="btn small outline" disabled={busy} onClick={() => void lifecycle(v, 'deactivate')}>停用</button></div></div>)}</div>
    {inactive.length > 0 && <><h3 style={{ marginTop: 24 }}>已停用</h3><div className="route-list">{inactive.map(v => <div className="route-item" key={v.visitTypeId}><div className="route-index">—</div><div><div className="route-name">{v.visitTypeName}</div><div className="route-address">{v.visitTypeCode}｜停用</div></div><div className="actions"><button className="btn small secondary" onClick={() => open(v)}>修改</button><button className="btn small ok" disabled={busy} onClick={() => void lifecycle(v, 'reactivate')}>重新啟用</button></div></div>)}</div></>}
  </div>;
}
