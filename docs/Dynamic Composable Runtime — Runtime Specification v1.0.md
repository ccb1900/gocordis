# Dynamic Composable Runtime
## Runtime Specification v1.0

**Status:** Implementation Contract  
**Target:** Go 1.24+  
**Purpose:** Define a general-purpose runtime for dynamically composable components with reversible effects, reactive dependencies, scoped contexts, and deterministic lifecycle coordination.

---

# 00. Scope

本规范定义一个通用 Dynamic Composable Runtime（以下简称 Runtime）。

Runtime 必须支持：

1. Component 动态加载与卸载。
2. Component 对 Capability 的声明式依赖。
3. Dependency 的运行时动态变化。
4. Provider 消失与恢复。
5. Reversible Effect。
6. Fiber 生命周期管理。
7. Activation Context 隔离。
8. Parent/Child ownership。
9. Provider identity/generation tracking。
10. 并发生命周期操作的线性化。
11. Apply/Unwind failure handling。
12. Runtime-managed state recovery。
13. Eventual quiescence。
14. 条件性 confluence。

Runtime 不限定具体业务领域。

以下系统都应该可以建立在 Runtime 之上：

- Agent Runtime
- ETL Runtime
- Data Collector
- Industrial Runtime
- CAN Analysis Runtime
- HTTP Application Runtime
- Plugin Runtime
- Tool Runtime

---

# 01. Non-goals

以下能力不是 Kernel 的职责：

- HTTP Server
- MCP
- LLM
- Event Bus
- Configuration Parser
- YAML/TOML Parser
- Plugin Marketplace
- WASM Loader
- HMR
- File Watcher
- Scheduler
- Persistence
- Database abstraction
- Network abstraction
- UI

这些可以作为 Runtime Extension。

Kernel 不得因为某个 Extension 的存在而依赖该 Extension。

---

# 02. Terminology

## 2.1 Component

Component 是可组合能力的**定义**。

Component 不等于运行中的实例。

```text
Component
    ↓
Fiber
```

---

## 2.2 Fiber

Fiber 是 Component 的运行时实例。

Fiber 拥有：

- lifecycle state
- activation context
- dependency snapshot
- desired intent
- lifecycle error
- ownership relationship

---

## 2.3 Context

Context 是一次 Fiber activation 的作用域。

Context 拥有：

- Runtime-managed effects
- Provided capabilities
- child scopes
- activation-local state

每次重新激活 Fiber，必须创建新的 activation Context。

---

## 2.4 Capability

Capability 是 Runtime 可以解析的能力。

Capability 必须拥有稳定 identity。

Go 实现应该使用 typed key。

---

## 2.5 Provider

Provider 是当前为某 Capability 提供实现的 Fiber。

---

## 2.6 Dependency

Dependency 是 Component 对 Capability 的持续需求。

Dependency 不是启动阶段的一次性排序关系。

---

## 2.7 Effect

Effect 是 Runtime-managed mutation，并且必须具有对应的 inverse。

```text
Apply Effect
    ↓
Inverse
```

---

## 2.8 Activation Cycle

一次：

```text
Loading → Active → Unloading
```

构成一个 activation cycle。

每一个 activation cycle 必须拥有独立 Context。

---

## 2.9 Quiescent State

Runtime 处于 quiescent state，当：

- 没有待执行 lifecycle decision；
- 没有正在进行的 Apply；
- 没有正在进行的 Unwind；
- 所有可处理的 dependency reconciliation 已完成。

---

# 03. Core Model

Runtime Kernel 由以下对象组成：

```text
Runtime
 ├── Component
 ├── Fiber
 ├── Context
 ├── Effect
 ├── Capability Key
 ├── Dependency
 └── Lifecycle Coordinator
```

核心关系：

```text
Component
    │
    │ instantiate
    ▼
Fiber
    │
    │ activation
    ▼
Context
    │
    ├── Effect
    ├── Provide
    └── Child Scope
```

Dependency：

```text
Fiber B
    │
    │ Inject(X)
    ▼
Capability X
    ▲
    │
Fiber A
    │
    │ Provide(X)
```

---

# 04. Component Contract

推荐 Go API：

```go
type Component interface {
    Name() string

    Inject() []Key
    Provide() []Key

    Apply(ctx *Context) error
}
```

