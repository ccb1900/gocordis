import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import { fileURLToPath } from "node:url";

// The shell is deliberately thin: product renderers live in plugin client
// modules, reusable infrastructure lives in @gocordis/console-runtime.
export default defineConfig({
  server: { proxy: { "/api": "http://localhost:8080" } },
  plugins: [react()],
  resolve: {
    alias: {
      "@gocordis/console-runtime": fileURLToPath(
        new URL("../console-runtime/src/index.ts", import.meta.url)),
    },
  },
  build: { outDir: "dist", emptyOutDir: true },
});
