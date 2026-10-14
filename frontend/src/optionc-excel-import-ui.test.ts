import{describe,it,expect}from"vitest";
import{maySubmitCanonicalExcelCreates,maySubmitCanonicalExcelUpdates}from"./optionc-excel-import-ui";
import type{ImportPreview}from"./types";
const valid:ImportPreview={importBatchId:"b",importType:"locations",totalCount:1,
 validCount:0,errorCount:1,items:[{rowNumber:2,entityType:"Location",
 action:"Create",status:"Error",displayKey:"New",
 errorMessage:"LOCATION_EXCEL_CREATE_REQUIRES_CANONICAL_REVIEW"}]};
describe("Scheme A Excel gated canonical submission",()=>{
 it("permits only explicit canonical review-required Create preview rows",()=>{
  expect(maySubmitCanonicalExcelCreates(valid)).toBe(true);
  expect(maySubmitCanonicalExcelCreates({...valid,items:[
   ...valid.items,{rowNumber:3,entityType:"Location",action:"NoChange",
    status:"Valid",displayKey:"old"}],totalCount:2,validCount:1})).toBe(true);
 });
 it("rejects legacy updates, invalid rows, different import types and truncated previews",()=>{
  expect(maySubmitCanonicalExcelCreates({...valid,importType:"projects"})).toBe(false);
  expect(maySubmitCanonicalExcelCreates({...valid,totalCount:2})).toBe(false);
  expect(maySubmitCanonicalExcelCreates({...valid,items:[
   {...valid.items[0],action:"Update",status:"Valid",errorMessage:undefined}]})).toBe(false);
  expect(maySubmitCanonicalExcelCreates({...valid,items:[
   {...valid.items[0],errorMessage:"MISSING_ADDRESS"}]})).toBe(false);
  expect(maySubmitCanonicalExcelCreates({...valid,items:[],totalCount:0})).toBe(false);
 });
});

describe("Scheme A Excel published Update candidate",()=>{
 const update:ImportPreview={...valid,items:[{...valid.items[0],
  action:"Update",displayKey:"EX-10",
  errorMessage:"LOCATION_EXCEL_PUBLISHED_CHANGE_REQUIRES_UNIFIED_APPROVAL"}]};
 it("permits only review-required Update and unchanged rows",()=>{
  expect(maySubmitCanonicalExcelUpdates(update)).toBe(true);
  expect(maySubmitCanonicalExcelUpdates({...update,totalCount:2,validCount:1,
   items:[...update.items,{...update.items[0],action:"NoChange",
    status:"Valid",errorMessage:undefined}]})).toBe(true);
 });
 it("denies mixed Create, wrong errors, truncated previews and project imports",()=>{
  expect(maySubmitCanonicalExcelUpdates({...update,totalCount:2})).toBe(false);
  expect(maySubmitCanonicalExcelUpdates({...update,importType:"projects"})).toBe(false);
  expect(maySubmitCanonicalExcelUpdates({...update,items:[valid.items[0]]})).toBe(false);
  expect(maySubmitCanonicalExcelUpdates({...update,items:[{...update.items[0],
   errorMessage:"OTHER_ERROR"}]})).toBe(false);
  expect(maySubmitCanonicalExcelCreates(update)).toBe(false);
 });
});
