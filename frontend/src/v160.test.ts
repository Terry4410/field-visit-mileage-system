import{afterEach,describe,expect,it,vi}from"vitest";
import{monthStart,qs,todayTaipei}from"./v160";

describe("v1.6 helpers",()=>{
  it("builds month start",()=>expect(monthStart("2026-08-12")).toBe("2026-08-01"));
  it("omits empty query values",()=>expect(qs({a:1,b:"",c:null,d:"x"})).toBe("a=1&d=x"));
});

describe("Visitor date defaults use Taiwan calendar",()=>{
  afterEach(()=>vi.useRealTimers());

  it("uses Nov 23 in Taiwan when UTC is still Nov 22",()=>{
    vi.useFakeTimers();
    vi.setSystemTime(new Date("2026-11-22T16:30:00Z"));
    expect(todayTaipei()).toBe("2026-11-23");
  });

  it("uses the previous Taiwan calendar day before local midnight",()=>{
    vi.useFakeTimers();
    vi.setSystemTime(new Date("2026-11-22T15:30:00Z"));
    expect(todayTaipei()).toBe("2026-11-22");
  });

  it("uses padded year-month-day for early January",()=>{
    vi.useFakeTimers();
    vi.setSystemTime(new Date("2026-01-01T16:30:00Z"));
    expect(todayTaipei()).toBe("2026-01-02");
  });
});
