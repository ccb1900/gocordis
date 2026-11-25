import { ConfigProvider } from "antd";
import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { Designer } from "./Designer";
import "./styles.css";

const container = document.getElementById("root");
if (!container) {
  throw new Error("industrial-designer: #root container is missing");
}

// Standalone entry. The same app is served by the console at
// /client-modules/industrial-designer/index.html via the Cordis client-module
// contribution registered by IndustrialClientModuleComponent.
createRoot(container).render(
  <StrictMode>
    <ConfigProvider>
      <Designer />
    </ConfigProvider>
  </StrictMode>,
);
