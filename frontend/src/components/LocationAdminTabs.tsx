import {OPTIONC_APPROVAL_UI_ENABLED} from "../optionc-approval-flags";
import {NavLink} from "react-router-dom";
import {locationAdminGroups} from "../admin-navigation";

export default function LocationAdminTabs(){
 return <nav className="location-admin-tabs" aria-label="地點管理子功能">
  {locationAdminGroups(OPTIONC_APPROVAL_UI_ENABLED).map(group=><div className="location-admin-group" key={group.label}>
   <div className="location-admin-group-label">{group.label}</div>
   <div className="actions">{group.items.map(item=><NavLink key={item.path} end to={item.path} className={({isActive})=>`btn small ${isActive?"":"outline"}`}>{item.label}</NavLink>)}</div>
  </div>)}
 </nav>;
}