## 4.1 Name

`Name()` 用于诊断、日志和可观测性。

Name 不得作为 Capability identity。

---

## 4.2 Inject

`Inject()` 声明 Fiber Active 所必须满足的 Capability。

Runtime MUST 在进入 `Loading` 前确认所有 mandatory dependencies satisfied。

Component 不得自行实现生命周期级 dependency waiting。

禁止：

```go
func (c Component) Apply(ctx *Context) error {
    db, ok := ctx.Get(DatabaseKey)
    if !ok {
        return errors.New("database unavailable")
    }
}
```

如果 Database 是声明式依赖，应通过：

```go
Inject() []Key
```

表达。

---

## 4.3 Provide

`Provide()` 声明 Component 在 Active 状态期间提供的 Capability。

Provider 注册必须由 Context/Runtime 管理。

Component 不得绕过 Runtime registry 直接修改全局 provider map。

---

## 4.4 Apply

`Apply()` 执行一次 activation。

Apply：

- MAY 创建资源；
- MAY 注册 capability；
- MAY 注册 handler；
- MAY创建 goroutine；
- MAY初始化 Runtime-managed resource。

所有 Runtime-managed mutation MUST 通过 Context Effect mechanism 记录 inverse。

Apply 成功并不意味着 Fiber 一定进入 Active。

Runtime 必须在 Apply 完成后重新验证：

- Intent
- Dependency satisfaction
- Runtime lifecycle
- Provider validity

---

# 05. Capability Key

Kernel MUST 支持稳定 Capability identity。

推荐：

```go
type Key[T any] struct {
    Name string
}
```

例如：

```go
var DatabaseKey = Key[*sql.DB]{
    Name: "database",
}
```

Context：

```go
db, ok := ctx.Get(DatabaseKey)
```

Key identity MUST NOT depend on:

- pointer address
- Component instance address
- Fiber ID
- registration order

Capability identity 和 provider identity 必须分离。

---

# 06. Fiber State Machine

Fiber states：

```go
type FiberState uint8

const (
    Pending FiberState = iota
    Loading
    Active
    Unloading
    Failed
    Gone
)
```

状态语义：

### Pending

Fiber 已存在，但无法或尚未满足 activation 条件。

### Loading

Apply 正在执行。

### Active

当前 activation 已成功完成，并且所有 mandatory dependencies satisfied。

### Unloading

当前 activation 正在 unwind。

### Failed

当前 activation 因组件或 Runtime-visible lifecycle failure 无法成为 Active。

### Gone

Fiber 已完成退出。

---

# 07. Fiber Intent

State 不足以表达 lifecycle request。

Fiber MUST 维护 Desired Intent：

```go
type Intent uint8

const (
    Mounted Intent = iota
    Unmounted
)
```

因此：

```go
fiber.Load()
```

表达：

```text
Intent = Mounted
```

而：

```go
fiber.Dispose()
```

表达：

```text
Intent = Unmounted
```

Intent mutation MUST be idempotent。

---

# 08. State Transition Rules

核心 transition：

| Current | Intent | Dependency | Result |
|---|---|---|---|
| Pending | Mounted | unsatisfied | Pending |
| Pending | Mounted | satisfied | Loading |
| Pending | Unmounted | any | Gone |
| Loading | Mounted | satisfied | Active |
| Loading | Mounted | lost | Unloading |
| Loading | Unmounted | any | Unloading |
| Active | Mounted | satisfied | Active |
| Active | Mounted | lost | Unloading |
| Active | Unmounted | any | Unloading |
| Unloading | Mounted | satisfied | Loading |
| Unloading | Mounted | unsatisfied | Pending |
| Unloading | Unmounted | any | Gone |
| Failed | Mounted | satisfied | no automatic retry |
| Failed | Unmounted | any | Gone |
| Gone | Mounted | satisfied | Loading |
| Gone | Mounted | unsatisfied | Pending |
| Gone | Unmounted | any | Gone |

Component lifecycle code MUST NOT directly perform these transitions.

---

# 09. Lifecycle Decision Rule

Runtime MUST conceptually evaluate:

```text
NextState =
F(
    CurrentState,
    DesiredIntent,
    DependencyState,
    OperationResult,
    RuntimeState
)
```

