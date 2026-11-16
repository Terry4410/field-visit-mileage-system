import { describe, expect, it } from "vitest";
import { resolveApiBase } from "./api";

const HOST = "ashy-water-021992f00.2.azurestaticapps.net";
const GOOD = "https://api-fieldvisit-pathb-uat.azurewebsites.net/api/v1";

describe("Path B API isolation", () => {
  it("accepts only the exact Path B API origin and path", () => {
    expect(resolveApiBase(GOOD, HOST)).toBe(GOOD);
    expect(resolveApiBase("https://api-fieldvisit-pathb-uat.azurewebsites.net", HOST)).toBe(GOOD);
  });
  it.each([
    undefined, "", " ", "http://localhost:5080",
    "https://api-fieldvisit-uat-cxauf4g8fzdyfsd9.eastasia-01.azurewebsites.net/api/v1",
    "https://api-fieldvisit-uat.azurewebsites.net/api/v1",
    "https://api-fieldvisit-pathb-uat.azurewebsites.net.evil.test/api/v1",
    "http://api-fieldvisit-pathb-uat.azurewebsites.net/api/v1",
    "https://api-fieldvisit-pathb-uat.azurewebsites.net:444/api/v1",
    "https://api-fieldvisit-pathb-uat.azurewebsites.net/api/v2",
    "https://api-fieldvisit-pathb-uat.azurewebsites.net/api/v1?target=old",
  ])("blocks unsafe Path B API config: %s", (value) => {
    expect(() => resolveApiBase(value, HOST)).toThrow("已停止連線");
  });
  it("keeps localhost fallback only for local development", () => {
    expect(resolveApiBase(undefined, "localhost")).toBe("http://localhost:5080/api/v1");
    expect(() => resolveApiBase(undefined, "another.azurestaticapps.net")).toThrow();
  });
});
