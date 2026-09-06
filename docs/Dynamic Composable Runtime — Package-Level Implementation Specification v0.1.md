# Dynamic Composable Runtime

## Package-Level Implementation Specification v0.1

## 1. Implementation Goal

实现一个 Go Dynamic Composable Runtime，支持：

- Component 动态加载/卸载
- Fiber 生命周期管理
- 动态依赖解析
- Provider 动态注册/撤销
- Dependency loss / recovery
- Re-activation
- 可逆 Effect
- LIFO cleanup
- Fiber ownership
- Provider replacement
- Stable Registry
- 并发 Load / Dispose
- stale completion 防护
- Runtime Close
- 后续扩展 Registry / Event / Config / Loader / HMR / WASM

Kernel 不实现：

- HTTP
- MCP
- LLM
- WASM
- 文件监听
- 配置解析
- Agent
- ETL
- Scheduler

这些全部属于 extension/application plane。

---

# 2. Package Structure

第一版采用单 module：

```text
runtime/
├── go.mod
│
├── component.go
├── runtime.go
├── fiber.go
├── context.go
├── effect.go
├── key.go
├── provider.go
├── dependency.go
├── ownership.go
├── state.go
├── error.go
│
├── internal/
│   ├── orchestrator/
│   │   ├── orchestrator.go
│   │   ├── command.go
│   │   ├── reconcile.go
│   │   └── transition.go
│   │
│   ├── effects/
│   │   └── stack.go
│   │
│   ├── providers/
│   │   └── registry.go
│   │
│   └── wait/
│       └── signal.go
│
├── extensions/
│   ├── registry/
│   ├── events/
│   ├── config/
│   ├── loader/
│   ├── watch/
│   ├── hmr/
│   └── wasm/
│
└── tests/
    ├── lifecycle_test.go
    ├── dependency_test.go
    ├── effect_test.go
    ├── provider_test.go
    ├── ownership_test.go
    ├── concurrency_test.go
    ├── recovery_test.go
    └── property_test.go
```

第一阶段不要提前拆成大量 Go package。

**Kernel API 保持一个 `runtime` package。**

`internal/*` 只负责实现细节。

原因：

1. 避免第一版 API 被内部结构绑死。
2. 便于后续修改 orchestrator。
3. 用户 Component 只依赖 `runtime`。
4. extension 也只依赖 `runtime`。

依赖方向必须保持：

```text
extensions
     ↓
  runtime API
     ↓
runtime internal
```

禁止：

```text
runtime → extensions
runtime → agent
runtime → wasm
runtime → http
```

---

# 3. Core Domain Objects

Kernel 只有以下核心对象：

```text
Runtime
  │
  ├── Fiber
  │      │
  │      └── Activation
  │              │
  │              └── Context
  │                     ├── Effects
  │                     ├── Providers
  │                     └── Dependencies
  │
  └── Provider Registry
```

最重要的设计：

> Fiber 是长期存在的逻辑对象；Activation 是一次运行周期；Context 属于 Activation。

绝对不能让 Fiber 自己长期持有一个可重复使用的 Context。

---

# 4. Component

公开接口：

```go
type Component interface {
    Name() string

    Inject() []Dependency

    Provide() []Capability

    Apply(ctx *Context) (Cleanup, error)
}
```

其中：

```go
type Cleanup func() error
```

Component 不负责：

- 修改 Fiber State
- 修改 Provider Registry
- 直接控制其他 Fiber
- 调用内部 orchestrator
- 自己决定什么时候 unload

Component 只能通过 Context：

```go
ctx.Effect(...)
ctx.Provide(...)
ctx.Require(...)
ctx.Child(...)
```

参与 Runtime。

---

# 5. Fiber

Fiber 是 Component 的 Runtime-owned lifecycle object。

内部结构：

```go
type Fiber struct {
    id        FiberID
    component Component

    mu       sync.RWMutex
    state    FiberState
    intent   Intent

    generation uint64

    activation *activation

    parent *Fiber

    ownedChildren map[FiberID]*Fiber

    wait *stateSignal
}
```

但是：

**Fiber 的生命周期状态不能由 Fiber 自己修改。**

状态修改只能发生在 Orchestrator 的 serialized decision domain。

Fiber 对外只提供：

