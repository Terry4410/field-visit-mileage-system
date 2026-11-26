import {describe,expect,it} from "vitest";
import {adminNavGroups,adminNavItems} from "./admin-navigation";
import {locationAdminGroups} from "./components/LocationAdminTabs";
import {resolveNavigationTitle} from "./navigation-title";

describe("admin navigation consolidation",()=>{
 it("keeps all ten workspaces once and the dashboard first",()=>{
  expect(adminNavGroups.map(g=>g.label)).toEqual(["營運管理","人事與組織","業務主檔","報表與分析"]);
  expect(adminNavItems.map(x=>x.path)).toEqual([
   "/admin","/admin/query","/admin/corrections","/admin/users","/admin/teams",
   "/admin/locations","/admin/projects","/admin/visit-types","/admin/rates","/admin/reports"
  ]);
  expect(new Set(adminNavItems.map(x=>x.path)).size).toBe(10);
  expect(resolveNavigationTitle(adminNavItems,"/admin/locations/approvals")).toBe("地點管理");
 });
 it.each([true,false])("keeps feature-gated location tabs (enabled=%s)",enabled=>{
  const items=locationAdminGroups(enabled).flatMap(g=>g.items);
  expect(items.map(x=>x.path)).toEqual([
   "/admin/locations","/admin/locations/new","/admin/locations/bulk",
   "/admin/locations/duplicates",...(enabled?["/admin/locations/approvals"]:[]),
   "/admin/locations/centers","/admin/locations/official"
  ]);
  expect(items.filter(x=>x.path==="/admin/locations/approvals")).toHaveLength(enabled?1:0);
  expect(items.find(x=>x.path==="/admin/locations/centers")?.label).toBe("就業中心");
 });
});
