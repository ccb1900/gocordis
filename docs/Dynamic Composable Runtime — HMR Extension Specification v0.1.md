# Dynamic Composable Runtime — HMR Extension Specification v0.1

## 1. Objective

HMR Extension v0.1 为 Dynamic Composable Runtime 提供：

> **运行中 Component Implementation 的安全替换能力。**

HMR 负责：

```text
Old Implementation
        ↓
   Replacement Policy
        ↓
New Implementation
```

HMR 不拥有 Runtime Fiber 生命周期。

---

# 2. Architecture Position

```text
External Change
      │
      ▼
    Watch
      │
      ▼
    Change
      │
      ▼
     HMR
      │
      ├──────────────→ Loader
      │                   │
      │                   ▼
      │                New Module
      │
      ▼
   Runtime
      │
      ▼
 Existing Fiber
```

职责：

```text
Watch
    发现变化

Loader
    获得新 Module / Factory

HMR
    决定 replacement，并协调替换

Runtime
    执行 Fiber 生命周期
```

---

# 3. HMR Is an Extension

HMR：

- 不属于 Kernel
- 不修改 Kernel 生命周期语义
- 不修改 Provider Identity 规则
- 不修改 Dependency semantics
- 不实现第二套 lifecycle
- 不直接修改 Fiber state

---

# 4. Core Concepts

HMR v0.1：

```text
HMRTarget
Replacement
HMR
Operation
```

---

# 5. HMRTarget

```go
type Target struct {
    ID string

    ComponentID string

    Artifact loader.Artifact
}
```

要求：

- `ID` 非空
- `ComponentID` 非空
- Artifact 合法
- ID 是 HMR 逻辑身份
- ComponentID 是 Runtime Component/Fiber 对应身份

---

# 6. Target Identity

必须区分：

```text
HMR Target ID
Component ID
Fiber ID
Module ID
Module Version
Artifact ID
```

不得混用。

例如：

```text
Target:
    config-camera

Component:
    camera

Fiber:
    fiber-123

Old Module:
    camera-v1

New Module:
    camera-v2
```

---

# 7. Replacement

Replacement 描述一次完整替换：

```go
type Replacement struct {
    TargetID string

    OldModule loader.ModuleIdentity
    NewModule loader.ModuleIdentity
}
```

Replacement 是不可变值。

---

# 8. HMR Interface

```go
type HMR interface {
    Replace(context.Context, Target) error

    Close() error
    CloseContext(context.Context) error
}
```

---

# 9. Fundamental Rule

HMR 不允许：

```go
fiber.state = ...
```

也不允许：

```go
fiber.activate(...)
fiber.unload(...)
```

除非这些操作本身属于 Runtime 已公开且受 Kernel 语义保护的 API。

HMR 只能通过 Runtime public API 请求生命周期变化。

---

# 10. Replacement Preconditions

一次 Replacement 必须满足：

```text
Old Target exists
Old Component exists
Old Fiber belongs to target
New Artifact valid
New Module can be loaded
```

否则 Replacement 不得开始破坏旧实现。

---

# 11. Load New First

v0.1 采用：

> **Load-New-Before-Unload-Old**

即：

```text
Old Active
    │
    ▼
Loader.Load(New)
    │
    ├── fail → Old remains Active
    │
    ▼
New Module validated
    │
    ▼
Begin replacement
```

这是 HMR 的核心安全性质。

---

# 12. Failed New Load

如果：

```text
Loader.Load(New)
```

失败：

```text
Old implementation
        │
        ▼
     unchanged
```

不得：

```text
Old unload
    ↓
New load failed
    ↓
Component Gone
```

因此：

> 新实现无法加载时，旧实现保持运行。

---

# 13. Module Validation

Loader 返回：

```go
*loader.Module
```

后必须验证：

- Module ID
- Module Type
- Version
- Factory
- Target compatibility

验证失败：

```text
New module rejected
Old module remains active
```

---

# 14. Factory Compatibility

新 Module 的 Factory 必须与 Target Component 类型兼容。

例如：

```text
Target Component Type:
    camera

New Factory Type:
    camera
```

合法。

如果：

```text
camera
    ↓
factory type = database
```

则：

```go
ErrIncompatibleModule
```

旧实现保持运行。

---

# 15. Loader Usage