```go
func (f *Fiber) ID() FiberID
func (f *Fiber) Name() string
func (f *Fiber) State() FiberState
func (f *Fiber) Load() error
func (f *Fiber) Dispose() error
func (f *Fiber) Ready(ctx context.Context) error
func (f *Fiber) Gone(ctx context.Context) error
```

`Load()` / `Dispose()` 本质上只是向 Runtime 发送 Command。

---

# 6. Fiber State

```go
type FiberState uint8

const (
    StatePending FiberState = iota
    StateLoading
    StateActive
    StateUnloading
    StateFailed
    StateGone
)
```

Intent：

```go
type Intent uint8

const (
    IntentMounted Intent = iota
    IntentUnmounted
)
```

State 与 Intent 必须分离。

例如：

```text
State = Active
Intent = Unmounted
```

代表：

> 当前还在 Active，但已经收到卸载请求。

不能立即把 State 改成 Gone。

必须经过：

```text
Active
  ↓
Unloading
  ↓
Gone
```

---

# 7. Activation

Activation 是一次 Fiber 生命周期实例。

```go
type activation struct {
    id         uint64
    generation uint64

    fiber *Fiber
    ctx   *Context

    cancel context.CancelFunc

    dependencySnapshot []DependencySnapshot

    mu sync.Mutex

    state activationState
}
```

例如：

```text
Fiber A

activation 1
    ↓
Active
    ↓
Unloading
    ↓
Gone

activation 2
    ↓
Loading
    ↓
Active
```

activation 1 和 activation 2 必须完全隔离。

尤其：

**activation 1 的异步 completion 绝对不能修改 activation 2。**

---

# 8. Activation Identity

所有异步生命周期 completion 必须携带：

```go
type ActivationID uint64
```

Command：

```go
type cmdApplyDone struct {
    fiberID      FiberID
    activationID ActivationID
    err          error
}

type cmdUnwindDone struct {
    fiberID      FiberID
    activationID ActivationID
    err          error
}
```

Orchestrator 收到 completion 后：

```go
fiber.activation.id == cmd.activationID
```

才允许处理。

否则：

```text
ignore stale completion
```

这是整个 Runtime 防止旧异步任务污染新生命周期的核心机制。

---

# 9. Runtime

```go
type Runtime struct {
    mu sync.RWMutex

    state RuntimeState

    fibers map[FiberID]*Fiber

    orchestrator *orchestrator

    providers *providerRegistry

    nextFiberID atomic.Uint64
}
```

Runtime：

```go
type RuntimeState uint8

const (
    RuntimeRunning RuntimeState = iota
    RuntimeClosing
    RuntimeClosed
)
```

公开 API：

```go
func New(opts ...Option) *Runtime

func (r *Runtime) Load(c Component) *Fiber

func (r *Runtime) Close(ctx context.Context) error
```

`Load` 在 Runtime Closing / Closed 后必须拒绝。

---

# 10. Orchestrator

Orchestrator 是 Runtime 的唯一 lifecycle decision domain。

内部：

```go
type orchestrator struct {
    runtime *Runtime

    commands chan command

    stop chan struct{}
    done chan struct{}
}
```

Command：

```go
type command interface {
    apply(*orchestrator)
}
```

Command 类型：

```text
cmdLoad
cmdDispose
cmdApplyDone
cmdUnwindDone
cmdProviderChanged
cmdDependencyChanged
cmdClose
```

原则：

> 所有生命周期状态决策都在 Orchestrator 中完成。

但是：

> Component Apply / Cleanup 绝对不能在 Orchestrator goroutine 中执行。

因此：

```text
Orchestrator
     │
     ├── decide Loading
     │
     ├── launch Apply goroutine
     │
     └── continue processing commands
```

Apply 完成：

```text
Apply goroutine
     │
     └── cmdApplyDone
              ↓
        Orchestrator
```

---

# 11. Transition Algorithm

核心逻辑：

```go
func (o *orchestrator) reconcile(f *Fiber)
```

其职责不是“执行某个操作”，而是：

> 根据当前事实计算 Fiber 下一步应该做什么。

逻辑：

```text
intent
+
state
+
dependency state
+
activation state
+
runtime state
        ↓
    next action
```

例如：

```text
Pending
Mounted
dependencies satisfied
        ↓
      Loading
```

然后：

```text
Loading
Apply running
        ↓
      wait
```

