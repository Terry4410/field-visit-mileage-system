export const minimumStopsForMileageMessage=
  "正式送出至少需要 2 個公務地點，才能計算外訪里程與申請補助。";

export const claimedMileageRequiredMessage=
  "正式送出前必須填寫大於 0 的外訪員自行計算里程。";

export function validateTripMileageForSubmit(
  stopCount:number,
  claimedDistanceKm:string|number|undefined|null
):string|null{
  if(stopCount<2)return minimumStopsForMileageMessage;

  const value=Number(claimedDistanceKm);
  if(!Number.isFinite(value)||value<=0)return claimedMileageRequiredMessage;

  return null;
}
