import { describe, expect, it } from 'vitest';
import { dateShortcutRange, projectStatus } from './query-ux';

describe('Freeze QRY-001/002 dates', () => {
  it.each([
    ['month', '2026-09-07', '2026-09-01', '2026-09-07'],
    ['prev', '2026-01-07', '2025-12-01', '2025-12-31'],
    ['prev', '2024-03-15', '2024-02-01', '2024-02-29'],
    ['3m', '2026-09-07', '2026-06-07', '2026-09-07'],
    ['6m', '2026-09-07', '2026-03-07', '2026-09-07'],
    ['12m', '2026-09-07', '2025-09-07', '2026-09-07'],
    ['3m', '2026-05-31', '2026-02-28', '2026-05-31'],
    ['6m', '2024-08-31', '2024-02-29', '2024-08-31'],
    ['12m', '2024-02-29', '2023-02-28', '2024-02-29'],
    ['3m', '2026-01-31', '2025-10-31', '2026-01-31']
  ])('%s on %s preserves calendar boundaries', (kind, today, start, end) => {
    expect(dateShortcutRange(kind as 'month', today)).toEqual({ start, end });
  });
});

it.each([
  [false, '2027-01-01', null, '停用'],
  [true, '2027-01-01', null, '未開始'],
  [true, null, '2026-09-06', '已結束'],
  [true, '2026-09-07', '2026-09-07', '進行中'],
  [true, null, null, '進行中']
])('project status respects inclusive dates and inactive priority', (isActive, startDate, endDate, expected) => {
  expect(projectStatus({ isActive, startDate, endDate }, '2026-09-07')).toBe(expected);
});
