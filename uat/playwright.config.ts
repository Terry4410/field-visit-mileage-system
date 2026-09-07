import { defineConfig, devices } from "@playwright/test";

const baseURL =
  process.env.UAT_BASE_URL ??
  "https://terry4410.github.io/field-visit-mileage-system/";
const localPreview = /^https?:\/\/(127\.0\.0\.1|localhost)(:\d+)?\/?$/i.test(baseURL);

export default defineConfig({
  testDir: "./tests",
  fullyParallel: false,
  forbidOnly: Boolean(process.env.CI),
  retries: process.env.CI ? 1 : 0,
  workers: 1,
  timeout: 45_000,
  reporter: [
    ["list"],
    ["html", { outputFolder: "playwright-report", open: "never" }]
  ],
  webServer: localPreview ? {
    command: "npm --prefix ../frontend run preview -- --host 127.0.0.1 --port 4173",
    url: baseURL,
    reuseExistingServer: !process.env.CI,
    timeout: 120_000
  } : undefined,
  use: {
    baseURL,
    trace: "retain-on-failure",
    screenshot: "only-on-failure",
    video: "off",
    actionTimeout: 15_000,
    navigationTimeout: 30_000
  },
  projects: [
    {
      name: "chromium",
      use: { ...devices["Desktop Chrome"] }
    }
  ],
  outputDir: "test-results"
});
