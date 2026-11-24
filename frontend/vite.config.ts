import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import { resolve } from "node:path";

// MSAL Browser v5 popup authentication requires a separate redirect bridge.
// Keep the accepted main SPA entry unchanged and include redirect.html in dist.
export default defineConfig({
  plugins: [react()],
  base: "./",
  build: {
    rollupOptions: {
      input: {
        main: resolve(__dirname, "index.html"),
        redirect: resolve(__dirname, "redirect.html")
      }
    }
  }
});
