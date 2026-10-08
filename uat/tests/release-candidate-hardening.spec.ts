import {expect,test} from "@playwright/test";
import{
  addDays,apiBaseUrl,auth,cleanup,createTrip,deleteProject,login,loginUi,ok,setup,stop,unique
}from"./uat-test-helpers";

test("RC-AUTH-01 role boundaries fail closed for management APIs",async({request})=>{
  const visitor=await login(request,"pilotv01");
  const supervisor=await login(request,"pilots02");

  const people=await request.get(`${apiBaseUrl}/api/v1/admin/people?page=1&pageSize=10`,{
    headers:auth(visitor.accessToken,"visitor")
  });
  expect(people.status()).toBe(403);

  const projectMutation=await request.post(`${apiBaseUrl}/api/v1/projects`,{
    headers:auth(supervisor.accessToken,"supervisor"),
    data:{
      teamId:null,projectCode:unique("RC-AUTH"),projectName:"UAT-AUTO forbidden",
      description:"UAT-AUTO forbidden",locationMode:"SelfMaintained",
      startDate:null,endDate:null,isActive:true
    }
  });
  expect(projectMutation.status()).toBe(403);
});

test("RC-CONC-01 stale row version cannot overwrite a newer Draft",async({request})=>{
  const{visitor,admin,tid,locs,s}=await setup(request);
  const purpose=unique("RC-CONC");
  let id:number|null=null;
  try{
    const draft=await createTrip(request,visitor,tid,purpose,s,[stop(locs[0])],10);
    id=draft.visitTripId;
    const body={
      visitDate:s.visitDate,startTime:s.startTime,endTime:s.endTime,
      claimedDistanceKm:10,purpose,notes:purpose+"-FIRST",
      timeOverlapConfirmed:false,teamId:tid,stops:[stop(locs[0])]
    };
    const first=await request.put(`${apiBaseUrl}/api/v1/trips/${id}`,{
      headers:{...auth(visitor.accessToken,"visitor"),"If-Match":draft.rowVersion},
      data:body
    });
    await ok(first,"first draft update");

    const stale=await request.put(`${apiBaseUrl}/api/v1/trips/${id}`,{
      headers:{...auth(visitor.accessToken,"visitor"),"If-Match":draft.rowVersion},
      data:{...body,notes:purpose+"-STALE"}
    });
    expect(stale.ok()).toBe(false);
    expect(stale.status()).not.toBe(500);
    expect([409,412,422]).toContain(stale.status());

    const current=await request.get(`${apiBaseUrl}/api/v1/trips/${id}`,{
      headers:auth(visitor.accessToken,"visitor")
    });
    await ok(current,"reload after stale write");
    expect((await current.json()).notes).toBe(purpose+"-FIRST");
  }finally{
    await cleanup(request,admin,id,purpose);
  }
});

test("RC-DATE-01 future project is discoverable but rejected outside its effective period",async({request})=>{
  const{visitor,admin,tid,locs,s}=await setup(request);
  const code=unique("RC-PROJECT");
  const startDate=addDays(s.visitDate,2);
  const endDate=addDays(s.visitDate,4);
  let project:any=null;
  try{
    let r=await request.post(`${apiBaseUrl}/api/v1/projects`,{
      headers:auth(admin.accessToken,"admin"),
      data:{teamId:tid,projectCode:code,projectName:code,description:code,locationMode:"SelfMaintained",startDate,endDate,isActive:true}
    });
    await ok(r,"create RC future project");
    project=await r.json();

    r=await request.get(`${apiBaseUrl}/api/v1/projects`,{
      headers:auth(visitor.accessToken,"visitor")
    });
    await ok(r,"visitor project list");
    expect((await r.json()).some((x:any)=>x.projectId===project.projectId)).toBe(true);

    r=await request.post(`${apiBaseUrl}/api/v1/trips`,{
      headers:auth(visitor.accessToken,"visitor"),
      data:{
        visitDate:s.visitDate,startTime:s.startTime,endTime:s.endTime,
        claimedDistanceKm:null,purpose:code,notes:code,timeOverlapConfirmed:false,teamId:tid,
        stops:[stop(locs[0],project.projectId,null)]
      }
    });
    expect(r.ok()).toBe(false);
    expect(r.status()).not.toBe(500);
    expect(await r.text()).toContain("早於專案");
  }finally{
    if(project)await deleteProject(request,admin,project.projectId);
  }
});

