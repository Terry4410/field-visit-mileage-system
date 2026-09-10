import{describe,expect,it}from'vitest';
import{readFileSync}from'node:fs';

const source=(relative:string)=>readFileSync(new URL(relative,import.meta.url),'utf8');

describe('D-A1b managed location governance compatibility',()=>{
 it('soft-deactivation sends URL-encoded RowVersion from the managed row',()=>{
  const page=source('./pages/AdminPage.tsx');
  expect(page).toContain("`/managed-locations/${l.locationId}?rowVersion=${encodeURIComponent(l.rowVersion)}`");
  expect(page).toContain("`/managed-locations/${l.locationId}/permanent`");
 });

 it('does not reintroduce the abandoned shared api cache workaround',()=>{
  const api=source('./api.ts');
  expect(api).not.toContain('managedLocationVersions');
  expect(api).not.toContain('rememberManagedLocationVersions');
 });

 it('does not add D-A Integration governance UI yet',()=>{
  const page=source('./pages/AdminPage.tsx');
  expect(page).not.toContain('TaxId');
  expect(page).not.toContain('MasterNote');
  expect(page).not.toContain('DuplicateOfLocationId');
  expect(page).not.toContain('Governance');
 });
});
