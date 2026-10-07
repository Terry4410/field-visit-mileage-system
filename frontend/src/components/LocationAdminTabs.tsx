import{NavLink}from"react-router-dom";

export default function LocationAdminTabs(){
 return <div className="actions" style={{marginBottom:14}}>
  <NavLink end to="/admin/locations" className={({isActive})=>`btn small ${isActive?"":"outline"}`}>地點主檔</NavLink>
  <NavLink to="/admin/locations/duplicates" className={({isActive})=>`btn small ${isActive?"":"outline"}`}>疑似重複覆核</NavLink>
  <NavLink to="/admin/locations/official" className={({isActive})=>`btn small ${isActive?"":"outline"}`}>官方據點進階維護</NavLink>
 </div>;
}
