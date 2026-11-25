import {createHmac} from 'node:crypto';
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
 if(!r.ok())throw Error(method+' '+path+' HTTP '+r.status());
 return r.status()===204?null:r.json();
}
async function read(page,path){const token=await page.evaluate(()=>sessionStorage.getItem('fieldvisit_uat_token'));const r=await page.request.get(api+'/api/v1'+path,{headers:{Authorization:'Bearer '+token}});if(!r.ok())throw Error('READ '+path+' HTTP '+r.status());return r.json()}
async function go(page,path){await page.goto(origin+'/#'+path);await page.waitForLoadState('networkidle')}

async function loginAttempt(page,account){
 await page.goto(origin);await page.locator('input[autocomplete=username]').fill(account);
 await page.locator('input[type=password]').fill(password);
 const [r]=await Promise.all([page.waitForResponse(r=>new URL(r.url()).pathname==='/api/v1/auth/demo-login'),page.getByRole('button',{name:'登入',exact:true}).click()]);return r;
}
try{
 await check('ADMIN_BROWSER_TEAM_ROLE_AND_NEW_VISITOR_LOGIN',async()=>{
  const ac=await context(),vc=await context(),oc=await context();try{
   const p=await ac.newPage(),v=await vc.newPage();await signIn(p,'E2E-A','管理者');
   // Use already-verified personnel creation endpoint only to provision an independent synthetic actor.
   const token=await p.evaluate(()=>sessionStorage.getItem('fieldvisit_uat_token'));
   const today=new Intl.DateTimeFormat('sv-SE',{timeZone:'Asia/Taipei'}).format(new Date());
   const created=await p.request.post(api+'/api/v1/admin/people/internal-users',{headers:{Authorization:'Bearer '+token},data:{employeeNo:'E2E-ACCESS-01',displayName:'Synthetic Access Actor',hireDate:'2020-01-01',initialEmploymentStatus:'Active',statusEffectiveFrom:today}});
   expect(created.ok()).toBe(true);const u=await created.json();
   // Independently verify effective access; a successful identity login must not imply business authorization.
   await check('UNASSIGNED_PERSON_NO_BUSINESS_ACCESS',async()=>{
    const outsider=await oc.newPage();
    const owner=await (await context()).newPage();
    await signIn(owner,'E2E-V','外訪員');await owner.waitForLoadState('networkidle');
    await owner.locator('textarea[placeholder*="拜訪補充說明"]').fill('ISOLATED-ROLELESS-ACCESS-PROBE');
    const trip=await action(owner,'/trips','POST',()=>owner.getByRole('button',{name:'儲存預排行程',exact:true}).click());
    expect(trip.status).toBe('Draft');
    const login=await loginAttempt(outsider,'E2E-ACCESS-01');
    expect([401,403]).toContain(login.status());await expect(outsider.locator('.danger-note')).toBeVisible();
    console.log('ROLELESS_NEW_TOKEN_DENIED=PASS');
    // Reproduce an already-issued roleless JWT with the disposable runner key.
    const signingKey=process.env.BROWSER_TEST_SIGNING_KEY;
    if(!signingKey||signingKey.length<32)throw Error('ISOLATED_SIGNING_KEY_REQUIRED');
    const b64=x=>Buffer.from(JSON.stringify(x)).toString('base64url');
    const unsigned=b64({alg:'HS256',typ:'JWT'})+'.'+b64({iss:'FieldVisit.UAT',aud:'FieldVisit.UAT.Users',exp:Math.floor(Date.now()/1000)+600,'http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier':String(u.userId)});
    const oldToken=unsigned+'.'+createHmac('sha256',signingKey).update(unsigned).digest('base64url');
    const h={Authorization:'Bearer '+oldToken};
    const result=await outsider.request.get(api+'/api/v1/trips/'+trip.visitTripId,{headers:h});
    const me=await outsider.request.get(api+'/api/v1/me',{headers:h});
    expect([401,403]).toContain(result.status());expect([401,403]).toContain(me.status());
    console.log('ROLELESS_EXISTING_TOKEN_DENIED=PASS');
   });
   await go(p,'/admin/teams');await p.getByRole('button',{name:'＋新增小組',exact:true}).click();
   let d=p.getByRole('dialog');await d.getByPlaceholder('例如 TEAM-001').fill('E2E-ACCESS-T2');await d.getByPlaceholder('例如 北區第一組').fill('Synthetic Access Team');
   const team=await action(p,'/admin/teams','POST',()=>d.getByRole('button',{name:'新增小組',exact:true}).click());
   await expect(p.getByText('小組已新增。',{exact:true})).toBeVisible();
   const row=p.getByRole('row').filter({has:p.getByText('E2E-ACCESS-T2',{exact:true})});await expect(row).toBeVisible();console.log('TEAM_BROWSER_CREATE=PASS');
   await row.getByRole('button',{name:'維護',exact:true}).click();
   await d.getByPlaceholder('例如 北區第一組').fill('Synthetic Access Team Edited');
   await action(p,'/admin/teams/'+team.teamId,'PUT',()=>d.getByRole('button',{name:'儲存修改',exact:true}).click());
   await expect(row).toContainText('Synthetic Access Team Edited');console.log('TEAM_BROWSER_EDIT=PASS');
   await go(p,'/admin/teams/members');await p.locator('label').filter({hasText:/^管理小組/}).locator('select').selectOption(String(team.teamId));
   await expect(p.getByText('Synthetic Access Team Edited',{exact:true})).toBeVisible();
   await p.getByRole('button',{name:'＋加入小組成員',exact:true}).click();
   d=p.getByRole('dialog');await d.getByPlaceholder('工號、姓名或 Email').fill('E2E-ACCESS-01');
   await d.getByRole('checkbox',{name:'選取 Synthetic Access Actor',exact:true}).check();
   p.once('dialog',x=>x.accept());
   const joined=await action(p,'/admin/people/team-memberships/'+team.teamId+'/batch-add','POST',()=>d.getByRole('button',{name:'確認加入（1）',exact:true}).click());
   expect(joined.addedCount).toBe(1);
   await expect(p.getByRole('row').filter({hasText:'E2E-ACCESS-01'})).toContainText('Synthetic Access Team Edited');
   console.log('TEAM_BROWSER_MEMBER_PRIMARY=PASS');
   await go(p,'/admin/users/roles');await p.getByPlaceholder('工號、姓名或 Email').fill('E2E-ACCESS-01');
   const userRow=p.getByRole('row').filter({hasText:'E2E-ACCESS-01'});
   await userRow.getByRole('button',{name:'維護角色',exact:true}).click();
   d=p.getByRole('dialog');await d.getByLabel('外訪員',{exact:true}).check();
   await action(p,'/admin/people/internal-users/'+u.userId+'/roles','PUT',()=>d.getByRole('button',{name:'儲存角色',exact:true}).click());
   await expect(userRow).toContainText('外訪員');await expect(userRow).toContainText('允許登入');console.log('ROLE_BROWSER_ASSIGN=PASS');
   const allowed=await loginAttempt(v,'E2E-ACCESS-01');expect(allowed.status()).toBe(200);await expect(v.locator('.role-box')).toContainText('外訪員');
   const me=await read(v,'/me');expect(me.roles.map(x=>x.toLowerCase())).toEqual(['visitor']);expect(me.teamId).toBe(team.teamId);console.log('NEW_VISITOR_LOGIN_ROLE_TEAM_READBACK=PASS');
   const newToken=await v.evaluate(()=>sessionStorage.getItem('fieldvisit_uat_token'));
   const forbidden=await v.request.get(api+'/api/v1/admin/people',{headers:{Authorization:'Bearer '+newToken}});expect(forbidden.status()).toBe(403);
   await go(v,'/admin/users');await expect(v).toHaveURL(origin+'/#/');console.log('NEW_VISITOR_ADMIN_API_AND_UI_DENIED=PASS');
  }finally{await ac.close();await vc.close();await oc.close()}
 });
}finally{await browser.close();console.log('ACCESS_BROWSER_CASES='+JSON.stringify(results));if(results.length!==2||results.some(r=>!r.pass))process.exitCode=1;}