Apply 完成：

```text
Apply success
dependencies still satisfied
intent still Mounted
        ↓
      Active
```

如果 Apply 完成时依赖已经消失：

```text
Apply success
dependency lost
        ↓
    start unwind
```

而不是：

```text
Active
```

---

# 12. Dependency Safety Rule

必须新增一个实现级 invariant：

## Withdrawal Safety

如果 Provider P 即将退出：

```text
P → Unloading
```

那么所有依赖 P 的：

```text
Active
Loading
```

Fiber 必须先进入：

```text
Unloading
```

或者已经无法成为 Active。

因此：

```text
Dependent
    ↓
Unloading

Provider
    ↓
Unloading
```

不能反过来：

```text
Provider
    ↓
Unloading

Dependent
    ↓
Active
```

---

# 13. Dependency Withdrawal Ordering

当 Provider 消失时：

```text
Provider P
    │
    ├── Consumer A
    ├── Consumer B
    └── Consumer C
```

Orchestrator 必须首先计算 reverse dependency closure：

```text
P
↑
A B C
```

然后：

```text
A → Unloading
B → Unloading
C → Unloading
P → Unloading
```

对于多级依赖：

```text
P
↑
A
↑
B
```

退出顺序：

```text
B
↓
A
↓
P
```

即：

> Consumer-first withdrawal。

这是保证 Runtime 不产生“Provider 已撤销，但 Consumer 仍 Active”状态的关键。

---

# 14. Dependency Snapshot

Activation Loading 时建立 snapshot：

```go
type DependencySnapshot struct {
    Key      CapabilityKey
    Provider ProviderIdentity
}
```

Provider：

```go
type ProviderIdentity struct {
    FiberID     FiberID
    ActivationID uint64
}
```

这里不需要另外引入复杂的全局 generation。

一次 activation 本身就是 Provider generation。

---

# 15. Provider Registry

内部：

```go
type providerRegistry struct {
    mu sync.RWMutex

    providers map[CapabilityKey]*providerRecord
}
```

Provider：

```go
type providerRecord struct {
    key CapabilityKey

    identity ProviderIdentity

    owner *Fiber

    value any
}
```

Capability：

```go
type CapabilityKey struct {
    name reflect.Type
}
```

第一版可以使用泛型包装：

```go
type Key[T any] struct {
    name string
}
```

例如：

```go
var LoggerKey = NewKey[Logger]("logger")
```

但 Runtime 内部最终必须使用不可冲突的 typed key。

禁止：

```go
ctx.Get("logger")
```

推荐：

```go
logger, ok := Get(ctx, LoggerKey)
```

---

# 16. Provider Registration

Provider 注册必须原子完成：

```text
check key
     ↓
verify no duplicate
     ↓
insert provider
     ↓
create reversible registration
```

不能：

```text
if !exists {
    // unlock
    insert later
}
```

否则存在：

```text
Fiber A ─┐
         ├── check → no provider
Fiber B ─┘
         ├── check → no provider
         ↓
both insert
```

必须在同一 critical section 完成：

```text
check + insert
```

---

# 17. Duplicate Provider

默认 Capability 为 exclusive。

如果：

```text
Provider A → Logger
Provider B → Logger
```

则 B 注册失败：

```go
ErrDuplicateProvider
```

并且：

> Provider A 必须完全不受影响。

不能因为 B 的 Apply 失败而撤销 A。

---

# 18. Effect

Effect 是 Runtime reversible state mutation 的基本单元。

```go
type Effect struct {
    inverse func() error
}
```

但内部不能简单：

```go
install()
effects = append(effects, inverse)
```

因为存在并发 race：

```text
Apply
 │
 ├── install external resource
 │
 │       Context begins Unloading
 │
 └── install returns
```

如果这时 Context 已经开始 unwind：

> 必须仍然保证 inverse exactly once。

---

# 19. Effect Slot

内部使用两阶段 Effect Slot：

```go
type effectState uint8

const (
    effectInstalling effectState = iota
    effectCommitted
    effectUndoing
    effectUndone
)
```

```go
type effectSlot struct {
    seq uint64

    state effectState

    inverse func() error
}
```

Context：

```go
type Context struct {
    mu sync.Mutex

    state contextState

    effects []*effectSlot

    nextEffectSeq uint64
}
```

---

# 20. Effect Algorithm

