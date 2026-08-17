import{describe,expect,it}from"vitest";
import{
  correctionChangeText,
  correctionFieldLabel,
  isPendingCorrection
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
