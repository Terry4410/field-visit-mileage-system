import{describe,expect,it}from"vitest";
import{resolveNavigationTitle}from"./navigation-title";

const admin=[
 {path:"/admin",label:"管理儀表板"},
 {path:"/admin/users",label:"人員與權限"},
 {path:"/admin/teams",label:"小組與成員"},
 {path:"/admin/locations",label:"地點管理"},
 {path:"/admin/projects",label:"專案管理"},
 {path:"/admin/query",label:"行程查詢"},
 {path:"/admin/corrections",label:"更正管理"}
];

describe("navigation title resolution",()=>{
 it.each([
  ["/admin","管理儀表板"],
  ["/admin/users","人員與權限"],
  ["/admin/users/roles","人員與權限"],
  ["/admin/teams/members","小組與成員"],
  ["/admin/locations/new","地點管理"],
  ["/admin/locations/bulk","地點管理"],
  ["/admin/locations/centers","地點管理"],
  ["/admin/locations/official","地點管理"],
  ["/admin/projects/bulk","專案管理"],
  ["/admin/query","行程查詢"],
  ["/admin/corrections","更正管理"]
 ])("%s resolves to %s",(path,label)=>{
  expect(resolveNavigationTitle(admin,path)).toBe(label);
 });

 it("falls back to role home for an unknown path",()=>{
  expect(resolveNavigationTitle(admin,"/admin/unknown")).toBe("管理儀表板");
 });
});
