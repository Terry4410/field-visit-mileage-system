const TOKEN = "fieldvisit_uat_token";

const normalizeBase = (raw:string) => {
  const base=(raw||"http://localhost:5080").replace(/\/+$/,"");
  return /\/api\/v1$/i.test(base)?base:`${base}/api/v1`;
};

export const apiBase=()=>normalizeBase(window.APP_CONFIG?.API_BASE_URL||"http://localhost:5080");
export const getToken=()=>sessionStorage.getItem(TOKEN);
export const setToken=(v:string)=>sessionStorage.setItem(TOKEN,v);
export const clearToken=()=>sessionStorage.removeItem(TOKEN);

async function request(path:string,init:RequestInit={},timeoutMs=30000){
  const controller=new AbortController();
  const timer=setTimeout(()=>controller.abort(),timeoutMs);
  const headers=new Headers(init.headers||{});
  if(init.body && !(init.body instanceof FormData) && !headers.has("Content-Type"))headers.set("Content-Type","application/json");
  const token=getToken();if(token)headers.set("Authorization",`Bearer ${token}`);
  const activeRole=sessionStorage.getItem("fieldvisit_active_role");if(activeRole)headers.set("X-Active-Role",activeRole);
  try{
    const res=await fetch(`${apiBase()}${path}`,{...init,headers,signal:controller.signal});
    if(res.status===401){clearToken();window.dispatchEvent(new Event("fieldvisit:unauthorized"));}
    return res;
  }catch(e){
    if(e instanceof DOMException&&e.name==="AbortError")throw new Error("連線逾時，請稍後重試。");
    throw new Error("無法連線到系統 API，請確認網路或稍後重試。");
  }finally{clearTimeout(timer)}
}

async function errorFrom(res:Response){
  const type=res.headers.get("content-type")||"";
  const payload=type.includes("json")?await res.json():await res.text();
  if(typeof payload==="object"&&payload){
    const p=payload as {detail?:string;title?:string;errors?:Record<string,string[]>};
    const validation=p.errors?Object.values(p.errors).flat().join("；"):"";
    return validation||p.detail||p.title||JSON.stringify(payload);
  }
  return String(payload||`HTTP ${res.status}`);
}

export async function api<T>(path:string,init:RequestInit={},timeoutMs=30000):Promise<T>{
  const res=await request(path,init,timeoutMs);
  if(res.status===204)return undefined as T;
  if(!res.ok)throw new Error(await errorFrom(res));
  const type=res.headers.get("content-type")||"";
  return (type.includes("json")?await res.json():await res.text()) as T;
}

export async function apiDownload(path:string,filenameFallback:string,timeoutMs=120000){
  const res=await request(path,{},timeoutMs);
  if(!res.ok)throw new Error(await errorFrom(res));
  const blob=await res.blob();
  const cd=res.headers.get("content-disposition")||"";
  const utf=cd.match(/filename\*=UTF-8''([^;]+)/i)?.[1];
  const plain=cd.match(/filename="?([^";]+)"?/i)?.[1];
  const filename=utf?decodeURIComponent(utf):plain||filenameFallback;
  const url=URL.createObjectURL(blob);const a=document.createElement("a");a.href=url;a.download=filename;document.body.appendChild(a);a.click();a.remove();setTimeout(()=>URL.revokeObjectURL(url),0);
}
