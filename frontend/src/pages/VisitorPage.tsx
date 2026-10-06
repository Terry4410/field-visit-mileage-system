import {useEffect,useMemo,useState} from "react";
import {useSearchParams} from "react-router-dom";
import {api} from "../api";
import {useAuth} from "../auth";
import SmartLocationPicker from "../components/SmartLocationPicker";
import {TripRouteEndpointDisplay} from "../components/TripRouteDisplay";
import {manualFallbackDistanceForBody,validateTripMileageForSubmit} from "../trip-submit-rules";
import {isProjectAvailableOn} from "../project-date-rules";
import {resolveTripTeamForEdit} from "../trip-team-edit-rules";
import type {Project,RoutePreviewResult,SmartLocationItem,Trip,TripContext,TripStopInput,VisitType} from "../types";

type ModalKind="stop"|"endpoint"|"submit"|null;
type EndpointKind="start"|"end";
type LocationMethod="existing"|"temporary";
type OverlapResult={hasOverlap:boolean;message?:string;overlappingTrips?:Array<{visitTripId:number;tripNo:string;startTime?:string;endTime?:string;status:string}>};
const isListMode=(mode?:string)=>["list","清單"].includes((mode||"").toLowerCase());
const normalizeTime=(value:string)=>value.length===5?`${value}:00`:value;

export const hasMinimumVisitStops=(stopCount:number)=>stopCount>=1;
export const canCalculateGoogleMileage=(busy:boolean,stopCount:number,startSiteId:string,endSiteId:string)=>
  !busy&&hasMinimumVisitStops(stopCount)&&!!startSiteId&&!!endSiteId;

