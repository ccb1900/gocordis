# Dynamic Composable Runtime
## Config Extension Specification v0.1

**Status:** Implementation Specification  
**Target:** Code Agent  
**Dependency:** Kernel v0.1  
**Scope:** Config Extension only  
**Out of Scope:** Loader, HMR, Watch, WASM, Event, Scheduler, HTTP, MCP

---

# 1. Objective

Config Extension 用于描述：

> Runtime 当前期望存在什么 Component，以及这些 Component 应该处于什么配置状态。

Config Extension 的核心不是“读取配置文件”。

核心是：

```text
Desired Configuration
        ↓
Reconciliation
        ↓
Runtime lifecycle operations
        ↓
Actual Runtime State
```

因此 Config Extension 必须区分：

```text
Desired State
Actual State
```

---

# 2. Core Principle

配置不是 Kernel primitive。

Kernel 只负责：

```text
Fiber
Activation
Dependency
Provider
Effect
Ownership
Lifecycle
```

Config Extension 负责：

```text
Config
Desired Component Tree
Reconcile
Diff
Apply desired state
```

因此：

```text
Config ≠ Runtime State
Config ≠ Fiber
Config ≠ Provider
Config ≠ Dependency
```

---

# 3. Package

实现位置：

```text
dynamic-runtime/
└── extensions/
    └── config/
```

依赖方向：

```text
runtime
   ↑
config
```

Kernel 不得 import Config Extension。

---

# 4. Config Extension 的职责边界

Config Extension 可以：

- 保存 Desired Configuration
- 校验 Configuration
- 计算 Desired State Diff
- 创建/删除 Runtime-managed Component
- 调用 Kernel lifecycle API
- 等待 Runtime lifecycle result
- 报告 reconcile result

Config Extension 不负责：

- 解析 TOML/YAML/JSON
- 文件 Watch
- HMR
- Component 动态加载
- WASM 加载
- Event Bus
- Scheduler
- HTTP API

配置文件格式属于更上层 Adapter。

---

# 5. Configuration Model

v0.1 使用：

```go
type Config struct {
    Components []ComponentConfig
}
```

Component：

```go
type ComponentConfig struct {
    ID     string
    Type   string
    Config map[string]any
}
```

其中：

```text
ID
```

是 Desired Component 的稳定逻辑身份。

---

# 6. Component ID

Component ID 必须：

- 在一个 Config 中唯一
- 非空
- 稳定
- 不依赖数组位置

禁止：

```text
Components[0]
Components[1]
```

作为逻辑身份。

例如：

```text
scanner.main
collector.plc
database.primary
```

属于合法逻辑 ID。

---

# 7. Component Type

`Type` 表示：

> 哪一种 Component Factory 应该负责创建该 Component。

例如：

```text
"scanner"
"collector"
"database"
```

Type 不是 Kernel Capability Key。

Type 不等于 Provider Identity。

---

# 8. Component Factory

Config Extension 需要一个 Factory Registry。

建议：

```go
type Factory interface {
    Create(config ComponentConfig) (Component, error)
}
```

Factory Registry：

```go
type FactoryRegistry interface {
    Register(typeName string, factory Factory) error
    Lookup(typeName string) (Factory, bool)
}
```

---

# 9. Factory Registry 与 Runtime Registry

必须区分：

```text
FactoryRegistry
```

与之前的：

```text
Registry[T]
```

Factory Registry：

> Type → Component Factory

Runtime Registry：

> MemberID → Dynamic Member

不要强行复用 Registry Extension 来表达不同语义。

如果实现选择内部使用 `Registry[Factory]`，可以，但对外语义仍必须保持 Factory Registry。

---

# 10. Unknown Type

如果 Config 中：

```text
Type = "unknown"
```

Reconcile 不得：

- panic
- 创建空 Component
- 静默忽略

必须产生明确错误：

```go
ErrUnknownComponentType
```

