import { chromium, expect } from '@playwright/test';
const origin='http://127.0.0.1:4173', api='http://127.0.0.1:5080';
const password=process.env.BROWSER_DEMO_PASSWORD;
if(!password || password.length<20) throw Error('ISOLATED_PASSWORD_REQUIRED');
const browser=await chromium.launch({headless:true});
const results=[];
async function check(name,fn){try{await fn();results.push({name,pass:true});console.log(name+'=PASS')}catch(e){results.push({name,pass:false});console.log(name+'=FAIL '+String(e.message).split('\n')[0].slice(0,250))}}
const contexts=[];
async function context(mobile=false){const c=await browser.newContext(mobile?{viewport:{width:390,height:844},isMobile:true,hasTouch:true}:{viewport:{width:1440,height:900}});contexts.push(c);
 await c.route('**/*',r=>{const u=new URL(r.request().url());return (u.origin===origin||u.origin===api)?r.continue():r.abort()});return c;}
async function signIn(page,account,role){await page.goto(origin);await page.locator('input[autocomplete=username]').fill(account);await page.locator('input[type=password]').fill(password);
 const [r]=await Promise.all([page.waitForResponse(r=>r.url()===api+'/api/v1/auth/demo-login'),page.getByRole('button',{name:'登入',exact:true}).click()]);expect(r.status()).toBe(200);await expect(page.locator('.role-box')).toContainText(role);await expect(page.locator('.topbar h1')).toBeVisible();}
async function pageCheck(page,path,mobile=false){const issues=[];const onResponse=r=>{if(r.url().startsWith(api+'/api/') && r.status()>=400)issues.push(r.status()+':'+new URL(r.url()).pathname)};const onError=()=>issues.push('PAGE_EXCEPTION');page.on('response',onResponse);page.on('pageerror',onError);
 try{const nav=page.locator(mobile?'.mobile-tabs':'.sidebar nav');await nav.locator('a[href="#'+path+'"]').click();await expect(page).toHaveURL(origin+'/#'+path);await page.waitForLoadState('networkidle',{timeout:12000});await expect(page.locator('.content')).toBeVisible();expect((await page.locator('.content').innerText()).trim().length).toBeGreaterThan(0);expect(issues.join(',')).toBe('');await expect(page.locator('.content')).not.toContainText(/系統發生未預期錯誤|無法連線到系統 API/);}
 finally{page.off('response',onResponse);page.off('pageerror',onError);}}
try{
 const guest=await (await context()).newPage();
 await check('GUEST_PROTECTED_ROUTE',async()=>{await guest.goto(origin+'/#/admin/users');await expect(guest.getByRole('button',{name:'登入',exact:true})).toBeVisible();const r=await guest.request.get(api+'/api/v1/me');expect(r.status()).toBe(401)});
 await check('WRONG_PASSWORD_UI',async()=>{await guest.locator('input[autocomplete=username]').fill('E2E-V');await guest.locator('input[type=password]').fill('wrong-isolated-password');await guest.getByRole('button',{name:'登入',exact:true}).click();await expect(guest.locator('.danger-note')).toContainText('帳號或密碼錯誤')});
 for(const actor of [{id:'VISITOR',account:'E2E-V',role:'外訪員',home:'/',paths:['/','/history','/locations/manage']},{id:'LEADER',account:'E2E-L',role:'小組長',home:'/leader',paths:['/leader','/leader/review','/leader/query','/leader/locations']},{id:'ADMIN',account:'E2E-A',role:'管理者',home:'/admin',paths:['/admin','/admin/query','/admin/corrections','/admin/users','/admin/teams','/admin/locations','/admin/projects','/admin/visit-types','/admin/rates','/admin/reports']}]){
  const page=await (await context()).newPage();
  let signed=false;await check(actor.id+'_LOGIN',async()=>{await signIn(page,actor.account,actor.role);signed=true});
  if(!signed)continue;
  for(const path of actor.paths)await check(actor.id+'_NAV_'+path,()=>pageCheck(page,path));
  if(actor.id==='VISITOR'){
   await check('VISITOR_ADMIN_ROUTE_DENIED',async()=>{await page.goto(origin+'/#/admin/users');await expect(page).toHaveURL(origin+'/#/');await expect(page.locator('.sidebar nav a[href="#/admin/users"]')).toHaveCount(0)});
   await check('VISITOR_PREPLAN_UI',async()=>{await page.goto(origin+'/#/');await expect(page.getByRole('button',{name:'儲存預排行程',exact:true})).toBeEnabled();await page.locator('textarea[placeholder*="拜訪補充說明"]').fill('BROWSER-ISOLATED-PREPLAN');const [r]=await Promise.all([page.waitForResponse(r=>r.url()===api+'/api/v1/trips'&&r.request().method()==='POST'),page.getByRole('button',{name:'儲存預排行程',exact:true}).click()]);expect(r.ok()).toBe(true);const t=await r.json();expect(t.status).toBe('Draft');await expect(page.getByText(/預排行程已儲存/)).toBeVisible();const token=await page.evaluate(()=>sessionStorage.getItem('fieldvisit_uat_token'));const read=await page.request.get(api+'/api/v1/trips/'+t.visitTripId,{headers:{Authorization:'Bearer '+token}});expect(read.ok()).toBe(true);expect((await read.json()).notes).toBe('BROWSER-ISOLATED-PREPLAN')});
  }
  if(actor.id==='LEADER')await check('LEADER_ADMIN_ROUTE_DENIED',async()=>{await page.goto(origin+'/#/admin/users');await expect(page).toHaveURL(origin+'/#/leader')});
  await check(actor.id+'_LOGOUT',async()=>{await page.locator('.sidebar .logout-btn').click();await expect(page.getByRole('button',{name:'登入',exact:true})).toBeVisible();expect(await page.evaluate(()=>sessionStorage.getItem('fieldvisit_uat_token'))).toBeNull()});
  const mobile=await (await context(true)).newPage();
  await check(actor.id+'_MOBILE_LOGIN_NAV',async()=>{await signIn(mobile,actor.account,actor.role);await expect(mobile.locator('.mobile-tabs')).toBeVisible();await pageCheck(mobile,actor.paths[1],true)});
 }
}finally{await browser.close();console.log('BROWSER_CASES='+JSON.stringify(results));if(results.some(r=>!r.pass))process.exitCode=1;}
