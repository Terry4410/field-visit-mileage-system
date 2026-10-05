import type {Trip,TripStopInput} from "../types";

type Endpoint={label:"起點"|"終點";name?:string;code?:string;address?:string};
const endpoint=(label:Endpoint["label"],name?:string,code?:string,address?:string):Endpoint=>({label,name,code,address});

export function tripRouteSummary(t:Pick<Trip,"startDeploymentSiteName"|"startDeploymentSiteCode"|"endDeploymentSiteName"|"endDeploymentSiteCode"|"stops">){
  const start=t.startDeploymentSiteName||t.startDeploymentSiteCode||"所屬就業中心";
  const end=t.endDeploymentSiteName||t.endDeploymentSiteCode||"所屬就業中心";
  return [start,...t.stops.map(x=>x.locationName),end].join(" → ");
}

export default function TripRouteDisplay({trip,includeStopDetail=false,showStops=true}:{trip:Pick<Trip,"startDeploymentSiteName"|"startDeploymentSiteCode"|"startDeploymentAddress"|"endDeploymentSiteName"|"endDeploymentSiteCode"|"endDeploymentAddress"|"stops">;includeStopDetail?:boolean;showStops?:boolean}){
 const endpoints=[endpoint("起點",trip.startDeploymentSiteName,trip.startDeploymentSiteCode,trip.startDeploymentAddress),endpoint("終點",trip.endDeploymentSiteName,trip.endDeploymentSiteCode,trip.endDeploymentAddress)];
 const renderEndpoint=(x:Endpoint)=><div className="route-item endpoint-route" key={x.label}><div className="route-index route-endpoint">{x.label}</div><div><div className="route-name">{x.name||x.code||"所屬就業中心（尚未設定）"}</div>{x.code&&x.name&&<div className="muted">{x.code}</div>}<div className="route-address">{x.address||"—"}</div></div></div>;
 return <div className="route-list">{renderEndpoint(endpoints[0])}{showStops&&trip.stops.map((s:TripStopInput,i)=><div className="route-item" key={`${s.locationId||s.locationName}-${i}`}><div className="route-index">{i+1}</div><div><div className="route-name">{s.locationName}</div><div className="route-address">{s.address||"—"}</div>{includeStopDetail&&<div className="muted">目的：{s.visitPurpose||"未填"}</div>}</div></div>)}{renderEndpoint(endpoints[1])}</div>;
}
