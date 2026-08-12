import{useEffect,useState}from"react";
import{api}from"../api";
import type{DashboardSummary}from"../types";
export default function SupervisorPage(){const[d,setD]=useState<DashboardSummary|null>(null),[msg,setMsg]=useState('');useEffect(()=>{api<DashboardSummary>('/dashboard').then(setD).catch(e=>setMsg(e.message))},[]);return <><div className="grid cols-5 dashboard-cards">{[['本月行程',d?.thisMonthTrips],['待核准',d?.pendingApproval],['已核准',d?.approved],['待確認地點',d?.pendingLocations],['待處理更正',d?.pendingCorrections]].map(([n,v])=><div className="card stat" key={String(n)}><div className="label">{n}</div><div className="value">{v??'—'}</div></div>)}</div><div className="note" style={{marginTop:18}}>督導為唯讀角色；請使用「行程查詢」統一查詢並下載 Excel / PDF。</div>{msg&&<div className="note">{msg}</div>}</>}
