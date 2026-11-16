// Path B-only deployment candidate. NOT loaded until separately approved deployment
// explicitly replaces the generated site's config.js with these exact bytes.
window.APP_CONFIG = {
  API_BASE_URL: "https://api-fieldvisit-pathb-uat.azurewebsites.net/api/v1",
  AUTH_MODE: "Demo",
  ENTRA: { TENANT_ID: "", SPA_CLIENT_ID: "", API_SCOPE: "", REDIRECT_URI: "" }
};