export default function VisitorPage(){
  const {user}=useAuth();
  const today=new Date().toISOString().slice(0,10);
  const [sp,setSp]=useSearchParams();
  const editId=sp.get("edit");

  const teamScopes=useMemo(
    ()=>{
      if(user?.teamScopes?.length)
        return user.teamScopes;
      if(user?.teamId)
        return [{
          teamId:user.teamId,
          teamName:user.teamName||`Team ${user.teamId}`,
          isPrimary:true
        }];
      return [];
    },
    [user]
  );

  const [tripTeamId,setTripTeamId]=useState(
    user?.teamId?String(user.teamId):""
  );
  const [date,setDate]=useState(today),[start,setStart]=useState("08:30"),[end,setEnd]=useState("17:10"),[vehicleType,setVehicleType]=useState("Motorcycle"),[km,setKm]=useState(""),[notes,setNotes]=useState("");
  const [startDeploymentSiteId,setStartDeploymentSiteId]=useState(""),[endDeploymentSiteId,setEndDeploymentSiteId]=useState("");
  const [projects,setProjects]=useState<Project[]>([]),[visitTypes,setVisitTypes]=useState<VisitType[]>([]),[stops,setStops]=useState<TripStopInput[]>([]);
  const [rowVersion,setRowVersion]=useState(""),[returnReason,setReturnReason]=useState(""),[teamAccessWarning,setTeamAccessWarning]=useState(""),[tripContext,setTripContext]=useState<TripContext|null>(null),[tripContextError,setTripContextError]=useState(""),[overlap,setOverlap]=useState<OverlapResult>({hasOverlap:false}),[confirmOverlap,setConfirmOverlap]=useState(false),[routePreview,setRoutePreview]=useState<RoutePreviewResult|null>(null),[routePreviewBusy,setRoutePreviewBusy]=useState(false),[msg,setMsg]=useState(""),[busy,setBusy]=useState(false),[modal,setModal]=useState<ModalKind>(null);

  const [editingStopIndex,setEditingStopIndex]=useState<number|null>(null);
  const [editingEndpoint,setEditingEndpoint]=useState<EndpointKind|null>(null);
  const [endpointDraftSiteId,setEndpointDraftSiteId]=useState("");
  const [endpointSearch,setEndpointSearch]=useState("");
  const [locationMethod,setLocationMethod]=useState<LocationMethod>("existing");
  const [selectedExistingLocation,setSelectedExistingLocation]=useState<SmartLocationItem|null>(null);
  const [stopPurpose,setStopPurpose]=useState("");

  const [projectId,setProjectId]=useState(""),[visitTypeId,setVisitTypeId]=useState("");
  const [tempName,setTempName]=useState(""),[tempAddress,setTempAddress]=useState("");

  useEffect(()=>{
    if(!editId&&user?.teamId)
      setTripTeamId(String(user.teamId));
  },[user?.teamId,editId]);

  useEffect(()=>{
    Promise.all([
      api<Project[]>("/projects"),
      api<VisitType[]>("/visit-types")
    ])
      .then(([p,v])=>{
        setProjects(p);
        setVisitTypes(v);
        setProjectId("");
        setVisitTypeId("");
      })
      .catch(e=>setMsg(e.message));
  },[]);

  useEffect(()=>{
    if(!editId)return;
    api<Trip>(`/trips/${editId}`).then(t=>{
      const teamResolution=
        resolveTripTeamForEdit(
          t.teamId,
          t.teamName,
          teamScopes,
          user?.teamId,
          user?.teamName
        );

      setDate(t.visitDate);
      setTripTeamId(
        teamResolution.teamId
          ?String(teamResolution.teamId)
          :""
      );
      setStart((t.startTime||"").slice(0,5));
      setEnd((t.endTime||"").slice(0,5));
      setStartDeploymentSiteId(t.startDeploymentSiteId?String(t.startDeploymentSiteId):"");
      setEndDeploymentSiteId(t.endDeploymentSiteId?String(t.endDeploymentSiteId):"");
      setNotes(t.notes||"");
      setRowVersion(t.rowVersion);
      setVehicleType(t.vehicleType||"Motorcycle");
      setReturnReason(t.status==="Returned"?(t.returnReason||""):"");

      if(teamResolution.originalTeamStillAllowed){
        setKm(String(t.claimedDistanceKm||""));
        setStops(t.stops);
        setTeamAccessWarning("");
        setMsg(`已載入 ${t.tripNo}，修改完成後可重新送出。`);
        return;
      }

      setKm("");
      setStops([]);
      setProjectId("");
      setVisitTypeId("");
      setSelectedExistingLocation(null);
      setEditingStopIndex(null);
      setLocationMethod("existing");
      setStopPurpose("");
      setTempName("");
      setTempAddress("");
      setModal(null);
      setTeamAccessWarning(teamResolution.warning||"");
      setMsg(
        teamResolution.teamId
          ?`已載入 ${t.tripNo}；請重新確認行程歸屬小組與拜訪地點後再儲存送出。`
          :`已載入 ${t.tripNo}；目前沒有有效的小組可供重新歸屬。`
      );
    }).catch(e=>setMsg(e.message));
  },[editId,user?.teamId,user?.teamName,teamScopes]);

  useEffect(()=>{
    const timer=setTimeout(()=>{
      if(!date||!start||!end){setOverlap({hasOverlap:false});return}
      if(end<=start){setOverlap({hasOverlap:true,message:"結束時間必須晚於出發時間，請確認輸入是否正確。"});return}
      api<OverlapResult>("/trips/time-overlap-check",{
        method:"POST",
        body:JSON.stringify({visitDate:date,startTime:normalizeTime(start),endTime:normalizeTime(end),excludeVisitTripId:editId?Number(editId):null})
      }).then(r=>{setOverlap(r);if(!r.hasOverlap)setConfirmOverlap(false)}).catch(()=>{});
    },400);
    return()=>clearTimeout(timer);
  },[date,start,end,editId]);

  const selectedTeamId=
    tripTeamId?Number(tripTeamId):undefined;

  const selectedTripTeam=
    teamScopes.find(
      x=>x.teamId===selectedTeamId
    );

  useEffect(()=>{
    if(!date||!selectedTeamId){
      setTripContext(null);setTripContextError("");return;
    }
    api<TripContext>(`/trips/context?visitDate=${encodeURIComponent(date)}&teamId=${selectedTeamId}`)
      .then(x=>{setTripContext(x);setTripContextError("")})
      .catch(e=>{setTripContext(null);setTripContextError(e instanceof Error?e.message:"無法取得派駐中心資料")});
  },[date,selectedTeamId]);

  const officialDeploymentSites=
    tripContext?.officialDeploymentSites?.length
      ?tripContext.officialDeploymentSites
      :tripContext?.eligibleDeploymentSites||[];

  useEffect(()=>{
    if(!tripContext)return;
    const allowed=new Set(officialDeploymentSites.map(x=>x.deploymentSiteId));
    const startDefault=tripContext.defaultStartDeploymentSiteId?String(tripContext.defaultStartDeploymentSiteId):"";
    const endDefault=tripContext.defaultEndDeploymentSiteId?String(tripContext.defaultEndDeploymentSiteId):"";
    setStartDeploymentSiteId(current=>current&&allowed.has(Number(current))?current:startDefault);
    setEndDeploymentSiteId(current=>current&&allowed.has(Number(current))?current:endDefault);
  },[tripContext]);

  const defaultStartSite=officialDeploymentSites.find(x=>x.deploymentSiteId===tripContext?.defaultStartDeploymentSiteId);
  const defaultEndSite=officialDeploymentSites.find(x=>x.deploymentSiteId===tripContext?.defaultEndDeploymentSiteId);
  const selectedStartSite=officialDeploymentSites.find(x=>x.deploymentSiteId===Number(startDeploymentSiteId));
  const selectedEndSite=officialDeploymentSites.find(x=>x.deploymentSiteId===Number(endDeploymentSiteId));
  const endpointSearchText=endpointSearch.trim().toLocaleLowerCase("zh-TW");
  const filteredOfficialDeploymentSites=officialDeploymentSites.filter(site=>{
    if(!endpointSearchText)return true;
    return [
      site.centerName,
      site.centerCode,
      site.siteName,
      site.siteCode,
      site.locationName,
      site.locationCode||"",
      site.address||""
    ].some(value=>value.toLocaleLowerCase("zh-TW").includes(endpointSearchText));
  });

  useEffect(()=>{setRoutePreview(null)},[date,tripTeamId,startDeploymentSiteId,endDeploymentSiteId,vehicleType,stops]);

  const availableProjects=useMemo(
    ()=>projects.filter(
      p=>
        isProjectAvailableOn(p,date)
        &&(
          !p.teamId
          ||p.teamId===selectedTeamId
        )
    ),
    [projects,date,selectedTeamId]
  );

  const selectedProject=projects.find(x=>x.projectId===Number(projectId));

  useEffect(()=>{
    if(!projectId)return;
    if(availableProjects.some(
      p=>p.projectId===Number(projectId)
    ))return;

    setProjectId("");
    setSelectedExistingLocation(null);
    setLocationMethod("existing");
  },[availableProjects,projectId]);

  // Only list-mode projects restrict Smart Picker to ProjectLocations.
  // Other project modes preserve the existing behavior: users may still
  // switch to a normal Master Location or use a Temporary Location.
  const pickerProjectId=
    projectId&&isListMode(selectedProject?.locationMode)
      ?Number(projectId)
      :undefined;

  const hourOptions=useMemo(()=>Array.from({length:24},(_,i)=>String(i).padStart(2,"0")),[]);
  const minuteOptions=useMemo(()=>Array.from({length:60},(_,i)=>String(i).padStart(2,"0")),[]);

  const updateClock=(kind:"start"|"end",part:"hour"|"minute",value:string)=>{
    const current=kind==="start"?start:end;
    const [hour="00",minute="00"]=current.split(":");
    const next=part==="hour"?`${value}:${minute}`:`${hour}:${value}`;
    if(kind==="start")setStart(next);else setEnd(next);
  };

  const reset=()=>{
    setSp({});
    setTripTeamId(user?.teamId?String(user.teamId):"");
    setDate(today);setStart("08:30");setEnd("17:10");setVehicleType("Motorcycle");setKm("");setNotes("");setStartDeploymentSiteId("");setEndDeploymentSiteId("");setStops([]);setRowVersion("");setReturnReason("");setTeamAccessWarning("");setTripContext(null);setTripContextError("");setOverlap({hasOverlap:false});setConfirmOverlap(false);setRoutePreview(null);setMsg("");
  };

  const changeTripTeam=(value:string)=>{
    if(value===tripTeamId)
      return;

    if(stops.length>0){
      const confirmed=
        window.confirm(
          "切換行程歸屬小組後，已加入的拜訪地點會清除，並重新套用新小組可使用的地點與專案。是否繼續？"
        );
      if(!confirmed)
        return;
    }

    setTripTeamId(value);
    setStartDeploymentSiteId("");
    setEndDeploymentSiteId("");
    setStops([]);
    setKm("");
    setProjectId("");
    setSelectedExistingLocation(null);
    setLocationMethod("existing");
    setMsg(
      "已切換行程歸屬小組；地點與專案將依新小組重新篩選。"
    );
  };

  const clearStopEditor=()=>{
    setEditingStopIndex(null);
    setLocationMethod("existing");
    setSelectedExistingLocation(null);
    setStopPurpose("");
    setProjectId("");
    setVisitTypeId("");
    setTempName("");setTempAddress("");
  };

  const openNewStop=()=>{
    clearStopEditor();
    setModal("stop");
  };

  const openEndpointEditor=(kind:EndpointKind)=>{
    setEditingEndpoint(kind);
    setEndpointDraftSiteId(kind==="start"?startDeploymentSiteId:endDeploymentSiteId);
    setEndpointSearch("");
    setModal("endpoint");
  };

  const saveEndpoint=()=>{
    if(!editingEndpoint||!endpointDraftSiteId){
      setMsg("請選擇有效的派駐據點。");
      return;
    }
    if(editingEndpoint==="start")setStartDeploymentSiteId(endpointDraftSiteId);
    else setEndDeploymentSiteId(endpointDraftSiteId);
    setModal(null);
    setEditingEndpoint(null);
    setEndpointDraftSiteId("");
    setEndpointSearch("");
    setMsg(editingEndpoint==="start"?"已更新本次行程出發地。":"已更新本次行程結束地。");
  };

  const openEditStop=(index:number)=>{
    const stop=stops[index];

    setEditingStopIndex(index);
    setStopPurpose(stop.visitPurpose||"");
    setProjectId(stop.projectId?String(stop.projectId):"");
    setVisitTypeId(stop.visitTypeId?String(stop.visitTypeId):"");

    if(stop.locationId){
      setLocationMethod("existing");

      // Seed the current Stop so editing does not require downloading
      // the entire Location master or reselecting an unchanged location.
      setSelectedExistingLocation({
        locationId:stop.locationId,
        locationName:stop.locationName,
        locationType:"Existing",
        address:stop.address||null
      });

      setTempName("");
      setTempAddress("");
    }else{
      setLocationMethod("temporary");
      setSelectedExistingLocation(null);
      setTempName(stop.locationName||"");
      setTempAddress(stop.address||"");
    }

    setModal("stop");
  };

  const commitStop=(stop:TripStopInput)=>{
    setStops(rows=>{
      if(editingStopIndex===null)return[...rows,stop];
      return rows.map((row,i)=>i===editingStopIndex?stop:row);
    });
    setModal(null);
    clearStopEditor();
  };

  const saveStop=()=>{
    const selectedProjectId=projectId?Number(projectId):undefined;
    const visitType=visitTypeId
      ?visitTypes.find(x=>x.visitTypeId===Number(visitTypeId))
      :undefined;
    if(visitTypeId&&!visitType)
      return setMsg("請重新選擇有效的拜訪形式。");

    if(locationMethod==="existing"){
      const location=selectedExistingLocation;

      if(!location)
        return setMsg("請先選擇一個既有地點；若查無地點，可切換為「臨時新增地點」。");

      const duplicated=stops.some(
        (x,i)=>
          i!==editingStopIndex
          &&x.locationId===location.locationId
          &&x.projectId===selectedProjectId
      );

      if(duplicated)
        return setMsg("此地點已在目前行程中。");

      commitStop({
        locationId:location.locationId,
        projectId:selectedProjectId,
        visitTypeId:visitType?.visitTypeId,
        sourceType:
          selectedProjectId&&isListMode(selectedProject?.locationMode)
            ?"ProjectList"
            :"Master",
        locationName:location.locationName,
        address:
          location.address
          ||location.plusCode
          ||undefined,
        visitPurpose:stopPurpose.trim()||undefined
      });

      return;
    }

    if(!tempName.trim()||!tempAddress.trim())return setMsg("請填寫臨時地點名稱與地址或 Plus Code。");
    commitStop({
      projectId:selectedProjectId,
      visitTypeId:visitType?.visitTypeId,
      sourceType:"Temporary",
      locationName:tempName.trim(),
      address:tempAddress.trim(),
      visitPurpose:stopPurpose.trim()||undefined,
      notes:selectedProjectId?"專案臨時地點":"臨時公務地點"
    });
  };

  const move=(i:number,d:number)=>{
    const j=i+d;if(j<0||j>=stops.length)return;
    const c=[...stops];[c[i],c[j]]=[c[j],c[i]];setStops(c);
  };

  const validateForSubmit=()=>{
    if(!selectedTeamId){
      setMsg("請選擇本次行程的歸屬小組。");
      return false;
    }
    if(!startDeploymentSiteId||!endDeploymentSiteId){
      setMsg("請選擇本次行程的出發地與結束地。");
      return false;
    }
    const mileageError=validateTripMileageForSubmit(stops.length,km);
    if(mileageError){setMsg(mileageError);return false}
    if(end<=start){setMsg("結束時間必須晚於出發時間。");return false}
    return true;
  };

  const requestSubmit=()=>{if(!validateForSubmit())return;setModal("submit")};

  const buildTripBody=()=>({
    visitDate:date,startTime:normalizeTime(start),endTime:normalizeTime(end),
    claimedDistanceKm:manualFallbackDistanceForBody(stops.length,km),
    purpose:null,notes:notes.trim()||null,timeOverlapConfirmed:confirmOverlap,stops,
    teamId:selectedTeamId??null,
    startDeploymentSiteId:startDeploymentSiteId?Number(startDeploymentSiteId):null,
    endDeploymentSiteId:endDeploymentSiteId?Number(endDeploymentSiteId):null,
    vehicleType
  });

  const calculateGoogleMileage=async()=>{
    setMsg("");
    if(!selectedTeamId)return setMsg("請選擇本次行程的歸屬小組。");
    if(!startDeploymentSiteId||!endDeploymentSiteId)return setMsg("請選擇本次行程的出發地與結束地。");
    const mileageError=validateTripMileageForSubmit(stops.length,null);
    if(mileageError)return setMsg(mileageError);
    if(end<=start)return setMsg("結束時間必須晚於出發時間。");
    setBusy(true);
    setRoutePreviewBusy(true);
    setRoutePreview(null);
    try{
      const body=buildTripBody();
      let t:Trip;
      if(editId)t=await api<Trip>(`/trips/${editId}`,{method:"PUT",headers:{"If-Match":rowVersion},body:JSON.stringify(body)});
      else t=await api<Trip>("/trips",{method:"POST",body:JSON.stringify(body)});
      setRowVersion(t.rowVersion);
      if(!editId)setSp({edit:String(t.visitTripId)});
      const preview=await api<RoutePreviewResult>(`/trips/${t.visitTripId}/route-preview`,{method:"POST"});
      setRoutePreview(preview);
      if(preview.status==="Succeeded"&&preview.suggestedDistanceKm){
        setMsg(`Google Maps API 路線里程：${preview.suggestedDistanceKm} km。`);
      }else{
        setMsg(`Google Maps API 無法取得可用里程：${preview.errorMessage||preview.errorCode||"請改用人工里程"}。`);
      }
    }catch(e){
      setMsg(e instanceof Error?e.message:"Google Maps API 里程計算失敗");
    }finally{
      setRoutePreviewBusy(false);
      setBusy(false);
    }
  };

  const save=async(submit:boolean)=>{
    setMsg("");
    if(end<=start)return setMsg("結束時間必須晚於出發時間。");
    if(submit&&!validateForSubmit())return;
    if(submit&&overlap.hasOverlap&&!confirmOverlap)return setMsg("偵測到時間重疊，請勾選確認時間正確後再送出。");
    setBusy(true);
    try{
      const body=buildTripBody();
      let t:Trip;
      if(editId)t=await api<Trip>(`/trips/${editId}`,{method:"PUT",headers:{"If-Match":rowVersion},body:JSON.stringify(body)});
      else t=await api<Trip>("/trips",{method:"POST",body:JSON.stringify(body)});
      setRowVersion(t.rowVersion);
      if(submit){
        const sent=await api<Trip>(`/trips/${t.visitTripId}/submit`,{method:"POST",headers:{"If-Match":t.rowVersion},body:JSON.stringify({confirmTimeOverlap:confirmOverlap})});
        setModal(null);reset();setMsg(`已送出 ${sent.tripNo}，小組長可在另一台裝置看到。`);
      }else{
        setSp({edit:String(t.visitTripId)});
        setMsg(`草稿已儲存 ${t.tripNo}。未完成資料可稍後再補。`);
      }
    }catch(e){setMsg(e instanceof Error?e.message:"儲存失敗")}finally{setBusy(false)}
  };

  return <>
    <div className="grid cols-4">
      <div className="card stat"><div className="label">行程日期</div><div className="value" style={{fontSize:20}}>{date}</div><div className="hint">可事後補登</div></div>
      <div className="card stat"><div className="label">拜訪地點</div><div className="value">{stops.length}</div><div className="hint">依實際順序排列</div></div>
      <div className="card stat"><div className="label">Google／人工里程</div><div className="value">{routePreview?.status==="Succeeded"&&routePreview.suggestedDistanceKm?routePreview.suggestedDistanceKm:km||"--"}<span style={{fontSize:14}}> km</span></div><div className="hint">{!hasMinimumVisitStops(stops.length)?"至少 1 個拜訪地點才可計算":"先用 Google 計算；不合理時再填人工里程"}</div></div>
      <div className="card stat"><div className="label">目前狀態</div><div className="value" style={{fontSize:20}}>{editId?"修改中":"草稿"}</div><div className="hint">{editId?"可重新送出":"尚未送出"}</div></div>
    </div>

    {editId&&returnReason&&<div className="note danger-note" style={{marginTop:18}}><strong>主管退回原因：</strong>{returnReason}<br/><span>請依退回原因確認並修改資料後重新送出。</span></div>}

    {editId&&teamAccessWarning&&<div className="note danger-note" style={{marginTop:18}}><strong>小組權限已變更：</strong>{teamAccessWarning}</div>}

    <div className="card" style={{marginTop:18}}>
      <div className="section-title"><h2>基本資料</h2><span className="pill warn">可補登</span></div>
      <div className="grid cols-2">
        <div className="field"><label>行程日期</label><input type="date" value={date} onChange={e=>setDate(e.target.value)}/></div>
        <div className="field">
          <label>行程歸屬小組</label>
          {teamScopes.length>1
            ?<select
              value={tripTeamId}
              onChange={e=>changeTripTeam(e.target.value)}
            >
              {teamScopes.map(team=>
                <option
                  key={team.teamId}
                  value={team.teamId}
                >
                  {team.teamName}{team.isPrimary?" ★":""}
                </option>
              )}
            </select>
            :<input
              value={selectedTripTeam?.teamName||user?.teamName||""}
              disabled
            />
          }
          {teamScopes.length>1&&
            <div className="hint">
              預設為主要小組 ★；此選擇只影響本次行程，不會修改人員主檔。
            </div>
          }
        </div>
        <div className="field"><label>交通工具</label><select value={vehicleType} onChange={e=>setVehicleType(e.target.value)}><option value="Motorcycle">機車</option><option value="Car">汽車</option></select><div className="hint">預設機車；交通工具會決定適用的里程補助費率。</div></div>
        <div className="field"><label>出發時間</label><div className="time-select"><select aria-label="出發時間－時" value={start.slice(0,2)} onChange={e=>updateClock("start","hour",e.target.value)}>{hourOptions.map(x=><option key={x} value={x}>{x}</option>)}</select><span>時</span><select aria-label="出發時間－分" value={start.slice(3,5)} onChange={e=>updateClock("start","minute",e.target.value)}>{minuteOptions.map(x=><option key={x} value={x}>{x}</option>)}</select><span>分</span></div></div>
        <div className="field"><label>結束時間</label><div className="time-select"><select aria-label="結束時間－時" value={end.slice(0,2)} onChange={e=>updateClock("end","hour",e.target.value)}>{hourOptions.map(x=><option key={x} value={x}>{x}</option>)}</select><span>時</span><select aria-label="結束時間－分" value={end.slice(3,5)} onChange={e=>updateClock("end","minute",e.target.value)}>{minuteOptions.map(x=><option key={x} value={x}>{x}</option>)}</select><span>分</span></div></div>
      </div>
      {tripContextError&&<div className="note danger-note" style={{marginTop:10}}>派駐中心資料：{tripContextError}</div>}
      <div className="note">住家地址不允許加入行程；里程只計算派駐中心、正式或臨時公務地點之間的路線。</div>
      {overlap.hasOverlap&&<div className="note danger-note" style={{marginTop:10}}><strong>時間提醒：</strong>{overlap.message||"此時間與既有紀錄重疊，請確認是否輸入正確。"}<label className="check-row"><input type="checkbox" checked={confirmOverlap} onChange={e=>setConfirmOverlap(e.target.checked)}/>我確認時間正確，送出時仍要繼續</label></div>}
    </div>

    <div className="card" style={{marginTop:18}}>
      <div className="section-title">
        <div><h2>拜訪順序</h2><div className="sub">每一個拜訪地點都可開啟「地點維護」選擇來源、修改地點及填寫行程目的。</div></div>
        <div className="actions"><button className="btn small secondary" onClick={openNewStop}>＋新增拜訪地點</button></div>
      </div>
      <div className="route-list">
        <TripRouteEndpointDisplay endpoint={{label:"起點",name:selectedStartSite?`${selectedStartSite.centerName}／${selectedStartSite.siteName}`:undefined,code:selectedStartSite?.siteCode||undefined,address:selectedStartSite?.address||undefined}} onEdit={()=>openEndpointEditor("start")}/>
        {stops.length?stops.map((s,i)=><div className="route-item" key={`${s.locationId||s.locationName}-${i}`}>
          <div className="route-index">{i+1}</div>
          <div>
            <div className="route-name">{s.locationName}</div>
            <div className="route-address">{s.address||"—"}</div>
            <div style={{marginTop:5}}>
              <span className="pill">{s.projectId?"專案地點":s.sourceType==="Temporary"?"臨時地點":"地點清單"}</span>
              {s.visitTypeId&&<span className="pill warn" style={{marginLeft:6}}>拜訪形式：{visitTypes.find(v=>v.visitTypeId===s.visitTypeId)?.visitTypeName||"—"}</span>}
            </div>
            <div className="route-purpose-summary"><strong>行程目的：</strong>{s.visitPurpose?.trim()||"未填寫（選填）"}</div>
          </div>
          <div className="drag-controls">
            <button className="edit-stop" onClick={()=>openEditStop(i)}>編輯</button>
            <button onClick={()=>move(i,-1)} disabled={i===0}>↑</button>
            <button onClick={()=>move(i,1)} disabled={i===stops.length-1}>↓</button>
            <button onClick={()=>setStops(stops.filter((_,x)=>x!==i))}>×</button>
          </div>
        </div>):<div className="empty">尚未加入拜訪地點，請按「＋新增拜訪地點」。</div>}
        <TripRouteEndpointDisplay endpoint={{label:"終點",name:selectedEndSite?`${selectedEndSite.centerName}／${selectedEndSite.siteName}`:undefined,code:selectedEndSite?.siteCode||undefined,address:selectedEndSite?.address||undefined}} onEdit={()=>openEndpointEditor("end")}/>
      </div>
    </div>

    <div className="card" style={{marginTop:18}}>
      <div className="section-title"><div><h2>里程計算</h2><div className="sub">拜訪地點確認完成後，可直接用 Google Maps API 預覽本次路線里程，不需要先由主管核准。</div></div><span className="pill">草稿可計算</span></div>
      <div className="actions" style={{marginBottom:14}}>
        <button className="btn secondary" disabled={!canCalculateGoogleMileage(busy,stops.length,startDeploymentSiteId,endDeploymentSiteId)} onClick={()=>void calculateGoogleMileage()}>{routePreviewBusy?"Google 計算中…":"用 Google Maps API 計算里程"}</button>
      </div>
      {routePreview?.status==="Succeeded"&&routePreview.suggestedDistanceKm&&<div className="note ok-note" style={{marginBottom:14}}><strong>Google Maps API 建議里程：</strong>{routePreview.suggestedDistanceKm} km{routePreview.durationSeconds?`｜預估行車時間 ${Math.round(routePreview.durationSeconds/60)} 分鐘`:""}<br/><span>若此結果合理，人工里程可留白；若路線結果不符合實際情況，再於下方填寫人工里程。</span></div>}
      {routePreview&&routePreview.status!=="Succeeded"&&<div className="note danger-note" style={{marginBottom:14}}><strong>Google Maps API 未取得可用里程。</strong><br/>{routePreview.errorMessage||routePreview.errorCode||"請填寫人工里程。"}</div>}
      <div className="grid cols-2">
        <div className="field"><label>人工里程（公里） <span className="optional">選填</span></label><input type="number" min="0" step="0.1" value={km} onChange={e=>setKm(e.target.value)} placeholder="Google 結果不適用時再填寫"/></div>
        <div className="note">Google Maps API 會依「實際出發地 → 拜訪順序 → 實際結束地」計算。若 API 無法取得結果，或外訪員確認 Google 路線與實際行程不符，可填寫人工里程；最後仍由小組長核定補助里程。</div>
      </div>
    </div>

    <div className="card" style={{marginTop:18}}>
      <div className="section-title"><h2>行程備註</h2><span className="pill">選填</span></div>
      <div className="field"><label>備註</label><textarea value={notes} onChange={e=>setNotes(e.target.value)} placeholder="可填寫臨時狀況、拜訪補充說明或其他行程資訊"/></div>
    </div>

    <div className="card action-footer" style={{marginTop:18}}>
      <div className="section-title"><div><h2>{editId?"修改行程":"完成本次行程資料"}</h2><div className="sub">草稿可以先保存未完成資料；正式送出前才檢查地點數、里程與時間重疊確認。行程目的為每個拜訪地點的選填資料。</div></div></div>
      {msg&&<div className={`note ${msg.includes("失敗")||msg.includes("請")||msg.includes("必須")||msg.includes("至少")?"danger-note":"ok-note"}`}>{msg}</div>}
      <div className="actions bottom-actions">
        {editId&&<button className="btn outline" disabled={busy} onClick={reset}>取消修改</button>}
        <button className="btn outline" disabled={busy} onClick={()=>void save(false)}>{busy?"處理中…":"儲存草稿"}</button>
        <button className="btn ok" disabled={busy} onClick={requestSubmit}>{editId?"重新送出":"送出行程"}</button>
      </div>
    </div>

    {modal==="endpoint"&&<div className="modal" onMouseDown={e=>{if(e.target===e.currentTarget){setModal(null);setEditingEndpoint(null);setEndpointDraftSiteId("");setEndpointSearch("")}}}>
      <div className="modal-panel">
        <h3>{editingEndpoint==="start"?"修改出發地":"修改結束地"}</h3>
        <div className="note" style={{marginBottom:14}}>
          預設由人事歸屬的主要就業中心／派駐據點帶入；可改選公司其他有效中心或就業服務站。此處修改只影響本次行程，不會修改人員主檔。
        </div>
        <div className="field">
          <label>搜尋官方據點</label>
          <input value={endpointSearch} onChange={e=>setEndpointSearch(e.target.value)} placeholder="搜尋中心、就業服務站、代碼或地址"/>
        </div>
        <div className="section-title smart-result-title">
          <strong>官方據點</strong>
          <span className="pill">{filteredOfficialDeploymentSites.length} 筆</span>
        </div>
        <div className="existing-location-results">
          {filteredOfficialDeploymentSites.map(site=>{
            const defaultId=editingEndpoint==="start"?tripContext?.defaultStartDeploymentSiteId:tripContext?.defaultEndDeploymentSiteId;
            const isDefault=site.deploymentSiteId===defaultId;
            const selected=String(site.deploymentSiteId)===endpointDraftSiteId;
            return <label key={site.deploymentSiteId} className={`existing-location-choice ${selected?"selected":""}`}>
              <input type="radio" name="official-endpoint-site" value={site.deploymentSiteId} checked={selected} onChange={()=>setEndpointDraftSiteId(String(site.deploymentSiteId))}/>
              <span>
                <strong>{site.centerName}／{site.siteName}</strong>
                <small>{site.address||site.locationName||"未提供地址"}</small>
                <small>{site.siteCode}{isDefault?"　★ 人員歸屬預設":""}</small>
              </span>
            </label>;
          })}
          {filteredOfficialDeploymentSites.length===0&&<div className="empty compact-empty">查無符合條件的有效官方據點。</div>}
        </div>
        <div className="modal-sticky-actions">
          <button className="btn secondary" onClick={()=>{setModal(null);setEditingEndpoint(null);setEndpointDraftSiteId("");setEndpointSearch("")}}>取消</button>
          <button className="btn ok" disabled={!endpointDraftSiteId} onClick={saveEndpoint}>套用</button>
        </div>
      </div>
    </div>}

    {modal==="stop"&&<div className="modal" onMouseDown={e=>{if(e.target===e.currentTarget)setModal(null)}}>
      <div className="modal-panel stop-editor-modal">
        <h3>{editingStopIndex===null?"新增拜訪地點":"拜訪地點維護"}</h3>

        <div className="field">
          <label>專案 <span className="optional">選填</span></label>
          <select value={projectId} onChange={e=>{
            const value=e.target.value;
            setProjectId(value);
            setSelectedExistingLocation(null);
            if(!value){
              setLocationMethod("existing");
              return;
            }
            const p=projects.find(x=>x.projectId===Number(value));
            setLocationMethod(isListMode(p?.locationMode)?"existing":"temporary");
          }}>
            <option value="">不屬於專案</option>
            {availableProjects.map(p=><option key={p.projectId} value={p.projectId}>{p.projectName}</option>)}
          </select>
        </div>

        <div className="field">
          <label>拜訪形式 <span className="muted">選填</span></label>
          <select value={visitTypeId} onChange={e=>setVisitTypeId(e.target.value)}>
            <option value="">未指定</option>
            {visitTypes.map(v=><option key={v.visitTypeId} value={v.visitTypeId}>{v.visitTypeName}</option>)}
          </select>
        </div>

        <div className="field">
          <label>地點取得方式</label>
          <div className="stop-method-switch">
            <button type="button" className={`stop-method-btn ${locationMethod==="existing"?"active":""}`} onClick={()=>setLocationMethod("existing")}>從既有地點選擇</button>
            <button type="button" className={`stop-method-btn ${locationMethod==="temporary"?"active":""}`} onClick={()=>setLocationMethod("temporary")}>臨時新增地點</button>
          </div>
        </div>

        {locationMethod==="existing"&&<>
          <div className="note" style={{marginBottom:14}}>
            {projectId&&isListMode(selectedProject?.locationMode)
              ?"此專案只會顯示管理者維護的專案正式地點；若本次有臨時地點，可切換「臨時新增地點」。"
              :"可搜尋正式地點，也可使用常用、最近或附近地點；若查無地點，可切換「臨時新增地點」。"}
          </div>

          <div className="field">
            <label>行程歸屬小組</label>
            <input value={selectedTripTeam?.teamName||""} disabled/>
          </div>

          <SmartLocationPicker
            key={
              `${selectedTeamId||"no-team"}-${
                pickerProjectId
                  ?`project-${pickerProjectId}`
                  :"master"
              }`
            }
            projectId={pickerProjectId}
            teamId={selectedTeamId}
            selectedLocationId={
              selectedExistingLocation?.locationId
            }
            onSelect={location=>{
              setSelectedExistingLocation(location);
              setMsg("");
            }}
          />

          {selectedExistingLocation&&
            <div className="note ok-note" style={{marginTop:12}}>
              <strong>已選擇：</strong>
              {selectedExistingLocation.locationName}
              <br/>
              {selectedExistingLocation.address
                ||selectedExistingLocation.plusCode
                ||"未提供地址"}
            </div>
          }
        </>}

        {locationMethod==="temporary"&&<>
          <div className="note" style={{marginBottom:14}}>
            不論是否屬於專案，都可以臨時新增本次拜訪地點；加入行程後會列入小組長的待確認地點。
          </div>
          <div className="field"><label>地點名稱</label><input value={tempName} onChange={e=>setTempName(e.target.value)} placeholder="例如：客戶 D"/></div>
          <div className="field"><label>地址或 Plus Code</label><input value={tempAddress} onChange={e=>setTempAddress(e.target.value)} placeholder="請輸入完整地址或 Plus Code"/></div>
        </>}

        <div className="field stop-purpose-field">
          <label>行程目的 <span className="optional">選填</span></label>
          <input value={stopPurpose} onChange={e=>setStopPurpose(e.target.value)} placeholder="例如：例行訪視、文件送達、專案訪談"/>
        </div>

        <div className="modal-sticky-actions">
          <button className="btn secondary" onClick={()=>{setModal(null);clearStopEditor()}}>取消</button>
          <button className="btn ok" onClick={saveStop}>{editingStopIndex===null?"加入行程":"儲存修改"}</button>
        </div>
      </div>
    </div>}

    {modal==="submit"&&<div className="modal" onMouseDown={e=>{if(e.target===e.currentTarget)setModal(null)}}>
      <div className="modal-panel">
        <h3>確認送出</h3>
        {overlap.hasOverlap&&<div className="note danger-note" style={{marginBottom:12}}><strong>時間提醒：</strong>{overlap.message||"時間與既有紀錄重疊。"}<label className="check-row"><input type="checkbox" checked={confirmOverlap} onChange={e=>setConfirmOverlap(e.target.checked)}/>我確認時間正確，仍要送出</label></div>}
        <p>送出後，小組長可查看並核定本次里程；Google Maps API 路線里程可在草稿階段由外訪員先行計算。</p>
        {msg&&<div className="note danger-note">{msg}</div>}
        <div className="actions"><button className="btn ok" disabled={busy} onClick={()=>void save(true)}>確認送出</button><button className="btn secondary" disabled={busy} onClick={()=>setModal(null)}>取消</button></div>
      </div>
    </div>}
  </>;
}
