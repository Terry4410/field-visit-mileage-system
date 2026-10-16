import {readFileSync} from 'node:fs';
import {expect,test} from '@playwright/test';
// No API route mocking: exact candidate React -> running API -> disposable SQL.
test('Leader eight-field draft create and update against real isolated API',async({page})=>{
 const fixture=JSON.parse(readFileSync(process.env.LEADER_RC_BROWSER_FIXTURE_PATH!,'utf8'));
 await page.addInitScript(token=>{sessionStorage.setItem('fieldvisit_uat_token',token);sessionStorage.setItem('fieldvisit_active_role','leader')},fixture.token);
 await page.goto('./#/leader/locations');
 await expect(page.getByRole('heading',{name:'小組長地點草稿維護'})).toBeVisible();
 await page.getByLabel('所屬小組',{exact:true}).selectOption(String(fixture.teamId));
 for(const [label,value] of [['地點名稱','Browser RC Created'],['縣市','Browser City'],['鄉鎮區','Browser District'],['地址（或 Plus Code）','Browser Road 18'],['Plus Code','TEST+BROWSER'],['統一編號（選填）','87654321'],['主檔備註（選填）','Browser note']])await page.getByLabel(label,{exact:true}).fill(value);
 await page.getByRole('button',{name:'新增地點（待審核）',exact:true}).click();
 const row=page.getByRole('row').filter({hasText:'Browser RC Created'});
 await expect(row).toContainText('Pending／未啟用');
 await row.getByRole('button',{name:'修改草稿',exact:true}).click();
 await expect(page.getByLabel('所屬小組',{exact:true})).toBeDisabled();
 await page.getByLabel('地點名稱',{exact:true}).fill('Browser RC Edited');
 await page.getByRole('button',{name:'儲存草稿修改（待審核）',exact:true}).click();
 await expect(page.getByRole('row').filter({hasText:'Browser RC Edited'})).toContainText('Pending／未啟用');
 await expect(page.getByRole('button',{name:'確認匯入',exact:true})).toHaveCount(0);
 await expect(page.getByRole('button',{name:'建立解析/發布背景工作',exact:true})).toHaveCount(0);
 console.log('REAL_BROWSER_API_FLOW_PASS; SQL persistence checked by separate probe; actual UAT HOLD');
});
