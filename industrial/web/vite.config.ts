import { defineConfig } from "vitest/config";
import react from "@vitejs/plugin-react";
import { fileURLToPath } from "node:url";

// A Cordis CLIENT MODULE, not a standalone app. React/antd are resolved to
// facade shims so the console owns the single instances; G6 is bundled here.
const shim = (name: string) => fileURLToPath(new URL(`./src/${name}.ts`, import.meta.url));

export default defineConfig({
  plugins: [react()],
  resolve: {
    // Tests render components with the real React instance supplied through
    // the facade; aliasing in Vitest would make the facade alias itself.
    alias: process.env.VITEST ? [] : [
      { find: /^react\/jsx-runtime$/, replacement: shim("facade-jsx") },
      { find: /^react$/, replacement: shim("facade-react") },
      { find: /^antd$/, replacement: shim("facade-antd") },
    ],
  },
  test: {
    exclude: ["e2e/**", "node_modules/**", "dist/**"],
  },
  build: {
    outDir: "dist",
    emptyOutDir: true,
    lib: { entry: fileURLToPath(new URL("./src/module.ts", import.meta.url)), formats: ["es"], fileName: () => "module.js" },
    rollupOptions: { external: [] },
  },
});
