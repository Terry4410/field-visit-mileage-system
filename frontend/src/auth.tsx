import React,{createContext,useContext,useEffect,useMemo,useState}from"react";
import{api,clearToken,getToken,setToken}from"./api";
import type{CurrentUser,LoginResponse}from"./types";

type Ctx={user:CurrentUser|null;loading:boolean;login:(account:string,password:string)=>Promise<void>;logout:()=>void;refresh:()=>Promise<void>};
const AuthContext=createContext<Ctx|undefined>(undefined);
export function AuthProvider({children}:{children:React.ReactNode}){
 const[user,setUser]=useState<CurrentUser|null>(null);const[loading,setLoading]=useState(true);
 const refresh=async()=>{if(!getToken()){setUser(null);return}setUser(await api<CurrentUser>("/me"))};
 useEffect(()=>{(async()=>{try{await refresh()}catch{clearToken();setUser(null)}finally{setLoading(false)}})();const unauth=()=>setUser(null);window.addEventListener("fieldvisit:unauthorized",unauth);return()=>window.removeEventListener("fieldvisit:unauthorized",unauth)},[]);
 const value=useMemo<Ctx>(()=>({user,loading,refresh,login:async(a,p)=>{const r=await api<LoginResponse>("/auth/demo-login",{method:"POST",body:JSON.stringify({account:a,password:p})});setToken(r.accessToken);setUser(r.user)},logout:()=>{clearToken();setUser(null)}}),[user,loading]);
 return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}
export function useAuth(){const x=useContext(AuthContext);if(!x)throw new Error("AuthProvider missing");return x}
