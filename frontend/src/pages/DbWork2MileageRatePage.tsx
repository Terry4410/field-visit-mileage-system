import { useEffect, useMemo, useState } from 'react';
import { ApiError, api } from '../api';
import { money, todayTaipei } from '../v160';

type MileageRate = {
  mileageRateRuleId: number;
  organizationId?: number | null;
  ruleName: string;
  vehicleType: string;
  ratePerKm: number;
  effectiveFrom: string;
  effectiveTo?: string | null;
  isActive: boolean;
  rowVersion: string;
};

type MileageRateImpact = {
  effectiveFrom: string;
  vehicleType: string;
  approvedTripCount: number;
  firstApprovedVisitDate: string | null;
  lastApprovedVisitDate: string | null;
  requiresAcknowledgement: boolean;
};

export default function DbWork2MileageRatePage() {
  const [rows, setRows] = useState<MileageRate[]>([]);
  const [edit, setEdit] = useState<MileageRate | null>(null);
  const [name, setName] = useState('');
  const [rate, setRate] = useState('2.50');
  const [from, setFrom] = useState(todayTaipei());
  const [busy, setBusy] = useState(false);
  const [msg, setMsg] = useState('');

  const load = async () => setRows(await api<MileageRate[]>('/mileage-rate-rules'));
  useEffect(() => { void load().catch(e => setMsg(e instanceof Error ? e.message : '讀取費率失敗')); }, []);

  const reset = () => {
    setEdit(null);
    setName('');
    setRate('2.50');
    setFrom(todayTaipei());
  };
  const open = (row: MileageRate) => {
    setEdit(row);
    setName(row.ruleName);
    setRate(String(row.ratePerKm));
    setFrom(row.effectiveFrom);
  };
  const impact = (effectiveFrom: string) =>
    api<MileageRateImpact>(`/mileage-rate-rules/impact?effectiveFrom=${encodeURIComponent(effectiveFrom)}&vehicleType=Motorcycle`);
  const impactText = (x: MileageRateImpact, verb: string) =>
    `此異動自 ${x.effectiveFrom} 起可能影響費率判讀；該日期之後已有 ${x.approvedTripCount} 筆已核准且具有費率快照的行程${x.firstApprovedVisitDate && x.lastApprovedVisitDate ? `（${x.firstApprovedVisitDate}～${x.lastApprovedVisitDate}）` : ''}。\n\n${verb}後不會自動重算既有 Snapshot，因此可能出現同一行程日期具有不同歷史費率。若需追溯調整，應透過更正流程建立新 Snapshot。\n\n是否仍要繼續？`;
  const conflict = async (e: unknown, action: string) => {
    if (!(e instanceof ApiError) || e.status !== 409) return false;
    setMsg(`${action}發生版本衝突；已重新載入最新資料，系統沒有自動重試。`);
    reset();
    try { await load(); } catch { /* keep conflict message */ }
    return true;
  };

  const save = async () => {
    setBusy(true);
    setMsg('');
    try {
      const nextRate = Number(rate);
      const material = !edit || edit.ratePerKm !== nextRate || edit.effectiveFrom !== from || edit.vehicleType !== 'MOTORCYCLE' || !edit.isActive;
      let acknowledged = false;
      if (material) {
        const impactFrom = edit && edit.effectiveFrom < from ? edit.effectiveFrom : from;
        const x = await impact(impactFrom);
        if (x.requiresAcknowledgement) {
          if (!window.confirm(impactText(x, edit ? '修改費率版本' : '新增費率版本'))) return;
          acknowledged = true;
        }
      }
      const body = {
        ruleName: name,
        vehicleType: 'Motorcycle',
        ratePerKm: nextRate,
        effectiveFrom: from,
        effectiveTo: null,
        isActive: true,
        acknowledgeHistoricalImpact: acknowledged,
        ...(edit ? { rowVersion: edit.rowVersion } : {})
      };
      if (edit) await api(`/mileage-rate-rules/${edit.mileageRateRuleId}`, { method: 'PUT', body: JSON.stringify(body) });
      else await api('/mileage-rate-rules', { method: 'POST', body: JSON.stringify(body) });
      setMsg('費率版本已儲存；前後版本失效日期已由系統自動銜接。歷史 Snapshot 不會自動重算。');
      reset();
      await load();
    } catch (e) {
      if (!await conflict(e, '費率儲存')) setMsg(e instanceof Error ? e.message : '儲存失敗');
    } finally {
      setBusy(false);
    }
  };

  const deactivate = async (row: MileageRate) => {
    setBusy(true);
    setMsg('');
    try {
      const x = await impact(row.effectiveFrom);
      let acknowledged = false;
      if (x.requiresAcknowledgement) {
        if (!window.confirm(impactText(x, '停用此費率版本'))) return;
        acknowledged = true;
      } else if (!window.confirm(`確定停用 ${row.effectiveFrom} 起生效的費率版本？歷史核准費率快照不受影響。`)) return;
      await api(`/mileage-rate-rules/${row.mileageRateRuleId}?acknowledgeHistoricalImpact=${acknowledged ? 'true' : 'false'}&rowVersion=${encodeURIComponent(row.rowVersion)}`, { method: 'DELETE' });
      setMsg('費率版本已停用，剩餘有效版本日期已重新銜接；歷史 Snapshot 不會自動重算。');
      if (edit?.mileageRateRuleId === row.mileageRateRuleId) reset();
      await load();
    } catch (e) {
      if (!await conflict(e, '費率停用')) setMsg(e instanceof Error ? e.message : '停用失敗');
    } finally {
      setBusy(false);
    }
  };

  const current = useMemo(() => rows
    .filter(r => r.isActive && r.effectiveFrom <= todayTaipei() && (!r.effectiveTo || r.effectiveTo >= todayTaipei()))
    .sort((a, b) => b.effectiveFrom.localeCompare(a.effectiveFrom))[0], [rows]);

  return <>
    <div className="grid cols-3">
      <Stat label="目前每公里補助" value={money(current?.ratePerKm)} />
      <Stat label="版本數" value={rows.length} />
      <Stat label="日期規則" value="自動銜接" hint="只輸入生效日" />
    </div>
    <div className="card" style={{ marginTop: 18 }}>
      <div className="section-title">
        <div><h2>{edit ? '修改費率版本' : '新增費率版本'}</h2><div className="sub">管理者只維護生效日期；上一版失效日由後端自動設定為新生效日前一天。</div></div>
        {edit && <button className="btn small outline" onClick={reset}>取消修改</button>}
      </div>
      <div className="grid cols-3">
        <div className="field"><label htmlFor="dbw2-rate-effective-from">生效日期</label><input id="dbw2-rate-effective-from" type="date" value={from} onChange={e => setFrom(e.target.value)} /></div>
        <div className="field"><label htmlFor="dbw2-rate-per-km">每公里補助</label><input id="dbw2-rate-per-km" type="number" step="0.01" min="0" value={rate} onChange={e => setRate(e.target.value)} /></div>
        <div className="field"><label htmlFor="dbw2-rate-name">規則名稱／備註</label><input id="dbw2-rate-name" value={name} onChange={e => setName(e.target.value)} /></div>
      </div>
      <button className="btn" disabled={busy} onClick={() => void save()}>{edit ? '儲存修改' : '新增費率版本'}</button>
      {msg && <div className="note">{msg}</div>}
      <div className="table-wrap"><table><thead><tr><th>生效日期</th><th>失效日期（系統）</th><th>每公里</th><th>規則</th><th>狀態</th><th>操作</th></tr></thead><tbody>{rows.map(row => <tr key={row.mileageRateRuleId}><td>{row.effectiveFrom}</td><td>{row.effectiveTo || '無期限'}</td><td>{money(row.ratePerKm)}</td><td>{row.ruleName}</td><td>{row.isActive ? '啟用' : '停用'}</td><td><div className="actions"><button className="btn small secondary" onClick={() => open(row)}>修改</button>{row.isActive && <button className="btn small outline" disabled={busy} onClick={() => void deactivate(row)}>停用</button>}</div></td></tr>)}</tbody></table></div>
    </div>
  </>;
}

function Stat({ label, value, hint }: { label: string; value: string | number; hint?: string }) {
  return <div className="card stat"><div className="label">{label}</div><div className="value">{value}</div>{hint && <div className="hint">{hint}</div>}</div>;
}
