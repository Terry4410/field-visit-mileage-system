import{describe,expect,it}from"vitest";
import{resolveTripTeamForEdit}from"./trip-team-edit-rules";
import type{TeamScope}from"./types";

const north:TeamScope={
  teamId:4,
  teamName:"北區第一組",
  isPrimary:true
};

const nantou:TeamScope={
  teamId:8,
  teamName:"南投就業中心",
  isPrimary:false
};

describe("resolveTripTeamForEdit",()=>{
  it("keeps the saved trip team when it is still authorized",()=>{
    const result=
      resolveTripTeamForEdit(
        8,
        "南投就業中心",
        [north,nantou],
        4,
        "北區第一組"
      );

    expect(result.teamId).toBe(8);
    expect(result.reassigned).toBe(false);
    expect(result.originalTeamStillAllowed).toBe(true);
    expect(result.warning).toBeUndefined();
  });

  it("moves a stale draft to the current primary team",()=>{
    const result=
      resolveTripTeamForEdit(
        8,
        "南投就業中心",
        [north],
        4,
        "北區第一組"
      );

    expect(result.teamId).toBe(4);
    expect(result.teamName).toBe("北區第一組");
    expect(result.reassigned).toBe(true);
    expect(result.originalTeamStillAllowed).toBe(false);
    expect(result.warning).toContain("南投就業中心");
    expect(result.warning).toContain("北區第一組");
  });

  it("returns no selectable team when all team access is gone",()=>{
    const result=
      resolveTripTeamForEdit(
        8,
        "南投就業中心",
        [],
        undefined,
        undefined
      );

    expect(result.teamId).toBeUndefined();
    expect(result.reassigned).toBe(false);
    expect(result.warning).toContain("目前沒有可使用的小組");
  });
});