并保持已有 Runtime 状态不被无关地破坏。

---

# 11. Desired State

Config Extension 内部需要维护：

```text
Desired State
```

以及：

```text
Applied State
```

其中 Applied State 表示：

> Config Extension 当前已经成功提交到 Runtime 的配置状态。

注意：

```text
Applied State ≠ Runtime actual Fiber state
```

因为 Component 可能：

- Loading
- Active
- Failed
- Pending

Config Extension 不得假装 Component 已经 Active。

---

# 12. Reconcile

核心 API：

```go
func (c *Controller) Reconcile(ctx context.Context, desired Config) error
```

语义：

> 使 Runtime 朝 Desired Config 收敛。

不是：

```text
Load everything from scratch
```

而是：

```text
Current Applied Config
        ↓
Diff
        ↓
Minimal Runtime Operations
```

---

# 13. Reconcile Idempotence

如果：

```text
Reconcile(C)
```

成功后再次：

```text
Reconcile(C)
```

不得：

- 重建 Component
- 重新 Load
- 触发 unnecessary Unload/Load
- 改变 Provider identity

除非 Component 本身发生了 Runtime-required replacement。

---

# 14. Empty Configuration

：

```go
Config{
    Components: nil,
}
```

表示：

> Desired Runtime Component 集合为空。

Reconcile 后：

```text
所有由该 Controller ownership 的 Components
    ↓
Unloaded
```

但：

> Controller 不得卸载不属于自己的 Runtime Component。

---

# 15. Ownership Boundary

这是 Config Extension 的关键语义。

Controller 只能管理：

> 自己成功创建并登记 ownership 的 Components。

禁止：

```text
Reconcile(empty)
    ↓
Runtime 全部 Fiber.Dispose()
```

必须：

```text
Controller-owned fibers
    ↓
reconcile
```

其他 Runtime Component 不受影响。

---

# 16. Desired Component Addition

当前：

```text
Applied = {}
Desired = {A}
```

Reconcile：

```text
Create A
   ↓
Load A
   ↓
record ownership
```

如果 Load 成功：

```text
A ∈ Applied
```

如果 Load 失败：

```text
A ∉ Applied
```

并返回错误。

---

# 17. Partial Reconcile

例如：

```text
Desired:
A
B
C
```

当前：

```text
A
```

Reconcile 过程中：

```text
B succeeds
C fails
```

v0.1 不要求全局 transactional rollback。

但是必须定义：

> 已经成功应用的操作不得被假装成未应用。

因此最终状态可能：

```text
Runtime:
A
B
C failed/not loaded

Applied:
A
B
```

Reconcile 返回 error。

---

# 18. No Fake Atomicity

禁止声称：

```text
Reconcile = transaction
```

v0.1 不提供全局原子事务。

如果未来需要：

```text
A+B+C
    ↓
all succeed or rollback
```

必须单独设计 Transaction Extension。

---

# 19. Component Removal

当前：

```text
Applied:
A
B

Desired:
A
```

Reconcile：

```text
B
 ↓
Dispose/Unload
 ↓
remove ownership
```

A 保持不变。

---

# 20. Component Replacement

以下变化：

```text
ID = A

Type:
scanner
→
collector
```

或者 Config 本身要求替换：

```text
A config v1
→
A config v2
```

必须先定义是否需要 replacement。

v0.1 规则：

> 如果 ComponentConfig 的 `Type` 或 `Config` 发生变化，则视为 Component Replacement。

Replacement：

```text
old Component
    ↓
Unload
    ↓
Create new Component
    ↓
Load
```

不得原地修改已有 Component。

---

# 21. Replacement Identity

Component replacement 后：

> 新 Component 是新的 Runtime Fiber。

因此 Provider identity 如果由该 Component 提供：

```text
old Fiber/Activation
    ↓
new Fiber/Activation
```

必须由 Kernel 正常产生新的 Provider identity。

