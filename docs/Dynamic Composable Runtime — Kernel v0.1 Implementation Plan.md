# Dynamic Composable Runtime
## Kernel v0.1 Implementation Plan

## 1. Implementation Strategy

严格遵循：

```text
Domain Types
    ↓
Fiber + Activation
    ↓
Context + Effect
    ↓
Provider
    ↓
Dependency Graph
    ↓
Orchestrator
    ↓
Concurrency
    ↓
Contract Tests
    ↓
Property Tests
```

不要一开始实现完整 Runtime。

每完成一个阶段：

```bash
go test ./...
go test -race ./...
```

必须通过后再进入下一阶段。

---

# 2. Initial Project

创建：

```text
dynamic-runtime/
├── go.mod
└── runtime/
```

`go.mod`：

```go
module dynamic-runtime
```

第一版不引入第三方依赖。

仅使用：

```text
context
errors
fmt
reflect
sync
sync/atomic
```

以及 Go 标准库。

---

# 3. Domain Types

## 3.1 FiberID

```go
type FiberID uint64
```

ID：

- Runtime 内唯一
- 单调递增
- 0 保留为 invalid

---

## 3.2 ActivationID

```go
type ActivationID uint64
```

每次 Fiber 激活产生新的 ActivationID。

例如：

```text
Fiber 10

Activation 1
    ↓
Gone

Activation 2
    ↓
Active
```

ActivationID 不能重复。

---

# 4. State

实现：

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

提供：

```go
func (s FiberState) String() string
func (i Intent) String() string
```

必须覆盖所有状态。

---

# 5. Component API

第一版：

```go
type Component interface {
    Name() string
    Inject() []Dependency
    Provide() []Capability
    Apply(*Context) (Cleanup, error)
}
```

```go
type Cleanup func() error
```

注意：

`Inject()` 和 `Provide()` 是声明。

实际 Provider registration 必须发生在 Activation 的 Context 中。

因此：

```text
Component.Provide()
```

只描述“我可能提供什么”。

真正注册：

```text
ctx.Provide(...)
```

---

# 6. Capability Key

采用泛型 Key：

```go
type Key[T any] struct {
    name string
}
```

构造：

```go
func NewKey[T any](name string) Key[T]
```

内部 identity 不能只依赖字符串。

推荐：

```go
type capabilityID struct {
    typeID reflect.Type
    name   string
}
```

这样：

```go
NewKey[Logger]("logger")
```

与：

```go
NewKey[Database]("logger")
```

不会冲突。

---

# 7. Dependency

```go
type Dependency struct {
    Key CapabilityKey
}
```

内部统一：

```go
type CapabilityKey struct {
    typeID reflect.Type
    name   string
}
```

第一版只支持：

```text
required dependency
```

不实现：

```text
optional
multiple
fallback
priority
```

这些属于后续扩展。

---

# 8. Provider Identity

```go
type ProviderIdentity struct {
    FiberID      FiberID
    ActivationID ActivationID
}
```

Provider：

```go
type providerRecord struct {
    key      CapabilityKey
    identity ProviderIdentity
    owner    FiberID
    value    any
}
```

原则：

> Provider Identity 表示“谁在这一轮 Activation 中提供了这个 Capability”。

因此 Provider replacement：

```text
A / Activation 1
        ↓
A / Activation 2
```

一定被认为是新的 Provider。

---

# 9. Fiber Internal Structure

推荐最终结构：

```go
type Fiber struct {
    id        FiberID
    component Component
    runtime   *Runtime

    mu sync.RWMutex

    state  FiberState
    intent Intent

    generation ActivationID

    activation *activation

    parent *Fiber

    children map[FiberID]*Fiber

    signal *stateSignal
}
```

其中：

```go
type activation struct {
    id ActivationID

    fiber *Fiber

    ctx    *Context
    cancel context.CancelFunc

    dependencies []DependencySnapshot

    mu    sync.Mutex
    state activationState
}
```

Activation 是短生命周期对象。

Fiber 是长期对象。

---

# 10. Activation State

```go
type activationState uint8

const (
    activationLoading activationState = iota
    activationActive
    activationUnwinding
    activationGone
)
```

禁止直接从 Component 修改。

---

# 11. Runtime

```go
type Runtime struct {
    mu sync.RWMutex

    state RuntimeState

    fibers map[FiberID]*Fiber

    providers *providerRegistry

    orch *orchestrator

    nextFiberID     atomic.Uint64
    nextActivationID atomic.Uint64
}
```