Component code only reports:

```text
Apply completed
Apply failed
Inverse completed
Inverse failed
```

Lifecycle Coordinator owns state transition decisions.

---

# 10. Lifecycle Linearization

All lifecycle-changing operations MUST have a linearization order.

包括：

```text
Load
Dispose
Replace
ApplyFinished
UnwindFinished
DependencyChanged
Close
```

Concurrent callers MUST behave as if these operations occurred in some valid sequential order.

Runtime MUST NOT expose a partially applied lifecycle state as a stable state.

---

# 11. Per-Fiber Transition Exclusivity

At most one lifecycle transition may execute for a Fiber at any time.

禁止：

```text
Fiber A
 ├── Apply()
 └── Inverse()
```

同时执行。

允许：

```text
Fiber A → Apply
Fiber B → Apply
Fiber C → Apply
```

并发执行。

因此：

```text
Global concurrency = allowed
Per-Fiber lifecycle overlap = forbidden
```

---

# 12. Apply Execution

Lifecycle Coordinator MUST NOT hold its synchronization lock while executing user Component code.

错误：

```text
lock()
Apply()
unlock()
```

正确模型：

```text
Coordinator
    │
    ├── decide Loading
    │
    └── execute Apply asynchronously
              │
              ▼
        Apply finished
              │
              ▼
        Coordinator
              │
              └── decide next state
```

---

# 13. Apply Cancellation

Runtime MUST support cooperative cancellation.

Context MUST expose cancellation state equivalent to:

```go
Done() <-chan struct{}
Err() error
```

Runtime MUST NOT assume that arbitrary Go goroutines can be forcibly terminated.

Runtime MUST NOT use goroutine killing as lifecycle semantics.

If:

```text
Loading
Dispose()
Apply returns because of cancellation
```

and the cancellation was caused by Unmounted intent, this is a normal withdrawal path rather than Component failure.

Expected final state:

```text
Gone
```

If Apply returns cancellation while Intent remains Mounted, the result MAY be treated as activation failure:

```text
Failed
```

---

# 14. Effect Contract

Recommended:

```go
type Effect func() error

func (ctx *Context) Effect(
    install func() (Effect, error),
) error
```

Semantics:

```text
install()
    │
    ├── error → no committed effect
    │
    └── success
          │
          ▼
       commit inverse
```

An Effect MUST NOT be considered committed until its inverse has been successfully recorded into the activation scope.

---

# 15. Effect Ownership

Every Runtime-managed Effect MUST belong to exactly one activation Context.

When the Context is unwound:

```text
Effect N
Effect N-1
...
Effect 1
```

MUST be unwound in reverse logical registration order.

---

# 16. Effect During Unwind

If an Effect install completes after its Context has entered unwinding:

```text
install success
       │
       ▼
Context already unwinding
       │
       ▼
DO NOT commit as persistent effect
       │
       ▼
execute returned inverse
```

This prevents late effect leakage.

---

# 17. Effect Reversibility Boundary

Runtime Recovery applies only to Runtime-managed state.

### Strongly reversible

Examples:

```text
register → unregister
add → remove
open → close
subscribe → unsubscribe
provide → unprovide
```

Kernel MUST support these.

### Compensatable

Examples:

```text
create record → mark deleted
external command → compensating command
```

这些由 Application Layer 管理。

### Irreversible

Examples：

```text
send email
physical print
external payment
irreversible external command
```

Kernel MUST NOT claim that these are reversible.

---

# 18. Context API

Recommended conceptual API:

```go
type Context struct {
    // internal
}

func (ctx *Context) Get[T any](
    key Key[T],
) (T, bool)

func (ctx *Context) Provide[T any](
    key Key[T],
    value T,
) error

func (ctx *Context) Effect(
    install func() (Effect, error),
) error

func (ctx *Context) Child() *Context

func (ctx *Context) Done() <-chan struct{}

func (ctx *Context) Err() error
```

Kernel MUST NOT expose unrestricted:

```go
Set(key, value)
```

because this would permit mutations outside Effect ownership.

---

# 19. Provide Semantics

Conceptually：

```text
Provide(X, value)
```

MUST be equivalent to a Runtime-managed Effect:

```text
register X
inverse:
    unregister X
```

Therefore:

```text
Fiber Active
    ↓
X registered
    ↓
Fiber Unloading
    ↓
X unregistered
```

Provider availability MUST correspond to its owning activation.

---

# 20. Provider Identity

Provider resolution MUST return identity information, not only existence.

Conceptually:

```go
type ProviderIdentity struct {
    Owner      FiberID
    Generation uint64
}
```

Dependency snapshot：

```text
Database
    owner      = Fiber-A
    generation = 17
```

If current provider becomes：

```text
Database
    owner      = Fiber-C
    generation = 31
```

then the dependency is stale even though:

```text
Database exists
```

仍然成立。

---

# 21. Dependency Snapshot

When a Fiber enters Loading, Runtime MUST capture the provider identity of every mandatory dependency.

A Fiber becomes Active only if the dependency snapshot remains valid after Apply completes.

因此：

```text
Apply success
```

不能直接产生：

```text
Active
```

必须：

```text
Apply success
    ↓
validate dependency snapshot
    ↓
validate Intent
    ↓
validate Runtime state
    ↓
Active
```

---

# 22. Dependency Withdrawal

如果 Active Fiber 的 mandatory dependency disappears：

```text
Active
   ↓
Unloading
   ↓
Pending
```

不得进入：

```text
Failed
```

因为 dependency disappearance 本身不是 Component failure。

---

# 23. Dependency Recovery

当依赖重新满足：

```text
Pending
   ↓
Loading
   ↓
Active
```

重新 activation MUST 使用新的 Context。

不得复用旧 activation Context。

---

# 24. Provider Replacement

对于 exclusive Capability：

```text
Provider A
```

替换为：

```text
Provider B
```

Kernel 默认语义：

```text
A Active
   ↓
A Unloading
   ↓
A Gone
   ↓
B Loading
   ↓
B Active
```

不允许：

```text
A Active
B Active
```

同时作为同一个 exclusive Capability 的 provider。

---

# 25. Atomic Replacement

Kernel MUST NOT provide zero-downtime atomic replacement as a basic lifecycle guarantee.

如果需要：

```text
old provider
    +
new provider
    ↓
atomic commit
```

应作为上层 Transactional Swap Extension。

Kernel 默认优先保证：

```text
provider uniqueness
lifecycle correctness
dependency correctness
```

而不是无缝切换。

---

# 26. Duplicate Provider

对于 exclusive Capability：

```text
A provides X
B provides X
```

Runtime MUST reject the second provider.

失败不得破坏已经 Active 的 Provider。

最终：

```text
A Active
B Failed
```

或者 B 根本无法进入有效 activation。

---

# 27. Stable Registry Pattern

集合型 Capability 不应通过多 Provider 模型实现。

错误：

```text
ToolRegistry
    ▲
 ┌──┼──┐
ToolA ToolB ToolC
```

其中 A/B/C 都声称：

```text
Provide(ToolRegistry)
```

正确：

```text
Agent
  │
  ▼
Stable ToolRegistry
  ▲
  │
 ┌┼────────┐
 A B        C
```

Registry 自身是稳定 Capability。

成员通过 reversible membership effect：

```text
registry.Add(tool)
inverse:
    registry.Remove(tool)
```

因此：

```text
Tool A Gone
```

只改变：

```text
Registry membership
```

而不导致：

```text
Agent unload
```

---

# 28. Ownership

Fiber 可以拥有 Child Fiber。

Ownership 必须显式建立。

拥有关系：

```text
Parent
 ├── Child A
 ├── Child B
 └── Child C
```

当 Parent withdrawal 时：

```text
Child A/B/C withdrawal
        ↓
children Gone
        ↓
Parent withdrawal
        ↓
Parent Gone
```

必须遵循：

> Owned resources are withdrawn before their owner becomes Gone.

Context.Child 本身不自动意味着 Fiber ownership。

---

# 29. Failure Semantics

## 29.1 Apply Failure

```text
Loading
   ↓
Apply error
   ↓
unwind partial effects
   ↓
Failed
```

Apply 已经创建的 Runtime-managed effects MUST NOT remain active。

---

## 29.2 Apply + Cleanup Failure

例如：

```text
Apply error
    ↓
Inverse B error
Inverse A error
```

Runtime MUST：

