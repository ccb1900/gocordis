# UI 插件生命周期（Client Module Lifecycle）

本文描述插件前端模块（`ui-client` 组件）在控制台中的完整生命周期：激活、
停用、重新启用、内容更新与失败回滚。它与 GOCORDIS Runtime 的 activation
语义一一镜像——前端模块管理器是论文"可逆 effect、LIFO 撤销"在浏览器侧的
对应物。

权威来源：`docs/论文.pdf`（唯一规范）；撤销顺序的运行时锚点是
`runtime/component.go`（"Apply 返回的 Cleanup 被转换为 activation 的最后
一个 effect，因此最先被撤销；`Context.Effect` 的逆随后按 LIFO 执行"）。

## 参与者

| 参与者 | 位置 | 职责 |
|---|---|---|
| `ui-client` 组件 | `extensions/console/host/clientmodule.go` | 声明层：一个组件 = 一个前端模块；启用/停用走声明开关 |
| Composition Registry | `extensions/console/registry/composition.go` | 模块清单的权威；注册/注销都是 Effect 拥有的可逆操作 |
| webui manifest | `extensions/console/webui/server.go` `GET /api/ui/client-modules` | 输出期望模块集 `{name, url}`；url 带内容修订 `?v=<sha256 前 8 hex>` |
| Module Manager | `web/console/src/lib/client-modules.ts` | 前端 reconcile：对比期望集与激活集，执行激活/停用/重载 |
| keyed registry | `web/console/src/views/registry.tsx` | 渲染器资源池；dispose 由 Manager 持有并执行 |

## 生命周期一览

| 事件 | 行为 |
|---|---|
| 插件启用（声明 `enabled=true`，Load） | `ui-client` Apply → `RegisterClientModule` → manifest 出现该模块 → SSE `composition.changed` → 前端 reconcile → 动态 import → 调用 `register(facade)` → 激活 |
| 插件停用（Disable / uninstall） | Effect 撤销 → 模块离开 manifest → SSE → 前端 dispose：先执行 `register` 返回的 dispose，再按 LIFO 撤销 facade 注册的渲染器 |
| 插件重新启用 | manifest 重新出现 → reconcile 重新执行 `register`（ESM 模块体留在浏览器缓存，不会重跑；只有注册逻辑重跑） |
| 模块文件内容更新 | manifest url 的 `?v=` 哈希变化 → reconcile 视为新 URL → dispose 旧激活 → 重新 import + 注册（会话内热更新，无需刷新页面） |
| import / register 失败 | 已注册部分全部回滚，激活不落地；下一次**调和触发**时重新尝试（见下文锚定），从不自发重试；一个模块失败不影响其他模块 |
| manifest 拉取失败 | 视为"期望状态未知"，**保留**当前激活，不卸载（防止瞬时故障卸光全部插件） |

## register 契约

```js
// plugins/<name>/ui.js
export default function register(api) {
  // 经 facade 注册的一切资源由框架记账，模块无需自己维护清理逻辑
  const disposeGauge = api.registerBlockRenderer("gauge", GaugeBlock);
  const disposePage  = api.registerPageRenderer("alarm", AlarmPage);

  // 可选：返回一个 dispose，覆盖 facade 之外创建的资源
  // （定时器、WebSocket、自管理的事件订阅……）
  return () => {
    disposeGauge();
    disposePage();
  };
}
```

规则：

1. **资源归属在框架**：通过 `api.register*Renderer` 注册的每一项都被记入
   本次激活的撤销栈；模块提前调用返回的 dispose 也可以（幂等，once 包装，
   不会双重执行）。
2. **返回的 dispose 是"最后一个 effect"**：停用时**最先**执行它，然后按
   LIFO 撤销 facade 注册（镜像 `runtime/component.go`）。
3. **facade 之外的资源必须由返回的 dispose 覆盖**：模块体顶层不要建立
   需要清理的副作用——模块体只在首次 import 时执行一次，之后启停只重跑
   `register`。
4. 内置渲染器（`views/blocks.tsx`、控制台基建页面）不走模块管理器，
   生命周期为进程级，行为不变。

## 失败与重试的论文锚定

前端模块的失败重试**不是**铁律 5"不会自动重试"的反例，边界如下：

