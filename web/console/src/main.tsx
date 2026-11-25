import React from "react";
import { createRoot } from "react-dom/client";
import { ConfigProvider, theme as antdTheme } from "antd";
import zhCN from "antd/locale/zh_CN";
import { ThemeProvider, useTheme } from "./theme";
import { reconcileClientModules } from "@gocordis/console-runtime";
import App from "./shell/App";

function ThemedRoot() {
  const { isDark } = useTheme();
  return (
    <ConfigProvider
      locale={zhCN}
      theme={{
        algorithm: isDark ? antdTheme.darkAlgorithm : antdTheme.defaultAlgorithm,
        token: {
          colorPrimary: "#4d6bfe",
          borderRadius: 8,
          ...(isDark
            ? { colorBgBase: "#0b0d12", colorBgContainer: "#12151d", colorBorder: "#232939" }
            : { colorBgBase: "#f6f7f9", colorBgContainer: "#ffffff", colorBorder: "#d9dee8" }),
        },
      }}
    >
      <App />
    </ConfigProvider>
  );
}

const root = document.getElementById("root");
if (root) {
  // Bootstrap order (P3 fix, 2026-11 audit): the first reconcile is STARTED
  // before React mounts — reconcile() bumps its inflight counter
  // synchronously, so the shell's FIRST paint observes the honest
  // "plugins loading" state instead of the settled "not registered" errors.
  // The old order mounted React only in .finally, serializing first paint
  // behind the manifest fetch and EVERY module import (slow manifest or one
  // large ui.js = blank page). Custom pages still resolve correctly: the
  // reactive registry version re-renders them the moment their renderer
  // registers. One broken module — or the whole manifest being absent —
  // never blocks the console.
  const boot = reconcileClientModules();
  createRoot(root).render(
    <React.StrictMode>
      <ThemeProvider>
        <ThemedRoot />
      </ThemeProvider>
    </React.StrictMode>
  );
  boot.catch(() => undefined);
}
