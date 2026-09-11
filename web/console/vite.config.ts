import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

// The shared console build: one static bundle every gocordis application
// serves unchanged (go:embed, DirFS, CDN). Dev mode proxies to a running
// application host.
export default defineConfig({
  server: { proxy: { "/api": "http://localhost:8080" } },
  plugins: [react()],
  build: { outDir: "dist", emptyOutDir: true },
});
