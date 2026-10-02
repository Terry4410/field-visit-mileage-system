export const minimumStopsForMileageMessage=
  "正式送出至少需要 2 個公務地點，才能計算外訪里程與申請補助。";
export const manualFallbackMileageInvalidMessage=
  "人工備援里程如有填寫，必須大於 0。";
export function validateTripMileageForSubmit(stopCount:number,manualFallbackDistanceKm:string|number|undefined|null):string|null{
  if(stopCount<2)return minimumStopsForMileageMessage;
  if(manualFallbackDistanceKm!==undefined&&manualFallbackDistanceKm!==null&&String(manualFallbackDistanceKm).trim()!==""){
    const value=Number(manualFallbackDistanceKm);
    if(!Number.isFinite(value)||value<=0)return manualFallbackMileageInvalidMessage;
  }
  return null;
}
