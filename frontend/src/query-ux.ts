export type DateShortcut = 'month' | 'prev' | '3m' | '6m' | '12m' | 'custom';

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

export function projectStatus(p: { isActive: boolean; startDate?: string | null; endDate?: string | null }, today: string) {
  if (!p.isActive) return '停用';
  if (p.startDate && p.startDate > today) return '未開始';
  if (p.endDate && p.endDate < today) return '已結束';
  return '進行中';
}
