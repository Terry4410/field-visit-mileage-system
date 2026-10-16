import {describe,expect,it} from "vitest";
import {filterLeaderMileageRowsByDate,isLeaderMileageCalculable,partitionLeaderMileageQueues} from "./leader-mileage-filter";
import type {Trip} from "./types";

const trip=(patch:Partial<Trip>):Trip=>({visitTripId:1,tripNo:"T1",userId:1,visitorName:"Visitor",visitDate:"2026-10-10",hasTimeOverlapWarning:false,timeOverlapConfirmed:false,status:"Submitted",statusName:"已送出",stops:[{sourceType:"Master",locationName:"site"}],rowVersion:"",...patch});

describe("leader mileage candidate safety",()=>{
  it("excludes out-of-range rows from retry candidates and keeps approvals in a separate queue",()=>{
    const rows=[trip({visitTripId:1,visitDate:"2026-10-01"}),trip({visitTripId:2,visitDate:"2026-10-15"}),trip({visitTripId:3,visitDate:"2026-10-20",status:"PendingApproval"})];
    expect(filterLeaderMileageRowsByDate(rows,"DateRange","2026-10-10","2026-10-16").map(x=>x.visitTripId)).toEqual([2]);
    const queues=partitionLeaderMileageQueues(rows,"DateRange","2026-10-10","2026-10-16");
    expect(queues.retryCandidates.map(x=>x.visitTripId)).toEqual([2]);
    expect(queues.approvalQueue.map(x=>x.visitTripId)).toEqual([3]);
  });
  it("keeps only current-route failure rows inside the selected date range",()=>{
    const rows=[
      trip({visitTripId:10,visitDate:"2026-10-09",hasCurrentRouteFailureEvidence:true}),
      trip({visitTripId:11,visitDate:"2026-10-12",hasCurrentRouteFailureEvidence:true}),
      trip({visitTripId:12,visitDate:"2026-10-13",hasCurrentRouteFailureEvidence:false})
    ];
    const candidates=partitionLeaderMileageQueues(rows,"DateRange","2026-10-10","2026-10-12").retryCandidates
      .filter(isLeaderMileageCalculable);
    expect(candidates.map(x=>x.visitTripId)).toEqual([11]);
  });
  it("does not send a successful visitor Google route for batch recalculation",()=>{
    expect(isLeaderMileageCalculable(trip({mileageSource:"GoogleMapsAPI",routeCalculationAttemptId:42,systemDistanceKm:18.2}))).toBe(false);
    expect(isLeaderMileageCalculable(trip({mileageSource:"ManualFallback",claimedDistanceKm:9,hasCurrentRouteFailureEvidence:true}))).toBe(true);
  });
  it("requires current-route failure evidence and excludes stale or absent evidence",()=>{
    expect(isLeaderMileageCalculable(trip({status:"Submitted"}))).toBe(false);
    expect(isLeaderMileageCalculable(trip({status:"Submitted",hasCurrentRouteFailureEvidence:true}))).toBe(true);
    expect(isLeaderMileageCalculable(trip({status:"Submitted",hasCurrentRouteFailureEvidence:true,mileageSource:"GoogleMapsAPI",routeCalculationAttemptId:7,systemDistanceKm:12}))).toBe(false);
  });
});
