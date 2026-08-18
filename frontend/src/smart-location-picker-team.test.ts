import{describe,expect,it}from"vitest";
import{buildLocationSearchPath}from"./smart-location-picker";

describe("trip team location filtering",()=>{
  it("adds selected team to smart location search",()=>{
    const path=buildLocationSearchPath({
      query:"南投",
      teamId:9,
      page:1,
      pageSize:20
    });

    expect(path).toContain("teamId=9");
    expect(path).toContain("q=%E5%8D%97%E6%8A%95");
  });
});
