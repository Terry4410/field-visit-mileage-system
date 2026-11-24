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

try{
 await check('CORRECTION_BROWSER_NONFINANCIAL_AND_FINANCIAL_CLOSE',async()=>{
  const vc=await context(),lc=await context(),ac=await context();
  try{
   const v=await vc.newPage(),l=await lc.newPage(),a=await ac.newPage();
   await signIn(v,'E2E-V','外訪員');await signIn(l,'E2E-L','小組長');await signIn(a,'E2E-A','管理者');
   async function call(page,path,method,data,version){
    const token=await page.evaluate(()=>sessionStorage.getItem('fieldvisit_uat_token'));
    const r=await page.request.fetch(api+'/api/v1'+path,{method,headers:{Authorization:'Bearer '+token,...(version?{'If-Match':version}:{})},...(data===undefined?{}:{data})});
    if(!r.ok())throw Error('FIXTURE '+method+' '+path+' HTTP '+r.status());return r.json();
   }
   const me=await read(v,'/me'),date=new Intl.DateTimeFormat('sv-SE',{timeZone:'Asia/Taipei'}).format(new Date());
   const tc=await read(v,'/trips/context?visitDate='+date+'&teamId='+me.teamId);
   const locationId=Number(process.env.BROWSER_APPROVED_LOCATION_ID);expect(locationId).toBeGreaterThan(0);
   const t=await call(v,'/trips','POST',{visitDate:date,startTime:'09:00:00',endTime:'10:00:00',notes:'BROWSER-CORRECTION-BASE',claimedDistanceKm:null,timeOverlapConfirmed:false,teamId:me.teamId,startDeploymentSiteId:tc.defaultStartDeploymentSiteId,endDeploymentSiteId:tc.defaultEndDeploymentSiteId,vehicleType:'Motorcycle',stops:[{locationId,sourceType:'Location',locationName:'Approved',address:'Synthetic approved road 1'}]});
   const preview=await call(v,'/trips/'+t.visitTripId+'/route-preview','POST');expect(preview.status).toBe('Succeeded');
   const latest=await read(v,'/trips/'+t.visitTripId);
   const pending=await call(v,'/trips/'+t.visitTripId+'/submit','POST',{confirmTimeOverlap:false},latest.rowVersion);expect(pending.status).toBe('PendingApproval');

   const approved=await call(l,'/trips/'+t.visitTripId+'/approve','POST',{approvedDistanceKm:11.5,rowVersion:pending.rowVersion,distanceDecisionSource:'LeaderAdjusted',routeCalculationAttemptId:preview.routeCalculationAttemptId,adjustmentReason:'Synthetic dispatch conditions require adjustment.'});
   expect(approved.status).toBe('Approved');expect(Number(approved.approvedAmount)).toBeCloseTo(34.5);
   console.log('CORRECTION_APPROVED_FIXTURE=PASS');
   async function open(){
    await go(v,'/history');
    await v.getByRole('button',{name:'申請更正',exact:true}).click();
    const d=v.getByRole('dialog',{name:'申請更正',exact:true});await expect(d).toBeVisible();return d;
   }
   let d=await open();await d.getByPlaceholder('請說明錯誤原因與需要更正的內容').fill('Synthetic nonfinancial note correction');
   await d.locator('.field').filter({has:v.locator('label').filter({hasText:/^備註$/})}).locator('textarea').fill('BROWSER-CORRECTION-NOTE');
   const first=await action(v,'/corrections','POST',()=>d.getByRole('button',{name:'送出更正申請',exact:true}).click());expect(first.status).toBe('PendingLeaderReview');
   await expect(v.getByRole('button',{name:'更正審核中',exact:true})).toBeDisabled();console.log('CORRECTION_SUBMIT_DUPLICATE_UI_BLOCK=PASS');
   await go(l,'/leader/review');let row=l.getByRole('row').filter({hasText:'Synthetic nonfinancial note correction'});
   await expect(row).toBeVisible();l.once('dialog',x=>x.accept('Synthetic note approved'));
   const closed=await action(l,'/corrections/'+first.correctionRequestId+'/leader-review','POST',()=>row.getByRole('button',{name:'通過',exact:true}).click());expect(closed.status).toBe('Closed');console.log('CORRECTION_NONFINANCIAL_LEADER_CLOSE=PASS');
   await v.reload();d=await open();
   await d.getByPlaceholder('請說明錯誤原因與需要更正的內容').fill('Synthetic financial distance correction');
   await d.locator('.field').filter({has:v.locator('label').filter({hasText:/^申請更正核定里程$/})}).locator('input').fill('12.3');
   const second=await action(v,'/corrections','POST',()=>d.getByRole('button',{name:'送出更正申請',exact:true}).click());expect(second.status).toBe('PendingLeaderReview');
   await l.reload();row=l.getByRole('row').filter({hasText:'Synthetic financial distance correction'});await expect(row).toBeVisible();l.once('dialog',x=>x.accept('Synthetic financial review'));
   const pendingAdmin=await action(l,'/corrections/'+second.correctionRequestId+'/leader-review','POST',()=>row.getByRole('button',{name:'通過',exact:true}).click());expect(pendingAdmin.status).toBe('PendingAdminClose');console.log('CORRECTION_FINANCIAL_LEADER_TO_ADMIN=PASS');
   await go(a,'/admin/corrections');row=a.getByRole('row').filter({hasText:'Synthetic financial distance correction'});await expect(row).toBeVisible();
   const route=await action(a,'/corrections/'+second.correctionRequestId+'/route-preview','POST',()=>row.getByRole('button',{name:'Google 重算里程',exact:true}).click());
   expect(route.status).toBe('Succeeded');expect(Number(route.suggestedDistanceKm)).toBeCloseTo(12.3);
   a.once('dialog',x=>x.accept('Synthetic final close'));
   const final=await action(a,'/corrections/'+second.correctionRequestId+'/admin-close','POST',()=>row.getByRole('button',{name:'結案',exact:true}).click());
   expect(final.status).toBe('Closed');await expect(a.getByText('更正已結案並建立新 Snapshot。',{exact:true})).toBeVisible();console.log('CORRECTION_FINANCIAL_ADMIN_CLOSE=PASS');
  }finally{await vc.close();await lc.close();await ac.close()}
 });
}finally{await browser.close();console.log('CORRECTION_BROWSER_CASES='+JSON.stringify(results));if(results.length!==1||results.some(r=>!r.pass))process.exitCode=1;}
