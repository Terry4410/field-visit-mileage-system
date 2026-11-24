/**
 * Keep the independent Leader queues available even if the other endpoint fails.
 * A rejected request must never masquerade as a successful empty queue.
 */
export async function loadLeaderReviewLists<T, C>(
  fetchTrips: () => Promise<T[]>,
  fetchCorrections: () => Promise<C[]>
): Promise<{
  trips: T[];
  corrections: C[];
  tripsFailed: boolean;
  correctionsFailed: boolean;
  errors: string[];
}> {
  const [tripsResult, correctionsResult] = await Promise.allSettled([
    fetchTrips(),
    fetchCorrections()
  ]);
  const tripsFailed = tripsResult.status === 'rejected';
  const correctionsFailed = correctionsResult.status === 'rejected';
  const errorText = (reason: unknown) =>
    reason instanceof Error ? reason.message : String(reason ?? '未知錯誤');
  const errors = [
    ...(tripsFailed ? [`待核准行程讀取失敗：${errorText(tripsResult.reason)}`] : []),
    ...(correctionsFailed ? [`更正申請讀取失敗：${errorText(correctionsResult.reason)}`] : [])
  ];
  return {
    trips: tripsResult.status === 'fulfilled' ? tripsResult.value : [],
    corrections: correctionsResult.status === 'fulfilled' ? correctionsResult.value : [],
    tripsFailed,
    correctionsFailed,
    errors
  };
}