1. 保留原始 Apply error；
2. 继续执行所有剩余 cleanup；
3. 聚合 cleanup errors；
4. 最终 Fiber = Failed。

推荐使用：

```go
errors.Join(...)
```

---

# 30. Unwind Failure

正常：

```text
Active
   ↓
Unloading
   ↓
cleanup
   ↓
Gone
```

如果 cleanup 失败：

```text
Active
   ↓
Unloading
   ↓
cleanup errors
   ↓
Gone + Err
```

Unwind error 不应把一个已经完成退出的 Fiber 重新标记为 Active/Loading。

---

# 31. Cleanup Must Continue

假设：

```text
Effect A
Effect B
Effect C
```

Unwind：

```text
C → OK
B → ERROR
A → OK
```

Runtime MUST execute：

```text
C
B
A
```

而不能：

```text
C
B error
STOP
```

否则 Effect A 泄漏。

---

# 32. Dispose Idempotency

以下：

```go
fiber.Dispose()
fiber.Dispose()
fiber.Dispose()
```

必须与一次 Dispose 具有相同最终语义。

Dispose MUST NOT：

- double-unwind；
- panic；
- 创建多个 concurrent inverse；
- 破坏 Fiber state。

---

# 33. Load/Dispose Race

以下操作：

```text
G1: Load()
G2: Dispose()
```

必须拥有某个合法线性化顺序。

例如：

```text
Load → Dispose
```

或：

```text
Dispose → Load
```

Runtime MUST NOT 产生无法对应到合法线性化顺序的状态。

连续操作：

```text
Load
Dispose
Load
Dispose
```

最终 Intent：

```text
Unmounted
```

---

# 34. Last Writer Wins

对于 Desired Intent：

```text
Load
Dispose
Load
```

最终：

```text
Mounted
```

对于：

```text
Dispose
Load
Dispose
```

最终：

```text
Unmounted
```

但是 Last-Writer-Wins 不允许取消已经运行的 Apply/Inverse。

当前 transition 必须先完成，然后 Coordinator 根据最新 Intent 决定下一状态。

---

# 35. Runtime Close

Runtime 状态：

```text
Running
Closing
Closed
```

进入 Closing 后：

```text
Load()
```

MUST fail。

新的 Fiber 不得进入：

```text
Loading
```

Close：

```text
Running
   ↓
Closing
   ├── reject new Load
   ├── cancel active Apply
   ├── unload Active Fibers
   ├── wait transitions
   └── drain coordinator
   ↓
Closed
```

---

# 36. Runtime Close Semantics

Close MUST NOT return until：

- lifecycle coordinator drained；
- all lifecycle transitions completed；
- all reachable Runtime-managed Fibers reached terminal state；
- Runtime-managed resources have been given cleanup opportunity.

如果 Component 永久阻塞 Apply/Inverse，Runtime 无法保证强制终止。

---

# 37. Runtime Fatal Errors

以下属于 Runtime-level fatal condition：

- 内部状态违反核心 invariant；
- Fiber 同时存在多个 lifecycle transition；
- Context ownership corruption；
- provider registry corruption；
- Coordinator consistency failure。

Component 自身 Apply error：

**MUST NOT** 导致整个 Runtime 崩溃。

Runtime MUST isolate Component failures。

---

# 38. Recovery Guarantee

Recovery guarantee 只针对 Runtime-managed reversible state。

定义：

```text
S0 = state before activation

Apply
  ↓
S1

Unwind
  ↓
S2
```

要求：

```text
Observable(S2) ≡ Observable(S0)
```

这里的 Observable 指 Runtime-defined observable state。

不要求：

```text
ExternalWorld(S2) == ExternalWorld(S0)
```

---

# 39. Quiescence

Runtime 必须允许达到：

```text
Quiescent
```

状态。

在以下假设成立时：

1. 操作集合有限；
2. Apply 最终返回；
3. Inverse 最终返回；
4. Coordinator 最终处理 queued commands；
5. dependency resolution 最终稳定；
6. Application 不制造无限 dependency oscillation；

Runtime MUST eventually converge to a quiescent state。

---

# 40. Confluence

Runtime 不要求所有任意外部副作用都具有 Confluence。

保证范围：

> 对合法 Runtime-managed operations，在相同初始状态和相同有限操作集合下，不同合法调度最终应产生等价的 quiescent Runtime-managed observable state。