Runtime State：

```go
type RuntimeState uint8

const (
    RuntimeRunning RuntimeState = iota
    RuntimeClosing
    RuntimeClosed
)
```

---

# 12. Runtime.Load

```go
func (r *Runtime) Load(component Component) *Fiber
```

行为：

```text
Runtime Running
    ↓
create Fiber
    ↓
intent = Mounted
    ↓
send cmdLoad
```

注意：

`Load()` 不等待 Active。

因此：

```go
fiber := runtime.Load(component)
```

立即返回。

调用者如果需要等待：

```go
err := fiber.Ready(ctx)
```

---

# 13. Runtime.Load After Close

如果：

```text
RuntimeClosing
RuntimeClosed
```

禁止创建新的 Fiber。

由于 `Load()` 返回 `*Fiber`，推荐：

```go
func (r *Runtime) Load(component Component) (*Fiber, error)
```

因此最终 API 应修改为：

```go
func (r *Runtime) Load(component Component) (*Fiber, error)
```

这是实现阶段应该正式修正的 v0.1 API。

---

# 14. Fiber.Dispose

```go
func (f *Fiber) Dispose() error
```

本质：

```text
intent = Unmounted
```

而不是直接：

```text
state = Gone
```

发送：

```go
cmdDispose{
    fiberID: f.id,
}
```

Dispose 必须幂等。

---

# 15. Command Definition

```go
type command interface {
    apply(*orchestrator)
}
```

定义：

```go
type cmdLoad struct {
    fiberID FiberID
}

type cmdDispose struct {
    fiberID FiberID
}

type cmdApplyDone struct {
    fiberID      FiberID
    activationID ActivationID
    cleanup      Cleanup
    err          error
}

type cmdUnwindDone struct {
    fiberID      FiberID
    activationID ActivationID
    err          error
}

type cmdProviderChanged struct {
    key CapabilityKey
}

type cmdClose struct {
    done chan error
}
```

---

# 16. Orchestrator Main Loop

核心：

```go
func (o *orchestrator) run() {
    defer close(o.done)

    for {
        select {
        case cmd := <-o.commands:
            cmd.apply(o)

        case <-o.stop:
            return
        }
    }
}
```

但最终实现不能让 command 自己无限递归触发 reconcile。

推荐：

```text
receive command
    ↓
mutate decision state
    ↓
collect actions
    ↓
execute scheduling
```

用户代码永远在 Orchestrator 外执行。

---

# 17. Reconcile

核心：

```go
func (o *orchestrator) reconcile(f *Fiber)
```

伪代码：

```text
if runtime closing:
    desired = Unmounted

switch state:

Pending:
    if intent == Unmounted:
        → Gone
    else if dependencies satisfied:
        → Loading
    else:
        stay Pending

Loading:
    if intent == Unmounted:
        cancel activation
        → Unloading
    else if dependencies lost:
        cancel activation
        → Unloading
    else:
        wait Apply completion

Active:
    if intent == Unmounted:
        → Unloading
    else if dependencies lost:
        → Unloading
    else:
        stay Active

Unloading:
    wait unwind completion

Failed:
    if intent == Unmounted:
        → Gone
    else:
        stay Failed

Gone:
    if intent == Mounted && dependencies satisfied:
        → Loading
    else:
        stay Gone
```

---

# 18. Start Activation

从 Pending → Loading：

```text
allocate ActivationID
create activation
create Context
capture dependency snapshot
transition Loading
launch Apply
```

顺序非常重要：

```text
1. determine dependencies
2. create activation
3. create context
4. register activation
5. set Loading
6. launch Apply
```

不能先启动 Apply 再记录 Activation。

---

# 19. Apply Launch

不能：

```go
o.runApply()
```

因为会阻塞 Orchestrator。

必须：

```go
go func() {
    cleanup, err := component.Apply(ctx)

    o.commands <- cmdApplyDone{
        fiberID:      fiber.id,
        activationID: activation.id,
        cleanup:      cleanup,
        err:          err,
    }
}()
```

但生产实现必须考虑 Runtime Closing 后 command channel 已关闭的问题。

因此：

> Command channel 生命周期不能由单个 Apply goroutine 直接假定。

推荐 Runtime 使用：

```go
submit(command)
```

统一提交函数。

---

# 20. Safe Command Submission

内部：

```go
func (r *Runtime) submit(cmd command) bool
```

必须保证：

```text
Running
Closing
Closed
```

