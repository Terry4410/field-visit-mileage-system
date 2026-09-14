import {describe,expect,it} from "vitest";
import {explicitDecisionAttemptId,GOOGLE_MAPS_ATTRIBUTION,googleMapsScriptUrl,leaderRouteRetryPath,parseExplicitGovernedMileage,routeSuggestionSummary,visitorRoutePreviewPath} from "./fc-google-map-ux";

describe("F-C Google map UX",()=>{
  it("is disabled by default and loads the browser map only after explicit UI action",()=>{
    Object.defineProperty(globalThis,"window",{value:{APP_CONFIG:{EPIC_F_GOOGLE:{ENABLED:false,MAPS_JS_API_KEY:""}}},configurable:true});
    expect(routeSuggestionSummary({routeCalculationAttemptId:1,correlationId:"c",status:"Succeeded",encodedPolyline:"p"}).canMap).toBe(false);
    expect(googleMapsScriptUrl("BROWSER KEY")).toContain("loading=async&libraries=geometry");
    expect(googleMapsScriptUrl("BROWSER KEY")).not.toContain("places");
  });

  it("keeps provider output transient and separate from claimed mileage",()=>{
    const result={routeCalculationAttemptId:1,correlationId:"c",status:"Succeeded",suggestedDistanceKm:12.34,durationSeconds:61};
    expect(routeSuggestionSummary(result)).toMatchObject({distance:"12.3 km",duration:"2 分鐘"});
    expect(GOOGLE_MAPS_ATTRIBUTION).toBe("Google Maps");
  });

  it("requires an attempt for provider decisions but not ManualFallback",()=>{
    const success={routeCalculationAttemptId:42,correlationId:"c",status:"Succeeded"};
    expect(explicitDecisionAttemptId("ProviderSuggested",success)).toBe(42);
    expect(explicitDecisionAttemptId("LeaderAdjusted",success)).toBe(42);
    expect(explicitDecisionAttemptId("ManualFallback",null)).toBeNull();
  });

  it("enables map only when both the flag and a browser-only key are present",()=>{
    Object.defineProperty(globalThis,"window",{value:{APP_CONFIG:{EPIC_F_GOOGLE:{ENABLED:true,MAPS_JS_API_KEY:"browser-key"}}},configurable:true});
    expect(routeSuggestionSummary({routeCalculationAttemptId:1,correlationId:"c",status:"Succeeded",encodedPolyline:"p"}).canMap).toBe(true);
    expect(visitorRoutePreviewPath(12)).toBe("/trips/12/route-preview");
    expect(leaderRouteRetryPath(12)).toBe("/trips/12/route-retry");
  });

  it("requires an explicitly entered positive governed mileage",()=>{
    expect(parseExplicitGovernedMileage("")).toBeNull();
    expect(parseExplicitGovernedMileage(" ")).toBeNull();
    expect(parseExplicitGovernedMileage("0")).toBeNull();
    expect(parseExplicitGovernedMileage("-1")).toBeNull();
    expect(parseExplicitGovernedMileage("abc")).toBeNull();
    expect(parseExplicitGovernedMileage("Infinity")).toBeNull();
    expect(parseExplicitGovernedMileage("12.5")).toBe(12.5);
  });
});
