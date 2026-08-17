import {describe,expect,it} from "vitest";
import {
  claimedMileageRequiredMessage,
  minimumStopsForMileageMessage,
  validateTripMileageForSubmit
} from "./trip-submit-rules";

describe("validateTripMileageForSubmit",()=>{
  it("rejects a single-stop formal submission",()=>{
    expect(validateTripMileageForSubmit(1,"")).toBe(minimumStopsForMileageMessage);
  });

  it("rejects missing or zero claimed mileage for two stops",()=>{
    expect(validateTripMileageForSubmit(2,"")).toBe(claimedMileageRequiredMessage);
    expect(validateTripMileageForSubmit(2,"0")).toBe(claimedMileageRequiredMessage);
  });

  it("allows two stops with positive claimed mileage",()=>{
    expect(validateTripMileageForSubmit(2,"12.3")).toBeNull();
  });
});
