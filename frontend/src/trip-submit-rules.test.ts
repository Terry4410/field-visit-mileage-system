import {describe,expect,it} from "vitest";
import {manualFallbackMileageInvalidMessage,minimumStopsForMileageMessage,validateTripMileageForSubmit} from "./trip-submit-rules";
describe("validateTripMileageForSubmit",()=>{
  it("rejects a single-stop formal submission",()=>{expect(validateTripMileageForSubmit(1,"")).toBe(minimumStopsForMileageMessage)});
  it("allows two stops without pre-entered manual fallback mileage",()=>{expect(validateTripMileageForSubmit(2,"")).toBeNull();expect(validateTripMileageForSubmit(2,null)).toBeNull()});
  it("rejects invalid manual fallback mileage when supplied",()=>{expect(validateTripMileageForSubmit(2,"0")).toBe(manualFallbackMileageInvalidMessage);expect(validateTripMileageForSubmit(2,"-1")).toBe(manualFallbackMileageInvalidMessage)});
  it("allows two stops with positive manual fallback mileage",()=>{expect(validateTripMileageForSubmit(2,"12.3")).toBeNull()});
});
