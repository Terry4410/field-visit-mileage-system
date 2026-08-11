import {Navigate,NavLink,Route,Routes,useLocation} from "react-router-dom";
import {useAuth} from "./auth";
import LoginPage from "./pages/LoginPage";
import VisitorPage from "./pages/VisitorPage";
import HistoryPage from "./pages/HistoryPage";
import LeaderPage from "./pages/LeaderPage";
import AdminPage from "./pages/AdminPage";
import SupervisorPage from "./pages/SupervisorPage";

type NavItem={path:string;label:string;short:string};
const navByRole:Record<string,NavItem[]>={
  visitor:[{path:"/",label:"今日行程",short:"首頁"},{path:"/history",label:"歷史紀錄",short:"紀錄"}],
  leader:[{path:"/leader",label:"小組總覽",short:"總覽"},{path:"/leader/review",label:"行程審核",short:"審核"},{path:"/leader/locations",label:"地點管理",short:"地點"}],
  admin:[{path:"/admin",label:"管理儀表板",short:"總覽"},{path:"/admin/users",label:"人員與權限",short:"人員"},{path:"/admin/projects",label:"專案與拜訪形式",short:"專案"},{path:"/admin/rates",label:"補助費率",short:"費率"},{path:"/admin/reports",label:"報表中心",short:"報表"}],
  supervisor:[{path:"/supervisor",label:"查詢總覽",short:"總覽"},{path:"/supervisor/locations",label:"地點查詢",short:"地點"},{path:"/supervisor/visitors",label:"外訪員查詢",short:"人員"},{path:"/supervisor/reports",label:"整體報表查詢",short:"報表"}]
};

export default function App(){
  const {user,loading,logout}=useAuth();
  const location=useLocation();
  if(loading)return <div className="center">載入中…</div>;
  if(!user)return <LoginPage/>;
  const role=(user.roles[0]||"visitor").toLowerCase();
  const nav=navByRole[role]||navByRole.visitor;
  const home=nav[0].path;
  const title=nav.find(x=>x.path===location.pathname)?.label||nav.find(x=>location.pathname.startsWith(x.path)&&x.path!=="/")?.label||nav[0].label;
  return <div className="app-shell">
    <aside className="sidebar">
      <div className="brand">外訪行程管理<small>Field Visit Mileage System</small></div>
      <div className="role-box"><label>目前登入</label><strong>{user.displayName}</strong><span>{user.employeeNo}｜{user.teamName||"全部"}</span></div>
      <nav className="nav">{nav.map(x=><NavLink key={x.path} end={x.path===home} to={x.path}>● {x.label}</NavLink>)}</nav>
      <button className="btn secondary logout-btn" onClick={logout}>登出</button>
      <div className="sidebar-footer">UAT｜Azure SQL Schema 1.5.0<br/>Prototype v2 介面基準</div>
    </aside>
    <main className="main">
      <header className="topbar"><div><h1>{title}</h1><div className="top-subtitle">手機優先介面｜可事後補登｜Azure SQL 多人 UAT</div></div><div className="user-chip"><strong>{user.displayName}</strong><span>{user.teamName||"全部"}</span><button className="btn small secondary" onClick={logout}>登出</button></div></header>
      <section className="content"><Routes>
        <Route path="/" element={role==="visitor"?<VisitorPage/>:<Navigate to={home}/>}/>
        <Route path="/history" element={role==="visitor"?<HistoryPage/>:<Navigate to={home}/>}/>
        <Route path="/leader" element={role==="leader"?<LeaderPage section="dashboard"/>:<Navigate to={home}/>}/>
        <Route path="/leader/review" element={role==="leader"?<LeaderPage section="review"/>:<Navigate to={home}/>}/>
        <Route path="/leader/locations" element={role==="leader"?<LeaderPage section="locations"/>:<Navigate to={home}/>}/>
        <Route path="/admin" element={role==="admin"?<AdminPage section="dashboard"/>:<Navigate to={home}/>}/>
        <Route path="/admin/users" element={role==="admin"?<AdminPage section="users"/>:<Navigate to={home}/>}/>
        <Route path="/admin/projects" element={role==="admin"?<AdminPage section="projects"/>:<Navigate to={home}/>}/>
        <Route path="/admin/rates" element={role==="admin"?<AdminPage section="rates"/>:<Navigate to={home}/>}/>
        <Route path="/admin/reports" element={role==="admin"?<AdminPage section="reports"/>:<Navigate to={home}/>}/>
        <Route path="/supervisor" element={role==="supervisor"?<SupervisorPage section="dashboard"/>:<Navigate to={home}/>}/>
        <Route path="/supervisor/locations" element={role==="supervisor"?<SupervisorPage section="locations"/>:<Navigate to={home}/>}/>
        <Route path="/supervisor/visitors" element={role==="supervisor"?<SupervisorPage section="visitors"/>:<Navigate to={home}/>}/>
        <Route path="/supervisor/reports" element={role==="supervisor"?<SupervisorPage section="reports"/>:<Navigate to={home}/>}/>
        <Route path="*" element={<Navigate to={home}/>}/>
      </Routes></section>
    </main>
    <nav className="mobile-tabs">{nav.map(x=><NavLink key={x.path} end={x.path===home} to={x.path}>{x.short}</NavLink>)}</nav>
  </div>;
}
