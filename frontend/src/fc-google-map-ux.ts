import type {V180RouteSuggestion} from "./types";

export const GOOGLE_MAPS_ATTRIBUTION="Google Maps";
export const visitorRoutePreviewPath=(tripId:number|string)=>`/trips/${tripId}/route-preview`;
export const leaderRouteRetryPath=(tripId:number|string)=>`/trips/${tripId}/route-retry`;

export const googleMapsConfig=()=>({
  enabled:window.APP_CONFIG?.EPIC_F_GOOGLE?.ENABLED===true,
  browserKey:(window.APP_CONFIG?.EPIC_F_GOOGLE?.MAPS_JS_API_KEY||"").trim()
});

export const canLoadGoogleMap=()=>{
  const config=googleMapsConfig();
  return config.enabled&&config.browserKey.length>0;
};

export const googleMapsScriptUrl=(browserKey:string)=>
  `https://maps.googleapis.com/maps/api/js?key=${encodeURIComponent(browserKey)}&loading=async&libraries=geometry&callback=__fcGoogleMapsReady`;

export const routeSuggestionSummary=(result:V180RouteSuggestion)=>({
  distance:result.suggestedDistanceKm==null?"—":`${result.suggestedDistanceKm.toFixed(1)} km`,
  duration:result.durationSeconds==null?"—":`${Math.ceil(result.durationSeconds/60)} 分鐘`,
  canMap:result.status==="Succeeded"&&!!result.encodedPolyline&&canLoadGoogleMap()
});

export const explicitDecisionAttemptId=(source:string,result?:V180RouteSuggestion|null)=>
  source==="ManualFallback"?null:result?.status==="Succeeded"?result.routeCalculationAttemptId:null;
