export type DateShortcut = 'month' | 'prev' | '3m' | '6m' | '12m' | 'custom';
export type TripDateShortcut = 'yesterday' | 'today' | 'tomorrow' | 'lastWeek' | 'thisWeek' | 'nextWeek' | 'prev' | 'month' | 'nextMonth' | 'custom';

export function dateShortcutRange(kind: Exclude<DateShortcut, 'custom'>, today: string) {
  const [year, month, day] = today.split('-').map(Number);
  const iso = (date: Date) => date.toISOString().slice(0, 10);
  if (kind === 'month') return { start: today.slice(0, 7) + '-01', end: today };
  if (kind === 'prev') return {
    start: iso(new Date(Date.UTC(year, month - 2, 1))),
    end: iso(new Date(Date.UTC(year, month - 1, 0)))
  };
  const months = Number(kind.slice(0, -1));
  const target = new Date(Date.UTC(year, month - 1 - months, 1));
  const lastDay = new Date(Date.UTC(target.getUTCFullYear(), target.getUTCMonth() + 1, 0)).getUTCDate();
  target.setUTCDate(Math.min(day, lastDay));
  return { start: iso(target), end: today };
}

export function tripDateShortcutRange(kind: Exclude<TripDateShortcut, 'custom'>, today: string) {
  const [year, month, day] = today.split('-').map(Number);
  const iso = (date: Date) => date.toISOString().slice(0, 10);
  const offset = (days: number) => iso(new Date(Date.UTC(year, month - 1, day + days)));
  if (kind === 'yesterday' || kind === 'today' || kind === 'tomorrow') {
    const date = offset(kind === 'yesterday' ? -1 : kind === 'tomorrow' ? 1 : 0);
    return {start: date, end: date};
  }
  if (kind === 'lastWeek' || kind === 'thisWeek' || kind === 'nextWeek') {
    const weekday = (new Date(Date.UTC(year, month - 1, day)).getUTCDay() + 6) % 7;
    const shift = kind === 'lastWeek' ? -7 : kind === 'nextWeek' ? 7 : 0;
    return {start: offset(shift - weekday), end: offset(shift - weekday + 6)};
  }
  if (kind === 'month') return { start: today.slice(0, 7) + '-01', end: iso(new Date(Date.UTC(year, month, 0))) };
  if (kind === 'nextMonth') return {start: iso(new Date(Date.UTC(year, month, 1))), end: iso(new Date(Date.UTC(year, month + 1, 0)))};
  if (kind === 'prev') return {
    start: iso(new Date(Date.UTC(year, month - 2, 1))),
    end: iso(new Date(Date.UTC(year, month - 1, 0)))
  };
  throw new Error(`Unsupported trip date shortcut: ${kind}`);
}

export function projectStatus(p: { isActive: boolean; startDate?: string | null; endDate?: string | null }, today: string) {
  if (!p.isActive) return '停用';
  if (p.startDate && p.startDate > today) return '未開始';
  if (p.endDate && p.endDate < today) return '已結束';
  return '進行中';
}

export type ProjectDateShortcut = 'all'|'month'|'prev'|'nextMonth'|'future'|'custom';
export function projectDateShortcutRange(kind:Exclude<ProjectDateShortcut,'custom'>,today:string) {
  if(kind==='all')return {start:'',end:''};
  if(kind==='future')return {start:tripDateShortcutRange('tomorrow',today).start,end:''};
  return tripDateShortcutRange(kind,today);
}