HMR 使用 Loader Module 时必须显式：

```go
usage.Acquire(module.Identity)
```

Module 在 HMR replacement 尚未完成时不得被 Unload。

---

# 16. Replacement Ownership

一次 Replacement 必须记录：

```text
Old Module
New Module
Old Fiber
New Fiber / Activation
```

HMR 不得依赖隐式 refcount。

Loader Usage 必须显式 Acquire / Release。

---

# 17. Replacement Strategies

HMR v0.1 只支持：

```text
Replace
```

不支持：

```text
Canary
Blue-Green
Rolling
Shadow
Dual-Run
Traffic Split
```

---

# 18. No Zero-Downtime Guarantee

v0.1：

> 不保证旧实现和新实现同时 Active。

标准替换：

```text
New Module Loaded
       ↓
Old Fiber Unload
       ↓
New Fiber Load
       ↓
New Fiber Active
```

因此可能存在短暂：

```text
No Active implementation
```

这是正式语义。

---

# 19. Fiber Replacement

默认创建新的 Fiber。

即：

```text
Old Fiber
    ↓
Gone

New Fiber
    ↓
Loading
    ↓
Active
```

不得复用旧 Fiber 的 Activation Context。

---

# 20. Fiber Identity

Replacement 后：

```text
Old Fiber ID != New Fiber ID
```

这是强制要求。

---

# 21. Activation Identity

Replacement 后：

```text
Old Activation ID != New Activation ID
```

不得复用旧 Context。

---

# 22. Provider Identity

如果 Component 提供 Capability：

```text
Old ProviderIdentity
    ↓
New ProviderIdentity
```

必须发生 identity/generation 变化。

因此依赖该 Provider 的 Consumer 必须按照 Kernel 已定义的 Provider Replacement semantics 重新激活。

HMR 不自行实现该机制。

---

# 23. Dependency Safety

如果旧 Fiber 是 Provider：

```text
Provider
   ↓
Consumer
```

HMR 不得直接：

```text
Unload Provider
```

而绕过 Kernel dependency withdrawal safety。

必须通过 Runtime public lifecycle operation。

---

# 24. Consumer Behavior

Provider Replacement 后：

```text
Old Provider Identity
        ↓
        X
        │
        ▼
New Provider Identity
```

Consumer：

```text
Active
  ↓
Unloading
  ↓
Loading
  ↓
Active
```

具体生命周期由 Kernel 决定。

HMR 不维护 Consumer state。

---

# 25. Atomicity Boundary

一次 HMR Replace 的原子性边界：

```text
New Module Loaded
        +
New Module Validated
```

之后才允许影响旧运行实例。

因此：

> Load/validation failure 必须是 non-destructive。

但是：

> Runtime replacement 本身不是事务。

---

# 26. Replacement Failure

例如：

```text
New Module Load       PASS
Old Fiber Unload      PASS
New Fiber Load        FAIL
```

v0.1：

> 不要求自动恢复旧 Fiber。

结果：

```text
Old Fiber = Gone
New Fiber  = Failed
```

HMR 返回错误。

不得伪造：

```text
Replacement success
```

---

# 27. Why No Rollback

Runtime Effect rollback 能恢复：

```text
Runtime-managed reversible state
```

但不能保证：

```text
external side effects
```

因此 HMR v0.1 不提供：

```text
automatic rollback
```

---

# 28. Replacement State

HMR Operation 内部可以：

```text
Preparing
Replacing
Completed
Failed
```

但：

> 该 Operation State 不是 Fiber lifecycle。

不得成为第二套生命周期系统。

---

# 29. Concurrent Replace

同一个 Target：

```text
Replace(A)
Replace(B)
```

不得并发执行。

必须串行化。

例如：

```text
R1
 ↓
R2
```

或者：

```text
R2
 ↓
R1
```

必须有明确 linearization order。

---

# 30. Latest Intent

v0.1：

> concurrent Replace 不自动合并。

如果：

```text
R1 = v2
R2 = v3
```

且 R1 先线性化：

```text
v1 → v2 → v3
```

合法。

如果 R2 先线性化：

```text
v1 → v3 → v2
```

也只有在显式调用顺序允许的情况下合法。

HMR 不提供 latest-wins policy。

---

# 31. Replace During Closing

HMR 开始 Closing 后：

