import { useEffect, useRef, useState } from 'react';
import { api } from './api';
import { qs } from './v160';
import type { PagedResult } from './types';

export function useDebouncedValue<T>(value: T, delay = 400) {
  const [settled, setSettled] = useState(value);
  useEffect(() => {
    const timer = setTimeout(() => setSettled(value), delay);
    return () => clearTimeout(timer);
  }, [value, delay]);
  return settled;
}

type Filters = Record<string, string | number | boolean | null | undefined>;
export function usePagedQuery<T>(path: string, filters: Filters, enabled = true) {
  const rawKey = JSON.stringify(filters);
  const currentRawKey = useRef(rawKey);
  currentRawKey.current = rawKey;
  const textKeys = ['keyword', 'q', 'city', 'district'];
  const rawText = JSON.stringify(Object.fromEntries(Object.entries(filters).filter(([k]) => textKeys.includes(k))));
  const settledText = useDebouncedValue(rawText);
  const params: Filters = { ...filters, ...JSON.parse(settledText) };
  const filterKey = JSON.stringify(params);
  const [position, setPosition] = useState({ key: filterKey, page: 1 });
  const page = position.key === filterKey ? position.page : 1;
  const [pageSize, changePageSize] = useState(50);
  const [revision, setRevision] = useState(0);
  const [result, setResult] = useState<{ key: string; data: PagedResult<T> } | null>(null);
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(false);
  const requestKey = JSON.stringify([path, filterKey, page, pageSize, revision]);
  useEffect(() => {
    let active = true;
    const expectedRawKey = currentRawKey.current;
    setError('');
    if (!enabled || rawText !== settledText) { setLoading(false); return; }
    setLoading(true);
    api<PagedResult<T>>(path + '?' + qs({ ...params, page, pageSize }))
      .then(data => {
        if (active && currentRawKey.current === expectedRawKey) setResult({ key: requestKey, data });
      })
      .catch(e => { if (active && currentRawKey.current === expectedRawKey) setError(e.message); })
      .finally(() => { if (active) setLoading(false); });
    return () => { active = false; };
    // Serialized filters define request identity; no request on every render.
  }, [requestKey, enabled, rawText, settledText]);
  const data: PagedResult<T> = enabled && result?.key === requestKey ? result.data :
    { items: [], page, pageSize, totalCount: 0, totalPages: 0 };
  return {
    data, error, appliedFilters: enabled && result?.key === requestKey && rawText === settledText ? params : null, loading: loading || rawText !== settledText, page, pageSize,
    setPage: (next: number) => setPosition({ key: filterKey, page: Math.max(1, next) }),
    setPageSize: (next: number) => { changePageSize(next); setPosition({ key: filterKey, page: 1 }); },
    reload: () => setRevision(x => x + 1)
  };
}
