import { describe, expect, it } from 'vitest';
import { loadLeaderReviewLists } from './leader-review-queue';

describe('independent Leader queue failure handling', () => {
  it('keeps real pending trips visible when correction read fails', async () => {
    const result = await loadLeaderReviewLists(
      async () => [{ visitTripId: 12 }],
      async () => { throw new Error('correction lookup denied'); }
    );
    expect(result.trips).toEqual([{ visitTripId: 12 }]);
    expect(result.corrections).toEqual([]);
    expect(result.tripsFailed).toBe(false);
    expect(result.correctionsFailed).toBe(true);
    expect(result.errors).toEqual(['更正申請讀取失敗：correction lookup denied']);
  });

  it('keeps pending corrections visible when trip queue read fails', async () => {
    const result = await loadLeaderReviewLists(
      async () => { throw new Error('trip lookup failed'); },
      async () => [{ correctionRequestId: 91 }]
    );
    expect(result.trips).toEqual([]);
    expect(result.corrections).toEqual([{ correctionRequestId: 91 }]);
    expect(result.tripsFailed).toBe(true);
    expect(result.correctionsFailed).toBe(false);
    expect(result.errors).toEqual(['待核准行程讀取失敗：trip lookup failed']);
  });

  it('reports both requests as successful when each returns an empty list', async () => {
    const result = await loadLeaderReviewLists(async () => [], async () => []);
    expect(result.errors).toEqual([]);
    expect(result.tripsFailed).toBe(false);
    expect(result.correctionsFailed).toBe(false);
  });
});
