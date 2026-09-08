> ⚠️ **ARCHIVED（已归档）— 2026-09-08**
>
> 本文档已整体归档，仅作历史参考，**不再作为规范来源或引用依据**。
> 本仓库现以论文原文为唯一规范来源：`docs/论文.pdf`
> （A Programming Paradigm for Spatiotemporal Composability）。
> 当前路线图与论文↔代码对照见 `docs/ROADMAP.md`。
>
> This document is archived for historical reference only and is no longer cited
> as authority. The paper is the sole normative source; see `docs/ROADMAP.md`.

# Dynamic Composable Runtime — Plugin Development Guide v0.1

> 本文面向「要在这个框架里写插件」的开发者。
> 它参考 DeepSeek Harness 的 *Your first plugin*（https://deepseek-harness.github.io/deepseek-harness/en/develop/basic/）教程叙事，
> 但内容完全基于本仓库（module `dynamic-runtime`）当前真实存在的 API 与示例：
> `runtime/`（Kernel）、`extensions/config`、`extensions/loader`、`extensions/hmr`、`extensions/http`，
> 以及 `cmd/example`、`cmd/host`、`cmd/collector`、`cmd/wasmhmr` 四个可运行程序。
> 文中所有接口签名、状态名、构造方法都来自仓库源码；片段尽量与真实示例逐字一致。

---

## 0. 参考教程与本仓库的对应关系

DeepSeek Harness 里「插件 = 一个导出 `apply(ctx)` 的 TS 模块」，本仓库没有这种隐式模块，
但它把同一套思想映射到了 Go 类型上：

| DeepSeek Harness 教程概念 | 本仓库（go-cordis / dynamic-runtime）的对应物 |
| --- | --- |
| 插件模块导出 `apply(ctx)` | 实现 `runtime.Component` 接口的普通 Go 类型，核心是 `Apply(ctx) (runtime.Cleanup, error)` |
| 在 `cordis.yml` 里注册插件 | 把一条 `config.ComponentConfig{ID, Type, Config}`（manifest）交给 `config.Controller.Reconcile`，或直接 `rt.Load(comp)` |
| 框架加载插件并调用 `apply` | Kernel 为每个 Component 创建 Fiber，在依赖满足后执行一次 `Apply`（一次 Activation） |
| 通过 `ctx` 注册的能力在卸载时自动清理 | `ctx.Effect(...)` 登记的逆操作 / `Apply` 返回的 `Cleanup`，在 Unloading 时按 LIFO 逆序执行恰好一次 |
| `inject: ['tools']` 声明依赖 | `Inject() []runtime.Dependency` + `runtime.Requires(key)`；框架先等依赖就绪才执行 `Apply` |
| 提供 `Service` 给其他插件 | `Provide() []runtime.Capability` 声明 + `Apply` 内 `runtime.Provide(ctx, key, value)` 注册 typed capability |
| `ctx.effect(() => () => disposer)` | `ctx.Effect(func() (func() error, error))`；单一资源也可直接用 `Apply` 的返回值 |

下面按教程的节奏走一遍：最小插件 → 交给框架 → 自动清理 → 依赖注入 → 替换。

---

## 1. 什么是插件（一句话模型）

**插件 = 一个 `runtime.Component` + 一个把它从配置造出来的 `config.Factory`。**

- `Component` 是**声明**，不是运行实例。它只描述：叫什么、需要什么、能提供什么、激活时做什么。
- 框架（Runtime Kernel + Config/Controller + Loader/HMR）负责把声明变成 Fiber，
  按依赖顺序驱动它的 Activation，并在配置变化/依赖丢失时自动卸载与替换。
- 你写的 `Apply` 才是**执行主体**：打开文件、解析 CSV、监听端口、启动 goroutine……
  这些业务动作都在你的代码里；框架不替你做事，只替你管生命周期与依赖关系。

所以之前那个问题的答案是：**不是「写个插件就完了」，而是「插件本身就是执行主体」——
你要在 `Apply` 里做真实工作，并只通过 `ctx` 把副作用登记给框架管理。**

一个 Component 只有 4 个方法：