情况下不会向已经关闭的 channel 发送导致 panic。

第一版推荐：

**永远不要 close commands channel。**

Runtime 只通过：

```text
stop
```

结束 Orchestrator。

这样晚到的 Apply completion 可以安全进入 submit 层并被丢弃。

---

# 21. Stale Completion

收到：

```go
cmdApplyDone{
    fiberID: 10,
    activationID: 1,
}
```

首先：

```text
fiber.activation.id == 1 ?
```

如果不是：

```text
ignore
```

如果 Fiber 已经：

```text
Gone
```

同样 ignore。

绝不能修改当前 Activation。

---

# 22. Apply Completion

如果：

```text
Apply success
```

不能立即：

```text
Loading → Active
```

必须再次验证：

```text
Intent == Mounted
dependencies still identical
activation still current
runtime still valid
```

全部满足：

```text
Loading → Active
```

否则：

```text
Loading → Unloading
```

并进入 cleanup。

这是 Runtime 正确处理并发变化的关键。

---

# 23. Dependency Validation

Activation 保存：

```go
type DependencySnapshot struct {
    key      CapabilityKey
    provider ProviderIdentity
}
```

Apply 完成后：

```text
current provider
       ↓
compare snapshot
```

结果：

```text
same identity
    ↓
valid
```

或者：

```text
missing
changed identity
    ↓
invalid
```

---

# 24. Dependency Graph

Provider Registry 之外需要维护：

```go
type dependencyGraph struct {
    mu sync.RWMutex

    consumers map[CapabilityKey]map[FiberID]struct{}
}
```

例如：

```text
Logger
 ↑
Agent
 ↑
Session
```

表示：

```text
Agent depends Logger
Session depends Agent
```

Provider 变化时：

```text
Logger changed
```

立即找到：

```text
Agent
```

然后继续：

```text
Agent → Session
```

形成 reverse dependency closure。

---

# 25. Dependency Registration Timing

Dependency edge 应该在 Activation Loading 时登记。

```text
Fiber A Loading
    ↓
capture dependencies
    ↓
graph[A] = dependencies
```

当 Activation Gone：

```text
remove graph edges
```

不能留下旧 Activation 的 dependency edge。

否则会产生：

```text
ghost dependency
```

---

# 26. Provider Withdrawal Algorithm

Provider P 即将撤销：

```text
find all consumers of P
        ↓
recursive dependent closure
        ↓
sort reverse-topologically
        ↓
request Unloading
        ↓
wait consumers withdrawn
        ↓
withdraw P
```

简单 DAG：

```text
P
↑
A
↑
B
```

顺序：

```text
B
A
P
```

---

# 27. Dependency Cycles

第一版必须明确：

**禁止强依赖环。**

例如：

```text
A → B
B → A
```

必须在 Dependency Graph 建立时检测。

返回：

```go
ErrDependencyCycle
```

原因：

否则无法定义：

```text
consumer-first withdrawal
```

以及稳定 activation ordering。

---

# 28. Effect API

公开：

```go
func (c *Context) Effect(
    install func() (func() error, error),
) error
```

例如：

```go
err := ctx.Effect(func() (func() error, error) {
    resource, err := openResource()
    if err != nil {
        return nil, err
    }

    return func() error {
        return resource.Close()
    }, nil
})
```

---

# 29. Effect Internal

```go
type effectSlotState uint8

const (
    effectInstalling effectSlotState = iota
    effectCommitted
    effectUndoing
    effectUndone
)
```

```go
type effectSlot struct {
    seq uint64

    state effectSlotState

    inverse func() error
}
```

---

# 30. Effect Installation

算法：

```text
lock Context

if Context != Active:
    reject

create Installing slot
append slot

unlock

run install()

lock

if install failed:
    remove slot
    unlock
    return error

if Context Active:
    slot.inverse = inverse
    slot.state = Committed
    unlock
    return nil

Context already Unwinding:
    slot.inverse = inverse
    slot.state = Undoing
    unlock

execute inverse

mark Undone
```

这里的核心保证：

```text
install completed
```

之后资源一定有唯一的 cleanup owner。

---

# 31. Context Unwind

进入：

```text
Unwinding
```

后：

```text
Context state = Closing
```

禁止新的 Effect。

然后：

```text
snapshot effect slots
```

按照：

```text
seq DESC
```

执行 cleanup。

注意：

**不能持有 Context mutex 执行 inverse。**

---

# 32. Cleanup Error

定义：

