import{describe,expect,it}from"vitest";
import{isGoogleMileageSource,mileageSourceDistance,mileageSourceLabel}from"./mileage-source-ui";

describe("mileage source UI",()=>{
  it("labels Google as the mileage origin",()=>{
    expect(mileageSourceLabel("GoogleMapsAPI")).toBe("Google Maps API");
    expect(mileageSourceLabel("GoogleMapsRoutes")).toBe("Google Maps API");
    expect(isGoogleMileageSource("GoogleMapsAPI")).toBe(true);
  });

  it("uses system distance for Google mileage",()=>{
    expect(mileageSourceDistance({mileageSource:"GoogleMapsAPI",systemDistanceKm:209.74,claimedDistanceKm:199}))
      .toBe(209.74);
  });

  it("uses claimed field only as manual fallback distance",()=>{
    expect(mileageSourceLabel("ManualFallback")).toBe("人工備援");
    expect(mileageSourceDistance({mileageSource:"ManualFallback",claimedDistanceKm:12.3,systemDistanceKm:99}))
      .toBe(12.3);
  });

  it("does not invent a third business mileage source",()=>{
    expect(mileageSourceLabel(undefined)).toBe("待計算");
  });
});
