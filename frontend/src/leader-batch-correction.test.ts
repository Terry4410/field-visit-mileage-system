import {describe,it,expect,vi} from "vitest";
import {reviewSelectedCorrections} from "./leader-batch-correction";
const rows=[1,2,3].map(n=>({correctionRequestId:n,tripNo:"T"+n,status:"PendingLeaderReview",rowVersion:"v"+n,changes:[{}]}));
describe("batch corrections preserve individual review authority",()=>{
 it("deduplicates selection and forwards approval, shared comment and individual versions",async()=>{
  const send=vi.fn(async()=>{});
  expect(await reviewSelectedCorrections(rows,[1,1,3],true," 說明 ",send)).toEqual({success:2,unconfirmed:[],notRun:0});
  expect(send).toHaveBeenCalledWith(rows[0],true,"說明");expect(send).toHaveBeenCalledWith(rows[2],true,"說明");
 });
 it("does not submit stale rows or approval without differences or rejection without reason",async()=>{
  const send=vi.fn(async()=>{});
  await expect(reviewSelectedCorrections(rows,[9],true,"",send)).rejects.toThrow();
  await expect(reviewSelectedCorrections([{...rows[0],changes:[]}],[1],true,"",send)).rejects.toThrow();
  await expect(reviewSelectedCorrections(rows,[1],false," ",send)).rejects.toThrow();
  expect(send).not.toHaveBeenCalled();
 });
 it("stops on unknown result without replaying remaining writes",async()=>{
  const send=vi.fn(async()=>{}).mockResolvedValueOnce(undefined).mockRejectedValueOnce(new Error("timeout"));
  expect(await reviewSelectedCorrections(rows,[1,2,3],false,"原因",send)).toEqual({success:1,unconfirmed:["T2：timeout"],notRun:1});
  expect(send).toHaveBeenCalledTimes(2);
 });
});
