# Config Watch Adapter (v0.1)

把外部配置文件的变化（由 Watch Extension 观察）转成 Config Controller 的
`Reconcile` 请求。

## 1. Adapter 的职责

只做一件事：

```text
Watch Change -> 重新读取 Source -> 解析 -> Reconcile(Config)
```

- 复用 `extensions/watch`、`extensions/config`、`runtime`，不复制其语义。
- **不拥有** Component 生命周期 / Config Applied 状态。
- 从不调用 `Runtime.Load` / `Fiber.Dispose`，从不实现 HMR，从不自己 merge 多个
  Source（一个 Adapter = 一个 Source = 一个 Config Controller）。

## 2. Source → Watch → Adapter → Config → Runtime

```text
config.toml
   └─(native Watch)─> Change
                        └─> Adapter(单个处理循环)
                              ├─ Read（重新读当前文件）
                              ├─ Parse（TOML -> config.Config）
                              └─ Config.Controller.Reconcile
                                        └─ Factory -> Component -> Runtime Fiber
```

关键点：

- Change 只是"外部可能变了"的信号，不是配置本身 —— Adapter 始终读取
  **latest external state**。
- 组件最终是否真的增删/替换，由 Config Controller 的 Applied equality 决定。

## 3. Initial Sync

`New` 时**先订阅 Watch**，再调用 `Sync(ctx)` 同步当前状态（避免"先同步再订阅"
导致窗口期变化丢失）。之后 `Run(ctx)` 消费 Change。

```go
adapter, err := configwatch.New(source, controller, fileWatcher)
err = adapter.Sync(ctx)     // 初始同步
go adapter.Run(ctx)         // 处理后续变化（串行、latest-state）
```

## 4. Delete semantics

Source 被删除（Reader 返回 `ErrSourceNotFound`）=> 该 Source 的期望配置为空：

```text
Config.Reconcile(config.Config{})
```

只影响本 Adapter 绑定的 Config Controller 拥有的组件，绝不删除其它 Controller
或外部 Fiber。文件重新创建后照常进入新配置（全新 activation，不复用旧 Fiber）。

## 5. Error semantics

- 读取失败 / 解析失败 / 配置非法：**Applied 与 Runtime 不变**，错误作为一次
  failed attempt 记录（`Stats.LastError` / 返回值）。
- 错误分层：Source 读取错误（Adapter）≠ 配置错误（Reconcile）≠ Runtime 组件
  失败（Fiber 状态）。Adapter 不把任何一层伪装成自己的 Fiber 状态。

## 6. Close semantics

`Close()` / `CloseContext(ctx)`：幂等；停止接受新工作；当前一次处理允许跑完；
关闭 Watch 订阅；等待内部 goroutine。Config Controller 不属于 Adapter，不关闭。
超时返回 `ctx.Err()`，不谎报已 Closed。

## 7. 为什么 Adapter 不直接调用 Runtime.Load

组件生命周期、依赖、Provider 语义只属于 Runtime/Config Controller。如果
Adapter 直接 `Runtime.Load`，就会绕过 `Desired/Applied` 与生命周期决策，产生
第二套状态。Adapter 的终点就是 `controller.Reconcile(desired)`。

## 8. 为什么 Adapter 不实现 HMR

配置文件里 `type/config` 的变化会经 Config Reconcile 按其既有 replacement
语义处理（新 Fiber 替换旧 Fiber）。真正的"代码实现热替换"属于另一条
Loader+HMR pipeline，由上层显式编排，Adapter 不自动触发。

## 解析器

默认 Parser 由完整 TOML 实现支持（`github.com/pelletier/go-toml/v2`），映射为
`config.Config`（`[[components]]` + `[components.config]`）；TOML 语法错误或
结构越界（非 components 的顶层表/键、非数组 components、缺 id/type）都会作为
解析错误隔离，不改动 applied 状态。
