window.APP_CONFIG = {
  API_BASE_URL: "https://api-fieldvisit-uat-cxauf4g8fzdyfsd9.eastasia-01.azurewebsites.net/api/v1",

  // Demo | Entra
  AUTH_MODE: "Demo",

  ENTRA: {
    // Microsoft Entra Directory (tenant) ID
    TENANT_ID: "",

    // SPA App Registration - Application (client) ID
    SPA_CLIENT_ID: "",

    // Full delegated API scope, for example:
    // api://<WEB_API_CLIENT_ID>/access_as_user
    API_SCOPE: "",

    // Must exactly match an Entra SPA redirect URI.
    // GitHub Pages example:
    // https://terry4410.github.io/field-visit-mileage-system/
    REDIRECT_URI: ""
  }
};
