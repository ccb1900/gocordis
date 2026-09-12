import React from "react";
import { createRoot } from "react-dom/client";
import { ConfigProvider, theme as antdTheme } from "antd";
import zhCN from "antd/locale/zh_CN";
import { ThemeProvider, useTheme } from "./theme";
import { loadClientModules } from "./lib/client-modules";
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
  // Plugin client modules register renderers; load them before first render
  // so custom pages resolve on first paint. One broken module — or the whole
  // manifest being absent — never blocks the console.
  loadClientModules()
    .catch(() => undefined)
    .finally(() => {
      createRoot(root).render(
        <React.StrictMode>
          <ThemeProvider>
            <ThemedRoot />
          </ThemeProvider>
        </React.StrictMode>
      );
    });
}
