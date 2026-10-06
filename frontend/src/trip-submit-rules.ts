export const minimumVisitStopCount=1;
export const hasMinimumVisitStops=(stopCount:number)=>stopCount>=minimumVisitStopCount;
export const minimumStopsForMileageMessage=
  "正式送出至少需要 1 個拜訪地點，才能計算外訪里程與申請補助。";
export const manualFallbackMileageInvalidMessage=
  "人工備援里程如有填寫，必須大於 0。";
export function validateTripMileageForSubmit(stopCount:number,manualFallbackDistanceKm:string|number|undefined|null):string|null{
  if(!hasMinimumVisitStops(stopCount))return minimumStopsForMileageMessage;
  if(manualFallbackDistanceKm!==undefined&&manualFallbackDistanceKm!==null&&String(manualFallbackDistanceKm).trim()!==""){
    const value=Number(manualFallbackDistanceKm);
    if(!Number.isFinite(value)||value<=0)return manualFallbackMileageInvalidMessage;
  }
  return null;
}
export function manualFallbackDistanceForBody(stopCount:number,value:string):number|null{
  if(!hasMinimumVisitStops(stopCount)||!value.trim())return null;
  return Number(value);
}