```go
type CleanupError struct {
    Errors []error
}
```

实现：

```go
func (e *CleanupError) Error() string
func (e *CleanupError) Unwrap() []error
```

如果 Go 版本支持 `errors.Join`，直接使用。

例如：

```text
cleanup C → error
cleanup B → success
cleanup A → error
```

最终：

```go
errors.Join(errC, errA)
```

但所有 cleanup 都必须执行。

---

# 33. Provider Registration

Context：

```go
func (c *Context) Provide[T any](
    key Key[T],
    value T,
) error
```

内部：

```text
Context
 ↓
Runtime Provider Registry
```

Provider registration 本身必须成为 Effect：

```text
register provider
       ↓
record inverse
       ↓
unregister provider
```

这样：

```text
Activation unload
```

自动撤销 Provider。

---

# 34. Provider Removal

撤销时必须验证 identity：

```text
key
+
FiberID
+
ActivationID
```

只有完全匹配：

```text
remove
```

否则：

```text
do nothing
```

这是防止旧 Activation cleanup 删除新 Activation Provider 的第二道防线。

---

# 35. Provider Race Example

必须能够正确处理：

```text
A activation 1
    ↓
Provider X

A unload
    ↓
provider removal pending

A activation 2
    ↓
Provider X
```

旧 cleanup 到达时：

```text
current Provider X = A / activation 2
```

所以：

```text
old removal ignored
```

绝不能把 activation 2 的 Provider 删除。

---

# 36. Ownership Implementation

Fiber：

```go
parent *Fiber
children map[FiberID]*Fiber
```

Child 创建：

```go
func (c *Context) Child(component Component) (*Fiber, error)
```

Child Fiber：

```text
owner = current Fiber
```

Parent unload：

```text
children first
parent last
```

---

# 37. Ownership vs Dependency

必须保持两个完全独立的图：

```text
Ownership Graph
A
└── B

Dependency Graph
B
└── requires C
```

不能使用：

```text
parent
```

推断：

```text
dependency
```

也不能使用：

```text
dependency
```

推断：

```text
ownership
```

---

# 38. Wait Implementation

```go
type stateSignal struct {
    mu sync.Mutex

    version uint64
    ch      chan struct{}
}
```

状态变化：

```text
lock
old := ch
version++
ch = make(chan struct{})
close(old)
unlock
```

Wait：

```text
loop:
    lock
    inspect state
    ch := signal.ch
    unlock

    if target reached:
        return

    select {
    case <-ch:
    case <-ctx.Done():
        return ctx.Err()
    }
```

这样不会产生 missed wakeup。

---

# 39. Fiber.Ready

```go
func (f *Fiber) Ready(ctx context.Context) error
```

返回：

```text
Active → nil

Failed → failure
Gone → ErrFiberGone
context canceled → ctx.Err()
```

---

# 40. Fiber.Gone

```go
func (f *Fiber) Gone(ctx context.Context) error
```

返回：

```text
Gone → cleanup error / nil
```

如果 Fiber 已经 Gone，即使 cleanup 有错误：

```text
Gone == true
```

仍然成立。

---

# 41. Close Algorithm

```text
Runtime Running
       ↓
Closing
       ↓
all root Fibers intent = Unmounted
       ↓
ownership withdrawal
       ↓
dependency withdrawal
       ↓
all Fibers Gone
       ↓
stop orchestrator
       ↓
Closed
```

不允许：

```text
Close
 ↓
kill goroutines
```

---

# 42. Close Timeout

如果：

```go
ctx, cancel := context.WithTimeout(...)
defer cancel()

runtime.Close(ctx)
```

超时：

```text
return ctx.Err()
```

但 Runtime 可能仍处于：

```text
Closing
```

不能错误地设置：

```text
Closed
```

只有真正 drain 完成才能：

```text
Closed
```

---

# 43. First End-to-End Example

第一版 Demo 只实现：

```text
Logger Component
Database Component
Service Component
```

结构：

```text
Logger
   ↑
Database
   ↑
Service
```

启动：

```text
Logger Loading
Database Loading
Service Pending

Logger Active
Database Loading
Service Pending

Database Active
Service Loading

Service Active
```

卸载 Logger：

```text
Service Unloading
Database Unloading
Logger Unloading
```

恢复：

```text
Logger Active
Database Loading
Service Pending

Database Active
Service Loading

Service Active
```

这个 Demo 必须跑通。

---

# 44. Test 1 — Basic Lifecycle

验证：

