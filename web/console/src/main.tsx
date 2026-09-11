import React from "react";
import { createRoot } from "react-dom/client";
import { ConfigProvider, theme as antdTheme } from "antd";
import zhCN from "antd/locale/zh_CN";
import App from "./App";

const dark = localStorage.getItem("gc-theme") === "dark";

createRoot(document.getElementById("root")!).render(
  React.createElement(ConfigProvider, {
    locale: zhCN,
    theme: { algorithm: dark ? antdTheme.darkAlgorithm : antdTheme.defaultAlgorithm, token: { colorPrimary: "#4d6bfe", borderRadius: 8 } },
  }),
  React.createElement(App)
);