```go
// runtime/component.go
type Component interface {
    Name() string                         // 仅用于诊断/日志，不是能力身份
    Inject() []Dependency                 // 声明需要的能力
    Provide() []Capability                // 声明可能提供的能力
    Apply(ctx *Context) (Cleanup, error)  // 一次激活：做真实工作
}
```

---

## 2. 最小插件：hello

新建一个 Go 包，实现上面 4 个方法即可。这是完整可运行的「最小插件 + 直接加载」示例
（结构对应教程的 *Create the plugin file* + *Register it* 两步）：

```go
package main

import (
	"context"
	"fmt"
	"time"

	"dynamic-runtime/runtime"
)

// helloComp 是最小插件：没有依赖、不提供能力，只在激活时打印一行。
type helloComp struct{ id string }

func (c *helloComp) Name() string                  { return "hello:" + c.id }
func (c *helloComp) Inject() []runtime.Dependency  { return nil }
func (c *helloComp) Provide() []runtime.Capability { return nil }
func (c *helloComp) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	fmt.Printf("[hello %s] plugin loaded!\n", c.id)
	return nil, nil
}

func main() {
	rt, err := runtime.New() // 创建 Runtime（Kernel 入口）
	if err != nil {
		panic(err)
	}
	defer rt.Close(context.Background())

	// Load 是异步的：它把 Component 交给 Kernel，由 Kernel 驱动生命周期。
	f, err := rt.Load(&helloComp{id: "demo"})
	if err != nil {
		panic(err)
	}

	ctx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
	defer cancel()
	// Ready 阻塞直到 Fiber 进入 Active（或返回 Apply 的错误）。
	if err := f.Ready(ctx); err != nil {
		panic(err)
	}
	fmt.Printf("fiber %s state=%s\n", f.ID(), f.State())

	// 卸载：Dispose 表达 Intent=Unmounted；Gone 等待它真正退出。
	if err := f.Dispose(); err != nil {
		panic(err)
	}
	if err := f.Gone(ctx); err != nil {
		panic(err)
	}
	fmt.Printf("after dispose state=%s\n", f.State())
}
```

运行输出：

```text
[hello demo] plugin loaded!
fiber fiber:1 state=Active
after dispose state=Gone
```

要点：

- `runtime.New()` 创建并启动 Runtime；`rt.Load(comp)` 返回 `*runtime.Fiber`。
- `Load` 只是「表达意图」，不等待结果；等待用 `f.Ready(ctx)`。
- 不需要你自己维护状态机：`Apply` 成功 → `Active`；`Dispose` → `Unloading` → `Gone`。

---

## 3. 把插件交给框架：manifest + Config Controller（推荐）

真实程序不会手写 `rt.Load`，而是「声明期望状态（manifest），让 Controller 对账」。
参考 `cmd/host/main.go` 与 `cmd/collector/main.go`，需要三样东西：

1. **FactoryRegistry**：`type -> Factory` 的注册表，`Factory.Create(ComponentConfig)` 把配置变成 Component。
2. **manifest**：一组 `config.ComponentConfig{ID, Type, Config}`，`ID` 是稳定逻辑身份，`Type` 选 Factory，`Config` 是业务配置。
3. **Controller**：`config.NewController(rt, reg)`，`ctrl.Reconcile(ctx, cfg)` 做 diff + 最小生命周期操作。

```go
import (
	"dynamic-runtime/extensions/config"
	"dynamic-runtime/runtime"
)

// Factory 适配器：把「从 ComponentConfig 造 Component 的函数」变成 config.Factory。
type adapterFactory struct {
	build func(config.ComponentConfig) (runtime.Component, error)
}

func (a *adapterFactory) Create(cc config.ComponentConfig) (runtime.Component, error) {
	return a.build(cc)
}

// 1) 注册 Factory：每种 Type 一条。
reg := config.NewFactoryRegistry()
_ = reg.Register("hello", &adapterFactory{build: func(cc config.ComponentConfig) (runtime.Component, error) {
	return &helloComp{id: cc.ID}, nil
}})

// 2) manifest：一条期望状态。
desired := config.Config{Components: []config.ComponentConfig{
	{ID: "greeter", Type: "hello", Config: map[string]any{}},
}}

// 3) Controller 对账。
rt, _ := runtime.New()
ctrl := config.NewController(rt, reg)
ctx, cancel := context.WithTimeout(context.Background(), 15*time.Second)
defer cancel()
if err := ctrl.Reconcile(ctx, desired); err != nil {
	// 只有「结构性问题 / 工厂未知 / Load 被拒绝」会在这里返回。
}

// Reconcile 是异步的：等每个 owned 组件稳定到 Active。
for _, o := range ctrl.Owned() { // OwnedComponent{ID, Type, Config, Fiber}
	if err := o.Fiber.Ready(ctx); err != nil {
		// Apply 失败会在这里以 error 形式浮出（组件进入 Failed）。
	}
}
```

