# gocordis 应用开发指南

> 面向读者:要在这套 Runtime 上开发插件/应用的工程师。
> 权威说明:本仓库以论文为唯一规范来源(`docs/论文.pdf`,*A Programming Paradigm
> for Spatiotemporal Composability*);本指南讲的是"怎么用",语义争议以论文与
> `docs/ROADMAP.md` §1 对照表为准。历史文档已归档,勿作参考。

---

## 1. 心智模型(5 分钟)

你要写的单元叫 **Component**(组件)——它是*定义*,不是运行中的实例。
Runtime 把组件实例化为 **Fiber**(fiber)并驱动它的生命周期:

```
你声明:  Component = { Name, Inject(需要什么), Provide(提供什么), Apply(启动逻辑) }
Runtime:  声明满足 → Apply → Active;依赖消失 → 撤回(不是失败);Dispose → Gone
```

三条纪律,记住它们就掌握了 80%:

1. **能力是类型化的**:依赖与提供都用 `Key[T]` 标识,类型 + 名字即身份;
2. **声明是权威**:Apply 里只能 `Require`/`Provide` Inject/Provide 集合里声明过的 key;
3. **Active 的含义是严格的**:组件说"我在服务了",Runtime 才标 Active——
   反过来看,看到 Active 就可以信任资源真的就绪。

一个应用 = 一组组件 + 一份声明(谁启用、配置是什么)。Runtime 负责
"声明 → 运行"的调和,你不需要手写启动顺序。

---

## 2. 快速开始:第一个组件

```go
package main

import (
    "context"
    "fmt"
    "time"

    "dynamic-runtime/runtime"
)

// typed key:能力的身份。(类型, 名字) 相同即为同一能力。
var greeting = runtime.NewKey[string]("app.greeting")

// Greeter 提供问候语。
type Greeter struct{}

func (c *Greeter) Name() string                 { return "greeter" }
func (c *Greeter) Inject() []runtime.Dependency { return nil }
func (c *Greeter) Provide() []runtime.Capability {
    return []runtime.Capability{greeting.Capability()}
}
func (c *Greeter) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
    // Provide 等价于一个可逆 effect:激活撤销时自动注销。
    return nil, runtime.Provide(ctx, greeting, "hello")
}

// Printer 依赖问候语并消费它。
type Printer struct{ done chan struct{} }

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
    close(c.done)
    return nil, nil // 返回的 Cleanup(若有)会成为首个被撤销的逆
}

func main() {
    rt, err := runtime.New()
    if err != nil {
        panic(err)
    }
    p := &Printer{done: make(chan struct{})}
    if _, err := rt.Load(&Greeter{}); err != nil {
        panic(err)
    }
    pf, err := rt.Load(p)
    if err != nil {
        panic(err)
    }
    // Ready:等到 Active(成功)/ Failed(带错误)/ Gone。
    ctx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
    defer cancel()
    if err := pf.Ready(ctx); err != nil {
        panic(err)
    }
    select {
    case <-p.done:
    case <-ctx.Done():
        panic("printer did not run")
    }
    _ = rt.Close(ctx)
}
```

要点:

- `Load` 只是表达意图(幂等),真正的状态迁移由 Runtime 内部的单一决策域串行完成;
- 消费者**不需要等待**提供者——把两个组件都 `Load`,消费者会自动等依赖满足后才激活
  (声明满足即触发,这就是"反应式门控")。

---

## 3. 生命周期与错误语义

| 情形 | 结果 | 你的感受 |
|---|---|---|
| 声明满足 | Pending → Loading → **Active** | Apply 成功返回 |
| 依赖中途消失 | 激活**撤回**(撤销已装的 effect)→ **Pending** | 不是失败;依赖恢复后自动重新激活 |
| Apply 返回 error / panic | 撤销已装部分 → **Failed** | 错误留在该 fiber 上(`f.Err()`),**不会**向父传播、不会自动重试 |
| `Dispose()` | 进入 Unloading → **Gone** | 撤销按 LIFO 恰好一次 |
| 关闭中的依赖仍可读 | Cleanup 期间依赖照样能 `Require` | **趁依赖还在时落盘/收尾**——顺序由 Runtime 保证 |

Fiber 上的等待原语:`Ready`(等 Active/Failed/Gone)、`WaitInactive`(等当前
激活周期结束)、`Gone`(等终态)。`State()`/`Err()` 随时可读。

**开关**:`Dispose()` = 关,`Load()` = 开;失败后的重启用 = 先 `Dispose` 清掉
outcome 再 `Load`(声明式场景下这一步由 config 控制器代劳,见 §8)。

**子组件与所有权**:`ctx.Child(comp)` 创建你拥有的子 fiber(你撤销它先撤销);
`ctx.Child(comp, runtime.WithScope())` 额外获得一个独立命名空间。