- **铁律 5 约束的是 Runtime fiber**：组件 Apply 失败 → Failed outcome 阻止
  重入，直到显式 Load（"重试是修正声明后重新启用"）。前端模块加载失败
  不经过任何 fiber——`ui-client` 组件本身 Active（它只负责把模块写入
  manifest，且已成功），失败的只是浏览器侧对 manifest 的实现。
- **前端 manager 镜像的是 config Controller 的 diff 调和**
  （`extensions/config/config.go`）：声明组件加载失败时**不进入 `applied`
  集合**，每次 `Reconcile` 触发（声明文件变更/显式调用）都按 diff 重新
  尝试；"no automatic retry" 指的是 Controller 不自发循环，而不是失败
  永久粘滞。前端 manager 逐字镜像：激活不落地 = 不进 applied；下一次
  调和触发（`composition.changed` / 引导）重新尝试；两次触发之间**从不
  自发重试**。
- **修正声明的三个入口**（对应"重试是修正声明后重新启用"）：内容修订
  （`?v=` 哈希变化）、插件停用→重新启用（离开再进入期望集）、页面刷新
  （浏览器侧"重启 = 全新调和"，铁律 9）。

## 五种扩展点契约

插件前端通过 register 函数拿到的 facade（`ClientModuleAPI`）暴露五个扩展
点，全部返回幂等 dispose 并由模块管理器记账——插件永不自己维护全局注册表
的清理：

```js
export default function register(api) {
  const p = api.pages.register({ name: "alarm-console", component: AlarmPage });
  const w = api.widgets.register("alarm-summary", AlarmSummary);
  api.panels.register({ name: "alarm-feed", component: AlarmFeed });
  api.blocks.register("alarm-trend", AlarmTrendBlock);
  api.settings.register(AlarmSettings);            // 绑定本插件
  api.actions.register({
    id: "alarm-rerun", title: "重跑当日采集",
    when: ({ page }) => page?.id === "collections",
    run: ({ ctx }) => ctx.hubCommand("rerun", {}),
  });
  return () => { p(); w(); };  // 可选：facade 之外的资源在此覆盖
}
```

| 扩展点 | 注册内容 | 消费方 | 声明来源 |
|---|---|---|---|
| Page | 页面**渲染器** `{name, component}` | shell `renderPage` | 页面**定义**（标题/路由）是 Go 侧组合声明（`ui-page` 组件 / `RegisterPage`） |
| Panel | 面板渲染器 `{name, component}` | shell 面板槽位 | 面板定义同上（Go 侧声明） |
| Widget | 可组合组件 `registerWidget(name, component)` | 内置 `widget` 视图块：`{kind:"widget", params:{name, ...}}`，params 走 `$focus` 解析 | 组件在插件模块内，引用在页面声明里 |
| Settings | 本插件的设置界面 `settings.register(component)` | Explorer 详情区：有注册用之，否则回落结构化 ConfigEditor | 绑定插件模块名 |
| Action | 上下文操作 `{id, title, when?, run}` | shell 页面卡片上方的「上下文操作」下拉，`when({ctx, page})` 过滤 | 组件在插件模块内 |

**为什么 `api.pages.register` 注册的是渲染器而不是页面定义**：页面定义
（id/标题/路由/顺序）是组合声明，声明权威在 Go 侧运行时（铁律 1/7）。
前端若能声明页面，控制台就成了第二个生命周期权威——启停、依赖撤销、
声明校验都会出现两套真相。所以前端只提供渲染器，定义经 `ui-page`
组件声明（声明存储里的一行，enabled 开关即启停）。

`when`/`run` 收到的 `ctx` 与声明式视图同源（hub 查询/命令、focus、busy），
`run` 返回 Promise 时 shell 跟踪其完成但不阻塞 UI。

## 插件业务单元（follows）与坏清单（plugin-error）

一个 manifest 插件展开成后端行 + 前端模块行 + 页面行，它们必须**同进退**，
但不能引入第二个生命周期权威（铁律 7）。机制是 Runtime 自己的依赖门控：

- 后端组件（proc-plugin）Provide `console:plugin-alive/<id>`（声明激活标记，
  Apply 顶端提供——语义是"声明活着"，不是"进程健康"）；
- 有后端的插件，发现层给其 `ui-client`/`ui-page` 行加 `follows = <插件名>`，
  这些组件的 Inject 因此多一条依赖；
