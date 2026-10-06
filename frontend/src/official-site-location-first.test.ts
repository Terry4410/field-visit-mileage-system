import{describe,expect,it}from"vitest";
import{eligibleOfficialCentersForDate}from"./components/LocationMaintenanceModal";

describe("location-first official site center eligibility",()=>{
 it("shows only active centers covering the selected effective date",()=>{
  const rows=[
   {id:1,key:"C1",detail:"A",effectiveFrom:"2026-01-01",effectiveTo:null,isActive:true},
   {id:2,key:"C2",detail:"B",effectiveFrom:"2026-11-01",effectiveTo:null,isActive:true},
   {id:3,key:"C3",detail:"C",effectiveFrom:"2025-01-01",effectiveTo:"2026-09-30",isActive:true},
   {id:4,key:"C4",detail:"D",effectiveFrom:"2025-01-01",effectiveTo:null,isActive:false}
  ];
  expect(eligibleOfficialCentersForDate(rows,"2026-10-06").map(x=>x.key)).toEqual(["C1"]);
 });
});