```go
Replace(...)
```

必须：

```go
ErrHMRClosed
```

不得建立新的 Replacement。

---

# 32. Close

HMR Close：

1. reject new Replace
2. wait in-flight replacements
3. release HMR-owned Loader usages
4. close internal resources
5. become Closed

不得：

```text
Close
 ↓
强制杀死 replacement goroutine
```

---

# 33. CloseContext

如果 timeout：

```text
CloseContext(ctx)
```

返回：

```go
ctx.Err()
```

但 HMR 仍处于：

```text
Closing
```

不得谎报 Closed。

---

# 34. Context Cancellation

Replace 接收 Context：

```go
Replace(ctx, target)
```

取消语义：

### Before new module load

直接取消。

旧实现不变。

### During Loader.Load

遵循 Loader cooperative cancellation。

### After replacement has started

不得强制中断 Runtime lifecycle。

必须等待当前 Runtime operation 到达合法状态。

---

# 35. No Goroutine Killing

禁止：

```text
runtime.Goexit
unsafe kill
goroutine termination
```

所有取消必须 cooperative。

---

# 36. Watch Integration

HMR 不主动创建 Watch。

推荐 Adapter：

```text
Watch
  ↓
Change
  ↓
HMR.Replace
```

HMR 核心不依赖 Watch。

---

# 37. Watch Change Mapping

Watch Change：

```text
SourceID
```

由 Adapter 映射：

```text
SourceID
    ↓
HMR TargetID
    ↓
Artifact
```

HMR 不猜测：

```text
SourceID == TargetID
```

除非 Adapter 显式建立映射。

---

# 38. Loader Integration

HMR 依赖 Loader 接口：

```go
type Loader interface {
    Load(...)
    Unload(...)
}
```

HMR：

```text
Artifact
   ↓
Loader.Load
   ↓
Module
```

---

# 39. Module Usage

HMR 必须保证：

```text
Acquire(new)
    ↓
replacement
    ↓
Release(old)
```

任何阶段发生失败，都不得错误释放仍在使用的 Module。

---

# 40. Old Module Release

只有在：

```text
Old Fiber = Gone
```

并且：

```text
no HMR ownership
```

后才能：

```go
usage.Release(old)
```

然后 Loader 才允许：

```go
Unload(old)
```

---

# 41. New Module Release

如果 New Module Load 成功但 replacement 后续失败：

必须根据实际 ownership：

```text
New Module
    ↓
unused
    ↓
Release
    ↓
Unload
```

不得泄漏。

---

# 42. No Module Replacement

Loader 本身：

```text
Module ID
```

仍然遵循 Loader v0.1：

```text
duplicate load
    ↓
ErrModuleExists
```

HMR 不要求 Loader 原地替换 Module。

正确模式：

```text
old module = camera/v1
new module = camera/v2
```

Module Identity 不同。

---

# 43. Version Semantics

HMR 不自动解释：

```text
Version
```

例如：

```text
v1 → v2
```

并不意味着：

```text
compatible
```

兼容性由 Target/Factory policy 验证。

---

# 44. Component Construction

新 Fiber 必须由：

```text
New Module Factory
```

构造新的 Component。

不得复制旧 Fiber：

```text
copy(oldFiber)
```

也不得复制旧 Activation Context。

---

# 45. Component State

v0.1：

> 不提供 Component state migration。

例如：

```text
Old Component:
    internal state = S1

New Component:
    internal state = S2
```

HMR 不负责：

```text
S1 → S2
```

状态迁移属于未来专门的 State Migration extension/policy。

---

# 46. External Resources

旧 Component 的外部资源：

```text
socket
file
goroutine
timer
connection
```

如果由 Runtime Effect 管理：

```text
Old Activation
    ↓
Unloading
    ↓
Effect unwind
```

由 Kernel 回收。

HMR 不重复实现 cleanup。

---

# 47. Effect Completeness

HMR 不得假设：

```text
Dispose()
```

可以 magically undo external side effects。

仍然遵守 Kernel Effect completeness semantics。

---

# 48. Replacement Result

推荐：

```go
type Result struct {
    TargetID string

    OldFiberID string
    NewFiberID string

    OldModule loader.ModuleIdentity
    NewModule loader.ModuleIdentity
}
```

Result 为 immutable value。