- **停用/移除后端** → 能力退休 → UI 行按消费优先规则 Active→Pending，
  manifest 与页面同步消失；**重新启用** → 依赖再满足 → 自动重激活。
  无轮询、无手工编排，撤销方向天然干净（UI 行的 Apply 从未执行，贡献为零）。
- 降级路径不级联：后端启动失败或进程异常退出时 fiber 仍 Active、marker 仍
  在——UI 保留可见、hub 词汇诚实报 plugin_error、`plugin.exited` 观察上报。
  让"进程死亡"撤销页面会把瞬时故障变成整插件消失，比可见的错误更难诊断，
  且崩溃不重跑 Apply、无法用声明机制表达——死亡不是生命周期事件（铁律 12
  合作假设的边界）。
- 纯前端插件（无 backend）**不得**带 follows：提供者永不存在，UI 行会
  永远 Pending（无超时、无错误——`TestDependencyPendingUntilProvider` 钉
  的行为）。发现层天然不会加上这条。

坏清单：manifest 存在但不可读/解析失败/名不副实时，发现层产出
`plugin-error` 行（Apply 即 raise → Failed 带原因，explorer 可见），绝不
静默跳过——坏配置不等于操作员卸载（铁律 6），而静默跳过会让下一次调和
把插件的既有组件**整体撤销**。修复 manifest = 声明修正 → 行被替换 →
自动重装（铁律 5 的合法恢复路径）。

## 安全边界（授权接缝）

前端的一切可见性控制都只是展示：**`Action.when` 过滤、菜单隐藏、渲染器
缺失都不撤销底层能力**——`/api/command/*`、`/api/plugins/install|uninstall|
control`、配置写入始终由服务端决定。控制台保持"无鉴权开发姿态"
（用户手册 §13），UI-03 提供正式接缝让**应用**（而非框架）落策略：

```go
srv.SetAccessPolicy(func(ctx context.Context, req webui.AccessRequest) error {
    if req.Kind == webui.AccessCommand && dangerous(req.Name) && !isAdmin(ctx, req.Request) {
        return fmt.Errorf("operation %q requires admin", req.Name)
    }
    return nil // 默认允许 = 开发姿态不变
})
```

- 四类门：`query` / `command` / `lifecycle` / `config`；拒绝 → `403
  forbidden`，前端经 hubError/块内错误如实显示。
- `nil` policy = 全允许——接缝存在是事实，默认姿态不变也是事实；把接缝
  当成安全边界用（部署时不设 policy 就暴露公网）是错误用法，文档负责
  说清这一点。
- 接缝在传输层，不是内核 capability 拦截——论文 §6.3 的 provider 按元
  数据裁决（Def 26/27）仍归 P3/ADR-0002，未因本接缝提前。

## 一致性契约（中间态与收敛）

组合更新期间页面与模块**允许短暂不匹配**——Go 侧 remove-then-add、每个
fiber 独立转换、SSE 无状态载荷，前端两次 fetch 也存在先后。UI-03 的整改
不是伪造原子性，而是让中间态**诚实可见**并修掉真实的错误路径：

- **Manager 可观察调和态**：`clientModuleManagerState().reconciling` +
  `subscribeClientModulesState`（useSyncExternalStore 友好）。调和进行中
  渲染器缺失显示"插件模块加载中"，调和结束仍缺失才显示"未注册"错误——
  未就绪 ≠ 失败（铁律 4 的展示面推论）。
- **Go 侧 replace 不再孤儿化**：声明调和里 replacement 的旧侧撤销未确认
  Gone 时，新侧**不加载**（`extensions/config`，C21 回归测试锚定）——旧
  fiber 的清理句柄不可能被丢弃，Applied 不撒谎；下一次 Reconcile 完整重
  试整个替换。
- **收敛保证**：reconcile 按期望态 diff，不依赖事件顺序；相同期望集重复
  reconcile 是 no-op。刷新页面 = 重新引导 = 全新调和（铁律 9），因此任何
  前端中间态 bug 都不可能持久化。
- **proc 后端异常退出可观察**：进程意外死亡发布 `plugin.exited` 观察
  （fiber 保持 Active、注册保留——降级不阻断）；恢复走操作员开关
  （disable/enable），平台不自作重启（铁律 5）。