这里 `cc.Config` 是你的插件**业务配置的入口**：`collector` 的 `path`、`camera` 的 `mode`、
`http` 的 `address` 都从 `cc.Config` 读（见第 8、9 节）。Controller 保证：

- `ID` 唯一；`Type` 必须已注册；整份 Config 先校验，非法则不触碰 Runtime。
- 相同 `ID` 配置变化 = **替换**：先卸载旧 Fiber（等 Gone），再加载新 Fiber（见第 7 节）。
- `Reconcile` 返回 nil 只代表「已交给 Kernel」，不代表已 Active——失败在 Fiber 上异步发生。

---

## 4. 声明依赖、提供能力：typed key

能力（capability）是本框架的「依赖注入」单位，v0.1 里每个能力是**独占**的：
同一时刻至多一个 Active Provider。

先建 typed key（身份 = `T` 的类型 + name，与内存地址、注册顺序无关）：

```go
// 能力值：一个接口或结构体
type frame interface{ Name() string }

var framesKey = runtime.NewKey[frame]("frames")
```

**提供方**：`Provide()` 声明 + `Apply` 里 `runtime.Provide(ctx, key, value)` 注册：

```go
type cameraComp struct{ id, mode string }

func (c *cameraComp) Name() string                 { return "camera:" + c.id }
func (c *cameraComp) Inject() []runtime.Dependency { return nil }
func (c *cameraComp) Provide() []runtime.Capability {
	return []runtime.Capability{framesKey.Capability()}
}
func (c *cameraComp) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	if err := runtime.Provide(ctx, framesKey, frame(frameV{name: c.id + "[" + c.mode + "]"})); err != nil {
		return nil, err
	}
	return func() error { /* cleanup */ return nil }, nil
}
```

**消费方**：`Inject()` 声明需要 + `Apply` 里 `runtime.Require(ctx, key)` 取回值。
Kernel 保证：依赖未就绪时消费方**不会**进入 Loading/Active；依赖丢失时消费方自动卸载回 Pending。

```go
type recorderComp struct{ id string }

func (c *recorderComp) Name() string { return "recorder:" + c.id }
func (c *recorderComp) Inject() []runtime.Dependency {
	return []runtime.Dependency{runtime.Requires(framesKey)}
}
func (c *recorderComp) Provide() []runtime.Capability { return nil }
func (c *recorderComp) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	fr, err := runtime.Require(ctx, framesKey) // 依赖已就绪，必成功
	if err != nil {
		return nil, err
	}
	// ... 使用 fr ...
	return nil, nil
}
```

（上面两个片段取自 `cmd/host/main.go` 的 camera/recorder，略去了日志输出；完整代码直接读该文件。）

- `Provide()` / `Inject()` 在 Fiber 创建时被框架缓存，用于依赖图求解；**不要在方法里做有副作用的逻辑**。
- Provider 注册本身是一个可逆 Effect：Activation 卸载时自动注销，不需要手动 remove。

---

## 5. 自动清理：`ctx.Effect` 与 `Cleanup`

对应教程 *Automatic cleanup*：凡通过 `ctx` 登记的副作用，框架在卸载时自动逆序清理，
你**不需要**手动 remove/close，也不需要实现卸载逻辑。

两种写法：

1. 单一资源：`Apply` 直接返回 `Cleanup`（会被自动转成 activation 的最后一个 Effect，最先执行）：

```go
func (c *xComp) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	res := openResource(...)
	return func() error { return res.Close() }, nil
}
```

