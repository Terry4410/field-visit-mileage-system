import{describe,expect,it}from"vitest";
import type{Trip}from"./types";
import{
  formatTripTime,
  hasLeaderTimeOverlap,
  hasQueueTimeOverlap,
  leaderOverlapConfirmMessage,
  leaderOverlapWarningText
}from"./leader-overlap";

function trip(overrides:Partial<Trip>={}):Trip{
  return{
    visitTripId:1,
    tripNo:"T1",
    userId:7,
    visitorName:"測試",
    teamId:8,
    teamName:"南投就業中心",
    visitDate:"2026-08-19",
    startTime:"08:30:00",
    endTime:"09:10:00",
    hasTimeOverlapWarning:false,
    timeOverlapConfirmed:false,
    status:"PendingApproval",
    statusName:"待核准",
    stops:[],
    rowVersion:"AA==",
    ...overrides
  };
}

describe("leader overlap review",()=>{
  it("formats HH:mm time ranges",()=>{
    expect(formatTripTime(trip())).toBe("08:30～09:10");
  });

  it("marks both visible rows when their ranges overlap",()=>{
    const a=trip({visitTripId:1,startTime:"08:30:00",endTime:"09:10:00"});
    const b=trip({visitTripId:2,startTime:"08:50:00",endTime:"09:30:00",hasTimeOverlapWarning:true,timeOverlapConfirmed:true});
    const rows=[a,b];
    expect(hasQueueTimeOverlap(a,rows)).toBe(true);
    expect(hasQueueTimeOverlap(b,rows)).toBe(true);
    expect(hasLeaderTimeOverlap(a,rows)).toBe(true);
    expect(hasLeaderTimeOverlap(b,rows)).toBe(true);
    expect(leaderOverlapWarningText(a,rows)).toBe("⚠ 時間重疊");
    expect(leaderOverlapWarningText(b,rows)).toBe("⚠ 時間重疊｜外訪員已確認");
  });

  it("does not treat adjacent ranges as overlap",()=>{
    const a=trip({visitTripId:1,startTime:"08:30:00",endTime:"09:10:00"});
    const b=trip({visitTripId:2,startTime:"09:10:00",endTime:"09:50:00"});
    expect(hasQueueTimeOverlap(a,[a,b])).toBe(false);
    expect(hasQueueTimeOverlap(b,[a,b])).toBe(false);
  });

  it("uses persisted warning even if counterpart is not in current queue",()=>{
    const a=trip({hasTimeOverlapWarning:true,timeOverlapConfirmed:true});
    expect(hasLeaderTimeOverlap(a,[a])).toBe(true);
    expect(leaderOverlapConfirmMessage(a,[a])).toContain("外訪員已確認");
  });

  it("does not compare different visitors",()=>{
    const a=trip({visitTripId:1,userId:7});
    const b=trip({visitTripId:2,userId:99,startTime:"08:50:00",endTime:"09:30:00"});
    expect(hasQueueTimeOverlap(a,[a,b])).toBe(false);
  });
});
