import React,{
  createContext,
  useContext,
  useEffect,
  useMemo,
  useState
}from"react";
import{
  api,
  clearToken,
  getToken,
  setToken
}from"./api";
import{
  getFrontendAuthConfig
}from"./auth-config";
import type{
  CurrentUser,
  LoginResponse
}from"./types";

type Ctx={
  user:CurrentUser|null;
  loading:boolean;
  authMode:"Demo"|"Entra";
  login:(account:string,password:string)=>Promise<void>;
  loginEntra:()=>Promise<void>;
  logout:()=>void;
  refresh:()=>Promise<void>;
};

const AuthContext=createContext<Ctx|undefined>(
  undefined
);

export function AuthProvider({
  children
}:{children:React.ReactNode}){
  const[user,setUser]=useState<CurrentUser|null>(null);
  const[loading,setLoading]=useState(true);

  const authMode=
    getFrontendAuthConfig().mode;

  const refresh=async()=>{
    if(!getToken()){
      setUser(null);
      return;
    }

    setUser(
      await api<CurrentUser>("/me")
    );
  };

  useEffect(()=>{
    (async()=>{
      try{
        await refresh();
      }catch{
        clearToken();
        setUser(null);
      }finally{
        setLoading(false);
      }
    })();

    const unauth=()=>setUser(null);

    window.addEventListener(
      "fieldvisit:unauthorized",
      unauth
    );

    return()=>window.removeEventListener(
      "fieldvisit:unauthorized",
      unauth
    );
  },[]);

  const value=useMemo<Ctx>(()=>({
    user,
    loading,
    authMode,
    refresh,

    login:async(account,password)=>{
      if(authMode!=="Demo")
        throw new Error(
          "目前系統使用 Microsoft Entra 登入。"
        );

      const r=await api<LoginResponse>(
        "/auth/demo-login",
        {
          method:"POST",
          body:JSON.stringify({
            account,
            password
          })
        }
      );

      setToken(r.accessToken);
      setUser(r.user);
    },

    loginEntra:async()=>{
      if(authMode!=="Entra")
        throw new Error(
          "目前系統使用 Demo 登入。"
        );

      /*
       * Remove any previous FieldVisit JWT first.
       * /auth/entra-login must receive the Entra access token,
       * not an existing FieldVisit application token.
       */
      clearToken();

      // Load MSAL only when Entra login is actually used.
      // Demo/UAT users do not need to download the Microsoft auth bundle.
      const{
        acquireEntraAccessToken
      }=await import("./entra-auth");

      const entraToken=
        await acquireEntraAccessToken();

      const r=await api<LoginResponse>(
        "/auth/entra-login",
        {
          method:"POST",
          headers:{
            Authorization:`Bearer ${entraToken}`
          }
        }
      );

      setToken(r.accessToken);
      setUser(r.user);
    },

    logout:()=>{
      /*
       * Sign out of FieldVisit only.
       * Do not sign the employee out of the corporate Microsoft
       * browser session; this preserves normal enterprise SSO.
       */
      clearToken();
      sessionStorage.removeItem(
        "fieldvisit_active_role"
      );
      setUser(null);
    }
  }),[
    user,
    loading,
    authMode
  ]);

  return(
    <AuthContext.Provider value={value}>
      {children}
    </AuthContext.Provider>
  );
}

export function useAuth(){
  const value=useContext(AuthContext);

  if(!value)
    throw new Error("AuthProvider missing");

  return value;
}
