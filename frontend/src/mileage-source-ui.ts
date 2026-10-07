export type MileageSourceRow={
  mileageSource?:string;
  claimedDistanceKm?:number;
  systemDistanceKm?:number;
};

export const mileageSourceLabel=(source?:string)=>{
  if(source==="GoogleMapsAPI"||source==="GoogleMapsRoutes")return "Google Maps API";
  if(source==="ManualFallback")return "人工備援";
  if(source==="MockRoute/UAT")return "UAT 測試路線";
  return "待計算";
};

export const mileageSourceDistance=(row:MileageSourceRow)=>{
  if(row.mileageSource==="ManualFallback")return row.claimedDistanceKm;
  return row.systemDistanceKm;
};

export const isGoogleMileageSource=(source?:string)=>
  source==="GoogleMapsAPI"||source==="GoogleMapsRoutes";
