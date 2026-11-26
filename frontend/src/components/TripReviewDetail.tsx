import type {Trip} from "../types";
import {km,money,timeRange} from "../v160";
import {mileageSourceLabel} from "../mileage-source-ui";
import ReadOnlyDetail from "./ReadOnlyDetail";
import TripRouteDisplay from "./TripRouteDisplay";
export default function TripReviewDetail({trip,onClose}:{trip:Trip;onClose:()=>void}){
 return <ReadOnlyDetail title={"行程明細｜"+trip.tripNo} onClose={onClose} fields={[["外訪員",trip.visitorName],["小組",trip.teamName],["日期",trip.visitDate],["時間",timeRange(trip.startTime,trip.endTime)],["狀態",trip.statusName||trip.status],["行程目的",trip.purpose],["行程備註",trip.notes],["里程來源",mileageSourceLabel(trip.mileageSource)],["人工備援",km(trip.claimedDistanceKm)],["Google／系統里程",km(trip.systemDistanceKm)],["核定里程（已儲存）",km(trip.approvedDistanceKm)],["費率",money(trip.ratePerKmSnapshot)],["補助",money(trip.approvedAmount)]]}>
 <h4>拜訪路線</h4><TripRouteDisplay trip={trip} includeStopDetail/>
 {trip.stops.some(s=>s.notes)&&<><h4>站點備註</h4>{trip.stops.map((s,i)=>s.notes?<p key={i}>{i+1}. {s.locationName}：{s.notes}</p>:null)}</>}
 {trip.hasTimeOverlapWarning&&<div className="note">行程存在時間重疊提醒。</div>}
 <div className="note">僅供查看審核清單已取得的資料；未儲存的核定里程仍以原審核欄位為準。</div>
 </ReadOnlyDetail>;
}