Config Extension 不得伪造 identity。

---

# 22. Failed Replacement

例如：

```text
A v1 Active

Reconcile:
A v2
```

如果：

```text
A v1 unload succeeds
A v2 create/load fails
```

v0.1 不要求自动恢复 A v1。

最终：

```text
A v1 gone
A v2 failed/not applied
```

但 Config Controller 必须返回错误。

如果未来需要 zero-downtime replacement：

> 单独设计 Transactional Replacement Extension。

---

# 23. Dependency Handling

Config Controller 不负责解决 Component Dependency。

例如：

```text
A requires B
```

Controller 只负责：

```text
ensure A desired
ensure B desired
```

真正的：

```text
B available?
A Loading?
A Active?
B disappears?
```

全部由 Kernel Dependency 机制处理。

禁止 Controller 自己实现：

```text
if B.Active {
    Load(A)
}
```

来替代 Kernel。

---

# 24. Config Ordering

Config 中：

```go
Components []ComponentConfig
```

的数组顺序：

> 默认不具有生命周期 ordering 语义。

例如：

```text
[A, B]
```

不意味着：

```text
A must load before B
```

Dependency 应该通过 Kernel 表达。

---

# 25. Duplicate Component ID

Config：

```text
A
A
```

非法。

必须返回：

```go
ErrDuplicateComponentID
```

并且：

> 不得部分应用该 Config。

即 Config validation 应发生在 reconcile mutations 之前。

---

# 26. Config Validation

在任何 Runtime mutation 前：

```text
Parse/receive Config
    ↓
Validate entire Config
    ↓
Diff
    ↓
Mutate Runtime
```

至少验证：

```text
ID 非空
ID 唯一
Type 非空
Component Config 合法
```

未知 Type 如果需要 Factory Registry：

```text
Factory exists
```

必须在 mutation 前检查。

---

# 27. Validation Failure

如果 Config 非法：

```text
Reconcile(invalid)
```

必须保证：

```text
Runtime unchanged
Applied unchanged
Ownership unchanged
```

这是 v0.1 的重要 Contract。

---

# 28. Config Immutability

Controller 不得保存调用方传入的可变 Config 引用并假设其永不变化。

例如：

```go
cfg.Components[0].Config["x"] = ...
```

之后调用方修改原 map。

Controller 不得因此出现数据竞争或状态偷偷变化。

实现必须：

- defensive copy
- 或明确 immutable internal representation

推荐 defensive copy。

---

# 29. Applied State

Controller 内部必须保存足以进行 Diff 的 Applied Config。

例如：

```text
A:
Type = scanner
Config hash = ...
```

实现可以保存完整 Config，也可以保存 canonical representation/hash。

但必须能够可靠判断：

```text
same
added
removed
replaced
```

---

# 30. Config Equality

v0.1 不依赖 Go：

```go
reflect.DeepEqual
```

直接定义所有语义。

必须建立明确的 Config equality。

推荐：

```text
canonicalized representation
```

但不要求特定实现。

至少：

```text
同一 logical config → equal
不同 Type → not equal
不同 Config value → not equal
```

---

# 31. Map Ordering

Config：

```go
map[string]any
```

不得依赖 Go map iteration order。

如果需要 hash：

> 必须使用 deterministic canonicalization。

例如：

```text
keys sorted
```

再计算 canonical representation。

---

# 32. Reconcile Serialization

同一个 Controller：

```text
Reconcile(C1)
Reconcile(C2)
```

并发调用时必须被线性化。

禁止两个 Reconcile 同时修改：

```text
ownership
Applied
Runtime components
```

推荐：

```text
Controller mutex / serialized command loop
```

但：

> 不得持有 Controller lock 调用 Runtime/User Component code。

---

# 33. Reconcile × Reconcile

例如：

```text
G1: Reconcile(C1)
G2: Reconcile(C2)
```

必须等价于：

```text
C1 → C2
```