- **跨模块覆盖可见且可恢复（影子栈）**：插件模块经 facade 覆盖**其他模块**
  已注册的同名扩展点键时 console 告警点名双方模块；同模块重启（先 dispose
  再注册）不告警。2026-11 起所有 keyed 注册表为**影子栈**：覆盖是遮蔽而非
  替换——禁用遮蔽模块时自动**恢复**被遮蔽模块的贡献；被遮蔽模块自身的
  dispose 在其遮蔽期间是 no-op（不可观测、不 bump），但会真正移除其条目，
  死模块的贡献不会在后续弹栈中复活。async `register`（返回 Promise）受完整
  契约保护：激活等 Promise 落定后才落地，其返回 dispose 生效，await 后的
  异常照常回滚部分注册。

## 版本契约

三条互不混淆的版本线：

| 线 | 载体 | 语义 |
|---|---|---|
| 门面契约 | `globalThis.__CORDIS_CONSOLE.frameworkVersion`（`FACADE_VERSION`，当前 `"1"`） | Kit ↔ 控制台的 API 面；major 对齐，插件可在 register 特性检测 |
| 模块内容修订 | manifest URL `?v=<sha256 前 8 hex>` | ui.js 字节变化 → 会话内热重导入；与契约版本无关 |
| proc 协议 | 握手串 `GORIDIS-PROC-PLUGIN 1` | 后端 RPC 线协议版本 |

plugin-kit 的 shims 在门面缺失时抛出可读错误（bundle 必须被控制台加载），
不再是对 `undefined` 的属性访问崩溃。manifest.toml 尚未有插件语义版本字段
（记为已知边界，升级兼容策略等真实需要时再定，避免空转字段）。

## 驱动机制

- 期望集的唯一来源是 manifest（`GET /api/ui/client-modules`），它由
  Composition Registry 派生——**磁盘上存在模块文件从不等于加载**。
- 驱动信号是观察流：`composition.changed` → `composition` domain 版本
  bump（250ms 合并）→ `App.tsx` 的 effect 重新 fetch pages/panels 并调用
  `reconcileClientModules()`。首次渲染前（`main.tsx`）也 reconcile 一次。
- reconcile 被串行化：并发调用排队执行，期望集相同的重跑是 no-op。

## 撤销顺序（与 Runtime 对照）

```
register(facade) 期间                    dispose（停用 / URL 变更）
  ├─ facade.register* → 记入撤销栈  ⇄   3. 撤销栈 LIFO（后注册先撤）
  └─ return dispose                ⇄   1. 返回的 dispose 最先执行
                                          （= Apply 返回的 Cleanup：
                                            "最后一个 effect，最先撤销"）
```

## 验收对照（Iteration UI-01）

| 验收标准 | 保证机制 | 测试 |
|---|---|---|
| 插件启动两次，不产生重复注册 | 期望集相同 → no-op diff | `unchanged desired set does not re-register` |
| 插件停止后，所有对应 UI Contribution 消失 | dispose = moduleDispose + LIFO | `disable disposes everything` |
| 插件重新启动，页面可以恢复 | 重跑 register（模块体走缓存） | `re-enable re-registers on the cached module` |
| 一个模块加载失败，不影响其他模块 | per-module try/catch + 回滚 | `failing mid-register rolls back…` / `one broken module never blocks` |
| 部分注册失败，可回滚已注册资源 | 激活不落地 + 已入栈 dispose 全执行 | 同上 |
| 连续启停 100 次，无持续增长 | 每次停用全额撤销 | `100 enable/disable cycles leave no residue` |
| TypeScript 检查、单元测试通过 | CI 门禁 | `npx tsc --noEmit && npm test` |

## 相关代码

- `web/console/src/lib/client-modules.ts` — Module Manager（reconcile / activate / unwind）
- `web/console/src/views/registry.tsx` — keyed 渲染器注册与 dispose
- `web/console/src/shell/App.tsx` — composition domain 驱动 reconcile
- `extensions/console/host/clientmodule.go` — 声明层 ui-client 组件
- `extensions/console/webui/server.go` — manifest（含 `?v=` 内容修订）与模块文件服务
- 测试：`web/console/src/lib/client-modules.test.ts`、
  `extensions/console/webui/client_modules_test.go`