调用：

```go
ctx.Effect(install)
```

步骤：

### Step 1

锁 Context。

```text
Context Active?
```

如果不是：

```text
reject
```

### Step 2

创建：

```text
effectSlot = Installing
```

加入 effects。

### Step 3

释放锁。

执行：

```go
inverse, err := install()
```

### Step 4

重新获取锁。

如果：

```text
Context still Active
```

则：

```text
Installing → Committed
```

如果：

```text
Context already Unwinding
```

则：

```text
Installing → Undoing
```

然后释放锁并立即执行 inverse。

这样可以保证：

```text
install
```

和：

```text
unwind
```

之间不会出现 effect 丢失。

---

# 21. Effect Exactly-Once

任何 Effect 最终只能进入：

```text
Committed
    ↓
Undoing
    ↓
Undone
```

或者：

```text
Installing
    ↓
install failed
    ↓
removed
```

禁止：

```text
Committed
    ↓
Undoing
    ↓
Undoing
```

因此每个 effect slot 必须拥有明确状态。

---

# 22. Effect Unwind

Cleanup 顺序：

```text
effect N
effect N-1
...
effect 2
effect 1
```

即：

```text
LIFO
```

执行 Cleanup 时：

```go
for i := len(effects)-1; i >= 0; i-- {
    cleanup(effects[i])
}
```

但：

> 第一个 cleanup error 不能终止后续 cleanup。

必须：

```text
cleanup A → error
cleanup B → success
cleanup C → error
```

最终：

```text
aggregate(errorA, errorC)
```

---

# 23. Context

Context 是一次 Activation 的资源作用域。

```go
type Context struct {
    runtime *Runtime

    fiberID      FiberID
    activationID uint64

    parent context.Context

    mu sync.Mutex

    state contextState

    effects []*effectSlot
}
```

Context 不应该成为万能 mutable map。

禁止：

```go
ctx.Set("foo", value)
ctx.Get("foo")
```

Context 的主要职责：

```text
Effect
Provide
Dependency resolution
Child ownership
Cancellation
```

---

# 24. Context Cancellation

每个 Activation：

```go
base, cancel := context.WithCancel(parent)
```

然后：

```go
ctx.Context()
```

Component 必须自行响应：

```go
select {
case <-ctx.Done():
    return ctx.Err()

case work := <-input:
    ...
}
```

Runtime：

**不允许 kill goroutine。**

---

# 25. Apply Execution

Orchestrator：

```text
Pending
 ↓
Loading
 ↓
create Activation
 ↓
create Context
 ↓
launch Apply
```

Apply：

```go
cleanup, err := component.Apply(ctx)
```

如果 Component 没有通过 `ctx.Effect` 注册资源，而直接返回：

```go
cleanup
```

Runtime 必须把它转成 Effect。

即：

```go
cleanup
```

自动成为 Activation 的最后一个 effect。

但是更推荐：

```go
ctx.Effect(...)
```

因为 Component 内部可能有多个独立 reversible operations。

---

# 26. Apply Failure

假设：

```text
Effect A committed
Effect B committed
Effect C install failed
```

那么：

```text
Apply failed
       ↓
unwind A/B
       ↓
Failed
```

必须保证：

```text
B cleanup
A cleanup
```

全部执行。

最终：

```go
StateFailed
```

而不是：

```text
StateGone
```

因为：

> Apply 本身从未成功进入 Active。

---

# 27. Failed Semantics

Failed 不自动无限重试。

```text
Failed + Mounted
        ↓
remain Failed
```

重新尝试必须由：

```text
Reload
Retry
Config reconciliation
```

等上层机制显式触发。

这样避免：

```text
Apply fails
 ↓
retry
 ↓
fails
 ↓
retry
 ↓
...
```

形成 Runtime 内部无限重试。

---

# 28. Dependency Loss

Active Fiber：

```text
A Active
  ↓
Provider P disappears
```

A：

```text
Active
 ↓
Unloading
 ↓
Gone
```

如果 Intent 仍然是 Mounted：

```text
Gone
 ↓
dependency unavailable
 ↓
Pending
```

Provider 恢复：

```text
Pending
 ↓
Loading
 ↓
Active
```

因此：

> Dependency loss 是 lifecycle condition，不是 Apply failure。

---

# 29. Provider Replacement

