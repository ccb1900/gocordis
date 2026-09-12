# gocordis 用户手册

> **读者**:使用 gocordis 开发插件化应用的工程师。
> **性质**:任务导向的实操手册;所有 API 均以当前代码为准(代码为第一性参照),
> 论文 *A Programming Paradigm for Spatiotemporal Composability*(`docs/论文.pdf`)
> 是语义的最终裁决来源。
> **配套**:框架内部架构与贡献流程见《开发者手册》(`developer-manual.md`)。

---

## 目录

1. [gocordis 是什么](#1)
2. [安装与模块布局](#2)
3. [核心概念(10 分钟)](#3)
4. [第一个组件](#4)
5. [能力与依赖](#5)
6. [生命周期与错误语义](#6)
7. [外部资源绑定(ctx.Effect)](#7)
8. [插件开关与声明存储](#8)
9. [事件](#9)
10. [隔离、作用域与命名空间迁移](#10)
11. [拦截与元数据](#11)
12. [多实现复用(broker)](#12)
13. [控制台(extensions/console)](#13)
14. [可观测性](#14)
15. [测试](#15)
16. [边界与最佳实践](#16)
17. [示例索引](#17)
18. [故障排查](#18)

---

## <a id="1"></a>1. gocordis 是什么

gocordis 是论文 *A Programming Paradigm for Spatiotemporal Composability* 的 Go
实现:一个**动态组合运行时**——组件可以在运行期被安全地装载、卸载、替换、重配,
同时保证:

- **时间可组合**:卸载组件时,它对环境的一切修改被完整、有序地回滚;
- **空间可组合**:组件声明依赖,Runtime 反应式地解析、激活、去激活。

仓库分四层:

```
runtime/       内核:组件模型、生命周期、能力解析、事件流(论文 §2–§5 语义)
extensions/    平台能力:config/loader/hmr/event/registry/scheduler/broker/
               observe/console/... 每个都是独立包
cmd/           可运行示例(collector/backfill/httpd/procplugindemo/wasmhmr)
integration/   跨包组合验收测试
```

---

## <a id="2"></a>2. 安装与模块布局

```bash
go get dynamic-runtime
```

要求 Go ≥ 1.24(运行时)。可选依赖按需引入:wazero(WASM 插件)、fsnotify(配置监听)。

应用的项目结构建议(两部分:应用本体 + 插件)见 `AGENTS.md` §2;最小布局:

```
myapp/
├── go.mod
├── manifest.toml            # 声明存储:插件开关 + 配置(持久化在这里)
├── cmd/myapp/main.go        # 组合根
├── internal/contracts/      # 契约层:key 定义 + 接口(应用与插件唯一共享物)
├── internal/app/            # 应用本体组件
└── plugins/                 # 插件(只 import contracts)
```

---

## <a id="3"></a>3. 核心概念(10 分钟)

| 概念 | 是什么 | 在代码里 |
|---|---|---|
| **Component** | 组件的*定义*:声明需要什么、提供什么、启动逻辑 | 实现 `runtime.Component` 接口 |
| **Fiber** | 组件的*运行实例*,由 Runtime 拥有 | `rt.Load(comp) → *runtime.Fiber` |
| **能力(Capability)** | 类型化的服务槽位,同命名空间内一 key 一提供者 | `runtime.NewKey[T](name)` |
| **依赖(Dependency)** | 组件声明"我需要某能力";满足才激活 | `runtime.Requires(key)` |
| **调和(Reconcile)** | Runtime 持续把实例驱动向"声明要求的状态" | 控制器 `Reconcile` / 内核 sweep |
| **Effect** | 带逆操作的可追踪副作用;卸载时按 LIFO 回滚 | `ctx.Effect(...)` |
| **Entry 与 enablement** | 声明条目是存活的身份;fiber 是一次启停 | `Enabled` 开关 / `Dispose`/`Load` |

一个应用 = 一组组件 + 一份声明。**你不写启动顺序**——声明满足即触发。

---

## <a id="4"></a>4. 第一个组件

```go
package main

import (
    "context"
    "fmt"
    "time"

    "dynamic-runtime/runtime"
)

// 类型化 key:能力身份 = (Go 类型, 名字)。
var greeting = runtime.NewKey[string]("app.greeting")

// Greeter 提供问候语。
type Greeter struct{}

func (c *Greeter) Name() string                 { return "greeter" }
func (c *Greeter) Inject() []runtime.Dependency { return nil }
func (c *Greeter) Provide() []runtime.Capability {
    return []runtime.Capability{greeting.Capability()}
}
func (c *Greeter) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
    // Provide 是可逆 effect:激活撤销时自动注销。
    return nil, runtime.Provide(ctx, greeting, "hello")
}

// Printer 依赖问候语。依赖不满足时它保持 Pending,满足后自动激活。
type Printer struct{}

func (c *Printer) Name() string { return "printer" }
func (c *Printer) Inject() []runtime.Dependency {
    return []runtime.Dependency{runtime.Requires(greeting)}
}
func (c *Printer) Provide() []runtime.Capability { return nil }
func (c *Printer) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
    v, err := runtime.Require(ctx, greeting)
    if err != nil {
        return nil, err
    }
    fmt.Println(v)
    return nil, nil
}

func main() {
    rt, err := runtime.New()
    if err != nil {
        panic(err)
    }
    _, _ = rt.Load(&Greeter{})
    pf, err := rt.Load(&Printer{}) // 不需要等待 Greeter:门控自动完成
    if err != nil {
        panic(err)
    }
    ctx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
    defer cancel()
    if err := pf.Ready(ctx); err != nil { // Ready:Active/Failed/Gone 三态返回
        panic(err)
    }
    _ = rt.Close(ctx)
}
```

**要点**:

- `rt.Load(comp)` 表达意图(幂等),状态迁移由 Runtime 内部串行决策域完成;
- 消费者不等待提供者——两个都 `Load`,门控自动发生;
- `Provide` 也是可逆 effect:组件卸载时自动注销。

---

## <a id="5"></a>5. 能力与依赖

```go
var db = runtime.NewKey[*sql.DB]("app.db")

// 声明
func (c *Svc) Inject() []runtime.Dependency {
    return []runtime.Dependency{runtime.Requires(db)}
}
// 使用(仅限声明过的 key;未声明直接报错)
func (c *Svc) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
    conn, err := runtime.Require(ctx, db)
    ...
}
```

规则:

- **一个 key,一个提供者**(同一命名空间内);第二个 `Provide` 同 key 会得到
  `ErrDuplicateProvider`,需要多实现共存用 broker(§12);
- **取值时机**:在 `Apply`/`Cleanup` 里 `Require`;激活期间依赖被撤走,你的
  激活会被撤销(依赖消失≠失败,见 §6);
- 能力背后的**接口**放在应用的 `internal/contracts` 包,插件与应用只共享契约。

---

## <a id="6"></a>6. 生命周期与错误语义

| 情形 | Runtime 行为 | 你需要注意 |
|---|---|---|
| 声明满足 | Pending → Loading → **Active** | `Apply` 成功返回 |
| 依赖中途消失 | 撤销已装 effect → **Pending** | 不是失败;依赖恢复自动重新激活 |
| `Apply` 返回 error / panic | 撤销已装部分 → **Failed** | 错误留在该 fiber(`f.Err()`);不向父传播;**不自动重试** |
| `Dispose()` | Unloading → **Gone** | effect 按 LIFO 恰好一次回滚 |
| Failed 后重试 | `Dispose` 清 outcome,再 `Load` | 声明式场景由控制器代劳 |
| `Runtime.Close` | 全部 fiber 有序撤销 → Gone | |

等待原语:`Ready`(等 Active/Failed/Gone)、`WaitInactive`(等当前激活周期
结束)、`Gone`(等终态)。随时可读 `State()`/`Err()`。

**给依赖消失的组件写检查点**:`Cleanup` 执行期间,声明的依赖仍然可
`Require`——把 flush/收尾写在这里,顺序由 Runtime 保证。

---

## <a id="7"></a>7. 外部资源绑定(ctx.Effect)

任何需要"启动—就绪—优雅停止"的外部资源(端口、连接池、文件句柄)都按同一
模式接入:

```go
func (c *Server) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
    ln, err := net.Listen("tcp", c.addr)
    if err != nil {
        return nil, err
    }
    srv := startServing(ln) // 就绪探测通过后才算"在服务"

    // 注册可逆 effect:激活撤销时,优雅关停先于 fiber 终态执行。
    if err := ctx.Effect(func() (func() error, error) {
        return func() error { return gracefulStop(srv) }, nil
    }); err != nil {
        return nil, err
    }
    return nil, nil
}
```

两条不变式:**Active ⇔ 资源真实就绪**;**撤销先于终态**。完整样例:
`cmd/httpd`(HTTP server)、`cmd/backfill`(断电安全 journal)。

**信任边界**:只有经 `ctx.Effect`/`Provide` 注册的后果被追踪;你自己开的
goroutine、直接写的文件,Runtime 既不追踪也不回滚。

---

## <a id="8"></a>8. 插件开关与声明存储

真实应用不要手写 `Load` 顺序,用**声明 → 控制器 → 调和**:

```go
reg := config.NewFactoryRegistry()
_ = reg.Register("greeter", config.FactoryFunc(func(cc config.ComponentConfig) (runtime.Component, error) {
    return &Greeter{}, nil
}))
ctrl := config.NewController(rt, reg)

_ = ctrl.Reconcile(ctx, config.Config{Components: []config.ComponentConfig{
    {ID: "greeter", Type: "greeter"},
    {ID: "printer", Type: "printer", Enabled: boolPtr(false)}, // 插件开关
}})
```

- **`Enabled *bool`**(nil = 启用)是声明的一部分,持久化在声明存储里;
  disabled 的条目**仍然是被校验过的身份**,只是没有运行中的 fiber;
- 翻动开关 → 重新 `Reconcile` → Runtime 自动装载/卸载;重启后从声明存储
  重放,状态原样恢复;
- 接上文件监听,即可"改文件即启停":

```go
fw := watch.NewFileWatcher()
adapter, err := configwatch.New(configwatch.Source{
    ID: "app", Path: "manifest.toml", Format: configwatch.FormatTOML,
}, ctrl, fw)
_ = adapter.Sync(ctx)  // 启动同步
go adapter.Run(ctx)    // 之后文件变更实时调和
```

manifest.toml 形态:

```toml
[[components]]
id = "greeter"
type = "greeter"

[[components]]
id = "printer"
type = "printer"
enabled = false

[components.config]
greeting = "hello"
```

**持久化三分**(立场):声明(开关/配置)→ 声明存储;业务成果 → 组件层
journal + 幂等重放(`cmd/backfill` 样例);**runtime 内部状态永不持久化**。

---

## <a id="9"></a>9. 事件

插件定义事件用类型化 `EventKey`;处理器注册是**可逆 effect**(随激活撤销)。
派发模式在 `extensions/event`:

```go
var evt = runtime.NewEventKey[string]("app.config.changed")

// 组件内注册:
_ = runtime.On(ctx, evt, func(_ context.Context, payload string) error {
    log.Println("changed:", payload)
    return nil
})

// 派发(五种模式):
_ = event.Emit(ctx, evt, "v2")                            // 全体可见 handler,错误聚合
_ = event.Serial(context.Background(), ctx, evt, "v2")    // 严格顺序
_ = event.Parallel(context.Background(), ctx, evt, "v2")  // 并发执行,按注册序聚合
_ = event.Bail(context.Background(), ctx, evt, "v2")      // 首错即停
_ = event.Waterfall(context.Background(), ctx, evt, "v2") // 中间件链
```

作用域:handler 的可见性由注册者的命名空间路径决定(子 scope 的 handler 只有
本 scope 及后代能触发)。

---

## <a id="10"></a>10. 隔离、作用域与命名空间迁移

- `ctx.Child(comp)`:子组件,继承父的命名空间;父卸载级联子卸载;
- `ctx.Child(comp, runtime.WithScope())`:子组件获得**独立命名空间**——兄弟
  scope 互不可见,解析永不回落到父;
- `ctx.Child(comp, runtime.Isolate(k1, k2))`:**只把列出的 key** 放进新命名
  空间,其余 key 仍与父共享(逐 key 隔离 + 共享);
- 运行期迁移命名空间而不重载提供者:
  `f.Rehome(ctx, runtime.RehomeWithFreshIsolation())` ——提供者不下线,旧命名
  空间的消费者撤到 Pending(它们的声明未变),新命名空间的消费者自动绑定。

---

## <a id="11"></a>11. 拦截与元数据

- **值变换**(读取时改值,可逆,不动依赖图):
  `runtime.Intercept(ctx, key, func(value T, key Key[T]) (T, error))`;
- **元数据拦截**(策略/访问控制):key 携带元数据 monoid,provider 是解释器,
  拦截合并元数据、不改变解析目标、不触发 reload:

```go
var fsKey = runtime.NewMetaKey[string, fsMeta]("fs", fsZero, mergeFS)

// 提供者:解释元数据
_ = runtime.ProvideMeta(ctx, fsKey, func(mu fsMeta) string { return render(mu) })

// 消费者:声明元数据(声明不参与满足性判定)
runtime.RequiresMeta(fsKey, fsDeclared("consumer", "/data"))

// 上下文收紧(可运行时增删,不触发 reload):
_ = runtime.InterceptMeta(ctx, fsKey, fsMeta{write: false})
```

---

## <a id="12"></a>12. 多实现复用(broker)

同一能力需要多个提供者共存(负载均衡、滚动升级)时用
`extensions/broker`,不要自造注册表:

```go
b := broker.New[Codec](nil) // RoundRobin;或 broker.First[Codec]()
_ = broker.Register(ctx, b, "impl-v2", codecV2) // 可逆注册:卸载自动摘除
_ = b.Call(func(c Codec) error { ... })          // 消费者依赖稳定的 broker 能力
```

---

## <a id="13"></a>13. 控制台(extensions/console)

控制台是**给其他应用用的通用实现**:UI host(`UIHostKey`)、hub(命名查询/
命令 + 观测)、页面注册表、explorer(插件巡检与启停)、webui(HTTP + SSE 传输,
`go:embed` 前端)。

最小装配:

```go
_, _ = rt.Load(&host.UIComponent{})   // 宿主:注册表 + hub 能力
srv := webui.New(hostAdapter, assets) // HTTP+SSE 传输(go:embed 前端)
http.ListenAndServe(":8080", srv)
```

- 应用通过 `hub.RegisterQuery/RegisterCommand` 注册领域查询/命令(可逆);
- 控制台**没有鉴权**(开发姿态)——暴露到网络前请自行加反代/中间件;
- 插件启停走声明(`PluginLifecycle`/`Enabled`),由应用决定如何持久化;
  参考实现:`console.ControllerLifecycle`(声明存储 + 控制器调和驱动)。

---

## <a id="14"></a>14. 可观测性

- **快照**:`snap, _ := rt.Snapshot(ctx)` —— fibers/scopes/providers/
  dependencies/effects 全量只读投影 + `EventSequence`(线性化锚点);
- **事件流**:`sub, _ := obs.Subscribe(ctx, snap.EventSequence)` —— 无缝续传
  的增量事件流。接线:构造 runtime 时挂观察器
  `runtime.WithEventSink(events.NewObserver())`(`extensions/observe` 提供订阅面(`events.NewObserver()`));
- 组件内不要 print 状态机——排障用快照与事件流。

---

## <a id="15"></a>15. 测试

- 组件测试:普通 `runtime.New()` + `Load` + `Ready` + 断言状态/副作用;
- 三个必测场景:① Active 行为;② 依赖消失 → Pending → 恢复;③ Dispose 后
  资源确实消失(端口/句柄);
- 长激活(`ApplyIter`)必须覆盖 divert 路径(`yield` 返回
  `runtime.ErrDiverted`);
- 并发相关:`go test -race`;
- 仓库自身的定理级证据(Thm 64/68/70/73/80)见 `docs/theorem-verification.md`
  与 `docs/review/`。

---

## <a id="16"></a>16. 边界与最佳实践

1. **信任边界**:仅托管 effect 可逆;外部写入的可逆性不在保证范围;
2. **合作式假设**:Apply/Cleanup 有限步、可等待(`ctx.Done()`)、依赖图无环;
3. **单一生命周期权威**:任何代码不得绕过公开 API 改 fiber 状态;
4. **契约层**:key/事件/接口定义在 contracts 包;插件与应用互不 import;
5. 禁止清单(手写启动顺序、sleep 等依赖、全局单例、自旋重试……)见
   `AGENTS.md` §6。

---

## <a id="17"></a>17. 示例索引

| 示例 | 演示 |
|---|---|
| `cmd/collector` | 配置驱动替换、失败隔离 |
| `cmd/backfill` | 断电安全 journal + 幂等重放 |
| `cmd/httpd` | 外部资源绑定模式(Active ⇔ serving) |
| `cmd/procplugindemo` | 进程外插件:独立进程 + JSON-RPC,manifest 开关 |
| `cmd/wasmhmr` | WASM 插件热替换 |
| `cmd/host`、`cmd/example` | 最小装配 |

---

## <a id="18"></a>18. 故障排查

| 症状 | 原因 | 处理 |
|---|---|---|
| `duplicate provider` | 同命名空间已有同 key 提供者 | 检查是否两个组件声明了同 key;多实现用 broker |
| `unknown component type` | manifest 的 `type` 没有注册工厂 | 组合根 `reg.Register(type, factory)` |
| 组件卡 Pending | 依赖未满足 | `rt.Snapshot` 看 Dependencies 行的 Waiting 原因 |
| `UI contribution owner is empty` | 注册页面时 ContributionOwner 缺字段 | 填 PluginID/ComponentID/ActivationID |
| `sequence predates the retained ring` | 事件锚点早于保留环形 | 重新 `Snapshot` 换新锚点 |
| 进程外插件握手失败 | 可执行文件未输出握手头 | 确认插件用 `proc.Serve` 且 stdout 无其他输出 |