---

# 49. Result Semantics

Replace 返回 nil：

必须意味着：

```text
New Fiber reached Active
```

而不是：

```text
New Module merely loaded
```

这是非常重要的成功定义。

---

# 50. Failed Result

如果：

```text
New Module loaded
New Fiber failed
```

必须：

```text
error != nil
```

并且 Result 不得伪装为 successful replacement。

---

# 51. No Partial Success

调用者不能看到：

```text
Replace() == nil
```

同时：

```text
Fiber != Active
```

因此 HMR 的成功条件是：

```text
Replacement committed
AND
New Fiber Active
```

---

# 52. Observability

v0.1 不实现 Event integration。

但必须保留足够信息供未来 Event Adapter 使用：

```text
TargetID
OldModule
NewModule
OldFiber
NewFiber
Outcome
Error
```

---

# 53. Error Model

至少定义：

```go
var (
    ErrHMRClosed          = errors.New("hmr closed")
    ErrTargetNotFound     = errors.New("hmr target not found")
    ErrInvalidTarget      = errors.New("invalid hmr target")
    ErrIncompatibleModule = errors.New("incompatible module")
    ErrReplacementFailed  = errors.New("replacement failed")
    ErrReplacementRunning = errors.New("replacement already running")
)
```

支持：

```go
errors.Is(...)
```

---

# 54. Target Registry

HMR 可以维护：

```go
type TargetRegistry interface {
    Get(id string) (Target, bool)
    Has(id string) bool
    Snapshot() []Target
}
```

但：

> HMR Target Registry 不是 Runtime Registry。

不得提供 Capability。

不得进入 Kernel Provider Graph。

---

# 55. Registration

Target 注册必须明确：

```text
Target
  ↓
HMR.Register
```

不允许：

```text
Watch
 ↓
implicit HMR target
```

也不允许：

```text
Loader
 ↓
implicit HMR target
```

---

# 56. Target Ownership

HMR 只拥有：

```text
Target mapping
Replacement operation
Loader usage acquired by HMR
```

HMR 不拥有：

```text
Fiber lifecycle
Component lifecycle
Provider lifecycle
```

---

# 57. No Hidden Runtime State

HMR 不允许维护：

```text
own fiber state
own provider state
own dependency state
```

Runtime 是唯一 Fiber lifecycle authority。

---

# 58. Locking

HMR 内部锁中禁止调用：

- Loader
- Runtime
- Component Factory
- user code
- filesystem
- Watch
- Event

正确模式：

```text
lock
 ↓
capture
 ↓
unlock
 ↓
external operation
 ↓
lock
 ↓
commit
```

---

# 59. Panic Isolation

以下操作发生 panic：

```text
Factory
Loader adapter
Runtime adapter
```

必须转换为 error。

不得让 HMR goroutine 崩溃整个 process。

---

# 60. Failure Isolation

一个 Target replacement 失败：

```text
Target A → FAIL
```

不得影响：

```text
Target B
Target C
```

---

# 61. Dependency Failure

如果 New Fiber 因 dependency unsatisfied：

```text
New Fiber = Pending
```

则：

```text
Replace = FAIL
```

因为 v0.1 成功定义要求：

```text
New Fiber Active
```

但：

> HMR 不得把 Pending 强制解释为 Active。

---

# 62. Provider Replacement

如果 HMR Target 是 Provider：

```text
Old Provider
    ↓
New Provider
```

必须由 Kernel 处理：

```text
Provider identity change
Dependency withdrawal
Consumer lifecycle
Provider activation
```

HMR 不实现这些算法。

---

# 63. Strong Dependency Cycle

HMR 不允许通过 Replacement 创建：

```text
A → B
B → A
```

强依赖环。

Kernel 已禁止的 composition 仍然禁止。

---

# 64. Replacement Ordering

标准流程：

```text
1. Validate Target
2. Load New Module
3. Validate New Module
4. Acquire New Module usage
5. Create New Component
6. Request Runtime Load
7. Wait New Fiber Active
8. Request Old Fiber Dispose
9. Wait Old Fiber Gone
10. Release Old Module usage
11. Optionally unload Old Module
12. Commit HMR Result
```

注意：

步骤 6–9 必须通过 Runtime public API。

---

# 65. Why New Fiber First?

