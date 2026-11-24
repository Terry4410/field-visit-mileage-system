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

async function raw(page,path,method='GET',data,version){
 const token=await page.evaluate(()=>sessionStorage.getItem('fieldvisit_uat_token'));
 return page.request.fetch(api+'/api/v1'+path,{method,headers:{Authorization:'Bearer '+token,...(version?{'If-Match':version}:{})},...(data===undefined?{}:{data})});
}
async function provision(p,code,role,teamId,today){
 const u=await raw(p,'/admin/people/internal-users','POST',{employeeNo:code,displayName:code,hireDate:'2020-01-01',initialEmploymentStatus:'Active',statusEffectiveFrom:today});expect(u.ok()).toBe(true);const actor=await u.json();
 const joined=await raw(p,'/admin/people/team-memberships/'+teamId+'/batch-add','POST',{userIds:[actor.userId],effectiveFrom:today});expect(joined.ok()).toBe(true);
 const r=await raw(p,'/admin/people/internal-users/'+actor.userId+'/roles','PUT',{roles:[role],effectiveFrom:today});expect(r.ok()).toBe(true);
 return actor;
}
try{
 await check('CROSS_TEAM_REAL_LOGIN_AND_TRIP_SCOPE',async()=>{
  const vc=await context(),lc=await context(),ac=await context(),ov=await context(),ol=await context();
  try{
   const v=await vc.newPage(),l=await lc.newPage(),a=await ac.newPage(),otherV=await ov.newPage(),otherL=await ol.newPage();
   await signIn(v,'E2E-V','外訪員');await signIn(l,'E2E-L','小組長');await signIn(a,'E2E-A','管理者');
   async function call(page,path,method,data,version){
    const token=await page.evaluate(()=>sessionStorage.getItem('fieldvisit_uat_token'));
    const r=await page.request.fetch(api+'/api/v1'+path,{method,headers:{Authorization:'Bearer '+token,...(version?{'If-Match':version}:{})},...(data===undefined?{}:{data})});
    if(!r.ok())throw Error('FIXTURE '+method+' '+path+' HTTP '+r.status());return r.json();
   }
   const me=await read(v,'/me'),date=new Intl.DateTimeFormat('sv-SE',{timeZone:'Asia/Taipei'}).format(new Date());
   const tc=await read(v,'/trips/context?visitDate='+date+'&teamId='+me.teamId);
   const locationId=Number(process.env.BROWSER_APPROVED_LOCATION_ID);expect(locationId).toBeGreaterThan(0);
   const t=await call(v,'/trips','POST',{visitDate:date,startTime:'09:00:00',endTime:'10:00:00',notes:'BROWSER-SCOPE-PROTECTED',claimedDistanceKm:null,timeOverlapConfirmed:false,teamId:me.teamId,startDeploymentSiteId:tc.defaultStartDeploymentSiteId,endDeploymentSiteId:tc.defaultEndDeploymentSiteId,vehicleType:'Motorcycle',stops:[{locationId,sourceType:'Location',locationName:'Approved',address:'Synthetic approved road 1'}]});
   const preview=await call(v,'/trips/'+t.visitTripId+'/route-preview','POST');expect(preview.status).toBe('Succeeded');
   const latest=await read(v,'/trips/'+t.visitTripId);
   const pending=await call(v,'/trips/'+t.visitTripId+'/submit','POST',{confirmTimeOverlap:false},latest.rowVersion);expect(pending.status).toBe('PendingApproval');

   const createTeam=await raw(a,'/admin/teams','POST',{teamCode:'E2E-SCOPE-T2',teamName:'Synthetic Scope Team',isActive:true});expect(createTeam.ok()).toBe(true);const team=await createTeam.json();
   expect(team.teamId).not.toBe(me.teamId);
   await provision(a,'E2E-SCOPE-V','visitor',team.teamId,date);
   await provision(a,'E2E-SCOPE-L','leader',team.teamId,date);
   await signIn(otherV,'E2E-SCOPE-V','外訪員');await signIn(otherL,'E2E-SCOPE-L','小組長');
   const ovProfile=await read(otherV,'/me'),olProfile=await read(otherL,'/me');
   expect(ovProfile.teamId).toBe(team.teamId);expect(olProfile.teamId).toBe(team.teamId);expect(olProfile.teamScopes.map(x=>x.teamId)).toEqual([team.teamId]);
   console.log('TWO_TEAM_REAL_IDENTITIES=PASS');
   expect((await raw(v,'/trips/'+t.visitTripId)).status()).toBe(200);
   expect((await raw(l,'/trips/'+t.visitTripId)).status()).toBe(200);
   expect((await raw(a,'/trips/'+t.visitTripId)).status()).toBe(200);
   const ownQueue=await read(l,'/leader/review-queue');expect(ownQueue.some(x=>x.visitTripId===t.visitTripId)).toBe(true);
   console.log('AUTHORIZED_READ_POSITIVE_CONTROLS=PASS');
   for(const actor of [otherV,otherL]){
    const denied=await raw(actor,'/trips/'+t.visitTripId);expect([403,404]).toContain(denied.status());
   }
   const outsiderQueue=await read(otherL,'/leader/review-queue');expect(outsiderQueue.some(x=>x.visitTripId===t.visitTripId)).toBe(false);
   console.log('CROSS_TEAM_TRIP_READ_AND_QUEUE_DENIED=PASS');
   const attempt=await raw(otherL,'/trips/'+t.visitTripId+'/approve','POST',{approvedDistanceKm:12.3,rowVersion:pending.rowVersion,distanceDecisionSource:'ProviderSuggested',routeCalculationAttemptId:preview.routeCalculationAttemptId});expect([403,404]).toContain(attempt.status());
   const ret=await raw(otherL,'/trips/'+t.visitTripId+'/return','POST',{reason:'Unauthorized synthetic test',rowVersion:pending.rowVersion});expect([403,404]).toContain(ret.status());
   const visitorMutation=await raw(otherV,'/trips/'+t.visitTripId+'/route-preview','POST');expect([403,404]).toContain(visitorMutation.status());
   console.log('CROSS_TEAM_MUTATIONS_DENIED=PASS');
   const unchanged=await read(v,'/trips/'+t.visitTripId);expect(unchanged.status).toBe('PendingApproval');expect(unchanged.rowVersion).toBe(pending.rowVersion);expect(unchanged.notes).toBe('BROWSER-SCOPE-PROTECTED');console.log('PROTECTED_TRIP_UNCHANGED=PASS');
  }finally{await vc.close();await lc.close();await ac.close();await ov.close();await ol.close()}
 });
}finally{await browser.close();console.log('SCOPE_BROWSER_CASES='+JSON.stringify(results));if(results.length!==1||results.some(r=>!r.pass))process.exitCode=1;}
