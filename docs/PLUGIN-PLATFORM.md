# GoCordis 插件平台（Koishi 对照后的收敛结果）

基线：Koishi v4 插件体系（用户审查报告）；GoCordis 当前 HEAD。
本文件记录四阶段落地结果，不新增 Core 语义。

## 1. 冻结的分层

```text
Application / Product
Plugin Platform: Market · Explorer · Insight · SchemaForm · DevTools · Observe
Runtime Core: Component · Fiber · Activation · Effect · Provider · Dependency · Realm · Orchestration
Execution carriers: Go Native · WASM · Proc
```

- Market / Explorer / Insight / Schema UI 是平台管理能力；Go Native / WASM / Proc 是执行载体。
- Manifest 只描述插件，不管理生命周期；依赖描述映射到现有 `runtime.Dependency`/`Provider`，不产生第二套依赖系统。
- 不把 WASM/Proc 执行细节搬进 Core；Native 明确为构建期集成。
- 不照搬 Koishi 的聊天机器人领域插件。

## 2. 阶段 1：Plugin Manifest

`extensions/pluginmeta`（`manifest.go`）定义统一元数据：身份、版本、描述、Backend、
ModuleType、Requires/Provides 描述、Config、Compatibility、Source/Checksum。
- `Validate`：用 `golang.org/x/mod/semver` 校验版本；用 `Masterminds/semver/v3` 解析兼容范围；拒绝重复能力键与非法 checksum。
- `ValidateAgainst(m, runtimeVersion, apiVersion)`：Runtime 与 API 约束分别校验，空 apiVersion 跳过 API 检查。
- `Parse`：严格 JSON / TOML（`DisallowUnknownFields`）。
- `CapabilityKeys()`：仅描述性投影，不创建运行时边。
`extensions/pluginmeta/adapters` 将 Native / WASM / Proc 现有元数据统一投影；Proc 无版本字段时使用 `0.0.0` 哨兵。

## 3. 阶段 2：Insight + Config Schema

- `extensions/config/schema.go`：JSON Schema 子集（string/int/number/bool/enum/array/object、required/default、if/then/else），`JSONSchema()` 输出 Draft 2020-12；`Validate()` 用 `santhosh-tekuri/jsonschema/v5` 校验（编译结果缓存）。
- `SchemaFactory` 可选接口；`Controller.Schemas()/Declarations()`；Reconcile 仅对声明 Schema 的工厂校验，旧 `FactoryFunc` 不受影响。
- `extensions/console/insight`：hub 查询 `insight/graph` 与 `config/schema`，页面 `/insight`（renderer `insight-graph`）。声明图来自 Config 声明；运行时图**只**来自 `Runtime.Snapshot`（FiberID、ActivationID、ScopeID、Provider/Dependency、Pending 原因），Insight 不解析依赖、不改 Fiber。
- 前端 `web/console/src/views/InsightGraphBlock.tsx` 用 antd `Tree` 分 Declared/Runtime 两视图；`SchemaForm.tsx` 由 Schema 生成控件并保留旧 FieldSpec 回退；无新增前端依赖。

## 4. 阶段 3：本地 Market

`extensions/market`：扫描本地 `plugin.toml`/`plugin.json`，严格解析与兼容性校验，输出
`market/catalog` 查询与 `/market` 页面；hub 命令 `market/install|upgrade|uninstall`。
- 安装/升级/卸载只经 `config.Controller.Reconcile`，不触碰 Fiber；`reconcile` 以 `Controller.Owned()` 为基准，避免误删无关组件。
- WASM/Proc 安装校验 sha256 checksum；Native 为 build-time，操作返回 `ErrBuildTimePlugin`。
- `Uninstall` 由已安装行驱动，manifest 删除后仍可卸载；坏 manifest 以 `issues` 非致命告警返回，不空白整页。
- 前端 `MarketCatalog.tsx` 用 antd `Table`，Native 只读，动作按钮由声明的行操作驱动。

## 5. 阶段 4：Developer Experience

- `extensions/devtools`：`Diagnose`/`DiagnoseAgainst` 纯诊断（身份、版本兼容、重复能力键、checksum、Backend、Source 等），通过 typed key 提供，可逆注册。
- `cmd/plugin`：`create`（模板脚手架、失败回滚、`--force`）、`doctor`（解析+诊断）、`preflight`（临时 Runtime + Snapshot 摘要，干净关闭且不写用户文件；仅凭 manifest 无法加载时给出 `REQUIRES_BUILD_TIME_REGISTRATION` 诊断）。这是**声明预检**，不是安全沙箱；`sandbox` 只作为兼容别名保留。

## 6. 验收约束

- 每阶段先写失败测试再实现；并发相关跑 `-race`。
- 独立审查发现并修复：Manifest API/Runtime 版本混用、SchemaForm 违反 Rules of Hooks、Insight 假测试、Market 误删无关声明/卸载残留/坏目录空白页。
- 前端 `npm test`、`npx tsc --noEmit`、`npm run build`；Go `gofmt`/`build`/`vet`/`test`/`race` 全绿。
- 远程插件市场、签名验证、Sandbox 图形化、完整国际化仍属后续产品阶段，不在本轮范围。
- **热更新边界（当前口径）**：`Gocordis.Extensions.GenericHmr` 与 WASM `WasmHotSwapper`
  只是实验性代际原语，产品宿主没有调用链。当前代码变更走
  build → pack → upgrade（声明 Revision），**不能宣称无打包热更新已完成**。
