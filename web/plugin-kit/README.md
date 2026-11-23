# @gocordis/plugin-kit

Console 插件前端构建工具。一条命令把插件的 TSX/JSX 源码与其 npm 依赖
（echarts、d3……）自打包成单个自包含的 `ui.js`。

```bash
npm install --save-dev @gocordis/plugin-kit
gocordis-plugin-build            # src/ui.tsx -> ui.js
gocordis-plugin-build src/main.tsx -o dist/ui.js
```

每次构建还会在产物同目录写出 `gocordis-ui.json`（及 `ui/pages/*.json`、
`ui/panels/*.json`），声明 client-module 入口与可选的页面/Panel 定义；
插件包可直接把这些产物纳入 payload，包读取器会校验路径并纳入 checksum。
需要自定义页面/Panel 时提供 `--ui-config ui.config.json`：

```json
{ "module": "demo-ui", "pages": [{ "id": "demo", "title": "Demo", "route": "/demo", "renderer": "demo" }] }
```

`react` / `react/jsx-runtime` / `react-dom` / `react-dom/client` / `antd`
的导入在构建期被重定向到本包的 shims：运行时它们来自控制台门面
（`globalThis.__CORDIS_CONSOLE`）——全页面只有一个 React 实例与渲染器，
hooks 与 antd 弹层因此安全。版本与控制台门面契约同步演进。

### 兼容契约(Kit ↔ 控制台)

- **门面契约版本**:控制台在 `globalThis.__CORDIS_CONSOLE.frameworkVersion`
  暴露门面契约版本(与 `web/console` 的 `FACADE_VERSION` 一致,当前 `"1"`)。
  规则是 **major 对齐**:bundle 面向构建时的门面契约 major 运行;插件可在
  `register(api)` 里读 `api.frameworkVersion` 做特性检测。
- **缺失门面即报错**:shims 在 `__CORDIS_CONSOLE` 不存在时抛出可读错误
  (bundle 必须被 GoCordis 控制台加载,不能独立运行)——不再是对
  `undefined` 的属性访问崩溃。**shim 只再导出宿主实际存在的绑定**（2026-11
  审计）：react shim 移除了 React-19-only 的 `useOptimistic`（React 18 上该
  绑定为 undefined，调用即崩）；jsx-runtime shim 在生产 runtime 缺 `jsxDEV`
  时降级为 `jsx`（dev-condition esbuild 产物首次渲染不再崩溃，仅失去 dev
  源码标注）。
- **内容修订 ≠ 契约版本**:manifest URL 的 `?v=<内容哈希>` 只反映 ui.js
  自身字节变化(热更新用),与门面契约版本无关。
- **扩展点契约**:register 收到的分组 API(pages/panels/blocks/widgets/
  settings/actions 的 `register`)见 `docs/guide/ui-plugin-lifecycle.md`。