```text
Load
Pending
Loading
Active
Dispose
Unloading
Gone
```

---

# 45. Test 2 — Apply Failure

Component：

```text
Apply
 ↓
Effect A
 ↓
Effect B
 ↓
error
```

验证：

```text
B cleanup
A cleanup
Failed
```

---

# 46. Test 3 — Dependency Pending

```text
Service requires DB
DB absent
```

验证：

```text
Service Pending
```

而不是：

```text
Service Failed
```

---

# 47. Test 4 — Dependency Recovery

```text
Service Pending
DB loaded
```

验证：

```text
DB Active
Service Active
```

---

# 48. Test 5 — Dependency Withdrawal

```text
DB Active
Service Active
```

Dispose DB：

```text
Service Unloading
Service Gone
DB Unloading
DB Gone
```

---

# 49. Test 6 — Provider Replacement

```text
Provider A / activation 1
Consumer C
```

A replacement：

```text
A1 Gone
A2 Active
C reactivated
```

C 最终必须：

```text
ProviderIdentity == A2
```

---

# 50. Test 7 — Stale Apply

人为暂停：

```text
A1 Apply
```

然后：

```text
Dispose
Load
A2 Apply
```

再释放：

```text
A1 Apply
```

要求：

```text
A1 completion ignored
A2 unaffected
```

---

# 51. Test 8 — Effect Race

构造：

```text
install starts
        ↓
pause
        ↓
begin Unwind
        ↓
resume install
```

要求：

```text
inverse exactly once
```

---

# 52. Test 9 — Provider Race

构造：

```text
A1 provider X
A1 unload
A2 provider X
A1 cleanup arrives late
```

要求：

```text
X belongs to A2
```

---

# 53. Test 10 — Ownership

```text
Parent
└── Child
```

Dispose Parent：

```text
Child Unloading
Child Gone
Parent Unloading
Parent Gone
```

---

# 54. Test 11 — Duplicate Provider

```text
A provides X
B provides X
```

要求：

```text
A Active
B Failed
```

A 的 Provider 必须继续存在。

---

# 55. Test 12 — Cleanup Aggregation

三个 cleanup：

```text
A → error
B → success
C → error
```

要求：

```text
A executed
B executed
C executed
```

并且：

```go
errors.Is(result, errA)
errors.Is(result, errC)
```

均成立。

---

# 56. Test 13 — Dispose Idempotency

并发：

```go
go f.Dispose()
go f.Dispose()
go f.Dispose()
go f.Dispose()
```

要求：

```text
cleanup count == 1
state == Gone
```

---

# 57. Test 14 — Load / Dispose Race

随机：

```text
Load
Dispose
Load
Dispose
```

最终状态必须与：

```text
latest intent
```

一致。

---

# 58. Test 15 — Close Race

并发：

```text
Load
Load
Dispose
Close
Load
```

要求：

```text
Close 后 Load rejected
所有已接受 Fiber 最终 Gone
Runtime eventually Closed
```

---

# 59. Race Testing

所有测试：

```bash
go test -race ./...
```

必须通过。

特别关注：

```text
Fiber.state
Fiber.activation
Context.effects
Provider registry
Dependency graph
stateSignal
Runtime state
```

---

# 60. 不允许的实现方式

Codex 实现时禁止：

```text
time.Sleep() 驱动 lifecycle
```

禁止：

```text
global mutable registry
```

禁止：

```text
goroutine kill
```

禁止：

```text
mutex 中执行 Component.Apply
```

禁止：

```text
mutex 中执行 Cleanup
```

禁止：

```text
Component 直接修改 Fiber.state
```

禁止：

```text
Provider map 暴露给 Component
```

禁止：

```text
复用旧 Context
```

禁止：

```text
仅凭 FiberID 判断 Provider ownership
```

必须同时判断：

```text
FiberID + ActivationID
```

---

# 61. Codex Implementation Order

Codex 必须按照以下顺序执行。

### Step 1

创建：

```text
go.mod
runtime/state.go
runtime/error.go
```

实现 Domain Types。

---

### Step 2

实现：

```text
runtime/component.go
runtime/key.go
runtime/dependency.go
```

只实现声明模型。

---

### Step 3

实现：

```text
runtime/fiber.go
runtime/runtime.go
```

先不执行 Component。

---

### Step 4

实现：

```text
runtime/internal/wait
```

然后测试：

```text
Ready
Gone
```

---

### Step 5

实现：

```text
runtime/internal/orchestrator
```

