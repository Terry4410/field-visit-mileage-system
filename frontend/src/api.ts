const TOKEN = "fieldvisit_uat_token";
export const apiBase = () => (window.APP_CONFIG?.API_BASE_URL || "http://localhost:5080/api/v1").replace(/\/$/, "");
export const getToken = () => sessionStorage.getItem(TOKEN);
export const setToken = (v:string) => sessionStorage.setItem(TOKEN,v);
export const clearToken = () => sessionStorage.removeItem(TOKEN);

export async function api<T>(path:string, init:RequestInit = {}):Promise<T> {
  const headers = new Headers(init.headers || {});
  if (init.body && !headers.has("Content-Type")) headers.set("Content-Type","application/json");
  const token = getToken(); if (token) headers.set("Authorization",`Bearer ${token}`);
  const res = await fetch(`${apiBase()}${path}`,{...init,headers});
  if (res.status === 204) return undefined as T;
  const payload = (res.headers.get("content-type") || "").includes("json") ? await res.json() : await res.text();
  if (!res.ok) throw new Error(typeof payload === "object" ? (payload.detail || payload.title || JSON.stringify(payload)) : String(payload));
  return payload as T;
}
