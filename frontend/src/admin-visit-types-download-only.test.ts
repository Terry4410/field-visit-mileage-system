import {describe,expect,it} from "vitest";
import {createElement} from "react";
import {renderToStaticMarkup} from "react-dom/server";
import AdminPage from "./pages/AdminPage";

describe("admin visit types",()=>{
 it("offers only the Excel download while trip selection remains separate",()=>{
  const html=renderToStaticMarkup(createElement(AdminPage,{section:"visit-types"}));
  expect(html).toContain("下載拜訪型式 Excel");
  expect(html).toContain("行程仍可選用有效的拜訪型式");
  expect(html).not.toMatch(/新增拜訪型式|儲存拜訪型式|取消修改|停用|刪除|上移|下移/);
  expect((html.match(/<button/g)||[])).toHaveLength(1);
  expect(html).not.toMatch(/<input|<textarea|<select/);
 });
});
