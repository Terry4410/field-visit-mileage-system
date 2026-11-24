import { chromium, expect } from '@playwright/test';
const origin='http://127.0.0.1:4173', api='http://127.0.0.1:5080';
const password=process.env.BROWSER_DEMO_PASSWORD;
if(!password || password.length<20) throw Error('ISOLATED_PASSWORD_REQUIRED');
const browser=await chromium.launch({headless:true});
const results=[];
// Preserve the 23 prior observed PASS cases; recheck only the eight unresolved cases.
const delta = new Set(['VISITOR_MOBILE_LOGIN_NAV','LEADER_NAV_/leader','LEADER_NAV_/leader/query','LEADER_MOBILE_LOGIN_NAV','ADMIN_NAV_/admin','ADMIN_NAV_/admin/corrections','ADMIN_NAV_/admin/users','ADMIN_MOBILE_LOGIN_NAV']);
async function check(name,fn){try{await fn();results.push({name,pass:true});console.log(name+'=PASS')}catch(e){results.push({name,pass:false});console.log(name+'=FAIL '+String(e.message).replace(/\x1b\[[0-9;]*m/g,'').split('\n').slice(0,8).join(' | ').slice(0,900))}}
const contexts=[];
async function context(mobile=false){const c=await browser.newContext(mobile?{viewport:{width:390,height:844},isMobile:true,hasTouch:true}:{viewport:{width:1440,height:900}});contexts.push(c);
 await c.route('**/*',r=>{const u=new URL(r.request().url());return (u.origin===origin||u.origin===api)?r.continue():r.abort()});return c;}
async function signIn(page,account,role){await page.goto(origin);await page.locator('input[autocomplete=username]').fill(account);await page.locator('input[type=password]').fill(password);
 const [r]=await Promise.all([page.waitForResponse(r=>r.url()===api+'/api/v1/auth/demo-login'),page.getByRole('button',{name:'登入',exact:true}).click()]);expect(r.status()).toBe(200);await expect(page.locator('.role-box')).toContainText(role);await expect(page.locator('.topbar h1')).toBeVisible();}
async function pageCheck(page,path,mobile=false){
 const issues=[],pending=new Set();let lastActivity=Date.now();
 const relevant=r=>r.url().startsWith(api+'/api/');
 const onRequest=r=>{if(relevant(r)){pending.add(r);lastActivity=Date.now()}};
 const onFinished=r=>{if(relevant(r)){pending.delete(r);lastActivity=Date.now()}};
 const onFailed=r=>{if(relevant(r)){issues.push('REQUEST_FAILED:'+new URL(r.url()).pathname);onFinished(r)}};
 const onResponse=r=>{if(relevant(r)&&r.status()>=400)issues.push(r.status()+':'+new URL(r.url()).pathname)};
 const onError=()=>issues.push('PAGE_EXCEPTION');
 page.on('request',onRequest);page.on('requestfinished',onFinished);page.on('requestfailed',onFailed);page.on('response',onResponse);page.on('pageerror',onError);
 try{
  const nav=page.locator(mobile?'.mobile-tabs':'.sidebar nav');
  await nav.locator('a[href="#'+path+'"]').click();
  await expect(page).toHaveURL(origin+'/#'+path);
  await expect(page.locator('.content')).toBeVisible();
  // Hash navigation does not reset document networkidle. Observe actual SPA requests.
  const deadline=Date.now()+15000;
  while((pending.size>0||Date.now()-lastActivity<1000)&&Date.now()<deadline)await new Promise(r=>setTimeout(r,100));
  if(pending.size)throw Error('PENDING_API:'+Array.from(pending,r=>new URL(r.url()).pathname).join(','));
  if(issues.length)throw Error('PAGE_API_ERRORS:'+issues.join(','));
  await expect(page.locator('.content')).not.toContainText(/系統發生未預期錯誤|無法連線到系統 API/);
  expect((await page.locator('.content').innerText()).trim().length).toBeGreaterThan(0);
 }finally{
  page.off('request',onRequest);page.off('requestfinished',onFinished);page.off('requestfailed',onFailed);page.off('response',onResponse);page.off('pageerror',onError);
 }
}

try{
 for(const actor of [{id:'VISITOR',account:'E2E-V',role:'外訪員',paths:['/','/history']},{id:'LEADER',account:'E2E-L',role:'小組長',paths:['/leader','/leader/review','/leader/query']},{id:'ADMIN',account:'E2E-A',role:'管理者',paths:['/admin','/admin/query','/admin/corrections','/admin/users']}]){
  for(const path of actor.paths){
   const label=actor.id+'_NAV_'+path;if(!delta.has(label))continue;
   await check(label,async()=>{const c=await context();try{const page=await c.newPage();await signIn(page,actor.account,actor.role);await page.waitForLoadState('networkidle');await pageCheck(page,path)}finally{await c.close()}});
  }
  await check(actor.id+'_MOBILE_LOGIN_NAV',async()=>{const c=await context(true);try{const page=await c.newPage();await signIn(page,actor.account,actor.role);await expect(page.locator('.mobile-tabs')).toBeVisible();await page.waitForLoadState('networkidle');await pageCheck(page,actor.paths[1],true)}finally{await c.close()}});
 }
}finally{await browser.close();console.log('BROWSER_CASES='+JSON.stringify(results));if(results.length!==delta.size||results.some(r=>!r.pass))process.exitCode=1;}
