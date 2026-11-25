import { chromium, expect } from '@playwright/test';
const origin='http://127.0.0.1:4173', api='http://127.0.0.1:5080';
const password=process.env.BROWSER_DEMO_PASSWORD;
if(!password || password.length<20) throw Error('ISOLATED_PASSWORD_REQUIRED');
const browser=await chromium.launch({headless:true});
const results=[];
async function check(name,fn){try{await fn();results.push({name,pass:true});console.log(name+'=PASS')}catch(e){results.push({name,pass:false});console.log(name+'=FAIL '+String(e.message).replace(/\x1b\[[0-9;]*m/g,'').split('\n').slice(0,8).join(' | ').slice(0,900))}}
const contexts=[];
async function context(mobile=false){const c=await browser.newContext(mobile?{viewport:{width:390,height:844},isMobile:true,hasTouch:true}:{viewport:{width:1440,height:900}});contexts.push(c);
 await c.route('**/*',r=>{const u=new URL(r.request().url());return (u.origin===origin||u.origin===api)?r.continue():r.abort()});return c;}
async function signIn(page,account,role){await page.goto(origin);await page.locator('input[autocomplete=username]').fill(account);await page.locator('input[type=password]').fill(password);
 const [r]=await Promise.all([page.waitForResponse(r=>r.url()===api+'/api/v1/auth/demo-login'),page.getByRole('button',{name:'登入',exact:true}).click()]);expect(r.status()).toBe(200);await expect(page.locator('.role-box')).toContainText(role);await expect(page.locator('.topbar h1')).toBeVisible();}

async function action(page,path,method,click){
 const [r]=await Promise.all([page.waitForResponse(r=>new URL(r.url()).pathname==='/api/v1'+path&&r.request().method()===method,{timeout:20000}),click()]);
 if(!r.ok()){const problem=await r.json().catch(()=>({}));throw Error(method+' '+path+' HTTP '+r.status()+' '+String(problem.detail||'').slice(0,250));}
 return r.json();
}
async function read(page,path){const token=await page.evaluate(()=>sessionStorage.getItem('fieldvisit_uat_token'));const r=await page.request.get(api+'/api/v1'+path,{headers:{Authorization:'Bearer '+token}});if(!r.ok())throw Error('READ '+path+' HTTP '+r.status());return r.json()}
async function go(page,path){await page.goto(origin+'/#'+path);await page.waitForLoadState('networkidle')}
async function submit(page,id){
 await page.getByRole('button',{name:/^(送出行程|重新送出)$/}).click();
 await expect(page.getByRole('heading',{name:'確認送出',exact:true})).toBeVisible();
 const t=await action(page,'/trips/'+id+'/submit','POST',()=>page.getByRole('button',{name:'確認送出',exact:true}).click());
 expect(t.status).toBe('PendingApproval');return t;
}
async function routePreview(page,id){
 const button=page.getByRole('button',{name:'用 Google Maps API 計算里程',exact:true});await expect(button).toBeEnabled();
 const watched=[];const observe=r=>{if(new URL(r.url()).pathname==='/api/v1/trips/'+id&&r.request().method()==='PUT')watched.push(r)};
 page.on('response',observe);
 let preview;
 try{preview=await action(page,'/trips/'+id+'/route-preview','POST',()=>button.click())}
 catch(e){const errors=[];for(const r of watched){const p=await r.json().catch(()=>({}));errors.push(r.status()+':'+String(p.detail||'').slice(0,200))}throw Error('RETURNED_ROUTE_FAILED '+errors.join(',')+' UI '+(await page.locator('.note').allTextContents()).join(' | ').slice(-600)+' '+e.message)}
 finally{page.off('response',observe)}
 expect(preview.status).toBe('Succeeded');expect(Number(preview.suggestedDistanceKm)).toBeCloseTo(12.3);
 await expect(page.getByPlaceholder('Google 無可用結果時才開放')).toBeDisabled();
}
try{
 await check('TRIP_BROWSER_PROVIDER_SUCCESS_RETURN_RESUBMIT_APPROVE',async()=>{
  const vc=await context(),lc=await context();try{
   const v=await vc.newPage(),l=await lc.newPage();
   await signIn(v,'E2E-V','外訪員');await signIn(l,'E2E-L','小組長');
   // Provision a Returned record through previously verified API paths only.
   async function call(page,path,method,data,version){
    const token=await page.evaluate(()=>sessionStorage.getItem('fieldvisit_uat_token'));
    const r=await page.request.fetch(api+'/api/v1'+path,{method,headers:{Authorization:'Bearer '+token,...(version?{'If-Match':version}:{})},...(data===undefined?{}:{data})});
    if(!r.ok())throw Error('FIXTURE '+method+' '+path+' HTTP '+r.status());return r.json();
   }
   const me=await read(v,'/me'),date=new Intl.DateTimeFormat('sv-SE',{timeZone:'Asia/Taipei'}).format(new Date());
   const tc=await read(v,'/trips/context?visitDate='+date+'&teamId='+me.teamId);
   const locationId=Number(process.env.BROWSER_APPROVED_LOCATION_ID);expect(locationId).toBeGreaterThan(0);
   const t=await call(v,'/trips','POST',{visitDate:date,startTime:'09:00:00',endTime:'10:00:00',notes:'BROWSER-TRIP-FLOW',claimedDistanceKm:null,timeOverlapConfirmed:false,teamId:me.teamId,startDeploymentSiteId:tc.defaultStartDeploymentSiteId,endDeploymentSiteId:tc.defaultEndDeploymentSiteId,vehicleType:'Motorcycle',stops:[{locationId,sourceType:'Location',locationName:'Approved',address:'Synthetic approved road 1'}]});
   const preview=await call(v,'/trips/'+t.visitTripId+'/route-preview','POST');expect(preview.status).toBe('Succeeded');
   const latest=await read(v,'/trips/'+t.visitTripId);
   const pending=await call(v,'/trips/'+t.visitTripId+'/submit','POST',{confirmTimeOverlap:false},latest.rowVersion);expect(pending.status).toBe('PendingApproval');
   const returned=await call(l,'/trips/'+t.visitTripId+'/return','POST',{reason:'請補充本次拜訪備註',rowVersion:pending.rowVersion});expect(returned.status).toBe('Returned');
   console.log('RETURNED_FIXTURE_READY=PASS');
   await go(v,'/history');await v.getByRole('button',{name:'修改後重送',exact:true}).click();
   await expect(v.locator('.danger-note').filter({hasText:'主管退回原因：'})).toBeVisible();
   await expect(v.locator('textarea[placeholder*="拜訪補充說明"]')).toHaveValue('BROWSER-TRIP-FLOW');
   await expect(v.locator('.route-item:not(.endpoint-route) .route-name').filter({hasText:/^Approved$/})).toBeVisible();
   await v.locator('textarea[placeholder*="拜訪補充說明"]').fill('BROWSER-TRIP-FLOW-CORRECTED');
   // Save through the actual UI; observe the PUT separately from route preview.
   const savedEdit=await action(v,'/trips/'+t.visitTripId,'PUT',()=>v.getByRole('button',{name:'儲存預排行程',exact:true}).click());
   expect(savedEdit.notes).toBe('BROWSER-TRIP-FLOW-CORRECTED');expect(savedEdit.visitTripId).toBe(t.visitTripId);
   console.log('FLOW_RETURNED_EDIT=PASS');
   await expect(v.getByText(/預排行程已儲存/)).toBeVisible();
   await routePreview(v,t.visitTripId);
   console.log('FLOW_RETURNED_ROUTE=PASS');
   await go(l,'/leader/review');
   const row=l.getByRole('row').filter({has:l.getByText(t.tripNo,{exact:true})});
   const again=await submit(v,t.visitTripId);expect(again.visitTripId).toBe(t.visitTripId);console.log('FLOW_RESUBMIT_SAME_RECORD=PASS');
   await l.reload();await expect(row).toBeVisible();
   l.once('dialog',d=>d.accept());
   const approved=await action(l,'/trips/'+t.visitTripId+'/approve','POST',()=>row.getByRole('button',{name:'核准行程 '+t.tripNo,exact:true}).click());
   expect(approved.status).toBe('Approved');expect(Number(approved.approvedDistanceKm)).toBeCloseTo(12.3);expect(Number(approved.approvedAmount)).toBeCloseTo(36.9);console.log('FLOW_APPROVE_AMOUNT=PASS');
   const saved=await read(v,'/trips/'+t.visitTripId);expect(saved.status).toBe('Approved');expect(saved.notes).toBe('BROWSER-TRIP-FLOW-CORRECTED');console.log('FLOW_FINAL_READBACK=PASS');
  }finally{await vc.close();await lc.close()}
 });
}finally{await browser.close();console.log('BUSINESS_BROWSER_CASES='+JSON.stringify(results));if(results.length!==1||results.some(r=>!r.pass))process.exitCode=1;}