禁止将：

```text
external side effects
```

纳入 Kernel Confluence Guarantee。

---

# 41. Observable State

用于测试 Confluence/Recovery 的 Observable State 至少包括：

- Fiber state；
- Fiber intent；
- active provider identity；
- capability availability；
- registry membership；
- Runtime-managed resources；
- ownership relationship；
- lifecycle errors。

不应包括：

- map iteration order；
- goroutine scheduling order；
- memory address；
- internal mutex state；
- implementation-specific sequence unrelated to semantics。

---

# 42. Lifecycle Coordinator

Coordinator 是 Runtime 内部唯一 lifecycle decision authority。

建议 Go 实现：

```text
Coordinator goroutine
       │
       ├── command inbox
       │
       ├── Fiber state registry
       │
       └── transition decisions
```

但规范只要求：

> lifecycle decisions are serialized and linearizable.

规范不强制特定内部并发实现。

---

# 43. Recommended Runtime API

```go
type Runtime interface {
    Load(
        parent *Fiber,
        component Component,
    ) *Fiber

    Dispose(
        fiber *Fiber,
    )

    Replace(
        old *Fiber,
        component Component,
    ) *Fiber

    Close(ctx context.Context) error
}
```

---

# 44. Recommended Fiber API

```go
type Fiber struct {
    // private
}

func (f *Fiber) ID() FiberID

func (f *Fiber) Component() Component

func (f *Fiber) State() FiberState

func (f *Fiber) Err() error

func (f *Fiber) Load()

func (f *Fiber) Dispose()

func (f *Fiber) Ready(
    ctx context.Context,
) error

func (f *Fiber) Gone(
    ctx context.Context,
) error
```

用户不得直接：

```go
f.state = Active
```

或者：

```go
f.activate()
f.unload()
```

---

# 45. Extension Architecture

Kernel 之上：

```text
Runtime Kernel
      │
      ├── Registry
      ├── Events
      ├── Config
      ├── Loader
      ├── Watch
      ├── HMR
      ├── WASM
      ├── Scheduling
      └── Persistence
```

Extension MUST use Kernel APIs instead of modifying Kernel internal state。

---

# 46. HMR Semantics

HMR 不属于 Kernel。

HMR 可以实现为：

```text
old Fiber
    ↓
Dispose
    ↓
Gone
    ↓
Load new Component
```

因此 HMR 本质是：

> Component Replacement Policy。

---

# 47. Configuration Reconciliation

Configuration 不属于 Kernel。

配置系统应该产生：

```text
desired component tree
```

然后 Reconciler 将：

```text
Current Runtime State
          +
Desired State
          ↓
Runtime Commands
```

例如：

```text
Config changed
    ↓
Reconciler
    ↓
Replace(A, B)
```

Runtime 不应该解析配置文件。

---

# 48. Event Model

Event system 属于 Extension。

至少区分：

### Notification

短暂通知。

### Durable Event

需要持久化的业务事实。

### Interception

改变调用路径的 middleware/interceptor。

Kernel 不应依赖 Event Bus 才能完成 lifecycle correctness。

---

# 49. Stable Registry as General Pattern

Stable Registry 可以抽象为：

```text
Stable Capability
        +
Dynamic Membership
```

适用于：

- Tool Registry
- Driver Registry
- Protocol Registry
- Command Registry
- DataSource Registry
- Plugin Registry
- Agent Registry

核心原则：

> Consumer 依赖稳定 Registry，而不是依赖 Registry 中每一个动态成员。

---

# 50. Forbidden Implementations

以下实现明确禁止。

## F1

将 Fiber state 暴露为 public mutable field。

---

## F2

让 Component 直接修改 Runtime provider map。

---

## F3

让 Component 直接修改其他 Fiber state。

---

## F4

通过字符串全局变量实现 Dependency。

---

## F5

通过 polling loop 实现所有 Dependency。

Dependency changes MUST be event/reconciliation driven or equivalent。

---

## F6

通过 global singleton 保存所有 Component state。

---

## F7

在 Runtime mutex 内执行用户 Apply/Inverse。

---

## F8

同一个 Fiber 同时执行 Apply 和 Inverse。

---

## F9