v0.1 的标准流程实际上采用：

```text
Load New Module
    ↓
Create New Fiber
    ↓
New Fiber Active
    ↓
Old Fiber Unload
```

这是 **warm replacement**。

因此与第 18 节的“Old Fiber Unload → New Fiber Load”不同。

最终以本节为准：

> **New Fiber Active 后才允许 Old Fiber Unload。**

这样可以最大程度避免替换过程中出现服务空窗。

---

# 66. Replacement Strategy — Warm

完整状态：

```text
Old Active
      │
      ├──────────────┐
      │              │
      │          New Loading
      │              │
      │          New Active
      │              │
      └──────────────┘
             │
        Old Unloading
             │
          Old Gone
```

v0.1 HMR 默认采用此策略。

---

# 67. Important Constraint

Warm replacement 不意味着：

```text
Old Provider
+
New Provider
```

可以同时无条件提供同一个 exclusive Capability。

如果 New Fiber 与 Old Fiber 冲突：

```text
New Load
    ↓
duplicate provider
```

则：

```text
New replacement fails
Old remains Active
```

不得先卸载旧 Provider 来“制造空间”。

---

# 68. Provider Replacement Special Case

对于 exclusive Provider：

```text
Old Provider
    ↓
New Provider
```

如果 Kernel 的 exclusive-provider semantics 不允许 warm overlap：

HMR 必须使用：

```text
Kernel-supported replacement operation
```

或者：

```text
Old Unload
    ↓
New Load
```

但这个路径必须明确由 Runtime/Kernel public API 提供。

HMR 不得自行操纵 Fiber。

---

# 69. Atomic Replacement Policy

因此：

```text
Normal Component:
    New Active → Old Gone

Exclusive Provider:
    Kernel-defined safe replacement
```

HMR 不修改 Kernel 规则。

---

# 70. Target Registry Mutation

Target Register / Unregister：

```text
Register
Unregister
```

必须线性化。

Duplicate Register：

```go
ErrTargetExists
```

Missing Unregister：

```go
ErrTargetNotFound
```

---

# 71. Target Mutation vs Replacement

Register/Unregister 与 Replace 可以并发，但必须有明确 linearization。

如果：

```text
Unregister(Target)
Replace(Target)
```

最终顺序为：

```text
Unregister → Replace
```

则 Replace：

```text
ErrTargetNotFound
```

反之：

```text
Replace → Unregister
```

则 Replacement 正常完成，然后 Target 被移除。

---

# 72. HMR Close Conservation

Close 后必须：

```text
No new replacement
No new target registration
No hidden goroutine
No leaked Loader usage
```

---

# 73. Tests — Contract Tests

### H1 — Target Registration

Register 成功。

### H2 — Duplicate Target

Duplicate Register 返回 ErrTargetExists，原 Target 不变。

### H3 — Target Removal

Unregister 成功。

### H4 — Missing Target

返回 ErrTargetNotFound。

### H5 — New Module Load Failure

New Load 失败，Old Fiber 保持 Active。

### H6 — New Module Validation Failure

New Module invalid，Old Fiber 保持 Active。

### H7 — New Component Creation Failure

New Component 创建失败，Old Fiber 保持 Active。

### H8 — New Fiber Load Failure

New Fiber 未 Active，Old Fiber 不被破坏。

### H9 — Successful Warm Replacement

New Fiber Active 后 Old Fiber Gone。

### H10 — Fiber Identity

Old Fiber ID != New Fiber ID。

### H11 — Activation Identity

Old Activation != New Activation。

### H12 — Module Usage

New Module usage 正确 Acquire/Release。

### H13 — Old Module Release

Old Fiber Gone 后才能 Release Old Module。

### H14 — Replacement Failure Cleanup

失败后没有 Module Usage leak。

### H15 — Concurrent Replace

同 Target 不允许 overlapping replacement。

### H16 — Target Isolation

Target A failure 不影响 Target B。

### H17 — Context Cancellation

Replacement 未进入 destructive phase 时取消，Old remains Active。

### H18 — Provider Replacement

Provider Identity 正确变化，Consumer 遵循 Kernel lifecycle。

### H19 — Close

Close 阻止新 Replacement，并清理 HMR-owned resources。

### H20 — HMR Independence

HMR 不修改 Kernel semantics，不实现第二 lifecycle。

