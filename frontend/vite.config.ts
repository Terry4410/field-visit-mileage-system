import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

// MSAL Browser v5 popup authentication requires a separate redirect bridge.
// Keep the accepted main SPA entry unchanged and include redirect.html in dist.
export default defineConfig({
  plugins: [react()],
  base: "./",
  build: {
    rollupOptions: {
      input: {
        main: "index.html",
        redirect: "redirect.html"
      }
    }
  }
});
