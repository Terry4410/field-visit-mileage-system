import {describe,expect,it} from "vitest";
import {filteredExportEndpoint} from "./export-filtered-ui";

describe("filtered export target",()=>{
 it("routes the personnel history UI to the complete workbook endpoint and retains filters",()=>{
  expect(filteredExportEndpoint("personnel-full","employmentStatus=Active&teamId=3"))
   .toBe("/admin/query-exports/personnel-full.xlsx?employmentStatus=Active&teamId=3");
 });
});
