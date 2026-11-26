import{describe,expect,it,vi}from"vitest";
import{
  correctionChangeText,
  correctionFieldLabel,
  correctionStatusLabel,
  ALL_CORRECTION_STATUSES,
  correctionSearchEnabled,
  isPendingCorrection,
  withConfirmedCorrectionComment
}from"./correction-ui";

describe("correction UI",()=>{
  it("only treats active correction workflow as pending",()=>{
    expect(isPendingCorrection("PendingLeaderReview")).toBe(true);
    expect(isPendingCorrection("PendingAdminClose")).toBe(true);
    expect(isPendingCorrection("Closed")).toBe(false);
    expect(isPendingCorrection("Rejected")).toBe(false);
    expect(isPendingCorrection(undefined)).toBe(false);
  });

  it("translates correction field names",()=>{
    expect(correctionFieldLabel("ClaimedDistanceKm"))
      .toBe("外訪員自算里程");
    expect(correctionFieldLabel("ApprovedDistanceKm"))
      .toBe("核定里程");
    expect(correctionFieldLabel("SubsidyAmount"))
      .toBe("補助金額");
  });

  it("shows old and new values",()=>{
    expect(correctionChangeText({
      fieldName:"ApprovedDistanceKm",
      oldValue:"18.8",
      newValue:"15"
    })).toBe("核定里程：18.8 → 15");
  });

  it("handles empty values",()=>{
    expect(correctionChangeText({
      fieldName:"Notes",
      oldValue:"",
      newValue:"UAT"
    })).toBe("備註：— → UAT");
  });

  it("renders stop JSON as readable Chinese field differences",()=>{
    const oldValue=JSON.stringify([
      {
        stopSequence:1,
        locationCode:"LOC-1",
        locationName:"南投就業中心-埔里分站",
        address:"北辰街101號",
        visitPurpose:"電訪"
      },
      {
        stopSequence:2,
        locationCode:"LOC-2",
        locationName:"集集就業服務台",
        address:"民生路61號",
        visitPurpose:null
      }
    ]);

    const newValue=JSON.stringify([
      {
        stopSequence:1,
        locationCode:"LOC-1",
        locationName:"南投就業中心-埔里分站",
        address:"北辰街101號",
        visitPurpose:"電訪"
      },
      {
        stopSequence:2,
        locationCode:"LOC-2",
        locationName:"集集就業服務台",
        address:"民生路61號",
        visitPurpose:"修正拜訪目的"
      }
    ]);

    expect(correctionChangeText({
      fieldName:"Stops",
      oldValue,
      newValue
    })).toBe(
      "第 2 站（集集就業服務台）－拜訪目的：— → 修正拜訪目的"
    );
  });

  it("does not expose raw JSON when stop data is malformed",()=>{
    expect(correctionChangeText({
      fieldName:"Stops",
      oldValue:"not-json",
      newValue:"also-not-json"
    })).toBe("拜訪地點：內容已變更");
  });
});

describe("correction comment cancellation",()=>{
  it.each(["Leader review","Admin close","Admin batch close"])("%s never submits on Cancel",async()=>{
    const submit=vi.fn(async (_comments:string)=>{});
    await withConfirmedCorrectionComment(null,submit);
    expect(submit).not.toHaveBeenCalled();
  });
  it.each(["","已確認"])("explicit confirmation preserves comment %j",async comments=>{
    const submit=vi.fn(async (_comments:string)=>{});
    await withConfirmedCorrectionComment(comments,submit);
    expect(submit).toHaveBeenCalledTimes(1);
    expect(submit).toHaveBeenCalledWith(comments);
  });
});

it("labels correction workflow status in Chinese",()=>{expect(correctionStatusLabel("PendingAdminClose")).toBe("待管理者結案");expect(correctionStatusLabel("Rejected")).toBe("已退回")});

it("supports explicit All with blank or valid dates and rejects reversed dates",()=>{expect(ALL_CORRECTION_STATUSES).toBe("All");expect(correctionSearchEnabled("","")).toBe(true);expect(correctionSearchEnabled("2026-11-01","")).toBe(true);expect(correctionSearchEnabled("","2026-11-30")).toBe(true);expect(correctionSearchEnabled("2026-11-01","2026-11-30")).toBe(true);expect(correctionSearchEnabled("2026-11-30","2026-11-01")).toBe(false)});

it("uses Taipei request date at the UTC day boundary",async()=>{const{correctionRequestDate}=await import("./correction-ui");expect(correctionRequestDate("2026-11-25T16:00:00Z")).toBe("2026-11-26");expect(correctionRequestDate("2026-11-25T15:59:59")).toBe("2026-11-25")});
