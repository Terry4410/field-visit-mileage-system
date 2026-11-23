import {afterEach,describe,expect,it,vi} from "vitest";
import {getLocalCalendarDate} from "./local-calendar-date";

describe("visitor local calendar date",()=>{
  afterEach(()=>vi.unstubAllEnvs());

  it("uses Taipei calendar date when UTC is still the previous day",()=>{
    vi.stubEnv("TZ","Asia/Taipei");
    expect(getLocalCalendarDate(new Date("2026-11-22T16:30:00Z"))).toBe("2026-11-23");
  });

  it("preserves the local previous date in Los Angeles",()=>{
    vi.stubEnv("TZ","America/Los_Angeles");
    expect(getLocalCalendarDate(new Date("2026-11-23T03:30:00Z"))).toBe("2026-11-22");
  });

  it("pads single-digit month and day",()=>{
    vi.stubEnv("TZ","Asia/Taipei");
    expect(getLocalCalendarDate(new Date("2026-01-02T03:00:00Z"))).toBe("2026-01-02");
  });
});
