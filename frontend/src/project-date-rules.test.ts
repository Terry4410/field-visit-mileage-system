import {describe,expect,it} from "vitest";
import {isProjectAvailableOn} from "./project-date-rules";
import type {Project} from "./types";

const project:Project={
  projectId:1,
  projectCode:"UAT-PROJECT-DATE",
  projectName:"專案期間測試",
  locationMode:"List",
  startDate:"2026-08-10",
  endDate:"2026-08-31",
  isActive:true
};

describe("isProjectAvailableOn",()=>{
  it("excludes the day before start",()=>{
    expect(isProjectAvailableOn(project,"2026-08-09")).toBe(false);
  });

  it("includes the start date",()=>{
    expect(isProjectAvailableOn(project,"2026-08-10")).toBe(true);
  });

  it("includes the end date",()=>{
    expect(isProjectAvailableOn(project,"2026-08-31")).toBe(true);
  });

  it("excludes the day after end",()=>{
    expect(isProjectAvailableOn(project,"2026-09-01")).toBe(false);
  });

  it("excludes inactive projects",()=>{
    expect(isProjectAvailableOn({...project,isActive:false},"2026-08-20")).toBe(false);
  });

  it("supports open-ended dates",()=>{
    expect(
      isProjectAvailableOn(
        {...project,startDate:undefined,endDate:undefined},
        "2026-08-20"
      )
    ).toBe(true);
  });
});
