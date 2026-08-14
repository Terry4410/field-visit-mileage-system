interface Window{
  APP_CONFIG?:{
    API_BASE_URL?:string;
    AUTH_MODE?:"Demo"|"Entra"|string;
    ENTRA?:{
      TENANT_ID?:string;
      SPA_CLIENT_ID?:string;
      API_SCOPE?:string;
      REDIRECT_URI?:string;
    };
  };
}
