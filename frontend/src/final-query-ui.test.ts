import {describe,it,expect} from "vitest";
import {createElement} from "react";
import {renderToStaticMarkup} from "react-dom/server";
import {TripQueryTabs} from "./pages/UnifiedQueryPage";
import CorrectionHistory from "./components/CorrectionHistory";
describe("final query navigation",()=>{
 it("renders two consistent tab choices and selected state",()=>{
  const html=renderToStaticMarkup(createElement(TripQueryTabs,{historyOpen:true,onTrip:()=>{},onHistory:()=>{}}));
  expect(html).toContain('aria-pressed="false"');expect(html).toContain('aria-pressed="true"');
  expect(html).toContain(">行程查詢</button>");expect(html).toContain(">更正歷史</button>");
 });
 it("keeps one status selector, two exports and no duplicate back/status buttons",()=>{
  const html=renderToStaticMarkup(createElement(CorrectionHistory,{}));
  expect(html).toContain("下載 Excel");expect(html).toContain("下載 PDF");
  expect(html).toContain('value="All"');expect(html).toContain('value="PendingLeaderReview"');
  expect(html).not.toContain("返回行程查詢");expect(html).not.toContain(">待小組長審核</button>");
  expect(html).not.toContain(">待管理者結案</button>");expect(html).not.toContain(">已更正</button>");expect(html).not.toContain(">更正退回</button>");
 });
});