Apply failure 后留下未清理 Runtime-managed effects。

---

## F10

Inverse failure 后立即停止剩余 cleanup。

---

## F11

用 `runtime.Goexit`、goroutine kill 等方式实现生命周期取消。

---

## F12

把 HMR/WASM/HTTP/MCP/LLM 强耦合进 Kernel。

---

## F13

用多个 Provider 直接模拟 Collection Registry。

---

## F14

仅通过 capability existence 判断 Dependency 是否发生变化。

必须支持 Provider identity/generation。

---

## F15

复用不同 activation cycle 的 Context。

---

# 51. Property Tests

实现 MUST 提供 property-based tests。

---

## P1 Preservation

随机执行：

```text
Load
Dispose
Provide
Unprovide
Replace
Dependency change
```

最终 registry/provider state 必须满足所有 invariant。

---

## P2 Recovery Exactness

随机生成一个 Fiber activation：

```text
S0
 ↓
Apply
 ↓
S1
 ↓
Unwind
 ↓
S2
```

验证：

```text
Observable(S2) == Observable(S0)
```

仅比较 Runtime-managed reversible state。

---

## P3 Ordering

如果：

```text
B Inject X
A Provide X
```

则：

```text
B Active
```

不得发生在：

```text
A provider identity
```

有效之前。

---

## P4 Progress

给定：

- finite operations；
- Apply eventually returns；
- Inverse eventually returns；

Runtime 必须达到 quiescence。

---

## P5 Confluence

生成同一 operation set 的多个随机调度：

```text
Schedule A
Schedule B
Schedule C
```

最终：

```text
Observable(StateA)
==
Observable(StateB)
==
Observable(StateC)
```

---

## P6 Duplicate Provider

随机产生：

```text
A Provide(X)
B Provide(X)
```

验证：

```text
at most one active provider
```

并且第二个失败不得破坏第一个。

---

## P7 Provider Replacement

验证：

```text
A → X
B → X
```

替换过程中不存在：

```text
A Active && B Active && both provide X
```

---

## P8 Dependency Recovery

验证：

```text
Provider
   ↓
Dependent Active
   ↓
Provider Gone
   ↓
Dependent Pending
   ↓
Provider Restored
   ↓
Dependent Active
```

---

## P9 Idempotent Dispose

验证：

```text
Dispose()
Dispose()
Dispose()
```

和：

```text
Dispose()
```

产生等价最终状态。

---

## P10 Race Test

必须运行：

```bash
go test -race ./...
```

并随机并发执行：

```text
Load
Dispose
Replace
Close
```

---

# 52. Contract Tests

至少包括：

### CT1

Duplicate provider 不破坏原 provider。

### CT2

Apply partial failure 自动 cleanup。

### CT3

Cleanup failure 不阻止其他 inverse。

### CT4

Dependency disappearance → Pending。

### CT5

Dependency recovery → new activation。

### CT6

Provider identity replacement → dependent reactivation。

### CT7

Dispose idempotent。

### CT8

Load after Close rejected。

### CT9

Child ownership cascade。

### CT10

Stable Registry member churn 不导致 consumer reload。

---

# 53. Acceptance Criteria

Runtime v1.0 只有在满足以下条件后才算实现完成：

```text
[ ] Component lifecycle implemented
[ ] Fiber state machine implemented
[ ] Desired Intent implemented
[ ] Activation Context implemented
[ ] Reversible Effect implemented
[ ] LIFO unwind implemented
[ ] Dependency resolution implemented
[ ] Provider identity implemented
[ ] Generation/staleness detection implemented
[ ] Dependency recovery implemented
[ ] Provider replacement implemented
[ ] Ownership implemented
[ ] Failure isolation implemented
[ ] Cleanup aggregation implemented
[ ] Cooperative cancellation implemented
[ ] Lifecycle linearization implemented
[ ] Runtime Close implemented
[ ] Stable Registry extension implemented
[ ] Property tests implemented
[ ] Contract tests implemented
[ ] Race detector passes
[ ] No forbidden implementation present
```

---

# 54. Reference Runtime Structure

推荐第一版 Go 项目：

