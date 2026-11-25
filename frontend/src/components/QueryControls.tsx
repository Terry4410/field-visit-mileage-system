import { useEffect, useId, useRef, useState, type Ref } from 'react';
import { formatDateSlash, parseDateSlash } from '../date-slash';
import { dateShortcutRange, tripDateShortcutRange, projectDateShortcutRange, type DateShortcut, type TripDateShortcut, type ProjectDateShortcut } from '../query-ux';
import { todayTaipei } from '../v160';

function SlashDateField({label, value, onChange, inputRef}: {
  label: string;
  value: string;
  onChange: (iso: string) => void;
  inputRef?: Ref<HTMLInputElement>;
}) {
  const id = useId();
  const [display, setDisplay] = useState(() => formatDateSlash(value));
  const [invalid, setInvalid] = useState(false);

  // Quick-range presets and native calendar selections remain the authority.
  useEffect(() => {
    setDisplay(formatDateSlash(value));
    setInvalid(false);
  }, [value]);

  const typeDate = (next: string) => {
    setDisplay(next);
    if (!next.trim()) {
      setInvalid(false);
      onChange('');
      return;
    }
    const iso = parseDateSlash(next);
    if (iso) {
      setInvalid(false);
      onChange(iso);
    } else {
      // An incomplete or invalid value must never reach the query API.
      setInvalid(next.length >= 10);
    }
  };

  return <div className="field">
    <label htmlFor={id}>{label}</label>
    <div className="slash-date-control">
      <input
        id={id}
        ref={inputRef}
        type="text"
        inputMode="text"
        autoComplete="off"
        spellCheck={false}
        maxLength={10}
        value={display}
        placeholder="YYYY/MM/DD"
        aria-invalid={invalid}
        onChange={e => typeDate(e.target.value)}
        onBlur={() => {
          if (display.trim() && !parseDateSlash(display)) setInvalid(true);
        }}
      />
      <span className="slash-date-calendar-icon" aria-hidden="true">
        <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8">
          <rect x="3" y="5" width="18" height="16" rx="2"/>
          <path d="M7 3v4M17 3v4M3 10h18M7 14h2M12 14h2M7 18h2"/>
        </svg>
      </span>
      <input
        type="date"
        value={value}
        aria-label={`${label}：開啟日曆`}
        title={`使用日曆選擇${label}`}
        onChange={e => {
          const iso = e.currentTarget.value;
          setDisplay(formatDateSlash(iso));
          setInvalid(false);
          onChange(iso);
        }}
      />
    </div>
    {invalid && <div className="note danger-note" role="alert">
      請輸入有效日期 YYYY/MM/DD；目前輸入尚未套用。
    </div>}
  </div>;
}

export function DateFilters({ start, end, onChange, label = '日期', displayFormat = 'native', shortcutSet = 'legacy' }: {
  start: string; end: string; onChange: (start: string, end: string) => void; label?: string; displayFormat?: 'native' | 'slash'; shortcutSet?: 'legacy' | 'trip' | 'project'
}) {
  const startInput = useRef<HTMLInputElement>(null);
  const choices: Array<[DateShortcut | TripDateShortcut | ProjectDateShortcut, string]> = shortcutSet === 'trip'
    ? [['yesterday', '昨日'], ['today', '今日'], ['tomorrow', '明日'], ['lastWeek', '上週'], ['thisWeek', '本週'], ['nextWeek', '下週'], ['prev', '上月'], ['month', '本月'], ['nextMonth', '下月'], ['custom', '自訂']]
    : shortcutSet === 'project'
      ? [['all','全部'],['month','本月'],['prev','上月'],['nextMonth','下月'],['future','未來'],['custom','自訂']]
      : [['month', '本月'], ['prev', '上月'], ['3m', '前3個月'], ['6m', '前6個月'], ['12m', '前12個月'], ['custom', '自訂']];
  return <div>
    <div className={`quick-filters ${shortcutSet === 'trip' ? 'trip-date-shortcuts' : ''}`}>{choices.map(([key, text]) =>
      <button type="button" className="btn small outline" key={key} onClick={() => {
        if (key === 'custom') { startInput.current?.focus(); return; }
        const range = shortcutSet === 'trip'
          ? tripDateShortcutRange(key as Exclude<TripDateShortcut, 'custom'>, todayTaipei())
          : shortcutSet === 'project' ? projectDateShortcutRange(key as Exclude<ProjectDateShortcut,'custom'>,todayTaipei())
          : dateShortcutRange(key as Exclude<DateShortcut, 'custom'>, todayTaipei());
        onChange(range.start, range.end);
      }}>{text}</button>)}</div>
    <div className="grid cols-2">
      {displayFormat === 'slash'
        ? <SlashDateField label={`${label}起日`} value={start} inputRef={startInput} onChange={s => onChange(s, end)} />
        : <div className="field"><label>{label}起日<input ref={startInput} type="date" value={start} onChange={e => onChange(e.target.value, end)} /></label></div>}
      {displayFormat === 'slash'
        ? <SlashDateField label={`${label}迄日`} value={end} onChange={e => onChange(start, e)} />
        : <div className="field"><label>{label}迄日<input type="date" value={end} onChange={e => onChange(start, e.target.value)} /></label></div>}
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
