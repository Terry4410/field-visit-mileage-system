import {describe,expect,it} from "vitest";
import {createElement} from "react";
import {renderToStaticMarkup} from "react-dom/server";
import CorrectionDetail from "./components/CorrectionDetail";
import type {CorrectionRequest} from "./types";

const row:CorrectionRequest={correctionRequestId:12,visitTripId:34,tripNo:"T-DETAIL-TEST",visitorName:"UAT Visitor",teamName:"UAT Team",baseSnapshotVersion:5,status:"PendingAdminClose",reason:"更正目的",requestedAt:"2026-11-26T01:00:00",requestedBy:"pilotv01",leaderReviewedBy:"pilotl01",leaderComments:"確認差異",requiresAdminClose:true,rowVersion:"AA==",proposal:{visitDate:"2026-11-26",startTime:"09:00:00",endTime:"10:00:00",notes:"更正後備註",approvedDistanceKm:10.94,ratePerKm:2.5,subsidyAmount:27.35,stops:[{stopSequence:1,locationName:"測試地點",address:"測試地址",projectName:"測試專案",visitTypeName:"訪視",visitPurpose:"更正後目的",notes:"站點備註"}]},changes:[{fieldName:"Notes",oldValue:"原備註",newValue:"更正後備註"}]};
describe("read-only correction detail",()=>{
 it("shows the proposal, saved before/after values, review metadata and base version without approval controls",()=>{
  const html=renderToStaticMarkup(createElement(CorrectionDetail,{row,onClose:()=>{}}));
  for(const value of ["T-DETAIL-TEST","待管理者結案","V5","原備註","更正後備註","pilotl01","確認差異","測試專案","更正後目的","站點備註","27.35"])expect(html).toContain(value);
  expect(html).not.toContain(">結案</button>");expect(html).not.toContain(">拒絕</button>");
 });
 it("distinguishes a completed correction result version",()=>{
  const html=renderToStaticMarkup(createElement(CorrectionDetail,{row:{...row,status:"Closed",resultSnapshotVersion:6,adminClosedBy:"pilota01"},onClose:()=>{}}));
  expect(html).toContain("已結案");expect(html).toContain("V6");expect(html).toContain("pilota01");
 });
});
