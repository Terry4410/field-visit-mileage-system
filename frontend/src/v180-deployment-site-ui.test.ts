import{describe,expect,it}from"vitest";
import{deploymentSiteErrorMessage,deploymentSiteLifecycleStatus,V180_DEPLOYMENT_SITE_API}from"./v180-deployment-site-ui";

describe("v1.8 deployment site ui",()=>{
 it("treats lifecycle boundaries as inclusive",()=>{
  expect(deploymentSiteLifecycleStatus("2026-09-09","2026-09-09","2026-09-09")).toBe("生效中");
  expect(deploymentSiteLifecycleStatus("2026-09-10",null,"2026-09-09")).toBe("未來");
  expect(deploymentSiteLifecycleStatus("2026-09-01","2026-09-08","2026-09-09")).toBe("已結束");
 });
 it("maps rowversion and authoritative business errors to clear Traditional Chinese",()=>{
  expect(deploymentSiteErrorMessage(new Error("ROWVERSION_CONFLICT"))).toContain("重新整理");
  expect(deploymentSiteErrorMessage(new Error("DEPLOYMENT_LOCATION_OVERLAP"))).toContain("重疊");
  expect(deploymentSiteErrorMessage(new Error("TEAM_SITE_CENTER_CONFLICT"))).toContain("同一中心");
  expect(deploymentSiteErrorMessage(new Error("EMPLOYMENT_SITE_PRIMARY_OVERLAP"))).toContain("主要派駐點");
  expect(deploymentSiteErrorMessage(new Error("DEPLOYMENT_LOCATION_ORGANIZATION_MISMATCH"))).toContain("組織");
 });
 it("uses only v1.8 site lifecycle routes",()=>{
  expect(V180_DEPLOYMENT_SITE_API.sites).toBe("/admin/v180/deployment-sites");
  expect(V180_DEPLOYMENT_SITE_API.site(7)).toBe("/admin/v180/deployment-sites/7");
  expect(V180_DEPLOYMENT_SITE_API.deactivateSite(7)).toBe("/admin/v180/deployment-sites/7/deactivate");
 });
 it("uses v1.8 create update and end routes for every assignment type",()=>{
  expect(V180_DEPLOYMENT_SITE_API.locationAssignments).toBe("/admin/v180/deployment-site-location-assignments");
  expect(V180_DEPLOYMENT_SITE_API.locationAssignment(8)).toBe("/admin/v180/deployment-site-location-assignments/8");
  expect(V180_DEPLOYMENT_SITE_API.endLocationAssignment(8)).toBe("/admin/v180/deployment-site-location-assignments/8/end");
  expect(V180_DEPLOYMENT_SITE_API.teamAssignments).toBe("/admin/v180/team-deployment-site-assignments");
  expect(V180_DEPLOYMENT_SITE_API.teamAssignment(9)).toBe("/admin/v180/team-deployment-site-assignments/9");
  expect(V180_DEPLOYMENT_SITE_API.endTeamAssignment(9)).toBe("/admin/v180/team-deployment-site-assignments/9/end");
  expect(V180_DEPLOYMENT_SITE_API.employmentAssignments).toBe("/admin/v180/employment-deployment-site-assignments");
  expect(V180_DEPLOYMENT_SITE_API.employmentAssignment(10)).toBe("/admin/v180/employment-deployment-site-assignments/10");
  expect(V180_DEPLOYMENT_SITE_API.endEmploymentAssignment(10)).toBe("/admin/v180/employment-deployment-site-assignments/10/end");
 });
});