2. 多个独立资源：用 `ctx.Effect(install)`，每个 install 返回自己的逆操作：

```go
func (c *xComp) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	// Effect 1: 启动 HTTP server；逆操作 = 优雅停机
	if err := ctx.Effect(func() (func() error, error) {
		srv, err := startServer(cfg, c.handler)
		if err != nil {
			return nil, err
		}
		return func() error { return srv.stop(cfg) }, nil
	}); err != nil {
		return nil, err
	}
	// Effect 2（可选）: 注册 provider；逆操作 = 注销（runtime.Provide 内部就是 Effect）
	if c.provider {
		if err := runtime.Provide(ctx, ServerCapability, c.handle()); err != nil {
			return nil, err
		}
	}
	return nil, nil
}
```

（第二个片段来自 `extensions/http/component.go` 的 `Component.Apply`，是「真实外部资源严格遵循 Kernel 语义」的范本。）

语义要点：

- 逆操作按 LIFO 逆序执行，恰好一次；晚到的 Effect 若遇到已开始 Unwind，会立即自我撤销，绝不泄漏。
- Context 一旦开始撤销（withdrawing/cancelled），`ctx.Effect` / `ctx.Child` / `runtime.Provide` 会拒绝新工作并返回 `ErrContextClosed`。
- 千万别把清理逻辑散落在框架之外、或自己另搞一套卸载机制。

---

## 6. 生命周期状态与观察等待

Fiber 状态由 Kernel 独占驱动，你**只观察、不修改**：

| 状态 | 含义 |
| --- | --- |
| `StatePending` | Fiber 存在但激活条件不满足（如依赖缺失 / 刚 Mounted 还没轮到） |
| `StateLoading` | `Apply` 正在执行 |
| `StateActive` | 本次 Activation 成功，组件正在运行 |
| `StateUnloading` | 本次 Activation 正在撤销（cleanup 逆序执行） |
| `StateFailed` | `Apply` 返回错误，失败被隔离 |
| `StateGone` | 本次 Activation 已完全退出（Dispose 后的终态） |

观察等待 API（都在 `*runtime.Fiber` 上，都接受 `context.Context`）：

- `f.Ready(ctx)`：等待进入 `Active`；若进入 `Failed` 则返回 Apply 的错误（最常用）。
- `f.Gone(ctx)`：等待 Fiber 到达 `Gone`。
- `f.WaitInactive(ctx)`：等待**当前 Activation 周期结束**（cleanup 完成即返回）；
  若当前没有 live Activation 则立即返回。典型场景：依赖丢失后想等当前运行周期退出——
  `provider.Dispose(); consumer.WaitInactive(ctx)`，之后 consumer 可能是 `Pending`（这是正确的，不是 Gone）。
- `f.State()` / `f.Err()`：读当前快照。

**失败隔离**（参考 `cmd/collector/main.go` manifest v3）：`Reconcile` 成功 ≠ 组件 Active。
若 `Apply` 里打开文件失败，该 Fiber 进入 `Failed`，`o.Fiber.Ready(ctx)` 返回该错误，
**其它健康的组件不受影响**。恢复不是自动重试：`Failed` 后要 `Dispose` 再 `Load`（或改 manifest 让 Controller 重建）。

**依赖丢失 → Pending**（参考 `cmd/example/main.go` kernelDemo）：Mounted 的消费方在其 Provider 被 Dispose 后，
先 Unloading（cleanup 执行）→ 落回 `Pending`；Provider 重新 `Load` 后，消费方作为**新的 Activation** 自动恢复。

---

## 7. 配置与替换：为什么 manifest 里的 Config 是插件 API

把「配置」与「代码」解耦后，改配置 = 换执行体，业务插件不用重写：

- **Config Controller 路径**：同 `ID` 的 `ComponentConfig.Config` 变化 → Controller 先卸载旧 Fiber（等 Gone），
  再用同一 `Type` 的 Factory 造出**新 Component → 新 Fiber**。参考 `cmd/host/main.go` 的
  camera `mode: night → day`（`fiber:1` 被换成 `fiber:4`），以及 `cmd/collector/main.go` 的
  collector 从 `sample1.csv` 切到 `sample2.csv`（collector 被新 Fiber 替换、重新采集；sink Fiber 原封不动）。
