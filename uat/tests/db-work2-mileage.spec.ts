import { expect, test, type Page, type Route } from "@playwright/test";

const admin = { userId:1, employeeNo:"A001", displayName:"UAT Admin", organizationId:1, teamId:10, teamName:"Alpha", roles:["admin"], teamScopes:[{teamId:10,teamName:"Alpha",isPrimary:true}], dataScopes:[] };
type ApiHandler=(route:Route,url:URL)=>Promise<boolean>;
function json(route:Route,body:unknown,status=200){return route.fulfill({status,contentType:"application/json",body:JSON.stringify(body)})}
async function authenticatedAdmin(page:Page,onApi:ApiHandler){
  await page.addInitScript(()=>{sessionStorage.setItem("fieldvisit_uat_token","test-session");sessionStorage.setItem("fieldvisit_active_role","admin")});
  await page.route("**/api/v1/**",async route=>{const url=new URL(route.request().url());if(await onApi(route,url))return;if(url.pathname.endsWith("/me"))return json(route,admin);return route.fulfill({status:404,contentType:"application/problem+json",body:JSON.stringify({title:`Unmocked ${route.request().method()} ${url.pathname}`})})});
}
function rate(rowVersion="AAAAAAAAAAE=",isActive=true){return {mileageRateRuleId:1,organizationId:1,ruleName:"2026 rate",vehicleType:"MOTORCYCLE",ratePerKm:2.5,effectiveFrom:"2026-01-01",effectiveTo:null,isActive,rowVersion}}
function noImpact(){return {effectiveFrom:"2026-01-01",vehicleType:"MOTORCYCLE",approvedTripCount:0,firstApprovedVisitDate:null,lastApprovedVisitDate:null,requiresAcknowledgement:false}}

test("mileage rate ordinary edit sends RowVersion and keeps EffectiveTo database-owned",async({page})=>{
  let current=rate();let putBody:Record<string,unknown>|undefined;
  await authenticatedAdmin(page,async(route,url)=>{
    if(url.pathname.endsWith("/mileage-rate-rules")&&route.request().method()==="GET"){await json(route,[current]);return true}
    if(url.pathname.endsWith("/mileage-rate-rules/1")&&route.request().method()==="PUT"){putBody=route.request().postDataJSON();current={...current,ruleName:String(putBody!.ruleName),rowVersion:"AAAAAAAAAAI="};await json(route,current);return true}
    if(url.pathname.endsWith("/mileage-rate-rules/impact")){await json(route,noImpact());return true}
    return false;
  });
  await page.goto("./#/admin/rates");await expect(page.getByRole("heading",{name:"新增費率版本"})).toBeVisible();await page.getByRole("button",{name:"修改"}).click();await page.getByLabel("規則名稱／備註").fill("updated rate");await page.getByRole("button",{name:"儲存修改"}).click();
  await expect.poll(()=>putBody).toBeTruthy();expect(putBody).toMatchObject({rowVersion:"AAAAAAAAAAE=",effectiveTo:null,vehicleType:"Motorcycle",ruleName:"updated rate"});
});

test("stale mileage rate update reloads latest row and never automatically retries",async({page})=>{
  let putCount=0,getCount=0;
  await authenticatedAdmin(page,async(route,url)=>{
    if(url.pathname.endsWith("/mileage-rate-rules")&&route.request().method()==="GET"){getCount++;await json(route,[rate(getCount>1?"AAAAAAAAAAI=":"AAAAAAAAAAE=")]);return true}
    if(url.pathname.endsWith("/mileage-rate-rules/1")&&route.request().method()==="PUT"){putCount++;await json(route,{title:"ROWVERSION_CONFLICT"},409);return true}
    if(url.pathname.endsWith("/mileage-rate-rules/impact")){await json(route,noImpact());return true}
    return false;
  });
  await page.goto("./#/admin/rates");await page.getByRole("button",{name:"修改"}).click();await page.getByLabel("規則名稱／備註").fill("stale edit");await page.getByRole("button",{name:"儲存修改"}).click();
  await expect(page.getByText("費率儲存發生版本衝突；已重新載入最新資料，系統沒有自動重試。")).toBeVisible();await expect.poll(()=>getCount).toBeGreaterThan(1);await page.waitForTimeout(300);expect(putCount).toBe(1);
});

test("mileage rate deactivate keeps DELETE route and sends RowVersion with acknowledged impact",async({page})=>{
  let deleteQuery:Record<string,string>|undefined;
  page.on("dialog",dialog=>void dialog.accept());
  await authenticatedAdmin(page,async(route,url)=>{
    if(url.pathname.endsWith("/mileage-rate-rules")&&route.request().method()==="GET"){await json(route,[rate()]);return true}
    if(url.pathname.endsWith("/mileage-rate-rules/impact")){await json(route,{effectiveFrom:"2026-01-01",vehicleType:"MOTORCYCLE",approvedTripCount:2,firstApprovedVisitDate:"2026-02-01",lastApprovedVisitDate:"2026-03-01",requiresAcknowledgement:true});return true}
    if(url.pathname.endsWith("/mileage-rate-rules/1")&&route.request().method()==="DELETE"){deleteQuery={rowVersion:url.searchParams.get("rowVersion")||"",acknowledgeHistoricalImpact:url.searchParams.get("acknowledgeHistoricalImpact")||""};await route.fulfill({status:204});return true}
    return false;
  });
  await page.goto("./#/admin/rates");await page.getByRole("button",{name:"停用"}).click();await expect.poll(()=>deleteQuery).toEqual({rowVersion:"AAAAAAAAAAE=",acknowledgeHistoricalImpact:"true"});
});

test("stale mileage rate deactivate reloads latest row and never automatically retries",async({page})=>{
  let deleteCount=0,getCount=0;
  page.on("dialog",dialog=>void dialog.accept());
  await authenticatedAdmin(page,async(route,url)=>{
    if(url.pathname.endsWith("/mileage-rate-rules")&&route.request().method()==="GET"){getCount++;await json(route,[rate(getCount>1?"AAAAAAAAAAI=":"AAAAAAAAAAE=")]);return true}
    if(url.pathname.endsWith("/mileage-rate-rules/impact")){await json(route,noImpact());return true}
    if(url.pathname.endsWith("/mileage-rate-rules/1")&&route.request().method()==="DELETE"){deleteCount++;await json(route,{title:"ROWVERSION_CONFLICT"},409);return true}
    return false;
  });
  await page.goto("./#/admin/rates");await page.getByRole("button",{name:"停用"}).click();await expect(page.getByText("費率停用發生版本衝突；已重新載入最新資料，系統沒有自動重試。")).toBeVisible();await expect.poll(()=>getCount).toBeGreaterThan(1);await page.waitForTimeout(300);expect(deleteCount).toBe(1);
});
