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
 await check('MANUAL_FALLBACK_WORKER_AND_BROWSER_APPROVAL',async()=>{
  const vc=await context(),lc=await context();
  try{
   const v=await vc.newPage(),l=await lc.newPage();
   await signIn(v,'E2E-V','外訪員');await signIn(l,'E2E-L','小組長');
   async function call(page,path,method,data,version){
    const token=await page.evaluate(()=>sessionStorage.getItem('fieldvisit_uat_token'));
    const r=await page.request.fetch(api+'/api/v1'+path,{method,headers:{Authorization:'Bearer '+token,...(version?{'If-Match':version}:{})},...(data===undefined?{}:{data})});
    if(!r.ok())throw Error('FIXTURE '+method+' '+path+' HTTP '+r.status());return r.json();
   }
   const me=await read(v,'/me'),date=new Intl.DateTimeFormat('sv-SE',{timeZone:'Asia/Taipei'}).format(new Date());
   const tc=await read(v,'/trips/context?visitDate='+date+'&teamId='+me.teamId);
   const locationId=Number(process.env.BROWSER_APPROVED_LOCATION_ID);expect(locationId).toBeGreaterThan(0);
   const t=await call(v,'/trips','POST',{visitDate:date,startTime:'09:00:00',endTime:'10:00:00',notes:'BROWSER-MANUAL-FALLBACK',claimedDistanceKm:12.3,timeOverlapConfirmed:false,teamId:me.teamId,startDeploymentSiteId:tc.defaultStartDeploymentSiteId,endDeploymentSiteId:tc.defaultEndDeploymentSiteId,vehicleType:'Motorcycle',stops:[{locationId,sourceType:'Location',locationName:'Approved',address:'Synthetic approved road 1'}]});

   // API fixture sets a known manual claim; provider always fails on loopback.
   const failed=await call(v,'/trips/'+t.visitTripId+'/route-preview','POST');expect(failed.status).not.toBe('Succeeded');expect(failed.errorCode).toBeTruthy();
   const latest=await read(v,'/trips/'+t.visitTripId);
   const submitted=await call(v,'/trips/'+t.visitTripId+'/submit','POST',{confirmTimeOverlap:false},latest.rowVersion);
   expect(submitted.status).toBe('Submitted');expect(Number(submitted.claimedDistanceKm)).toBeCloseTo(12.3);console.log('FALLBACK_SUBMITTED_FIXTURE=PASS');
   await go(l,'/leader/review');
   const section=l.locator('details').filter({hasText:'Google Maps 里程重算'});
   await section.locator('summary').click();
   await expect(section.getByRole('button',{name:'批次重新計算里程',exact:true})).toBeEnabled();
   const queued=await action(l,'/jobs/mileage','POST',()=>section.getByRole('button',{name:'批次重新計算里程',exact:true}).click());
   expect(queued.backgroundJobId).toBeTruthy();console.log('FALLBACK_BROWSER_JOB_ENQUEUE=PASS');
   await expect.poll(async()=>{const j=await read(l,'/jobs/'+queued.backgroundJobId);return j.status},{timeout:45000,intervals:[1000,2000]}).not.toMatch(/^(Waiting|Processing)$/);
   const job=await read(l,'/jobs/'+queued.backgroundJobId);expect(job.totalCount).toBe(1);expect(job.successCount).toBe(1);expect(job.failedCount).toBe(0);
   const pending=await read(v,'/trips/'+t.visitTripId);expect(pending.status).toBe('PendingApproval');expect(pending.mileageSource).toBe('ManualFallback');expect(pending.systemDistanceKm??null).toBeNull();console.log('FALLBACK_WORKER_PENDING_APPROVAL=PASS');
   const row=l.getByRole('row').filter({has:l.getByText(t.tripNo,{exact:true})});await expect(row).toBeVisible();await expect(row).toContainText('人工備援');
   l.once('dialog',d=>d.accept());
   const approved=await action(l,'/trips/'+t.visitTripId+'/approve','POST',()=>row.getByRole('button',{name:'核准行程 '+t.tripNo,exact:true}).click());
   expect(approved.status).toBe('Approved');expect(Number(approved.approvedDistanceKm)).toBeCloseTo(12.3);expect(Number(approved.approvedAmount)).toBeCloseTo(36.9);
   console.log('FALLBACK_BROWSER_APPROVE_AMOUNT=PASS');
   const saved=await read(v,'/trips/'+t.visitTripId);expect(saved.status).toBe('Approved');expect(saved.mileageSource).toBe('ManualFallback');console.log('FALLBACK_FINAL_READBACK=PASS');
  }finally{await vc.close();await lc.close()}
 });
}finally{await browser.close();console.log('FALLBACK_BROWSER_CASES='+JSON.stringify(results));if(results.length!==1||results.some(r=>!r.pass))process.exitCode=1;}
