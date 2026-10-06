import{describe,expect,it}from"vitest";
import{canCalculateGoogleMileage,hasMinimumVisitStops}from"./pages/VisitorPage";

describe("one-stop route eligibility",()=>{
 it("allows one visit stop when both endpoints exist",()=>{
  expect(hasMinimumVisitStops(1)).toBe(true);
  expect(canCalculateGoogleMileage(false,1,"101","102")).toBe(true);
 });
 it("rejects zero stops, missing endpoints, or busy state",()=>{
  expect(hasMinimumVisitStops(0)).toBe(false);
  expect(canCalculateGoogleMileage(false,0,"101","102")).toBe(false);
  expect(canCalculateGoogleMileage(false,1,"","102")).toBe(false);
  expect(canCalculateGoogleMileage(false,1,"101","")).toBe(false);
  expect(canCalculateGoogleMileage(true,1,"101","102")).toBe(false);
 });
});