---

## 4. 外部资源绑定(ctx.Effect 模式)

任何外部资源(端口、连接池、文件、队列 consumer)都按同一模式接入:

```go
func (c *Server) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
    ln, err := net.Listen("tcp", c.addr)
    if err != nil {
        return nil, err
    }
    srv := startServing(ln, c.handler) // 就绪探测通过后才算"在服务"

    // 把"停服"注册为可逆 effect:激活撤销时优雅关停先于 fiber 终态。
    if err := ctx.Effect(func() (func() error, error) {
        return func() error { return gracefulStop(srv) }, nil
    }); err != nil {
        return nil, err
    }
    return nil, nil
}
```

两个不变式:**Active ⇔ 资源真实就绪**(先就绪、后提交);**撤销先于终态**
(关停完成前 fiber 不会 Gone)。多个 effect 的逆按 LIFO 撤销;effect install
或逆里的 panic 会被受控转换为生命周期错误。

完整可运行样例:`cmd/httpd`。这个模式适用于连接池、消息 consumer、gRPC server
等一切"启动—就绪—优雅停止"形态的资源。

---

## 5. 长激活:效果迭代器(可选)

激活默认是一次 `Apply` 到底。如果启动过程很长(分批预热、逐个建立连接),
可以实现 `IterComponent`,把激活表达为**逐步 yield**:

```go
func (c *Pool) ApplyIter(ctx *runtime.Context, yield func(step func() (runtime.Cleanup, error)) error) error {
    for i := 0; i < c.batches; i++ {
        i := i
        err := yield(func() (runtime.Cleanup, error) {
            conn := dial(i)                       // 这一步做的工作
            return func() error { conn.Close() }  // 这一步的逆(LIFO 入栈)
        })
        if err != nil {
            return err // runtime.ErrDiverted:目标翻转了,立刻停止,不要再装
        }
    }
    return nil
}
```

Runtime 在每个 yield 边界检查目标状态:依赖消失/被 Dispose 时,yield 返回
`ErrDiverted`,已落地步骤的逆会被照常撤销,**这不是失败**(fiber 不会变
Failed)。步骤中途的 error 则是失败:撤销已装部分,进入 Failed,且不会自动重试。
注意:实现了 `ApplyIter` 之后,`Apply` 不再被调用(接口要求保留一个空实现)。

---

## 6. 作用域、隔离与拦截

- 默认:子组件继承父的命名空间,同 key 全局只有一个提供者(重复 Provide 报
  `ErrDuplicateProvider`);
- `WithScope()`:子树获得**独立命名空间**——兄弟 scope 互不可见,解析永不回落
  到父(每个 key 恰好解析到一个命名空间,论文 §4.4 Isolation);
- `runtime.Isolate(keys...)`:只把列出的 key 放进新命名空间,**其余 key 仍与
  父共享**——"隔离一个、共享一片";
- 值变换: `runtime.Intercept(ctx, key, fn)` 在读取时变换值(可逆,不动依赖图);
- 元数据拦截(策略/访问控制):`NewMetaKey[T, M]` 声明 monoid,provider 收到的
  是 `声明元数据 ⊕ 上下文元数据`(context 优先),`InterceptMeta(ctx, key, ν)`
  可在不触发 reload 的情况下收紧权限——参考论文 §6.3 的能力控制模型。

---

## 7. 事件

处理器注册是**可逆 effect**(随激活撤销自动注销);派发模式在
`extensions/event`:

```go
var evt = runtime.NewEventKey[string]("app.config.changed")

// 组件内注册:
runtime.On(ctx, evt, func(_ context.Context, payload string) error {
    log.Println("changed:", payload)
    return nil
})

// 任意持有激活 context 的地方派发:
_ = event.Emit(ctx, evt, "v2")                                    // 全体可见 handler,错误聚合
_ = event.Serial(context.Background(), ctx, evt, "v2")            // 严格顺序
_ = event.Parallel(context.Background(), ctx, evt, "v2")          // 并发执行、按注册序聚合错误
_ = event.Bail(context.Background(), ctx, evt, "v2")              // 首错即停(fail-fast)
_ = event.Waterfall(context.Background(), ctx, evt, "v2")         // 中间件链(next() 驱动)
```

作用域规则:handler 的可见性由**注册者的命名空间路径**决定(子 scope 的 handler
只有本 scope 及后代能触发)。

---

## 8. 用声明式配置组装应用(推荐的应用形态)

手写 `Load` 适合 demo;真实应用建议走**声明 → 控制器 → 调和**:

