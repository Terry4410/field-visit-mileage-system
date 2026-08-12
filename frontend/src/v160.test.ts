import{describe,expect,it}from"vitest";import{monthStart,qs}from"./v160";
describe('v1.6 helpers',()=>{it('builds month start',()=>expect(monthStart('2026-08-12')).toBe('2026-08-01'));it('omits empty query values',()=>expect(qs({a:1,b:'',c:null,d:'x'})).toBe('a=1&d=x'))});