或者：

```text
C2 → C1
```

中的某个合法线性化顺序。

最终 Applied State 必须与最后一个线性化成功 reconcile 对应。

---

# 34. Reconcile × Close

Controller 必须提供：

```go
func (c *Controller) Close() error
```

Close 幂等。

Close 后：

```text
Reconcile()
```

必须返回：

```go
ErrControllerClosed
```

并且不得启动新的 Runtime mutation。

---

# 35. Close Ownership

Close Controller 时：

> Controller-owned Components 必须被清理。

即：

```text
Controller.Close()
    ↓
owned components unload
    ↓
ownership released
```

但如果 cleanup 因 Component failure 等原因未立即完成：

> 不得虚假报告 cleanup 已完成。

---

# 36. CloseContext

推荐：

```go
func (c *Controller) CloseContext(ctx context.Context) error
```

如果 cleanup 无法在 deadline 内完成：

```text
return ctx.Err()
```

但 Controller 不得虚假进入“所有资源已经 Gone”的状态。

这与 Kernel Close semantics 保持一致。

---

# 37. Factory Registration

Factory Registry：

```go
Register(typeName, factory)
```

必须：

- Type 非空
- Factory 非 nil
- duplicate registration 返回明确错误

建议：

```go
ErrFactoryExists
ErrFactoryNotFound
```

---

# 38. Factory Replacement

v0.1 不支持：

```text
Register("scanner", F1)
Register("scanner", F2)
```

自动替换。

如果已经存在：

```text
ErrFactoryExists
```

避免运行时悄悄改变 Config 的实际含义。

未来 HMR/Loader 可以定义 Factory replacement。

---

# 39. Factory Concurrency

Factory Registry 必须支持：

```text
Register × Lookup
Register × Register
Lookup × Lookup
```

并发安全。

禁止用户 Factory code 在 Registry lock 内执行。

---

# 40. Factory Create

`Factory.Create()` 属于用户/Extension code。

因此：

> 不得在 Factory Registry mutex 内调用。

正确：

```text
lookup factory
    ↓
unlock
    ↓
factory.Create()
```

---

# 41. Factory Panic

如果：

```go
factory.Create(...)
```

panic：

> 不得导致 Controller goroutine 永久死亡。

必须转换为明确失败。

例如：

```text
ErrComponentCreatePanic
```

并附带 panic value。

---

# 42. Component Ownership

Component 创建成功后：

> 必须在 Controller ownership model 中登记。

Ownership registration 必须与 Runtime lifecycle result 保持一致。

不能：

```text
Load failed
    ↓
仍然把 Fiber 当作 successfully applied
```

---

# 43. Runtime Component Interface

Config Extension 不得重新定义一套 Lifecycle。

它应该使用 Kernel 已有 Component/Fiber API。

例如：

```text
Factory.Create
    ↓
Kernel Load
    ↓
Fiber lifecycle
```

如果当前 Kernel API 无法支持：

```text
Create
Load
Dispose
```

的安全组合：

> 停止实现并报告 API gap。

禁止自行修改 Kernel。

---

# 44. Component Creation 与 Load

创建 Component 与 Runtime Load 是两个不同阶段：

```text
Factory.Create()
    ↓
Component object
    ↓
Runtime.Load()
    ↓
Fiber
```

如果 Create 成功但 Load 失败：

> Component/Fiber 必须按照其 ownership contract 清理。

不得泄漏。

---

# 45. Reconcile Algorithm

推荐：

```text
Reconcile(desired)

1. Validate desired
2. Build desired index
3. Snapshot current Applied
4. Calculate:
   added
   removed
   replaced
5. Apply removals/replacements/additions
6. Update Applied only according to actual successful operations
7. Return aggregate error
```

但：

> 具体 mutation ordering 不得成为隐式 Contract，除非 Dependency/Ownership 强制要求。

---

# 46. Mutation Ordering

