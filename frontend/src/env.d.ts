interface Window{
  APP_CONFIG?:{
    API_BASE_URL?:string;
    AUTH_MODE?:"Demo"|"Entra"|string;
    EPIC_F_GOOGLE?:{
      ENABLED?:boolean;
      MAPS_JS_API_KEY?:string;
    };
    ENTRA?:{
      TENANT_ID?:string;
      SPA_CLIENT_ID?:string;
      API_SCOPE?:string;
      REDIRECT_URI?:string;
    };
  };
}