- **Loader + HMR 路径**：实现级热替换。把「实现」变成可寻址的 Artifact，先加载成 Module，
  再让 HMR 用新实现顶替旧实现（新 Fiber 先到 `Active`，旧 Fiber 才卸载）。参考 `cmd/example/main.go` hmrDemo：

```go
ld := loader.NewBuiltinLoader()
defer ld.CloseContext(context.Background())
_ = ld.RegisterBuiltin("builtin://greeter-v1", func() config.Factory { return &versionedFactory{tag: "v1"} })
_ = ld.RegisterBuiltin("builtin://greeter-v2", func() config.Factory { return &versionedFactory{tag: "v2"} })

// 把 loaded Module 的 Factory 显式注册进 Config FactoryRegistry（绝不自动注册）：
// ld.RegisterFactories(reg)

h := hmr.New(rt, ld, ld.Usage())
defer h.CloseContext(context.Background())

tgt := hmr.Target{
	ID: "greeter", ComponentID: "greeter",
	Artifact: loader.Artifact{ID: "greeter-v2", Type: "greeter",
		Source: "builtin://greeter-v2", Version: "2"},
}
_ = h.Register(tgt)
_ = h.Bind("greeter", *m1, oldFiber) // 绑定当前运行的旧实现
_ = h.Replace(context.Background(), tgt) // 热替换：新 Active 后旧才卸载
```

（完整可运行版本见 `cmd/example/main.go`；`cmd/wasmhmr/main.go` 展示把 WASM 模块当作插件实现的 HMR。）

---

## 8. 真实业务插件：CSV 文件采集

这就是「我写插件就完了？」的答案——业务代码全在插件里，框架只管生命周期。
下面是 `cmd/collector/main.go` 的 collector 插件（逐字）：

```go
// collectorComp is the "business plugin": on activation it reads the CSV file
// named by its config and pushes every row into RowSink.
type collectorComp struct {
	id   string
	path string
}

func (c *collectorComp) Name() string { return "collector:" + c.id }
func (c *collectorComp) Inject() []runtime.Dependency {
	return []runtime.Dependency{runtime.Requires(rowSinkKey)}
}
func (c *collectorComp) Provide() []runtime.Capability { return nil }
func (c *collectorComp) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	sink, err := runtime.Require(ctx, rowSinkKey)
	if err != nil {
		return nil, err
	}
	fmt.Printf("   [collector %s] collecting %s\n", c.id, c.path)

	f, err := os.Open(c.path)
	if err != nil {
		// A missing/unreadable file is a component (lifecycle) failure: the
		// fiber becomes Failed and is isolated from the rest of the system.
		return nil, fmt.Errorf("open %s: %w", c.path, err)
	}
	r := csv.NewReader(f)
	n := 0
	for {
		row, err := r.Read()
		if err == io.EOF {
			break
		}
		if err != nil {
			f.Close()
			return nil, fmt.Errorf("parse %s: %w", c.path, err)
		}
		sink.Sink(row)
		n++
	}
	if err := f.Close(); err != nil {
		return nil, err
	}
	fmt.Printf("   [collector %s] done: %d rows from %s\n", c.id, n, filepath.Base(c.path))
	return func() error {
		fmt.Printf("   [collector %s] cleanup\n", c.id)
		return nil
	}, nil
}
```

配套的 sink 插件提供 `RowSink` 能力；host 里注册两个 Factory：

```go
_ = reg.Register("rowsink", &adapterFactory{build: func(cc config.ComponentConfig) (runtime.Component, error) {
	return &sinkComp{}, nil
}})
_ = reg.Register("csvcollector", &adapterFactory{build: func(cc config.ComponentConfig) (runtime.Component, error) {
	path, _ := cc.Config["path"].(string) // 业务配置从 manifest 的 Config 里来
	return &collectorComp{id: cc.ID, path: path}, nil
}})
```

然后声明 manifest 并 Reconcile（运行 `go run ./cmd/collector` 可见完整输出）：