如果没有 Dependency 关系：

```text
A
B
C
```

Controller 可以任意顺序执行。

如果 Kernel Dependency 自己产生：

```text
Pending
Loading
Active
```

Controller 不得自行等待并模拟 Dependency ordering。

---

# 47. Error Aggregation

一次 Reconcile 可以产生多个错误。

例如：

```text
Remove A failed
Create B failed
Create C failed
```

Controller 应继续处理独立操作，而不是第一个 error 就停止整个 reconcile。

但如果某个错误破坏了后续操作的前提，可以停止相关 branch。

实现可以使用：

```go
errors.Join(...)
```

但必须保持 Applied State 与真实成功操作一致。

---

# 48. No Partial-State Lie

这是 Config Extension 最重要的 invariant：

```text
Applied[ID]
```

只能表示：

> 该 ID 的 desired component 已成功交给 Runtime 管理。

不得因为：

```text
Create succeeded
Load failed
```

就把它写入 Applied。

同样：

```text
Unload failed
```

不得立即从 Applied 删除。

---

# 49. Runtime Failure vs Config Failure

必须区分：

```text
Config invalid
```

和：

```text
Component lifecycle failed
```

Config invalid：

```text
Runtime mutation = 0
```

Component lifecycle failure：

```text
Runtime may have partially changed
```

错误信息必须允许调用者区分二者。

---

# 50. No Automatic Retry

Reconcile 失败后：

> Controller 不得自动无限 retry。

调用方可以：

```text
Reconcile(desired)
```

再次尝试。

Scheduler/Event 等未来 Extension 可以在更高层实现 retry policy。

---

# 51. No Persistence

v0.1 不实现：

- Config persistence
- database
- WAL
- config history
- rollback
- version store

Controller 关闭后状态丢失是允许的。

Runtime Component 是否继续存在取决于 ownership/lifecycle。

---

# 52. No File Watch

v0.1 不实现：

```text
watch config file
```

文件变化：

```text
file changed
    ↓
Watch Extension
    ↓
Reconcile(new Config)
```

未来再组合。

---

# 53. No Event Integration

v0.1 不自动发布：

```text
component.added
component.removed
component.replaced
```

Event Extension 与 Config Extension 可以未来组合。

不要让 Config Extension 隐式依赖 Event Extension。

---

# 54. No Scheduler Integration

禁止：

```text
Config changed
    ↓
Scheduler retry
```

Scheduler 是独立 Extension。

---

# 55. No HMR

禁止：

```text
Factory changed
    ↓
Config automatically reload
```

Factory replacement 属于 Loader/HMR。

---

# 56. Runtime Ownership Graph

Config Controller 管理的关系：

```text
Controller
   │ owns
   ├── Fiber A
   ├── Fiber B
   └── Fiber C
```

这属于 Ownership。

Component Dependency：

```text
A → B
```

仍然属于 Kernel Dependency Graph。

二者不得混淆。

---

# 57. Ownership Cleanup Ordering

Controller Close/Reconcile removal：

> 必须使用 Kernel 自身生命周期与 Ownership 机制清理 Component。

Controller 不得直接操作：

```text
Fiber.state = Gone
```

不得直接：

```text
provider map delete
```

不得绕过 Kernel。

---

# 58. Config Controller as Runtime Component

v0.1 不要求 Controller 自己成为 Runtime Component。

如果未来需要：

```text
Config Controller
```

成为 Kernel-managed Component：

> 单独定义 integration。

不要为了“完整”自行加入。

---

# 59. API Summary

推荐最小公开 API：

```go
type ComponentConfig struct {
    ID     string
    Type   string
    Config map[string]any
}

type Config struct {
    Components []ComponentConfig
}

type Factory interface {
    Create(ComponentConfig) (Component, error)
}

type FactoryRegistry interface {
    Register(string, Factory) error
    Lookup(string) (Factory, bool)
}

type Controller struct {
    // internal
}

func NewController(
    runtime Runtime,
    factories FactoryRegistry,
) *Controller

func (c *Controller) Reconcile(
    context.Context,
    Config,
) error

func (c *Controller) Close() error

func (c *Controller) CloseContext(
    context.Context,
) error
```

