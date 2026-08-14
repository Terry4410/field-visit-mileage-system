import{
  BrowserCacheLocation,
  InteractionRequiredAuthError,
  PublicClientApplication
}from"@azure/msal-browser";
import{
  getFrontendAuthConfig,
  validateEntraFrontendConfig
}from"./auth-config";

let clientPromise:Promise<PublicClientApplication>|null=null;

async function getClient(){
  const config=getFrontendAuthConfig();

  if(config.mode!=="Entra")
    throw new Error(
      "目前前端登入模式不是 Microsoft Entra。"
    );

  validateEntraFrontendConfig(config.entra);

  if(!clientPromise){
    const client=new PublicClientApplication({
      auth:{
        clientId:config.entra.clientId,
        authority:
          `https://login.microsoftonline.com/${config.entra.tenantId}`,
        redirectUri:config.entra.redirectUri
      },
      cache:{
        cacheLocation:BrowserCacheLocation.SessionStorage
      }
    });

    clientPromise=(async()=>{
      await client.initialize();
      return client;
    })();
  }

  return clientPromise;
}

export async function acquireEntraAccessToken(){
  const config=getFrontendAuthConfig();
  validateEntraFrontendConfig(config.entra);

  const client=await getClient();

  const login=await client.loginPopup({
    scopes:[config.entra.apiScope],
    redirectUri:config.entra.redirectUri
  });

  if(!login.account)
    throw new Error(
      "Microsoft Entra 登入沒有取得使用者帳號。"
    );

  client.setActiveAccount(login.account);

  try{
    const token=await client.acquireTokenSilent({
      account:login.account,
      scopes:[config.entra.apiScope]
    });

    return token.accessToken;
  }catch(error){
    if(!(error instanceof InteractionRequiredAuthError))
      throw error;

    const token=await client.acquireTokenPopup({
      account:login.account,
      scopes:[config.entra.apiScope],
      redirectUri:config.entra.redirectUri
    });

    return token.accessToken;
  }
}
