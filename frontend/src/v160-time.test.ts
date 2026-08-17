import {describe,expect,it} from "vitest";
import {timeRange} from "./v160";

describe("timeRange",()=>{
  it("formats a complete trip time range",()=>{
    expect(timeRange("13:00:00","15:00:00")).toBe("13:00～15:00");
  });

  it("handles missing times clearly",()=>{
    expect(timeRange(undefined,undefined)).toBe("—");
    expect(timeRange("13:00:00",undefined)).toBe("13:00～—");
    expect(timeRange(undefined,"15:00:00")).toBe("—～15:00");
  });
});