默认 replacement：

```text
Old Provider
    ↓
Unloading
    ↓
Gone

New Provider
    ↓
Loading
    ↓
Active
```

如果存在 Consumer：

```text
Consumer
    ↓
Unloading
```

必须先于 Old Provider。

因此：

```text
Consumer
 ↓
Old Provider
 ↓
New Provider
 ↓
Consumer
```

最终 Consumer 会重新绑定 New Provider。

---

# 30. Stable Registry

对于动态集合：

```text
Tool A
Tool B
Tool C
```

不要让每个 Tool 成为独立 exclusive provider。

应该：

```text
ToolRegistry
    │
    ├── A
    ├── B
    └── C
```

Registry 本身稳定。

成员变化：

```text
Add A
Remove A
Add B
Remove B
```

只是 Registry 内部 reversible membership mutation。

因此：

```text
Consumer
    ↓
ToolRegistry
```

Consumer 不需要因为：

```text
Tool A removed
```

而重新加载。

这是后续 Agent / MCP / WASM Tool 动态系统的关键机制。

---

# 31. Ownership

Fiber：

```go
type Ownership struct {
    owner FiberID

    children map[FiberID]struct{}
}
```

明确区分：

```text
dependency
```

和：

```text
ownership
```

Dependency：

> 我需要你。

Ownership：

> 我的生命周期包含你。

二者绝对不能混为一谈。

---

# 32. Child Fiber

创建 Child：

```go
child := parent.Child(component)
```

表示：

```text
parent owns child
```

Parent Unload：

```text
child → Unloading
child → Gone
parent → Gone
```

但是：

```go
runtime.Load(component)
```

创建的 Root Fiber 不自动属于调用者。

---

# 33. Ownership Constraint

Parent 只有在：

```text
all owned children == Gone
```

之后才能：

```text
Parent → Gone
```

如果 child cleanup 尚未完成：

```text
Parent remains Unloading
```

不能提前 Gone。

---

# 34. Wait API

不要让调用者直接读取 channel。

提供：

```go
func (f *Fiber) Ready(ctx context.Context) error
func (f *Fiber) Gone(ctx context.Context) error
```

语义：

### Ready

成功条件：

```text
State == Active
```

失败条件：

```text
State == Failed
State == Gone
```

### Gone

成功条件：

```text
State == Gone
```

如果 cleanup 产生 error：

```text
Gone + error
```

仍然代表 Fiber 已经退出。

---

# 35. Wait Implementation

不能简单：

```go
stateCh <- state
```

否则存在 missed wakeup。

推荐：

```go
type stateSignal struct {
    mu sync.Mutex

    version uint64

    ch chan struct{}
}
```

状态发生变化：

```text
lock
version++
close(old ch)
ch = make(chan struct{})
unlock
```

Wait：

```text
read state + channel under lock
unlock
wait channel
retry
```

这是典型 condition-generation 模式。

---

# 36. Dispose Idempotency

：

```go
f.Dispose()
f.Dispose()
f.Dispose()
```

必须等价于一次：

```text
Intent = Unmounted
```

不能：

```text
second Dispose
    ↓
second cleanup
```

Fiber cleanup exactly once。

---

# 37. Concurrent Load / Dispose

例如：

```text
goroutine A: Load()
goroutine B: Dispose()
```

必须有明确 linearization point。

允许：

```text
Load wins
    ↓
Loading
    ↓
Dispose
    ↓
Unloading
```

也允许：

```text
Dispose wins
    ↓
Gone
```

但不能产生：

```text
Active
+
Intent Mounted
+
Dispose already completed
```

这种不一致状态。

最新 Intent 必须最终生效。

---

# 38. Stale Completion

最重要的并发测试：

```text
Activation 1
    ↓
Loading
    ↓
Apply running

Dispose
    ↓
Unloading
    ↓
Activation 1 Gone

Load again
    ↓
Activation 2
    ↓
Loading
```

此时：

```text
Activation 1 Apply completion
```

晚到：

```text
cmdApplyDone(
    Fiber = A,
    Activation = 1,
)
```

Orchestrator：

```text
current activation == 2
```

所以：

```text
ignore
```

绝对不能：

```text
Activation 2 → Active
```

或者：

```text
Activation 2 → Failed
```

---

# 39. Runtime Close

Runtime：

