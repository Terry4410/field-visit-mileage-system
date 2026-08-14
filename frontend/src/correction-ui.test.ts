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
});