---

# 74. Property Tests

### P1 — Replacement Conservation

New replacement success：

```text
New Active
Old Gone
```

### P2 — Failed New Load Preservation

New load failure：

```text
Old Active
```

### P3 — Identity Freshness

每次 successful replacement：

```text
FiberID_new != FiberID_old
ActivationID_new != ActivationID_old
```

### P4 — Usage Conservation

所有 Module Acquire 最终都有对应 Release。

### P5 — Target Isolation

A replacement failure 不影响 B。

### P6 — Serial Replacement

同 Target 任意并发 Replace 不出现 overlap。

### P7 — Close Safety

Close race Replace 不产生：

```text
panic
leak
send-on-closed
```

### P8 — Race Detector

```text
go test -race ./...
```

必须 PASS。

---

# 75. Forbidden Behaviors

任何一个都 FAIL：

- 修改 Kernel
- 修改 Fiber state
- 直接操作 Provider registry
- 自己实现 dependency resolution
- 自己实现 dependency withdrawal
- 自己实现 Consumer lifecycle
- 创建第二套 lifecycle system
- 复制旧 Fiber
- 复用旧 Activation Context
- Load New 失败后卸载 Old
- New Fiber 未 Active 就宣布成功
- 自动 rollback
- 自动 state migration
- 自动 retry
- 自动 Watch
- Watch 直接成为 HMR 内部实现
- Loader 直接触发 HMR
- Event/Scheduler 成为 HMR 核心依赖
- Target Registry 进入 Kernel Provider Graph
- Module Registry 被 HMR 私自修改
- 多 Target 失败互相影响
- user code 在 HMR lock 内执行
- 强制 kill goroutine
- Module usage 泄漏
- Old Fiber 未 Gone 就 Release Old Module
- 同 Target 并发 Replacement
- HMR 自己维护 Fiber State

---

# 76. Package Structure

推荐：

```text
extensions/
    hmr/
        hmr.go
        target.go
        replacement.go
        registry.go
        hmr_test.go
        hmr_concurrency_test.go
```

HMR 允许依赖：

```text
runtime
extensions/loader
```

但：

```text
runtime
```

不得依赖 HMR。

Watch 不要求成为 HMR dependency。

---

# 77. Dependency Direction

允许：

```text
hmr
 ├── runtime
 └── loader
```

允许未来：

```text
watch
   ↓
adapter
   ↓
hmr
```

禁止：

```text
runtime → hmr
loader  → hmr
kernel  → hmr
```

---

# 78. Completion Report

实现完成后必须严格按照：

```text
HMR EXTENSION

PASS / CONDITIONAL PASS / FAIL

1. 修改文件
2. 新增 API
3. Target model
4. Replacement model
5. Module integration
6. Runtime integration
7. Warm replacement semantics
8. Provider replacement semantics
9. Fiber identity semantics
10. Activation identity semantics
11. Module Usage semantics
12. Failure preservation
13. Cleanup semantics
14. Concurrent replacement semantics
15. Close semantics
16. Contract tests H1–H20
17. Property tests P1–P8
18. Kernel 是否修改
19. Loader 是否修改
20. Watch/Event/Scheduler/Config/Registry 是否修改
21. go test ./...
22. go test -race ./...
23. go vet ./...
24. 未解决问题
```

如果发现 P0/P1 semantic conflict：

```text
- Spec 条款
- 实现行为
- 冲突原因
- 是否修改实现
- 是否需要修改 Spec
```

必须明确报告。

---

# 79. Implementation Stop Point

Code Agent 完成 HMR Extension 后立即停止。

不得继续：

```text
WASM
HTTP HMR
Git HMR
Discovery
Deployment
State Migration
Config Watch Adapter
```

---

# 80. Final Acceptance

只有以下全部满足：

```text
H1–H20 PASS
P1–P8  PASS

go test ./...       PASS
go test -race ./... PASS
go vet ./...        PASS

Kernel unchanged
Loader semantics unchanged
Watch unchanged
Config unchanged
Event unchanged
Scheduler unchanged
Registry unchanged

No second lifecycle
No hidden Fiber state
No Module usage leak
No failed-replacement destruction
No HMR goroutine leak
```

才能宣布：

```text
HMR EXTENSION
PASS
```