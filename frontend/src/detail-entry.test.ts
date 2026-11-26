import {describe,expect,it} from "vitest";
import {createElement} from "react";
import {renderToStaticMarkup} from "react-dom/server";
import DetailLink from "./components/DetailLink";
import ReportRowDetail,{reportDetailColumn,exactReportPerson} from "./components/ReportRowDetail";
import TripReviewDetail from "./components/TripReviewDetail";
import type {Trip,V170PeopleRow} from "./types";
const person:V170PeopleRow={userId:1,userCode:"E01",employeeNo:"E01",userType:"Internal",displayName:"同名人員",adminEnabled:true,actualAccess:true,roles:["visitor"],teamAssignments:[]};
describe("consistent read-only detail entry",()=>{
 it("uses a real accessible button",()=>{const html=renderToStaticMarkup(createElement(DetailLink,{onClick:()=>{},ariaLabel:"查看明細",children:"E01"}));expect(html).toContain('type="button"');expect(html).toContain("underline");expect(html).toContain("E01")});
 it.each([["personnel","工號"],["personnel-full","姓名"],["roles","工號"],["members","姓名"],["teams","小組代碼"],["locations","地點代碼"],["centers","Center Code"],["deployment-sites","Site Code"],["locations-official","Location Code"],["projects","專案代碼"],["visit-types","代碼"]])("maps report %s to its actual identifier column",(k,h)=>{expect(reportDetailColumn(k,h)).toBe(true);expect(reportDetailColumn(k,"Email")).toBe(false)});
 it("renders exact report row even without identity",()=>{const html=renderToStaticMarkup(createElement(ReportRowDetail,{kind:"personnel-full",sheetName:"人員完整總覽",headers:["姓名","備註"],row:["同名人員","測試內容"],onClose:()=>{}}));expect(html).toContain("測試內容");expect(html).not.toContain("查看目前人員明細")});
 it("does not guess by name, incomplete pages or duplicate identifier",()=>{expect(exactReportPerson([person],1,"E01")).toBe(person);expect(exactReportPerson([person],2,"E01")).toBeNull();expect(exactReportPerson([person],1,"同名人員")).toBeNull();expect(exactReportPerson([person,{...person,userId:2}],2,"E01")).toBeNull()});
 it("renders approval trip route and notes without approval actions",()=>{const trip:Trip={visitTripId:1,tripNo:"T-TEST",userId:1,visitorName:"測試員工",visitDate:"2026-11-26",status:"PendingApproval",statusName:"待核准",hasTimeOverlapWarning:false,timeOverlapConfirmed:false,notes:"行程備註測試",stops:[{sourceType:"Master",locationName:"測試地點",address:"測試地址",visitPurpose:"拜訪目的測試",notes:"站點備註測試"}],rowVersion:"AA=="};const html=renderToStaticMarkup(createElement(TripReviewDetail,{trip,onClose:()=>{}}));for(const v of ["T-TEST","測試地址","拜訪目的測試","站點備註測試","行程備註測試"])expect(html).toContain(v);expect(html).not.toContain(">核准</button>")});
});
