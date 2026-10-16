import {describe,expect,it} from "vitest";
import {filterLeaderMileageRowsByDate,isLeaderMileageCalculable} from "./leader-mileage-filter";
import type {Trip} from "./types";

const trip=(patch:Partial<Trip>):Trip=>({visitTripId:1,tripNo:"T1",userId:1,visitorName:"Visitor",visitDate:"2026-10-10",hasTimeOverlapWarning:false,timeOverlapConfirmed:false,status:"Submitted",statusName:"已送出",stops:[{sourceType:"Master",locationName:"site"}],rowVersion:"",...patch});

describe("leader mileage candidate safety",()=>{
  it("excludes out-of-range rows while keeping approvals visible",()=>{
    const rows=[trip({visitTripId:1,visitDate:"2026-10-01"}),trip({visitTripId:2,visitDate:"2026-10-15"}),trip({visitTripId:3,visitDate:"2026-10-20",status:"PendingApproval"})];
    expect(filterLeaderMileageRowsByDate(rows,"DateRange","2026-10-10","2026-10-16").map(x=>x.visitTripId)).toEqual([2,3]);
  });
  it("does not send a successful visitor Google route for batch recalculation",()=>{
    expect(isLeaderMileageCalculable(trip({mileageSource:"GoogleMapsAPI",routeCalculationAttemptId:42,systemDistanceKm:18.2}))).toBe(false);
    expect(isLeaderMileageCalculable(trip({mileageSource:"ManualFallback",claimedDistanceKm:9}))).toBe(true);
  });
});
