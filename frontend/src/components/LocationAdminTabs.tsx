import {OPTIONC_APPROVAL_UI_ENABLED} from "../optionc-approval-flags";
import {NavLink} from "react-router-dom";
export function locationAdminGroups(approvalEnabled:boolean){
 return [
  {label:"地點資料維護",items:[
   {path:"/admin/locations",label:"地點主檔"},
   {path:"/admin/locations/new",label:"新增地點"},
   {path:"/admin/locations/bulk",label:"Excel 批次維護"}
  ]},
  {label:"審核與資料品質",items:[
   {path:"/admin/locations/duplicates",label:"重複地點處理"},
   ...(approvalEnabled?[{path:"/admin/locations/approvals",label:"地點申請複審"}]:[])
  ]},
  {label:"官方資料維護",items:[
   {path:"/admin/locations/centers",label:"就業中心"},
   {path:"/admin/locations/official",label:"鄉鎮台"}
  ]}
 ];
}


export default function LocationAdminTabs(){
 return <nav className="location-admin-tabs" aria-label="地點管理子功能">
  {locationAdminGroups(OPTIONC_APPROVAL_UI_ENABLED).map(group=><div className="location-admin-group" key={group.label}>
   <div className="location-admin-group-label">{group.label}</div>
   <div className="actions">{group.items.map(item=><NavLink key={item.path} end to={item.path} className={({isActive})=>`btn small ${isActive?"":"outline"}`}>{item.label}</NavLink>)}</div>
  </div>)}
 </nav>;
}
