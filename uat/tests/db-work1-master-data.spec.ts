import { expect, test, type Page, type Route } from "@playwright/test";

const admin = { userId:1, employeeNo:"A001", displayName:"UAT Admin", organizationId:1, teamId:10, teamName:"Alpha", roles:["admin"], teamScopes:[{teamId:10,teamName:"Alpha",isPrimary:true}], dataScopes:[] };
type ApiHandler=(route:Route,url:URL)=>Promise<boolean>;
function json(route:Route,body:unknown,status=200){return route.fulfill({status,contentType:"application/json",body:JSON.stringify(body)})}
async function authenticatedAdmin(page:Page,onApi:ApiHandler){
  await page.addInitScript(()=>{sessionStorage.setItem("fieldvisit_uat_token","test-session");sessionStorage.setItem("fieldvisit_active_role","admin")});
  await page.route("**/api/v1/**",async route=>{const url=new URL(route.request().url());if(await onApi(route,url))return;if(url.pathname.endsWith("/me"))return json(route,admin);if(url.pathname.endsWith("/teams"))return json(route,[{teamId:10,organizationId:1,teamCode:"T10",teamName:"Alpha"}]);return route.fulfill({status:404,contentType:"application/problem+json",body:JSON.stringify({title:`Unmocked ${route.request().method()} ${url.pathname}`})})});
}
function project(rowVersion="AAAAAAAAAAE=",isActive=true){return {projectId:1,teamId:10,projectCode:"P1",projectName:"Project One",description:"note",locationMode:"List",startDate:"2026-01-01",endDate:null,isActive,inactivatedAt:isActive?null:"2026-09-10T00:00:00Z",inactivatedByUserId:isActive?null:1,rowVersion,locationCount:0}}
const visitTypes=()=>[
  {visitTypeId:1,visitTypeCode:"VISIT",visitTypeName:"拜訪",description:null,sortOrder:10,isActive:true,inactivatedAt:null,inactivatedByUserId:null,rowVersion:"AAAAAAAAAAE="},
  {visitTypeId:2,visitTypeCode:"MEET",visitTypeName:"會議",description:null,sortOrder:20,isActive:true,inactivatedAt:null,inactivatedByUserId:null,rowVersion:"AAAAAAAAAAI="},
  {visitTypeId:3,visitTypeCode:"OLD",visitTypeName:"舊形式",description:null,sortOrder:30,isActive:false,inactivatedAt:"2026-09-01T00:00:00Z",inactivatedByUserId:1,rowVersion:"AAAAAAAAAAM="}
];

test("project ordinary edit has no lifecycle bypass and lifecycle uses refreshed RowVersion",async({page})=>{
  let current=project();let updateBody:Record<string,unknown>|undefined;const lifecycleBodies:Array<{path:string;body:unknown}>=[];
  await authenticatedAdmin(page,async(route,url)=>{
    if(url.pathname.endsWith("/admin/projects/search")){await json(route,{items:[current],page:1,pageSize:50,totalCount:1,totalPages:1});return true}
    if(url.pathname.endsWith("/projects/1")&&route.request().method()==="PUT"){updateBody=route.request().postDataJSON();current={...current,projectName:String(updateBody!.projectName),rowVersion:"AAAAAAAAAAQ="};await json(route,current);return true}
    if(/\/projects\/1\/(deactivate|reactivate)$/.test(url.pathname)){lifecycleBodies.push({path:url.pathname,body:route.request().postDataJSON()});current=url.pathname.endsWith("/deactivate")?project("AAAAAAAAAAU=",false):project("AAAAAAAAAAY=",true);await json(route,current);return true}
    return false;
  });
  page.on("dialog",d=>void d.accept());await page.goto("./#/admin/projects");await expect(page.getByRole("heading",{name:"專案主檔"})).toBeVisible();await page.getByRole("button",{name:"修改"}).click();await page.getByLabel("專案名稱").fill("Project Updated");await page.getByRole("button",{name:"儲存專案"}).click();await expect.poll(()=>updateBody).toBeTruthy();expect(updateBody).not.toHaveProperty("isActive");expect(updateBody).toMatchObject({rowVersion:"AAAAAAAAAAE=",projectName:"Project Updated"});await page.getByRole("button",{name:"停用"}).click();await expect.poll(()=>lifecycleBodies.length).toBe(1);expect(lifecycleBodies[0]).toEqual({path:"/api/v1/projects/1/deactivate",body:{rowVersion:"AAAAAAAAAAQ="}});await page.getByRole("button",{name:"重新啟用"}).click();await expect.poll(()=>lifecycleBodies.length).toBe(2);expect(lifecycleBodies[1]).toEqual({path:"/api/v1/projects/1/reactivate",body:{rowVersion:"AAAAAAAAAAU="}});
});

