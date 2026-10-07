import{NavLink}from"react-router-dom";

export default function ProjectAdminTabs(){
 return <div className="actions" style={{marginBottom:14}}>
  <NavLink end to="/admin/projects" className={({isActive})=>`btn small ${isActive?"":"outline"}`}>專案清單</NavLink>
  <NavLink to="/admin/projects/bulk" className={({isActive})=>`btn small ${isActive?"":"outline"}`}>Excel 批次維護</NavLink>
 </div>;
}