```go
colV1 := config.ComponentConfig{ID: "col", Type: "csvcollector",
	Config: map[string]any{"path": "/tmp/sample1.csv"}}
sink := config.ComponentConfig{ID: "sink", Type: "rowsink"}
_ = c.ctrl.Reconcile(ctx, config.Config{Components: []config.ComponentConfig{sink, colV1}})
```

---

## 9. 把现成扩展当插件：HTTP Server

`extensions/http` 已经是一个「插件化」的真实资源组件：`http.New(...)` 实现 `runtime.Component`，
`http.NewConfigFactory()` 返回 `config.Factory`。把它注册成一个 Type，就能在 manifest 里声明：

```go
import "dynamic-runtime/extensions/http"

_ = reg.Register("http", http.NewConfigFactory())

desired := config.Config{Components: []config.ComponentConfig{
	{ID: "api", Type: "http", Config: map[string]any{"address": "127.0.0.1:18080"}},
}}
```

它的 `Apply` 用 `ctx.Effect` 登记 listener/server（Active 即真正在 serve），卸载时优雅停机；
可选 `WithProvider()` 让它通过 `http.ServerCapability`（`runtime.NewKey[Handle]("http.server")`）对外提供
`Handle{ID, Addr}`，其它插件可 `Inject` 依赖它。这是「框架组件（HTTP 资源）如何反过来作为宿主能力」的范本。

---

## 10. 编写插件时必须遵守的边界

这些不是风格建议，是 Kernel 的不变量，违反即破坏语义：

- **不要碰生命周期模型**：不修改/绕过 Fiber 状态机；不引入第二套 lifecycle。
- **不要绕过 Provider Registry**：能力只能通过 `runtime.Provide` / `runtime.Require` 走 typed key。
- **不要直接修改 Fiber State**：状态由 Orchestrator 独占驱动，插件只观察。
- **不要复用 Activation Context**：每次激活都是新 `*runtime.Context`，绝不能跨激活保存复用。
- **不要引入全局 mutable state**：例如 HTTP 的 listener/server/mux 都是 Component 实例作用域（见 `extensions/http/doc.go`）。
- **不要用 sleep 解决并发**；**不要用 goroutine kill 实现卸载**——卸载 = 逆序执行 Effect，是协作式的。
- **不要在锁内执行用户代码**：`Apply` / `Cleanup` / `Factory.Create` 都不会在框架锁内被调用；你也不要在自己的锁里调用框架回调。
- **`Name()` 只是诊断名**，不是能力身份；能力身份永远用 `runtime.NewKey[T]`。
- 启动 goroutine / 注册 listener / 打开资源等 Runtime-managed 副作用，必须通过 `ctx.Effect`（或 `Apply` 的 Cleanup 返回）登记，
  否则卸载时无法撤销。

---

## 11. 运行与验证

```bash
# 四个可运行示例
go run ./cmd/example     # Kernel 依赖链 + Loader/HMR 热替换
go run ./cmd/host        # 最小插件宿主：manifest v1/v2/v3 对账
go run ./cmd/collector   # CSV 采集：sink + collector 插件
go run ./cmd/wasmhmr     # 把 WASM 模块当插件实现，配 HMR

# 验证（写插件后跑）
go test ./...
go test -race ./...
go vet ./...
```

---

## 12. Next steps

- 想深入 Kernel 语义（状态机、ownership、WaitInactive）：`docs/Dynamic Composable Runtime — Runtime Specification v1.0.md`、
  `docs/Dynamic Composable Runtime — Kernel Review Prompt.md`（F-01..F-05 冻结裁决）。
- 想用配置/TOML/热加载：`extensions/config`、`extensions/configwatch`、`extensions/loader`、`extensions/hmr`、`extensions/watch`。
- 想给插件加能力：`extensions/registry`（成员注册表）、`extensions/event`（事件）、`extensions/scheduler`（调度）、`extensions/http`（HTTP/RPC）。
- 想写 WASM 插件：`cmd/wasmhmr` + `extensions/loader/wasm` + `docs/Dynamic Composable Runtime — WASM Module Backend Specification v0.1.md`。
- 最直接的范本：照抄 `cmd/host/main.go`（插件 = Component + Factory + manifest），把 camera/recorder 换成你的业务。
