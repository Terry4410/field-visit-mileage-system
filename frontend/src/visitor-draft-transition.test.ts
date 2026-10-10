import{describe,expect,it}from"vitest";
import{shouldHydrateTripOnEdit}from"./visitor-draft-transition";

describe("visitor draft route preview transition",()=>{
  it("does not trigger a competing edit GET for the draft just created by route calculation",()=>{
    expect(shouldHydrateTripOnEdit("501","501")).toBe(false);
  });
  it("still hydrates an existing draft opened from history",()=>{
    expect(shouldHydrateTripOnEdit("501",null)).toBe(true);
  });
  it("hydrates a different draft after navigation instead of suppressing all GETs",()=>{
    expect(shouldHydrateTripOnEdit("502","501")).toBe(true);
  });
  it("does not fetch a trip with no edit id",()=>{
    expect(shouldHydrateTripOnEdit(null,"501")).toBe(false);
  });
});
