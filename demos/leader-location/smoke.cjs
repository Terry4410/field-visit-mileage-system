// UX smoke only. No API/SQL claims.
const assert = require('node:assert/strict');
const {pathToFileURL}=require('node:url');
const path=require('node:path');
const {chromium}=require(process.env.DEMO_PLAYWRIGHT_MODULE||'../../uat/node_modules/playwright');
(async()=>{
 const browser=await chromium.launch({headless:true});
 try{
 const page=await browser.newPage();let external=[];const errors=[];
 page.on('pageerror',e=>errors.push(e.message));
 page.on('request',r=>{if(/^https?:/.test(r.url()))external.push(r.url())});
 await page.goto(pathToFileURL(path.join(__dirname,'index.html')).href);
 assert.equal(await page.locator('#fields label').count(),8);
 await page.getByLabel('地點名稱',{exact:true}).fill('人工新增甲');
 await page.getByLabel('地址（或 Plus Code）',{exact:true}).fill('人工路 88 號');
 await page.getByRole('button',{name:'模擬新增並送審',exact:true}).click();
 assert.match(await page.locator('#locations').innerText(),/人工新增甲.*\n?/);
 await page.locator('#requests').getByRole('button',{name:'初審',exact:true}).click();
 assert.match(await page.locator('#message').innerText(),/不得自審/);
 await page.locator('#actor').selectOption('leader-b');
 await page.locator('#requests').getByRole('button',{name:'初審',exact:true}).click();
 await page.locator('#actor').selectOption('admin');
 await page.getByRole('button',{name:'最終核准',exact:true}).click();
 assert.match(await page.locator('#message').innerText(),/需獨立管理員/);
 await page.getByRole('button',{name:'模擬證據',exact:true}).click();
 await page.getByRole('button',{name:'最終核准',exact:true}).click();
 assert.match(await page.locator('#requests').innerText(),/Applied/);
 await page.locator('#actor').selectOption('leader-a');
 await page.getByRole('button',{name:'修改草稿',exact:true}).first().click();
 await page.getByLabel('地點名稱',{exact:true}).fill('授權組草稿修改');
 await page.getByRole('button',{name:'模擬儲存草稿修改',exact:true}).click();
 assert.match(await page.locator('#locations').innerText(),/授權組草稿修改/);
 await page.getByRole('button',{name:'模擬儲存 LowRisk 備註',exact:true}).click();
 assert.match(await page.locator('#message').innerText(),/地址未改變/);
 await page.getByRole('button',{name:'模擬建立 HighRisk 申請',exact:true}).click();
 assert.match(await page.locator('#requests').innerText(),/UpdatePublishedLocation/);
 await page.locator('#excel').selectOption('混合動作（拒絕示意）');
 await page.getByRole('button',{name:'模擬預覽',exact:true}).click();
 assert.equal(await page.locator('#submitExcel').isDisabled(),true);
 for(const action of ['Create','Update']){
 await page.locator('#excel').selectOption(action);
 await page.getByRole('button',{name:'模擬預覽',exact:true}).click();
 await page.locator('#submitExcel').click();
 assert.match(await page.locator('#excelResult').innerText(),/Submitted（Mock）/);
 }
 await page.locator('#actor').selectOption('revoked');
 await page.getByRole('button',{name:'模擬儲存 LowRisk 備註',exact:true}).click();
 assert.match(await page.locator('#message').innerText(),/角色已撤銷/);
 await page.locator('#actor').selectOption('leader-a');
 for(const [key,expected] of [['scope',/拒絕/],['legacy',/禁止/],['merge',/HOLD/],['trip',/Pending／Rejected/]]){
 await page.locator(`[data-check="${key}"]`).click();assert.match(await page.locator('#message').innerText(),expected);
 }
 await page.setViewportSize({width:390,height:844});
 assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),true);
 assert.deepEqual(errors,[]);assert.deepEqual(external,[]);
 console.log('MOCK_UX_SMOKE_PASS: 8 fields, create, managed draft, low/high risk, independent review simulation, Excel Create/Update simulation, denials, mobile, no external requests. REAL_BROWSER_API_SQL=HOLD');
 }finally{await browser.close()}
})().catch(e=>{console.error(e);process.exit(1)});