```text
runtime/
├── runtime.go
├── component.go
├── fiber.go
├── context.go
├── effect.go
├── key.go
├── dependency.go
├── provider.go
├── ownership.go
├── state.go
├── orchestrator.go
├── error.go
└── invariant.go

extensions/
├── registry/
├── events/
├── config/
├── loader/
├── watch/
├── hmr/
└── wasm/

tests/
├── contract_test.go
├── property_test.go
├── lifecycle_test.go
├── dependency_test.go
├── effect_test.go
├── concurrency_test.go
└── recovery_test.go
```

---

# 55. Reference Implementation Strategy

第一版实现 MUST 优先保证语义正确性，而不是性能。

推荐：

```text
                    ┌──────────────────┐
                    │ Lifecycle        │
                    │ Coordinator      │
                    └────────┬─────────┘
                             │
                        serialized
                        decisions
                             │
             ┌───────────────┼───────────────┐
             ▼               ▼               ▼
          Fiber A         Fiber B         Fiber C
             │               │               │
         Apply()         Apply()         Apply()
             │               │               │
             └───────────────┼───────────────┘
                             ▼
                       Coordinator
```

第一版可以采用：

```text
single orchestrator goroutine
+
mutex-protected Context state
+
async user code
+
completion commands
```

但这些是实现策略，而不是外部语义。

---

# 56. Kernel Design Principle

最终 Kernel 应遵守：

> **Runtime decides when a Component may exist. Component decides what resources it needs to establish its activation. Context owns the reversible consequences of that activation.**

即：

```text
Runtime
  ↓
决定生命周期

Component
  ↓
实现能力

Context
  ↓
拥有 activation effects

Dependency
  ↓
决定 activation 是否仍然成立
```

---

# 57. Final Semantic Equation

整个 Runtime 可以抽象为：

```text
Fiber State
=
F(
    Component,
    Intent,
    Dependency Satisfaction,
    Provider Identity,
    Activation Result,
    Runtime State
)
```

而一个 activation：

```text
Activation
=
Apply
+
Effect Set
+
Dependency Snapshot
+
Context
```

其 withdrawal：

```text
Withdrawal
=
Reverse(Effect Set)
```

并满足：

```text
Observable(
    Withdrawal(
        Activation(S)
    )
)
≈
Observable(S)
```

其中 `≈` 只针对 Runtime-managed observable state。

---

# 58. Final Architecture

最终架构冻结为：

```text
┌──────────────────────────────────────────────┐
│                 Applications                 │
│                                              │
│ Agent / ETL / Collector / CAN / Industrial  │
└──────────────────────▲───────────────────────┘
                       │
┌──────────────────────┴───────────────────────┐
│             Runtime Extensions               │
│                                              │
│ Registry / Events / Config / Loader / HMR   │
│ WASM / Watch / Scheduler / Storage / MCP    │
└──────────────────────▲───────────────────────┘
                       │
┌──────────────────────┴───────────────────────┐
│                 Runtime Kernel               │
│                                              │
│ Component                                    │
│ Fiber                                        │
│ Context                                      │
│ Effect                                       │
│ Capability Key                               │
│ Dependency                                   │
│ Provider Identity                            │
│ Ownership                                    │
│ Lifecycle Coordinator                        │
└──────────────────────────────────────────────┘
```

---

# 59. v1.0 Freeze

以下语义在 v1.0 后不得随意改变：

1. Component ≠ Fiber。
2. Fiber 拥有 activation lifecycle。
3. 每次 activation 使用新的 Context。
4. Dependency 是 reactive condition。
5. Provider identity 必须可检测变化。
6. Runtime-managed effects 必须可 unwind。
7. Effect unwind 为逆序。
8. Apply/Inverse 不得在同一 Fiber 上重叠。
9. Lifecycle decisions 必须线性化。
10. Component 不得直接修改 Fiber lifecycle。
11. Dependency loss → Pending，而不是 Failed。
12. Apply failure → Failed。
13. Normal withdrawal → Gone。
14. Cleanup failure → Gone + error。
15. Provider replacement 默认采用 withdraw-then-load。
16. Stable Registry 是 Extension Pattern。
17. HMR 是 Replacement Policy，不是 Kernel primitive。
18. Config 是 Reconciliation Layer，不是 Kernel primitive。
19. Kernel 不承诺恢复不可逆外部世界。
20. Confluence 只对 Runtime-managed semantic state 提供保证。