test("project 409 reloads current data and never automatically retries the write",async({page})=>{
  let putCount=0,searchCount=0;await authenticatedAdmin(page,async(route,url)=>{if(url.pathname.endsWith("/admin/projects/search")){searchCount++;await json(route,{items:[project(searchCount>1?"AAAAAAAAAAI=":"AAAAAAAAAAE=")],page:1,pageSize:50,totalCount:1,totalPages:1});return true}if(url.pathname.endsWith("/projects/1")&&route.request().method()==="PUT"){putCount++;await json(route,{title:"ROWVERSION_CONFLICT"},409);return true}return false});
  await page.goto("./#/admin/projects");await page.getByRole("button",{name:"修改"}).click();await page.getByLabel("專案名稱").fill("Conflict");await page.getByRole("button",{name:"儲存專案"}).click();await expect(page.getByText("專案儲存發生版本衝突；已重新載入最新資料，系統沒有自動重試。")).toBeVisible();await expect.poll(()=>searchCount).toBeGreaterThan(1);await page.waitForTimeout(300);expect(putCount).toBe(1);
});

test("visit type ordinary edit excludes SortOrder/IsActive and reorder sends complete active membership",async({page})=>{
  let rows=visitTypes();let updateBody:Record<string,unknown>|undefined;let reorderBody:unknown;await authenticatedAdmin(page,async(route,url)=>{if(url.pathname.endsWith("/visit-types")&&route.request().method()==="GET"){await json(route,rows);return true}if(url.pathname.endsWith("/visit-types/1")&&route.request().method()==="PUT"){updateBody=route.request().postDataJSON();rows=rows.map(x=>x.visitTypeId===1?{...x,description:String(updateBody!.description??""),rowVersion:"AAAAAAAAAAQ="}:x);await json(route,rows[0]);return true}if(url.pathname.endsWith("/visit-types/reorder")&&route.request().method()==="POST"){reorderBody=route.request().postDataJSON();rows=[rows[1],rows[0],rows[2]].map((x,i)=>x.isActive?{...x,sortOrder:(i+1)*10}:x);await json(route,rows.filter(x=>x.isActive));return true}return false});
  await page.goto("./#/admin/visit-types");await page.locator(".route-item").filter({hasText:"拜訪"}).getByRole("button",{name:"修改"}).click();await page.getByLabel("說明").fill("updated");await page.getByRole("button",{name:"儲存拜訪形式"}).click();await expect.poll(()=>updateBody).toBeTruthy();expect(updateBody).not.toHaveProperty("sortOrder");expect(updateBody).not.toHaveProperty("isActive");expect(updateBody).toMatchObject({rowVersion:"AAAAAAAAAAE="});await page.getByRole("button",{name:"下移 拜訪"}).click();await expect.poll(()=>reorderBody).toEqual({expectedOrder:[2,1]});
});

test("stale visit type reorder reloads membership and does not retry or silently merge",async({page})=>{
  let rows=visitTypes();let reorderCount=0,getCount=0;await authenticatedAdmin(page,async(route,url)=>{if(url.pathname.endsWith("/visit-types")&&route.request().method()==="GET"){getCount++;await json(route,rows);return true}if(url.pathname.endsWith("/visit-types/reorder")&&route.request().method()==="POST"){reorderCount++;rows=[{...rows[0],rowVersion:"AAAAAAAAAAQ="},{...rows[1],rowVersion:"AAAAAAAAAAU="},rows[2]];await json(route,{title:"VISITTYPE_ORDER_MEMBERSHIP_CONFLICT"},409);return true}return false});
  await page.goto("./#/admin/visit-types");await page.getByRole("button",{name:"下移 拜訪"}).click();await expect(page.getByText("拜訪形式排序發生版本衝突；已重新載入最新資料，系統沒有自動重試。")).toBeVisible();await expect.poll(()=>getCount).toBeGreaterThan(1);await page.waitForTimeout(300);expect(reorderCount).toBe(1);
});
