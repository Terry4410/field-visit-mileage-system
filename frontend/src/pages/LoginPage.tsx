import{useState}from"react";
import{useAuth}from"../auth";

const demos=[
  ["visitor01","外訪員示範"],
  ["leader01","小組長示範"],
  ["admin01","管理者示範"],
  ["gov01","督導示範"]
]as const;

export default function LoginPage(){
  const{
    login,
    loginEntra,
    authMode
  }=useAuth();

  const[account,setAccount]=
    useState("visitor01");

  const[password,setPassword]=
    useState("123456");

  const[error,setError]=
    useState("");

  const[busy,setBusy]=
    useState(false);

  const demoLogin=async(
    e:React.FormEvent
  )=>{
    e.preventDefault();
    setBusy(true);
    setError("");

    try{
      await login(account,password);
    }catch(x){
      setError(
        x instanceof Error
          ?x.message
          :"登入失敗"
      );
    }finally{
      setBusy(false);
    }
  };

  const entraLogin=async()=>{
    setBusy(true);
    setError("");

    try{
      await loginEntra();
    }catch(x){
      setError(
        x instanceof Error
          ?x.message
          :"Microsoft Entra 登入失敗"
      );
    }finally{
      setBusy(false);
    }
  };

  if(authMode==="Entra"){
    return(
      <div className="login-screen">
        <div className="login-card">
          <h1>外訪行程與里程管理</h1>
          <p>Microsoft Entra ID SSO｜v1.7.0</p>

          {error&&
            <div className="note danger-note">
              {error}
            </div>
          }

          <button
            type="button"
            className="btn full"
            disabled={busy}
            onClick={()=>void entraLogin()}
          >
            {busy
              ?"Microsoft 登入中…"
              :"使用 Microsoft 帳號登入"}
          </button>

          <div className="note">
            本環境使用公司 Microsoft Entra ID
            進行身分驗證；登入後的角色、小組及資料範圍
            仍由本系統權限設定控制。
          </div>
        </div>
      </div>
    );
  }

  return(
    <div className="login-screen">
      <form
        className="login-card"
        onSubmit={demoLogin}
      >
        <h1>外訪行程與里程管理</h1>
        <p>Azure SQL 多人 UAT｜Demo Login｜v1.7.0</p>

        <div className="field">
          <label>帳號</label>
          <input
            value={account}
            onChange={e=>
              setAccount(e.target.value)
            }
            autoComplete="username"
          />
        </div>

        <div className="field">
          <label>密碼</label>
          <input
            type="password"
            value={password}
            onChange={e=>
              setPassword(e.target.value)
            }
            autoComplete="current-password"
          />
        </div>

        {error&&
          <div className="note danger-note">
            {error}
          </div>
        }

        <button
          className="btn full"
          disabled={busy}
        >
          {busy?"登入中…":"登入"}
        </button>

        <div className="demo-users">
          {demos.map(([a,label])=>
            <button
              type="button"
              key={a}
              onClick={()=>{
                setAccount(a);
                setPassword("123456");
              }}
            >
              {label}
            </button>
          )}
        </div>

        <div className="note">
          Demo 模式保留供 UAT 測試。
          正式環境可由設定檔切換為 Microsoft Entra ID SSO。
        </div>
      </form>
    </div>
  );
}
