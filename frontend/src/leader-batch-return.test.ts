import {describe,it,expect,vi} from "vitest";
import {returnSelectedTrips} from "./leader-batch-return";
const rows=[1,2,3].map(n=>({visitTripId:n,tripNo:"T"+n,status:"PendingApproval",rowVersion:"v"+n}));
describe("batch return uses individual pending trip decisions",()=>{
 it("sends only selected unique rows with shared trimmed reason",async()=>{
  const send=vi.fn(async()=>{});
  expect(await returnSelectedTrips(rows,[1,1,3]," 修正內容 ",send)).toEqual({success:2,unconfirmed:[],notRun:0});
  expect(send.mock.calls.length).toBe(2);
 });
 it("rejects missing reason or stale selection before writes",async()=>{
  const send=vi.fn(async()=>{});
  await expect(returnSelectedTrips(rows,[1]," ",send)).rejects.toThrow();
  await expect(returnSelectedTrips(rows,[9],"原因",send)).rejects.toThrow();
  expect(send).not.toHaveBeenCalled();
 });
 it("stops on ambiguous result and never retries",async()=>{
  const send=vi.fn(async()=>{}).mockResolvedValueOnce(undefined).mockRejectedValueOnce(new Error("timeout"));
  expect(await returnSelectedTrips(rows,[1,2,3],"原因",send)).toEqual({success:1,unconfirmed:["T2：timeout"],notRun:1});
  expect(send).toHaveBeenCalledTimes(2);
 });
});
