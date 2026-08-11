import {useState} from "react";
import {useAuth} from "../auth";

const demos=[
  ["visitor01","外訪員示範"],
  ["leader01","小組長示範"],
  ["admin01","管理者示範"],
  ["gov01","督導示範"]
] as const;

export default function LoginPage(){
  const {login}=useAuth();
  const [account,setAccount]=useState("visitor01"),[password,setPassword]=useState("123456"),[error,setError]=useState(""),[busy,setBusy]=useState(false);
  const go=async(e:React.FormEvent)=>{e.preventDefault();setBusy(true);setError("");try{await login(account,password)}catch(x){setError(x instanceof Error?x.message:"登入失敗")}finally{setBusy(false)}};
  return <div className="login-screen"><form className="login-card" onSubmit={go}>
    <h1>外訪行程與里程管理</h1><p>Azure SQL 多人 UAT｜Prototype v2 介面</p>
    <div className="field"><label>帳號</label><input value={account} onChange={e=>setAccount(e.target.value)} autoComplete="username"/></div>
    <div className="field"><label>密碼</label><input type="password" value={password} onChange={e=>setPassword(e.target.value)} autoComplete="current-password"/></div>
    {error&&<div className="note danger-note">{error}</div>}
    <button className="btn full" disabled={busy}>{busy?"登入中…":"登入"}</button>
    <div className="demo-users">{demos.map(([a,l])=><button type="button" key={a} onClick={()=>{setAccount(a);setPassword("123456")}}>{l}</button>)}</div>
    <div className="note">UAT 以示範帳號測試真實 API、Azure SQL 與跨裝置資料流；正式環境登入將改由 Microsoft Entra ID。</div>
  </form></div>;
}
