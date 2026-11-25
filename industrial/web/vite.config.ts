import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

// This is a NEW independent app, not a rebuild of web/console. It is served by
// the console at /client-modules/industrial-designer/index.html, so asset URLs
// MUST be relative to that directory (absolute /assets/... would 404).
export default defineConfig({
  base: "./",
  plugins: [react()],
  server: { proxy: { "/api": "http://localhost:8080" } },
  build: { outDir: "dist", emptyOutDir: true },
});
