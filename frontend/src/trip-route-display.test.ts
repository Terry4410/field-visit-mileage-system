import{describe,expect,it}from"vitest";
import{tripRouteSummary}from"./components/TripRouteDisplay";

describe("trip route endpoint display",()=>{
 it("shows center at both ends without changing stop numbering input",()=>{
  expect(tripRouteSummary({startDeploymentSiteName:"貝林中心／貝林據點",endDeploymentSiteName:"貝林中心／貝林據點",stops:[{sourceType:"Master",locationName:"UAT-GOOGLE-GEOCODE-20261005"},{sourceType:"Master",locationName:"UAT-GOOGLE-ROUTE-B-20261005"}]})).toBe("貝林中心／貝林據點 → UAT-GOOGLE-GEOCODE-20261005 → UAT-GOOGLE-ROUTE-B-20261005 → 貝林中心／貝林據點");
 });
 it("keeps a trip-specific start and end around ordered visit stops",()=>{
  expect(tripRouteSummary({startDeploymentSiteName:"Start A",endDeploymentSiteName:"End B",stops:[{sourceType:"Master",locationName:"Stop 1"},{sourceType:"Master",locationName:"Stop 2"}]})).toBe("Start A → Stop 1 → Stop 2 → End B");
 });
});