test("RC-HISTORY-01 inactive project remains admin-queryable but disappears from new visitor selection",async({request})=>{
  const{visitor,admin,tid,s}=await setup(request);
  const code=unique("RC-HISTORY");
  let project:any=null;
  try{
    let r=await request.post(`${apiBaseUrl}/api/v1/projects`,{
      headers:auth(admin.accessToken,"admin"),
      data:{teamId:tid,projectCode:code,projectName:code,description:code,locationMode:"SelfMaintained",startDate:addDays(s.visitDate,-1),endDate:null,isActive:true}
    });
    await ok(r,"create RC history project");
    project=await r.json();

    r=await request.delete(`${apiBaseUrl}/api/v1/projects/${project.projectId}`,{
      headers:auth(admin.accessToken,"admin")
    });
    await ok(r,"deactivate RC project");

    r=await request.get(`${apiBaseUrl}/api/v1/admin/projects/search?keyword=${encodeURIComponent(code)}&status=Inactive&page=1&pageSize=20`,{
      headers:auth(admin.accessToken,"admin")
    });
    await ok(r,"admin inactive project query");
    expect((await r.json()).items.some((x:any)=>x.projectId===project.projectId)).toBe(true);

    r=await request.get(`${apiBaseUrl}/api/v1/projects`,{
      headers:auth(visitor.accessToken,"visitor")
    });
    await ok(r,"visitor active project list");
    expect((await r.json()).some((x:any)=>x.projectId===project.projectId)).toBe(false);
  }finally{
    if(project)await deleteProject(request,admin,project.projectId);
  }
});

test("RC-MOBILE-01 admin critical pages stay inside the mobile shell",async({page})=>{
  await page.setViewportSize({width:390,height:844});
  await loginUi(page,"pilota01","管理儀表板");

  const assertShell=async(title:string)=>{
    await expect(page.locator(".topbar h1")).toHaveText(title);
    const overflow=await page.evaluate(
      ()=>document.documentElement.scrollWidth-document.documentElement.clientWidth
    );
    expect(overflow).toBeLessThanOrEqual(2);
  };
  const mobile=page.locator(".mobile-tabs");

  await mobile.getByRole("link",{name:"人員",exact:true}).click();
  await assertShell("人員與權限");
  await page.getByRole("button",{name:"＋新增人員",exact:true}).click();
  await expect(page.getByRole("dialog")).toContainText("新增人員");
  await page.getByRole("dialog").getByRole("button",{name:"關閉",exact:true}).click();

  await mobile.getByRole("link",{name:"小組",exact:true}).click();
  await assertShell("小組與成員");
  await page.getByRole("link",{name:"成員配置",exact:true}).click();
  await assertShell("小組與成員");
  await expect(page.getByPlaceholder("小組代碼或名稱").first()).toBeVisible();

  await mobile.getByRole("link",{name:"地點",exact:true}).click();
  await assertShell("地點管理");
  for(const tab of ["新增地點","Excel 批次維護","就業中心","官方據點"]){
    await page.getByRole("link",{name:tab,exact:true}).click();
    await assertShell("地點管理");
  }

  await mobile.getByRole("link",{name:"查詢",exact:true}).click();
  await assertShell("行程查詢");

  await mobile.getByRole("link",{name:"更正",exact:true}).click();
  await assertShell("更正管理");
});
