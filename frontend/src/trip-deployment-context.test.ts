import{describe,expect,it}from"vitest";
import{canSubmitV180Trip,deploymentSiteWarning,reconcileDeploymentSite,V180_TRIP_CONTEXT_API}from"./trip-deployment-context";
import type{V180TripContext}from"./types";

const context=(primary?:number):V180TripContext=>({
 employmentId:10,visitDate:"2026-09-09",eligibleForTrip:true,validationCode:"OK",validationMessage:"ok",
 teams:[{teamId:1,code:"T1",name:"Team 1",isPrimary:true}],selectedTeamId:1,
 eligibleDeploymentSites:[1,2].map(id=>({deploymentSiteId:id,centerId:3,centerCode:"C",centerName:"Center",siteCode:`S${id}`,siteName:`Site ${id}`,locationId:id,locationName:`L${id}`,isPrimary:id===primary})),
 primaryDeploymentSiteId:primary,defaultStartDeploymentSiteId:primary,defaultEndDeploymentSiteId:primary
});

describe("v1.8 trip deployment context ui rules",()=>{
 it("uses visitor context and not the admin site route",()=>{
  expect(V180_TRIP_CONTEXT_API).toBe("/trips/context");
  expect(V180_TRIP_CONTEXT_API).not.toContain("/admin/");
 });
 it("keeps eligible selections and applies only deterministic primary defaults",()=>{
  expect(reconcileDeploymentSite(2,[1,2],1)).toBe(2);
  expect(reconcileDeploymentSite(9,[1,2],1)).toBe(1);
  expect(reconcileDeploymentSite(9,[1,2],undefined)).toBeUndefined();
 });
 it("allows start and end to differ and requires both only for submit",()=>{
  expect(canSubmitV180Trip(context(1),1,2)).toBe(true);
  expect(canSubmitV180Trip(context(1),1,undefined)).toBe(false);
 });
 it("warns without primary and allows an incomplete draft",()=>{
  expect(deploymentSiteWarning(context())).toContain("自行選擇");
  const empty={...context(),eligibleForTrip:false,validationCode:"NO_ELIGIBLE_DEPLOYMENT_SITE",eligibleDeploymentSites:[]};
  expect(deploymentSiteWarning(empty)).toContain("草稿仍可儲存");
  expect(canSubmitV180Trip(empty)).toBe(false);
 });
});