```go
reg := config.NewFactoryRegistry()
_ = reg.Register("greeter", config.FactoryFunc(func(cc config.ComponentConfig) (runtime.Component, error) {
    return &Greeter{}, nil // cc.Config 里取你的参数
}))
ctrl := config.NewController(rt, reg)

_ = ctrl.Reconcile(ctx, config.Config{Components: []config.ComponentConfig{
    {ID: "greeter", Type: "greeter"},
    {ID: "printer", Type: "printer", Enabled: boolPtr(true)},
}})
// Reconcile 是幂等的:重复调用、部分失败、最小差分都由控制器处理。
```

**插件开关**就是声明的一部分:`Enabled *bool`(nil = 启用)。翻动开关 → 重新
`Reconcile` → Runtime 自动装载/卸载;**disabled 的条目仍然是已声明身份**
(定义会被校验,只是没有运行中的 fiber)。把开关放进文件并监听它,就得到了
"改文件即启停"的运维形态:

```toml
[[components]]
id = "greeter"
type = "greeter"

[[components]]
id = "printer"
type = "printer"
enabled = false        # 开关持久化在声明里;重启后原样恢复
[components.config]
greeting = "hello"
```

```go
fw := watch.NewFileWatcher()
adapter, err := configwatch.New(configwatch.Source{
    ID: "app", Path: "app.toml", Format: configwatch.FormatTOML,
}, ctrl, fw)
_ = adapter.Sync(ctx)      // 启动时同步一次(重启恢复 = 重放声明)
go adapter.Run(ctx)        // 此后文件变更实时调和
```

**重启恢复的完整姿势**:声明文件(含开关)是持久化的唯一对象;业务成果用
组件自己的 journal 做幂等重放(参考 `cmd/backfill`);runtime 内部状态**从不**
持久化——重启 = 全新调和。持久化写入的顺序由 Runtime 保证(Cleanup 期间依赖
仍可解析),但**写入本身的可逆性不在保证范围内**(论文信任边界)。

---

## 9. 服务多路复用(可选,extensions/broker)

一个 key 只能有一个提供者(独占绑定);需要"多实现共存"时用 broker:

```go
b := broker.New[Codec](nil) // RoundRobin;或 broker.First[Codec]()
// 提供者组件在 Apply 里注册(注册是可逆 effect,卸载自动移出路由集):
_ = broker.Register(ctx, b, "impl-v2", codecV2)
// 消费者拿到的能力是稳定的 broker,替换后端不触发任何 reload:
_ = b.Call(func(c Codec) error { return c.Encode(x) })
```

---

## 10. 观测与测试

- 观测:`rt.Snapshot(ctx)` 返回全量只读快照(fibers/scopes/providers/
  dependencies/effects + 事件序号),可在任意时刻线性化读取;逐扩展 README 有
  更多接口;
- 测试组件:普通 `runtime.New()` + `Load` + `Ready` + 断言状态/副作用;并发
  相关逻辑跑 `go test -race`;长激活务必覆盖"激活中途依赖消失"的 divert 路径;
- 错误处理:统一 `errors.Is` + sentinel(`runtime.ErrDependencyMissing`、
  `runtime.ErrComponentApplyPanic`、`event.ErrEventHandlerPanic` 等)。

---

## 11. 边界与最佳实践

1. **信任边界**:只有经 `ctx.Effect`/`Provide` 注册的后果是 Runtime 管理的;
   你自己开的 goroutine、自己写的文件,Runtime 既不追踪也不撤销——把一切需要
   逆操作的后果交给 effect;
2. **合作式 Apply**:Apply/Cleanup 里不要无限阻塞;需要等待时用 `ctx.Done()`
   (激活取消时关闭);
3. **无环**:声明的依赖图必须无环(Runtime 会在装载边界拒绝并给出环链诊断);
4. **单一生命周期权威**:扩展与你的代码都不得绕过 Public API 改 fiber 状态;
   "谁该活"永远由声明 + 调和决定;
5. **假设条件**(论文声明的宿主边界):组件数量有限、Apply/Cleanup 合作式、
   依赖图无环、只有托管 effect 可逆。

---

## 12. 示例索引

| 示例 | 演示 |
|---|---|
| `cmd/collector` | 配置驱动替换:manifest v1→v2 热替换采集器,失败隔离 |
| `cmd/backfill` | 断电安全:journal + 幂等重放的业务持久化模式 |
| `cmd/httpd` | 外部资源绑定:Active ⇔ serving、优雅关停、同地址重绑 |
| `cmd/wasmhmr` | WASM 插件热替换 |
| `cmd/host`、`cmd/example` | 最小装配 |

## 13. 文档地图

- 论文(唯一规范来源):`docs/论文.pdf`
- 语义对照与路线:`docs/ROADMAP.md`
- 设计裁决:`docs/adr/0001..0004`
- 评审记录(含平台完备性矩阵):`docs/review/2026-09-08-paper-first-review-v0.1.md`
- 各扩展用法:对应包目录内 README
