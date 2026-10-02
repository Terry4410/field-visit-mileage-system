import{useState}from"react";
import{useAuth}from"../auth";

export default function LoginPage(){
  const{login,loginEntra,authMode}=useAuth();
  const[account,setAccount]=useState("");
  const[password,setPassword]=useState("");
  const[error,setError]=useState("");
  const[busy,setBusy]=useState(false);

  const demoLogin=async(e:React.FormEvent)=>{
    e.preventDefault();setBusy(true);setError("");
    try{await login(account,password)}
    catch(x){setError(x instanceof Error?x.message:"登入失敗")}
    finally{setBusy(false)}
  };

  const entraLogin=async()=>{
    setBusy(true);setError("");
    try{await loginEntra()}
    catch(x){setError(x instanceof Error?x.message:"Microsoft Entra 登入失敗")}
    finally{setBusy(false)}
  };

  if(authMode==="Entra")return <div className="login-screen"><div className="login-card">
    <h1>外訪行程與里程管理</h1>
    <p>Microsoft Entra ID SSO｜v1.8.0 UAT</p>
    {error&&<div className="note danger-note">{error}</div>}
    <button type="button" className="btn full" disabled={busy} onClick={()=>void entraLogin()}>{busy?"Microsoft 登入中…":"使用 Microsoft 帳號登入"}</button>
    <div className="note">本環境使用公司 Microsoft Entra ID 進行身分驗證；登入後的角色、小組及資料範圍仍由本系統權限設定控制。</div>
  </div></div>;

  return <div className="login-screen"><form className="login-card" onSubmit={demoLogin}>
    <h1>外訪行程與里程管理</h1>
    <p>Azure SQL 多人 UAT｜v1.8.0 UAT</p>
    <div className="field"><label>帳號</label><input value={account} onChange={e=>setAccount(e.target.value)} autoComplete="username"/></div>
    <div className="field"><label>密碼</label><input type="password" value={password} onChange={e=>setPassword(e.target.value)} autoComplete="current-password"/></div>
    {error&&<div className="note danger-note">{error}</div>}
    <button className="btn full" disabled={busy}>{busy?"登入中…":"登入"}</button>
    <div className="note">UAT 測試帳號由 UAT Coordinator 提供；請勿使用 Production 帳號。</div>
  </form></div>;
}
