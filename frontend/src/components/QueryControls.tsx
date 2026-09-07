import { useRef } from 'react';
import { dateShortcutRange, type DateShortcut } from '../query-ux';
import { todayTaipei } from '../v160';

export function DateFilters({ start, end, onChange, label = '日期' }: {
  start: string; end: string; onChange: (start: string, end: string) => void; label?: string
}) {
  const startInput = useRef<HTMLInputElement>(null);
  const choices: Array<[DateShortcut, string]> = [
    ['month', '本月'], ['prev', '上月'], ['3m', '前3個月'], ['6m', '前6個月'], ['12m', '前12個月'], ['custom', '自訂']
  ];
  return <div>
    <div className="quick-filters">{choices.map(([key, text]) =>
      <button type="button" className="btn small outline" key={key} onClick={() => {
        if (key === 'custom') { startInput.current?.focus(); return; }
        const range = dateShortcutRange(key, todayTaipei());
        onChange(range.start, range.end);
      }}>{text}</button>)}</div>
    <div className="grid cols-2">
      <div className="field"><label>{label}起日<input ref={startInput} type="date" value={start} onChange={e => onChange(e.target.value, end)} /></label></div>
      <div className="field"><label>{label}迄日<input type="date" value={end} onChange={e => onChange(start, e.target.value)} /></label></div>
    </div>
    {start && end && start > end && <div role="alert" className="note danger-note">結束日期不可早於開始日期。</div>}
  </div>;
}

export function Pagination({ page, pageSize, totalCount, totalPages, busy, onPage, onPageSize }: {
  page: number; pageSize: number; totalCount: number; totalPages: number; busy?: boolean;
  onPage: (page: number) => void; onPageSize: (size: number) => void
}) {
  return <div className="pager">
    <label>每頁 <select aria-label="每頁筆數" value={pageSize} onChange={e => onPageSize(Number(e.target.value))}>
      {[20, 50, 100].map(n => <option key={n}>{n}</option>)}
    </select></label>
    <button className="btn small outline" disabled={busy || page <= 1} onClick={() => onPage(page - 1)}>上一頁</button>
    <span>第 {page} / {Math.max(totalPages, 1)} 頁，共 {totalCount} 筆</span>
    <button className="btn small outline" disabled={busy || page >= totalPages} onClick={() => onPage(page + 1)}>下一頁</button>
  </div>;
}
