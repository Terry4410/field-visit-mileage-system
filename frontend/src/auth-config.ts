export type FrontendAuthMode="Demo"|"Entra";

export interface EntraFrontendConfig{
  tenantId:string;
  clientId:string;
  apiScope:string;
  redirectUri:string;
}

export interface FrontendAuthConfig{
  mode:FrontendAuthMode;
  entra:EntraFrontendConfig;
}

export function normalizeFrontendAuthMode(
  value?:string|null
):FrontendAuthMode{
  if(!value||!value.trim())return"Demo";

  if(value.trim().toLowerCase()==="demo")
    return"Demo";

  if(value.trim().toLowerCase()==="entra")
    return"Entra";

  throw new Error(
    `不支援的 AUTH_MODE：${value}`
  );
}

export function getFrontendAuthConfig():FrontendAuthConfig{
  const raw=window.APP_CONFIG;
  const mode=normalizeFrontendAuthMode(
    raw?.AUTH_MODE
  );

  const redirectUri=
    raw?.ENTRA?.REDIRECT_URI?.trim()
    ||`${window.location.origin}${window.location.pathname}`;

  return{
    mode,
    entra:{
      tenantId:raw?.ENTRA?.TENANT_ID?.trim()||"",
      clientId:raw?.ENTRA?.SPA_CLIENT_ID?.trim()||"",
      apiScope:raw?.ENTRA?.API_SCOPE?.trim()||"",
      redirectUri
    }
  };
}

export function validateEntraFrontendConfig(
  config:EntraFrontendConfig
){
  const missing:string[]=[];

  if(!config.tenantId)
    missing.push("ENTRA.TENANT_ID");

  if(!config.clientId)
    missing.push("ENTRA.SPA_CLIENT_ID");

  if(!config.apiScope)
    missing.push("ENTRA.API_SCOPE");

  if(!config.redirectUri)
    missing.push("ENTRA.REDIRECT_URI");

  if(missing.length)
    throw new Error(
      `Microsoft Entra 前端設定不完整：${missing.join("、")}`
    );
}
