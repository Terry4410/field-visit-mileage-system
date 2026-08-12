import{Navigate,NavLink,Route,Routes,useLocation,useNavigate}from"react-router-dom";
import{useEffect,useMemo,useState}from"react";
import{useAuth}from"./auth";
import LoginPage from"./pages/LoginPage";
import VisitorPage from"./pages/VisitorPage";
import HistoryPage from"./pages/HistoryPage";
import LeaderPage from"./pages/LeaderPage";
import AdminPage from"./pages/AdminPage";
import TeamManagementPage from"./pages/TeamManagementPage";
import SupervisorPage from"./pages/SupervisorPage";
import UnifiedQueryPage from"./pages/UnifiedQueryPage";

type NavItem={path:string;label:string;short:string};
const navByRole:Record<string,NavItem[]>={
 visitor:[{path:'/',label:'今日行程',short:'首頁'},{path:'/history',label:'歷史紀錄',short:'紀錄'}],
 leader:[{path:'/leader',label:'小組總覽',short:'總覽'},{path:'/leader/review',label:'行程審核',short:'審核'},{path:'/leader/query',label:'行程查詢',short:'查詢'},{path:'/leader/locations',label:'地點管理',short:'地點'}],
 admin:[{path:'/admin',label:'管理儀表板',short:'總覽'},{path:'/admin/users',label:'人員與權限',short:'人員'},{path:'/admin/teams',label:'小組與成員',short:'小組'},{path:'/admin/locations',label:'地點主檔',short:'地點'},{path:'/admin/projects',label:'專案與拜訪形式',short:'專案'},{path:'/admin/rates',label:'補助費率',short:'費率'},{path:'/admin/query',label:'行程查詢',short:'查詢'},{path:'/admin/corrections',label:'更正管理',short:'更正'}],
 supervisor:[{path:'/supervisor',label:'查詢總覽',short:'總覽'},{path:'/supervisor/query',label:'行程查詢',short:'查詢'}]
};
const roleLabel:Record<string,string>={visitor:'外訪員',leader:'小組長',admin:'管理者',supervisor:'督導'};
const priority=['admin','leader','supervisor','visitor'];

export default function App(){
 const{user,loading,logout}=useAuth();const loc=useLocation();const navigate=useNavigate();
 const roles=useMemo(()=>user?.roles.map(r=>r.toLowerCase()).filter(r=>navByRole[r])||[],[user]);
 const[activeRole,setActiveRole]=useState(()=>sessionStorage.getItem('fieldvisit_active_role')||'');
 useEffect(()=>{if(!roles.length)return;const next=roles.includes(activeRole)?activeRole:priority.find(r=>roles.includes(r))||roles[0];if(next!==activeRole){setActiveRole(next);sessionStorage.setItem('fieldvisit_active_role',next)}},[roles.join('|'),activeRole]);
 if(loading)return <div className="center">載入中…</div>;if(!user)return <LoginPage/>;
 const role=roles.includes(activeRole)?activeRole:(priority.find(r=>roles.includes(r))||roles[0]||'visitor');const nav=navByRole[role];const home=nav[0].path;const title=nav.find(x=>x.path===loc.pathname)?.label||nav.find(x=>x.path!=='/'&&loc.pathname.startsWith(x.path))?.label||nav[0].label;
 const switchRole=(r:string)=>{setActiveRole(r);sessionStorage.setItem('fieldvisit_active_role',r);navigate(navByRole[r][0].path)};
 const teamText=role==='leader'&&user.teamScopes?.length?user.teamScopes.map(x=>x.teamName).join('、'):user.teamName||'全部';
 return <div className="app-shell">
  <aside className="sidebar"><div className="brand">外訪行程管理<small>Field Visit Mileage System</small></div><div className="role-box"><label>目前登入</label><strong>{user.displayName}</strong><span>{user.employeeNo}｜{roleLabel[role]}｜{teamText}</span>{roles.length>1&&<select className="role-switch" value={role} onChange={e=>switchRole(e.target.value)}>{roles.map(r=><option key={r} value={r}>切換為：{roleLabel[r]||r}</option>)}</select>}</div><nav className="nav">{nav.map(x=><NavLink key={x.path} end={x.path===home} to={x.path}>● {x.label}</NavLink>)}</nav><button className="btn secondary logout-btn" onClick={logout}>登出</button><div className="sidebar-footer">UAT v1.6.1 Fix<br/>DB Schema 1.6.0</div></aside>
  <main className="main"><header className="topbar"><div><h1>{title}</h1><div className="top-subtitle">手機優先｜Azure SQL UAT｜v1.6.1 UAT Fix｜Route Provider：Mock</div></div><div className="user-chip"><strong>{user.displayName}</strong><span>{roleLabel[role]}｜{teamText}</span>{roles.length>1&&<select value={role} onChange={e=>switchRole(e.target.value)}>{roles.map(r=><option key={r} value={r}>{roleLabel[r]||r}</option>)}</select>}<button className="btn small secondary" onClick={logout}>登出</button></div></header><section className="content"><Routes>
   <Route path="/" element={role==='visitor'?<VisitorPage/>:<Navigate to={home}/>}/><Route path="/history" element={role==='visitor'?<HistoryPage/>:<Navigate to={home}/>}/>
   <Route path="/leader" element={role==='leader'?<LeaderPage section="dashboard"/>:<Navigate to={home}/>}/><Route path="/leader/review" element={role==='leader'?<LeaderPage section="review"/>:<Navigate to={home}/>}/><Route path="/leader/query" element={role==='leader'?<UnifiedQueryPage/>:<Navigate to={home}/>}/><Route path="/leader/locations" element={role==='leader'?<LeaderPage section="locations"/>:<Navigate to={home}/>}/>
   <Route path="/admin" element={role==='admin'?<AdminPage section="dashboard"/>:<Navigate to={home}/>}/><Route path="/admin/users" element={role==='admin'?<AdminPage section="users"/>:<Navigate to={home}/>}/><Route path="/admin/teams" element={role==='admin'?<TeamManagementPage/>:<Navigate to={home}/>}/><Route path="/admin/locations" element={role==='admin'?<AdminPage section="locations"/>:<Navigate to={home}/>}/><Route path="/admin/projects" element={role==='admin'?<AdminPage section="projects"/>:<Navigate to={home}/>}/><Route path="/admin/rates" element={role==='admin'?<AdminPage section="rates"/>:<Navigate to={home}/>}/><Route path="/admin/query" element={role==='admin'?<UnifiedQueryPage/>:<Navigate to={home}/>}/><Route path="/admin/corrections" element={role==='admin'?<AdminPage section="corrections"/>:<Navigate to={home}/>}/>
   <Route path="/supervisor" element={role==='supervisor'?<SupervisorPage/>:<Navigate to={home}/>}/><Route path="/supervisor/query" element={role==='supervisor'?<UnifiedQueryPage/>:<Navigate to={home}/>}/><Route path="*" element={<Navigate to={home}/>}/>
  </Routes></section></main>
  <nav className="mobile-tabs">{nav.map(x=><NavLink key={x.path} end={x.path===home} to={x.path}>{x.short}</NavLink>)}</nav>
 </div>
}
