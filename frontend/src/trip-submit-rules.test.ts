import{describe,expect,it}from"vitest";
import{
  manualFallbackDistanceForBody,
  manualFallbackMileageInvalidMessage,
  minimumStopsForMileageMessage,
  validateTripMileageForSubmit
}from"./trip-submit-rules";

describe("validateTripMileageForSubmit",()=>{
  it("rejects zero-stop formal submission",()=>{
    expect(validateTripMileageForSubmit(0,"")).toBe(minimumStopsForMileageMessage);
  });
  it("allows one stop without pre-entered manual fallback mileage",()=>{
    expect(validateTripMileageForSubmit(1,"")).toBeNull();
    expect(validateTripMileageForSubmit(1,null)).toBeNull();
  });
  it("rejects invalid manual fallback mileage when supplied",()=>{
    expect(validateTripMileageForSubmit(1,"0")).toBe(manualFallbackMileageInvalidMessage);
    expect(validateTripMileageForSubmit(1,"-1")).toBe(manualFallbackMileageInvalidMessage);
  });
  it("allows one stop with positive manual fallback mileage",()=>{
    expect(validateTripMileageForSubmit(1,"12.3")).toBeNull();
  });
});

describe("manualFallbackDistanceForBody",()=>{
  it("keeps positive manual fallback for a one-stop route",()=>{
    expect(manualFallbackDistanceForBody(1,"12.3")).toBe(12.3);
  });
  it("omits manual fallback for zero stops or blank input",()=>{
    expect(manualFallbackDistanceForBody(0,"12.3")).toBeNull();
    expect(manualFallbackDistanceForBody(1,"")).toBeNull();
  });
});
