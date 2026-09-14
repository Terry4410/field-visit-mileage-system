import {useEffect,useRef,useState} from "react";
import {googleMapsConfig,googleMapsScriptUrl} from "../fc-google-map-ux";

declare global{interface Window{google?:any;__fcGoogleMapsReady?:()=>void}}

let googleLoader:Promise<void>|null=null;
const loadGoogleMaps=()=>{
  if(window.google?.maps)return Promise.resolve();
  if(googleLoader)return googleLoader;
  const {browserKey}=googleMapsConfig();
  googleLoader=new Promise<void>((resolve,reject)=>{
    window.__fcGoogleMapsReady=()=>resolve();
    const script=document.createElement("script");
    script.src=googleMapsScriptUrl(browserKey);
    script.async=true;script.defer=true;
    script.onerror=()=>reject(new Error("Google Maps 載入失敗。"));
    document.head.appendChild(script);
  });
  return googleLoader;
};

export default function GoogleRouteMap({encodedPolyline}:{encodedPolyline:string}){
  const host=useRef<HTMLDivElement>(null);
  const[error,setError]=useState("");
  useEffect(()=>{
    let active=true;
    void loadGoogleMaps().then(()=>{
      if(!active||!host.current)return;
      const maps=window.google.maps;
      const path=maps.geometry.encoding.decodePath(encodedPolyline);
      const map=new maps.Map(host.current,{mapTypeControl:false,streetViewControl:false,fullscreenControl:false});
      const line=new maps.Polyline({path,map,strokeColor:"#2563eb",strokeOpacity:.9,strokeWeight:5});
      const bounds=new maps.LatLngBounds();path.forEach((point:any)=>bounds.extend(point));map.fitBounds(bounds);
      void line;
    }).catch(e=>active&&setError(e instanceof Error?e.message:"Google Maps 載入失敗。"));
    return()=>{active=false};
  },[encodedPolyline]);
  return error?<div className="note danger-note">{error}</div>:<div ref={host} className="google-route-map" aria-label="Google 路線地圖"/>;
}
