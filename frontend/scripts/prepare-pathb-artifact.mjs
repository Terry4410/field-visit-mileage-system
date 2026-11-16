// NONDEPLOY: assemble and validate Path B-only static assets after vite build.
import { readFileSync, writeFileSync, existsSync } from "node:fs";
import { resolve } from "node:path";
const root = resolve(import.meta.dirname, "..");
const dist = resolve(root, "dist");
const candidate = readFileSync(resolve(root, "config.pathb-uat.js"), "utf8");
if (!existsSync(resolve(dist, "index.html"))) throw Error("Missing frontend build");
const expected = "https://api-fieldvisit-pathb-uat.azurewebsites.net/api/v1";
if (!candidate.includes(`API_BASE_URL: "${expected}"`)) throw Error("Wrong Path B API URL");
if (!candidate.includes('AUTH_MODE: "Demo"')) throw Error("Unexpected auth mode");
if (/api-fieldvisit-uat(?:-|\.)/.test(candidate) || /localhost:5080/.test(candidate)) throw Error("Legacy/local target in candidate");
writeFileSync(resolve(dist, "config.js"), candidate, "utf8");
if (readFileSync(resolve(dist, "config.js"), "utf8") !== candidate) throw Error("Config copy mismatch");
console.log("PATHB_STATIC_ARTIFACT=PASS (NONDEPLOY)");