具体 Runtime Adapter 必须根据现有 Kernel API 实际调整。

---

# 60. Critical API Boundary

如果当前 Kernel 没有一个足够小的 Runtime Facade：

```go
type Runtime interface {
    Load(Component) (Fiber, error)
}
```

可以在 Config Extension 内定义一个最小 Adapter interface。

但 Adapter：

> 只能包装现有 Kernel API。

禁止偷偷扩张 Kernel。

---

# 61. Acceptance Contract

必须满足：

```text
[ ] Config Extension 独立于 Kernel implementation
[ ] Kernel 不 import Config
[ ] Desired State 与 Applied State 分离
[ ] Component ID 稳定且唯一
[ ] Config validation 在 mutation 前完成
[ ] invalid config 不改变 Runtime
[ ] Unknown Type 明确失败
[ ] Add component 使用 Factory
[ ] Remove 只影响 Controller-owned components
[ ] Replacement 创建新的 Runtime Component
[ ] Replacement 不原地修改旧 Component
[ ] Provider identity 由 Kernel 管理
[ ] Dependency 由 Kernel 管理
[ ] Config ordering 不隐式代表 dependency ordering
[ ] Reconcile 幂等
[ ] Concurrent Reconcile 线性化
[ ] Controller ownership 明确
[ ] Applied State 不虚假
[ ] Partial failure 正确反映实际状态
[ ] 不自动 retry
[ ] Close 幂等
[ ] Close 清理 owned components
[ ] Close timeout 不虚假报告完成
[ ] Factory Registry 并发安全
[ ] Factory Create 不在 registry lock 内
[ ] Factory panic 隔离
[ ] 无 Event implicit integration
[ ] 无 Scheduler implicit integration
[ ] 无 Watch
[ ] 无 HMR
[ ] 无 persistence
[ ] 无 transaction
[ ] 无 polling
[ ] 无 global singleton
[ ] 无 Kernel modification
[ ] go test ./... PASS
[ ] go test -race ./... PASS
[ ] go vet ./... PASS
```

---

# 62. Contract Tests

至少实现：

### C1 — Empty Reconcile

```text
Reconcile({})
→ no owned components
```

### C2 — Add Component

```text
{} → {A}
→ A loaded
→ A owned
→ A applied
```

### C3 — Remove Component

```text
{A} → {}
→ A unloaded
→ A ownership released
```

### C4 — Idempotence

```text
Reconcile(C)
Reconcile(C)
```

第二次不得产生：

```text
Load
Unload
Replacement
```

等 unnecessary lifecycle operation。

---

### C5 — Duplicate ID

```text
A
A
```

→ validation error

并证明：

```text
Runtime unchanged
```

---

### C6 — Unknown Type

```text
Type = unknown
```

→ error

且：

```text
Runtime unchanged
```

---

### C7 — Invalid Config Atomic Validation

一个 Config 同时包含：

```text
valid A
invalid B
```

验证：

> A 也不得被加载。

---

### C8 — Replacement

```text
A v1
→
A v2
```

证明：

```text
old Fiber ≠ new Fiber
```

---

### C9 — Partial Failure

```text
A succeeds
B fails
C succeeds
```

证明：

```text
Applied = A,C
```

或等价真实成功集合。

不得把 B 假装成 Applied。

---

### C10 — Ownership Isolation

Controller 只拥有：

```text
A
```

Runtime 还有：

```text
B
```

Reconcile({})：

```text
A gone
B untouched
```

---

### C11 — Concurrent Reconcile

并发：

```text
Reconcile(C1)
Reconcile(C2)
```

必须：