```text
Running
 ↓
Closing
 ↓
Closed
```

进入 Closing 后：

```text
reject new Load
```

然后：

```text
all mounted Fibers
       ↓
Intent Unmounted
       ↓
dependency-aware withdrawal
       ↓
all Gone
       ↓
orchestrator drain
       ↓
Closed
```

Runtime 不强杀 Component goroutine。

如果 Component 不响应 cancellation：

```text
Close(ctx)
```

可以超时返回。

但 Runtime 必须明确：

> timeout 不等于已经安全 Closed。

---

# 40. Runtime Fatal Error

Component failure：

```text
Apply error
Cleanup error
Dependency loss
```

全部属于正常 Runtime domain。

Runtime fatal 只用于：

```text
internal invariant corruption
```

例如：

```text
same effect undone twice
fiber activation identity corruption
impossible lifecycle state
provider registry internal corruption
```

因此：

```go
panic
```

只应该出现在真正违反 Runtime 内部 invariant 的位置，而不是 Component 抛错。

---

# 41. Error Model

定义：

```go
var (
    ErrRuntimeClosed       = errors.New("runtime closed")
    ErrDuplicateProvider   = errors.New("duplicate provider")
    ErrDependencyMissing   = errors.New("dependency missing")
    ErrInvalidState        = errors.New("invalid fiber state")
    ErrContextClosed       = errors.New("context closed")
    ErrInvariantViolation  = errors.New("runtime invariant violation")
)
```

错误需要能够判断：

```go
errors.Is(err, ErrDuplicateProvider)
```

不能只依赖字符串。

---

# 42. First Implementation Order

不要一次实现所有东西。

严格按照下面顺序：

## Phase 1 — Pure State

实现：

```text
state.go
component.go
fiber.go
```

只验证：

```text
state transition
intent
activation identity
```

暂时没有真正 Component execution。

---

## Phase 2 — Orchestrator

实现：

```text
runtime.go
internal/orchestrator/*
```

首先实现：

```text
Load
Dispose
Pending
Loading
Active
Unloading
Gone
Failed
```

使用 Fake Component。

---

## Phase 3 — Context + Effect

实现：

```text
context.go
effect.go
internal/effects/*
```

验证：

```text
LIFO
partial apply cleanup
cleanup aggregation
effect exactly once
late install
```

---

## Phase 4 — Provider

实现：

```text
key.go
provider.go
dependency.go
internal/providers/*
```

验证：

```text
Provide
Resolve
duplicate provider
provider disappearance
provider recovery
provider identity
```

---

## Phase 5 — Dependency Ordering

实现：

```text
dependency graph
consumer-first withdrawal
provider replacement
```

这一阶段非常关键。

---

## Phase 6 — Ownership

实现：

```text
ownership.go
```

验证：

```text
parent
child
cascade
child cleanup
parent waiting
```

---

## Phase 7 — Concurrency

实现：

```text
stale completion
Load/Dispose race
provider change during Apply
provider change during Unwind
Close race
```

然后：

```bash
go test -race ./...
```

必须通过。

---

# 43. Mandatory Contract Tests

第一批必须存在以下测试。

### TestDuplicateProvider

```text
A provides X
B provides X
```

要求：

```text
A Active
B Failed
X → A
```

---

### TestPartialApplyCleanup

```text
Effect A success
Effect B success
Effect C failure
```

要求：

```text
B cleanup
A cleanup
```

---

### TestCleanupContinuesAfterError

```text
A cleanup error
B cleanup success
C cleanup error
```

要求：

```text
A executed
B executed
C executed
```

最终：

```text
aggregated error
```

---

### TestDependencyLoss

```text
P Active
C Active → depends P

P removed
```

要求：

```text
C unload first
P unload second
```

---

### TestDependencyRecovery

```text
P Gone
C Pending

P restored
```

要求：

```text
P Active
C Active
```

---

### TestProviderReplacement

```text
P1 → X
C → X

P1 removed
P2 → X
```

要求：

```text
C does not remain bound to P1
C eventually binds P2
```

---

### TestStaleCompletion

```text
Activation 1 Apply
Activation 1 Gone
Activation 2 Loading

Activation 1 completion arrives
```

要求：

```text
Activation 2 unchanged
```

---

### TestEffectLateCommit

模拟：

