# @gocordis/plugin-kit

Console 插件前端构建工具。一条命令把插件的 TSX/JSX 源码与其 npm 依赖
（echarts、d3……）自打包成单个自包含的 `ui.js`。

```bash
npm install --save-dev @gocordis/plugin-kit
gocordis-plugin-build            # src/ui.tsx -> ui.js
gocordis-plugin-build src/main.tsx -o dist/ui.js
```

`react` / `react/jsx-runtime` / `react-dom` / `react-dom/client` / `antd`
的导入在构建期被重定向到本包的 shims：运行时它们来自控制台门面
（`globalThis.__CORDIS_CONSOLE`）——全页面只有一个 React 实例与渲染器，
hooks 与 antd 弹层因此安全。版本与控制台门面契约同步演进。
