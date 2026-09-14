import {useState} from "react";
import type {V180RouteSuggestion} from "../types";
import {canLoadGoogleMap,GOOGLE_MAPS_ATTRIBUTION,routeSuggestionSummary} from "../fc-google-map-ux";
import GoogleRouteMap from "./GoogleRouteMap";

export default function GoogleRouteSuggestionPanel({result,claimedDistanceKm,approvedDistanceKm}:{result:V180RouteSuggestion;claimedDistanceKm?:number;approvedDistanceKm?:number}){
  const[showMap,setShowMap]=useState(false);
  const summary=routeSuggestionSummary(result);
  const successful=result.status==="Succeeded";
  return <section className="google-route-panel" aria-label="Google 路線建議">
    <div className="section-title"><div><h3>Google 路線建議</h3><div className="sub">僅供本次檢視，不會覆寫自行申報或公司核定里程。</div></div><span className={`pill ${successful?"":"warn"}`}>{successful?"已取得":"未取得"}</span></div>
    <div className="google-route-metrics">
      <div><span>Google 建議</span><strong>{summary.distance}</strong></div>
      <div><span>預估時間</span><strong>{summary.duration}</strong></div>
      <div><span>自行申報</span><strong>{claimedDistanceKm==null?"—":`${claimedDistanceKm.toFixed(1)} km`}</strong></div>
      <div><span>公司核定</span><strong>{approvedDistanceKm==null?"—":`${approvedDistanceKm.toFixed(1)} km`}</strong></div>
    </div>
    {!successful&&<div className="note danger-note">{result.errorMessage||"無法取得路線；仍可使用人工判斷／ManualFallback。"}</div>}
    {successful&&result.encodedPolyline&&<div className="actions">
      {canLoadGoogleMap()?<button className="btn small outline" type="button" onClick={()=>setShowMap(x=>!x)}>{showMap?"關閉地圖":"開啟地圖"}</button>:<span className="muted">地圖未啟用；文字結果仍可使用。</span>}
    </div>}
    {showMap&&result.encodedPolyline&&<GoogleRouteMap encodedPolyline={result.encodedPolyline}/>}
    {!showMap&&<div className="google-attribution" translate="no">{GOOGLE_MAPS_ATTRIBUTION}</div>}
  </section>;
}