```text
install starts
 ↓
unwind starts
 ↓
install returns
```

要求：

```text
inverse executes exactly once
```

---

### TestIdempotentDispose

```text
Dispose()
Dispose()
Dispose()
```

要求：

```text
cleanup exactly once
```

---

### TestOwnedChild

```text
Parent owns Child
Parent Dispose
```

要求：

```text
Child Gone
before
Parent Gone
```

---

# 44. Property Tests

第一版至少实现：

```text
Preservation
Recovery
Ordering
Progress
Confluence
```

其中 Confluence 不要求所有外部世界状态一致，只验证：

> 在合法 Composition、有限操作、Runtime-managed observable state 下，不同合法调度最终收敛到等价状态。

---

# 45. Deterministic Test Hooks

为了测试并发 race，Runtime 应提供内部测试 hook：

```go
type testHooks struct {
    beforeApply   func(FiberID, ActivationID)
    afterApply    func(FiberID, ActivationID)
    beforeUnwind  func(FiberID, ActivationID)
    afterUnwind   func(FiberID, ActivationID)
}
```

生产 API 不暴露。

测试可以人为制造：

```text
Apply started
 ↓
pause
 ↓
Dispose
 ↓
Unloading
 ↓
resume Apply
```

这样才能稳定复现 stale completion，而不是依赖：

```go
time.Sleep(...)
```

禁止使用 sleep 驱动并发测试。

---

# 46. Implementation Invariants

实现过程中任何时候都必须保持：

### I1

一个 Fiber 同时最多一个 Activation。

### I2

一个 Activation 最多一个 Apply。

### I3

同一个 Activation 的 Apply 和 Unwind 不得同时执行。

### I4

一个 Effect inverse 最多执行一次。

### I5

所有 Runtime-managed Effect 都属于唯一 Activation。

### I6

旧 Activation completion 不得修改新 Activation。

### I7

Provider registration 与 removal 必须成对。

### I8

Provider withdrawal 前，依赖它的 Consumer 必须已经开始 withdrawal。

### I9

Parent Gone 前，Owned Child 必须 Gone。

### I10

Component 不能直接修改 Fiber lifecycle state。

### I11

Component 不能直接修改 Provider Registry。

### I12

Runtime mutex / Orchestrator decision lock 中禁止执行用户代码。

### I13

Runtime 不允许强杀 goroutine。

### I14

Dispose 必须幂等。

### I15

Cleanup 发生错误时仍必须继续执行剩余 cleanup。

---

# 47. First Milestone

第一阶段不要实现：

```text
WASM
HMR
Config
Event Bus
Registry Extension
MCP
LLM
Agent
```

只实现：

```text
Runtime
Component
Fiber
Activation
Context
Effect
Provider
Dependency
Ownership
Orchestrator
```

达到：

```text
go test ./...
go test -race ./...
```

并通过全部 Kernel Contract Tests。

---

# 48. Definition of Done

Kernel v0.1 完成的标准不是：

> “可以加载插件。”

而是必须证明：

```text
Component
   ↓
Fiber
   ↓
Activation
   ↓
Context
   ↓
Effect / Provider
   ↓
Active
   ↓
Dependency loss
   ↓
Consumer withdrawal
   ↓
Provider withdrawal
   ↓
Recovery
   ↓
new Activation
   ↓
Active
```

整个过程中：

```text
no leaked Runtime-managed effects
no stale completion corruption
no duplicate cleanup
no orphan owned child
no invalid provider binding
no lifecycle race
```

并且：

```bash
go test -race ./...
```

通过。

---

# 49. 下一阶段

完成本规格后，下一份文档不应该再讨论架构。

应该直接进入：

**Kernel v0.1 Implementation Plan**

内容包括：

1. 每个 Go 文件具体实现顺序
2. 每个 struct 的最终字段
3. 每个 public API 的完整签名
4. Orchestrator command 的完整定义
5. `reconcile()` 伪代码
6. Dependency graph 算法
7. Effect Slot 的完整状态机
8. Provider Registry 的并发算法
9. Wait/Signal 的实现
10. 逐测试实现顺序
11. 最小可运行 Demo
12. Codex/Claude Code 的逐阶段 coding instructions

最终目标是做到：

> **把下一份文档直接交给 Codex，让它从空目录开始实现，而不是让 AI 自己重新设计 Runtime。**