先实现：

```text
Load
Dispose
state transition
```

---

### Step 6

实现：

```text
runtime/context.go
runtime/effect.go
runtime/internal/effects
```

---

### Step 7

实现：

```text
runtime/provider.go
runtime/internal/providers
```

---

### Step 8

实现：

```text
dependency graph
```

---

### Step 9

加入：

```text
consumer-first withdrawal
```

---

### Step 10

加入：

```text
ownership
```

---

### Step 11

加入：

```text
stale completion
provider identity validation
```

---

### Step 12

实现：

```text
Runtime.Close
```

---

### Step 13

执行：

```bash
go test ./...
go test -race ./...
```

---

# 62. 最小验收标准

当以下程序能够稳定运行：

```go
rt, _ := runtime.New()

db, _ := rt.Load(Database{})
service, _ := rt.Load(Service{})

service.Ready(ctx)

db.Dispose()

service.Gone(ctx)

rt.Close(ctx)
```

并且 Runtime 正确处理：

```text
Service dependency loss
Service withdrawal
DB withdrawal
cleanup
state transition
```

Kernel v0.1 才算完成。

---

# 63. 最终 API 目标

第一版最终公开 API 控制在：

```go
type Runtime struct

func New(opts ...Option) (*Runtime, error)

func (r *Runtime) Load(
    component Component,
) (*Fiber, error)

func (r *Runtime) Close(
    ctx context.Context,
) error
```

```go
type Fiber struct

func (f *Fiber) ID() FiberID
func (f *Fiber) Name() string
func (f *Fiber) State() FiberState

func (f *Fiber) Dispose() error

func (f *Fiber) Ready(
    ctx context.Context,
) error

func (f *Fiber) Gone(
    ctx context.Context,
) error
```

```go
type Context struct

func (c *Context) Context() context.Context

func (c *Context) Effect(
    install func() (func() error, error),
) error
```

Provider / dependency API：

```go
func Provide[T any](
    c *Context,
    key Key[T],
    value T,
) error

func Require[T any](
    c *Context,
    key Key[T],
) (T, error)
```

---

# 64. Important API Decision

第一版**不要过度公开内部 Runtime 原语**。

例如不要公开：

```go
Fiber.ForceState(...)
Fiber.RegisterProvider(...)
Fiber.UnloadNow(...)
Runtime.Providers()
Runtime.Orchestrator()
Context.Effects()
```

否则外部代码可以绕过 lifecycle protocol。

Runtime 的核心价值恰恰是：

> 所有动态变化必须经过统一生命周期协议。

---

# 65. Completion Condition

Kernel v0.1 完成后，下一层才能开始：

```text
Kernel
  ↓
Registry Extension
  ↓
Event Extension
  ↓
Config Reconciler
  ↓
Loader
  ↓
Watch
  ↓
HMR
  ↓
WASM
```

最终形成：

```text
                    ┌──────── Agent
                    │
                    ├──────── ETL
                    │
                    ├──────── Collector
                    │
Runtime Kernel ──────┼──────── CAN Analyzer
                    │
                    ├──────── MCP Runtime
                    │
                    └──────── Industrial Runtime
```

Kernel 本身永远不知道这些应用是什么。

---

# 66. 给 Coding Agent 的执行原则

Coding Agent 不得重新设计上述模型。

如果实现过程中发现：

```text
API 冲突
并发问题
生命周期歧义
Provider race
Effect race
```

优先修改：

```text
implementation
```

而不是绕过约束。

如果确实发现规范存在逻辑矛盾：

1. 停止相关实现。
2. 建立最小可复现测试。
3. 明确违反的 invariant。
4. 修改 Specification。
5. 再继续实现。

禁止通过：

```text
sleep
retry
hidden goroutine
global lock
special case
```

掩盖生命周期问题。

---

# 67. 当前真正的工程边界

到这里，Runtime 的核心已经可以冻结：

```text
Component
Fiber
Activation
Context
Effect
Capability
Provider
Dependency
Ownership
Orchestrator
```

下一阶段不再增加新的 Kernel 概念。

重点转为：

> **把这个 Kernel 做成一个真正可以被动态组合系统使用的 Runtime。**

下一步应实现的第一个 Extension 是：

**Registry Extension + Event Extension**

它们会第一次验证这个 Kernel 是否真的能够承载：

```text
动态 Tool
动态 MCP
动态 Agent capability
动态 WASM module
动态工业设备
```

而不是只能做一个“高级插件加载器”。