import {describe,expect,it} from "vitest";
import {tripLocationAssignment} from "./trip-location-assignment";
import type {TripContext} from "./types";

describe("trip location assignment display",()=>{
  it("binds to the employment dispatch site, not selected team text or trip endpoint",()=>{
    const context:TripContext={employmentId:1,visitDate:"2026-10-16",eligibleForTrip:true,validationCode:"OK",validationMessage:"",selectedTeamId:8,eligibleDeploymentSites:[
      {deploymentSiteId:10,centerId:1,centerCode:"C1",centerName:"台北中心",siteCode:"S1",siteName:"派駐站",locationId:21,locationName:"派駐站",isPrimary:true},
      {deploymentSiteId:11,centerId:1,centerCode:"C1",centerName:"台北中心",siteCode:"S2",siteName:"其他可選站",locationId:22,locationName:"其他站",isPrimary:false}
    ],primaryDeploymentSiteId:10,defaultStartDeploymentSiteId:10};
    expect(tripLocationAssignment(context)).toBe("台北中心／派駐站");
  });
});
