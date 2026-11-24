/**
 * UI-only date representation: YYYY/MM/DD. Keep every API and query date as
 * ISO YYYY-MM-DD so existing comparisons, shortcuts and SQL behavior stay
 * unchanged.
 */
export function formatDateSlash(iso: string): string {
  return /^\d{4}-\d{2}-\d{2}$/.test(iso)
    ? iso.replace(/-/g, "/")
    : "";
}

export function parseDateSlash(text: string): string | null {
  const match = /^(\d{4})\/(\d{2})\/(\d{2})$/.exec(text.trim());
  if (!match) return null;
  const year = Number(match[1]), month = Number(match[2]), day = Number(match[3]);
  if (year < 1 || month < 1 || month > 12) return null;
  const leap = year % 4 === 0 && (year % 100 !== 0 || year % 400 === 0);
  const days = [31, leap ? 29 : 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31];
  if (day < 1 || day > days[month - 1]) return null;
  return `${match[1]}-${match[2]}-${match[3]}`;
}