- 无 race
- 无 ownership corruption
- Applied 与最终线性化结果一致

---

### C12 — Reconcile × Close

并发：

```text
Reconcile
Close
```

不得：

- panic
- 泄漏
- 在 Close linearization 后启动新的 mutation

---

### C13 — Factory Duplicate

重复：

```text
Register("A", F1)
Register("A", F2)
```

→ ErrFactoryExists

F1 保持有效。

---

### C14 — Factory Panic

Factory panic：

```text
Controller continues
```

panic 不得穿透 Controller 主循环。

---

### C15 — Component Create Failure

Create 失败：

```text
Applied unchanged
```

且没有泄漏已经创建的 Runtime resource。

---

### C16 — Component Load Failure

Create 成功、Load 失败：

```text
Applied unchanged for that ID
```

并完成必要 cleanup。

---

### C17 — Runtime Dependency

A requires B：

```text
Desired = {A,B}
```

证明：

> Dependency lifecycle 由 Kernel 处理，而不是 Config Controller 自己模拟。

---

### C18 — Provider Identity

Reconcile unchanged：

```text
Provider identity unchanged
```

Replacement：

```text
Provider identity changes according to Kernel
```

---

### C19 — Close Cleanup

Controller Close：

```text
owned A
owned B
```

最终：

```text
A/B cleaned
```

---

### C20 — CloseContext Timeout

stubborn Component cleanup：

```text
CloseContext(timeout)
→ ctx.Err()
```

不得虚假报告所有资源已经 Gone。

---

# 63. Property Tests

至少实现：

### P1 — Reconcile Idempotence

```text
R(C)
R(C)
```

第二次不产生额外 lifecycle mutations。

### P2 — Ownership Conservation

任何时刻：

```text
Applied ID
```

必须对应：

```text
Controller ownership
```

反之亦然。

### P3 — Validation Preservation

任何非法 Config：

```text
Runtime before == Runtime after
```

### P4 — No Foreign Mutation

Controller 不能修改非-owned Fiber。

### P5 — Replacement Identity

Config replacement：

```text
old Fiber identity != new Fiber identity
```

### P6 — Concurrent Reconcile Safety

随机：

```text
Add
Remove
Replace
Reconcile
```

不得产生 ownership corruption。

### P7 — Partial Failure Accuracy

任意 subset failure：

```text
Applied
```

必须准确反映成功 mutation。

### P8 — Close Idempotence

```text
Close^N
```

最终状态一致，无 double cleanup panic。

---

# 64. Forbidden Implementations

```text
❌ Config Extension 放进 Kernel
❌ Kernel import Config
❌ Config 自己实现 Dependency
❌ Config 自己修改 Fiber state
❌ Config 自己操作 Provider map
❌ Config order 代替 Dependency
❌ invalid config 先加载一部分再发现错误
❌ foreign Fiber cleanup
❌ automatic retry
❌ implicit Event integration
❌ implicit Scheduler integration
❌ file watch
❌ HMR
❌ persistence
❌ transaction
❌ polling
❌ global singleton
❌ second lifecycle
❌ 修改 Kernel
```

---

# 65. Completion Report

完成后只报告 Config Extension。

必须使用：

```text
CONFIG EXTENSION

PASS / CONDITIONAL PASS / FAIL

1. 修改文件
2. 新增 API
3. Config model
4. Desired/Applied semantics
5. Validation semantics
6. Reconcile semantics
7. Ownership semantics
8. Replacement semantics
9. Factory semantics
10. Concurrency semantics
11. Close semantics
12. Runtime integration
13. Contract tests C1–C20
14. Property tests P1–P8
15. Kernel 是否修改
16. go test ./...
17. go test -race ./...
18. go vet ./...
19. 未解决问题
```

如果存在 P0/P1 semantic conflict，必须明确报告。

完成 Config Extension 后停止，不进入 Loader、Watch、HMR 或其他 Extension。