import type {CorrectionRequest} from "../types";
import {correctionChangeText} from "../correction-ui";
import {km,money,timeRange} from "../v160";
import ReadOnlyDetail from "./ReadOnlyDetail";

const statusLabels:Record<string,string>={PendingLeaderReview:"待小組長審核",PendingAdminClose:"待管理者結案",Closed:"已結案",Rejected:"已拒絕"};
const present=(value?:string|null)=>value?.trim()?value:"—";
const wrap=(value?:string|null)=><span style={{whiteSpace:"pre-wrap",overflowWrap:"anywhere"}}>{present(value)}</span>;

export default function CorrectionDetail({row,onClose}:{row:CorrectionRequest;onClose:()=>void}){
 const p=row.proposal;
 return <ReadOnlyDetail title={"更正明細｜"+row.tripNo} onClose={onClose} fields={[
  ["更正編號",row.correctionRequestId],["外訪員",row.visitorName],["小組",present(row.teamName)],
  ["狀態",statusLabels[row.status]||row.status],["申請時間",row.requestedAt],["申請人",present(row.requestedBy)],
  ["更正原因",wrap(row.reason)],["原核准版本","V"+row.baseSnapshotVersion],
  ["更正結果版本",row.resultSnapshotVersion!=null?"V"+row.resultSnapshotVersion:"—"],
  ["小組長審核",present(row.leaderReviewedBy)],["審核時間",present(row.leaderReviewedAt)],["審核說明",wrap(row.leaderComments)],
  ["管理者結案",present(row.adminClosedBy)],["結案時間",present(row.adminClosedAt)],["結案說明",wrap(row.adminComments)]
 ]}>
  <h4>更正前後差異</h4>
  <div className="note">依申請保存的差異呈現原核准值及擬更正值；尚未結案的內容不是已核准結果。</div>
  {row.changes.length?<div className="correction-change-list">{row.changes.map((c,i)=><div key={i} style={{whiteSpace:"pre-wrap",overflowWrap:"anywhere"}}>{correctionChangeText(c)}</div>)}</div>:<div className="empty compact-empty">無差異明細。</div>}
  <h4>申請更正後的行程內容</h4>
  <div className="table-wrap"><table><tbody>
   <tr><th>拜訪日期</th><td>{p.visitDate}</td></tr>
   <tr><th>時間</th><td>{timeRange(p.startTime,p.endTime)}</td></tr>
   <tr><th>行程備註</th><td>{wrap(p.notes)}</td></tr>
   <tr><th>外訪員自算里程</th><td>{km(p.claimedDistanceKm)}</td></tr>
   <tr><th>申請核定里程</th><td>{km(p.approvedDistanceKm)}</td></tr>
   <tr><th>費率</th><td>{money(p.ratePerKm)}</td></tr>
   <tr><th>申請補助金額</th><td>{money(p.subsidyAmount)}</td></tr>
  </tbody></table></div>
  <h4>拜訪地點</h4>
  {p.stops.map((s,i)=><div className="correction-stop" key={i}>
   <strong>第 {s.stopSequence} 站｜{s.locationName}</strong>
   <div>地址：{wrap(s.address)}</div><div>專案：{present(s.projectName||s.projectCode)}</div>
   <div>拜訪形式：{present(s.visitTypeName||s.visitTypeCode)}</div><div>拜訪目的：{wrap(s.visitPurpose)}</div>
   <div>備註：{wrap(s.notes)}</div>
  </div>)}
 </ReadOnlyDetail>;
}
