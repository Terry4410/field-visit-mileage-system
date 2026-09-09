import{describe,expect,it}from"vitest";
import{lifecycleEndDate,lifecycleStatus,v180LifecycleErrorMessage}from"./v180-team-center-ui";

describe("v1.8 team center lifecycle ui",()=>{
 it("treats lifecycle boundaries as inclusive",()=>{
  expect(lifecycleStatus("2026-09-09","2026-09-09","2026-09-09")).toBe("生效中");
  expect(lifecycleStatus("2026-09-10",null,"2026-09-09")).toBe("未來");
  expect(lifecycleStatus("2026-09-01","2026-09-08","2026-09-09")).toBe("已結束");
 });
 it("never chooses a lifecycle end before the effective start",()=>{
  expect(lifecycleEndDate("2026-09-10","2026-09-09")).toBe("2026-09-10");
  expect(lifecycleEndDate("2026-09-01","2026-09-09")).toBe("2026-09-09");
 });
 it("turns rowversion conflicts into an explicit reload instruction",()=>{
  expect(v180LifecycleErrorMessage(new Error("ROWVERSION_CONFLICT：stale"))).toContain("重新整理");
 });
 it("explains dependency guards instead of hiding backend lifecycle rules",()=>{
  expect(v180LifecycleErrorMessage(new Error("TEAM_DEACTIVATION_BLOCKED：x"))).toContain("請先結束");
  expect(v180LifecycleErrorMessage(new Error("CENTER_DEACTIVATION_BLOCKED：x"))).toContain("請先結束");
 });
});
