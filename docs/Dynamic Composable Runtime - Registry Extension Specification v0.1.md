> ⚠️ **ARCHIVED（已归档）— 2026-09-08**
>
> 本文档已整体归档，仅作历史参考，**不再作为规范来源或引用依据**。
> 本仓库现以论文原文为唯一规范来源：`docs/论文.pdf`
> （A Programming Paradigm for Spatiotemporal Composability）。
> 当前路线图与论文↔代码对照见 `docs/ROADMAP.md`。
>
> This document is archived for historical reference only and is no longer cited
> as authority. The paper is the sole normative source; see `docs/ROADMAP.md`.

# Dynamic Composable Runtime
## Registry Extension Specification v0.1

### Status

Proposed / Implementation Target

### Scope

本规范定义 Runtime 的 **Registry Extension**。

Registry 用于表达：

> 一个稳定的 Runtime Capability，其内部成员可以动态增加、删除和替换，而依赖该 Registry 的 Consumer 不因成员变化而重新激活。

Registry 是 Extension，不属于 Kernel。

---

# 1. Design Goal

Kernel 已经解决：

```text
Fiber
Activation
Context
Effect
Provider
Dependency
Ownership
Lifecycle
```

Registry 在此基础上解决第二类组合问题：

```text
单一 Provider
```

与：

```text
动态 Provider 集合
```

之间的差异。

普通 Dependency：

```text
Consumer
    │
    ▼
Provider
```

Registry Dependency：

```text
Consumer
    │
    ▼
Registry
    │
    ├── Member A
    ├── Member B
    └── Member C
```

其中：

```text
Registry Identity = Stable
Member Set        = Dynamic
```

---

# 2. Core Semantic Rule

Registry 必须拥有稳定 Identity。

Member 的增加、删除、替换不得改变 Registry Identity。

因此：

```text
Add(Member)
Remove(Member)
Replace(Member)
```

都属于：

```text
Registry membership mutation
```

而不是：

```text
Registry provider replacement
```

---

# 3. Registry Is a Provider

Registry 本身是 Runtime Capability Provider。

例如：

```go
type RegistryKey[T any] struct {
    Name string
}
```

一个 Registry Activation 可以：

```text
Provide Registry[Handler]
```

Consumer：

```text
Require Registry[Handler]
```

Consumer 建立的是：

```text
Consumer
    ↓
Registry Identity
```

而不是：

```text
Consumer
    ↓
每一个 Member
```

因此 Member churn 不会造成 Consumer dependency invalidation。

---

# 4. Registry Member

Member 是 Registry 管理的动态实体。

概念模型：

```text
Registry
 ├── MemberID
 ├── Member value
 ├── Member metadata
 └── Member lifecycle
```

Member 必须拥有稳定的：

```go
type MemberID string
```

同一个 Registry 内：

```text
MemberID
```

必须唯一。

---

# 5. Registry Identity

Registry Identity 至少由：

```text
Owner Fiber
Activation
Registry Key
```

决定。

Registry 在重新 Activation 后：

```text
Old Registry Identity
        !=
New Registry Identity
```

因此：

```text
Registry activation replacement
```

仍然是普通 Provider replacement。

但是：

```text
Member add/remove
```

不得改变 Registry Identity。

---

# 6. Member Identity

Member Identity：

```text
Registry Identity
+
MemberID
```

MemberID 在同一个 Registry activation 中必须唯一。

例如：

```text
Registry: handlers
Members:

http
grpc
websocket
```

不能同时存在：

```text
http
http
```

---

# 7. Member Lifecycle

Member 不应该拥有独立于 Registry 的 Runtime Fiber 生命周期。

Member 的存在由 Registry membership 控制。

生命周期：

```text
Absent
  ↓ Add
Present
  ↓ Remove
Absent
```

如果 Member 背后本身对应一个 Fiber：

```text
Fiber
   ↓
Registry membership
```

则 membership 与 Fiber lifecycle 必须明确分离。

**Registry 不得擅自创建第二套 Fiber 生命周期。**

---

# 8. Add Semantics

添加成员：

```go
registry.Add(memberID, value)
```

必须具有以下语义：

```text
Absent
   ↓
Add
   ↓
Present
```

Add 成功后，Member 对新的 Registry snapshot 可见。

如果 MemberID 已存在：

```text
Add(existingID)
```

必须失败。

不得默认覆盖。

推荐错误：

```go
ErrMemberExists
```

---

# 9. Remove Semantics

```go
registry.Remove(memberID)
```

：

```text
Present
   ↓
Remove
   ↓
Absent
```

Remove 不存在的 Member：

```text
Remove(nonExisting)
```

应返回明确结果。

推荐：

```go
ErrMemberNotFound
```

不得静默删除其他 Member。

---

# 10. Replace Semantics

Registry 支持显式 Replace：

```go
registry.Replace(memberID, newValue)
```

语义：

```text
old member
    ↓
replacement
```

对于 Consumer：

```text
Registry Identity
```

保持不变。

因此 Consumer：

```text
不 Unloading
不 Loading
不重新 Apply
```

仅 Registry membership/value 发生变化。

Replace 必须是一个完整的 Registry mutation。

Consumer 不应该观察到：

```text
Member absent
```

这样的中间状态。

即：

```text
old → new
```

而不是：

```text
old → absent → new
```

---

# 11. Snapshot Semantics

Registry 必须提供 Snapshot API。

概念：

```go
snapshot := registry.Snapshot()
```

Snapshot 表示：

> 某一线性化时刻 Registry 的完整成员集合。

Snapshot 必须是不可变视图。

之后：

```text
registry.Add()
registry.Remove()
```

不得修改已经取得的 Snapshot。

因此：

```text
S1 := Snapshot()

Add(A)

S1
```

仍然保持原内容。

---

# 12. Lookup

Registry 至少需要：

```go
Get(MemberID)
Has(MemberID)
Snapshot()
```

建议：

```go
Range()
Len()
```

但 API 名称可以由实现决定。

核心 Contract 是：

> Lookup 必须针对某一个明确的 Registry state 进行。

不得产生：

```text
Get()
看到旧值

Has()
又看到新值
```

这种没有明确线性化语义的行为。

---

# 13. Concurrency

Registry 的所有 mutation 必须具有明确的 linearization point。

包括：

```text
Add
Remove
Replace
```

并发：

```text
Add(A)
Remove(A)
```

最终必须对应某一种合法串行顺序。

例如：

```text
Add → Remove
```

最终：

```text
Absent
```

或者：

```text
Remove → Add
```

最终：

```text
Present
```

不能产生第三种非法状态。

---

# 14. Consumer Visibility

Consumer 如果持有 Registry：

```text
Consumer
   ↓
Registry
```

Registry membership 发生变化：

```text
Add
Remove
Replace
```

Consumer 不得因为这个变化自动重新 Activation。

这是 Registry Extension 最重要的 Contract。

---

# 15. Registry Change Notification

Registry 必须能够向 interested consumers 暴露变化。

但是：

> Notification ≠ Dependency invalidation。

建议提供独立的 Watch / Subscribe 机制：

```text
Registry
   │
   └── Change Stream
          │
          ├── Added
          ├── Removed
          └── Replaced
```

Notification 是 Extension 层能力。

它不能修改 Kernel Dependency 语义。

---

# 16. Event Boundary

Registry Change Event 可以由 Registry Extension 发布。

例如：

```text
MemberAdded
MemberRemoved
MemberReplaced
```

但是：

```text
Dependency lost
Dependency recovered
Provider replaced
```

仍然属于 Kernel。

不要让 Event Bus 取代 Dependency。

---

# 17. Ownership

如果 Registry Member 对应 Runtime-managed resource：

```text
Registry
   ↓ owns
Member resource
```

那么 Remove Member 必须触发该资源的 cleanup。

如果 Member 只是一个普通值：

```go
registry.Add("foo", value)
```

Registry 不自动拥有 value 的外部生命周期。

因此：

> **Membership ownership 与 Fiber ownership 必须显式区分。**

---

# 18. Registry Disposal

Registry 自身进入：

```text
Unloading
```

之前：

```text
所有 Runtime-managed members
```

必须先被移除/清理。

最终：

```text
Registry
   ↓
Gone
```

不得留下 Registry-managed resource。

Consumer 对 Registry 的 Dependency 按 Kernel 原有规则处理：

```text
Registry disappears
    ↓
Consumer withdrawal
```

此时才属于 Dependency withdrawal。

---

# 19. Stable Registry Pattern

推荐的标准架构：

```text
             Runtime
                │
                ▼
       Registry Fiber
                │
        stable Provider
                │
        ┌───────┼────────┐
        ▼       ▼        ▼
       M1      M2       M3
```

Consumer：

```text
Consumer
   │
   ▼
Registry
```

而不是：

```text
Consumer
 ├── M1
 ├── M2
 └── M3
```

后一种模型会导致 Member churn 触发 Consumer lifecycle churn，违反 Registry 的设计目标。

---

# 20. Registry of Multiple Implementations

Registry 的主要用途之一：

```text
Registry[Handler]
```

包含：

```text
http
grpc
mqtt
```

Consumer 可以：

```text
handlers.Get("http")
```

或者遍历：

```text
handlers.Range(...)
```

因此 Registry 可以表达：

```text
one capability
+
dynamic implementations
```

这是传统 exclusive Provider 不适合表达的语义。

---

# 21. Nested Registry

Registry 可以作为 Registry Member 的 value。

例如：

```text
Registry A
   ├── Registry B
   └── Registry C
```

但是：

> Nested Registry 不得隐式创建 Dependency。

只有显式 Require / Provide 才建立 Runtime Dependency。

---

# 22. Duplicate Provider vs Duplicate Member

两者必须严格区分。

Provider：

```text
Capability Key
```

在 exclusive Provider 模式下：

```text
duplicate provider → error
```

Registry：

```text
Registry Key
+
MemberID
```

因此：

```text
same Registry
same MemberID
→ error
```

但是：

```text
same Registry
different MemberID
→ valid
```

例如：

```text
handlers/http
handlers/grpc
handlers/mqtt
```

全部合法。

---

# 23. Member Ordering

v0.1：

> Registry 不提供隐式 ordering guarantee。

如果 Consumer 需要顺序：

必须显式使用：

```text
priority
sequence
weight
```

等 Member metadata。

不能依赖：

```text
map iteration order
```

因此：

```text
Snapshot()
```

的默认顺序不具有业务语义。

如果实现选择提供 deterministic ordering：

可以，但不得宣称为业务 Contract，除非单独定义。

---

# 24. Transaction Boundary

单次 mutation：

```text
Add
Remove
Replace
```

必须原子完成。

但是 v0.1 不要求跨多个 Member 的 transaction。

例如：

```text
Add(A)
Add(B)
Remove(C)
```

不构成一个 atomic transaction。

未来可以提供：

```text
RegistryTransaction
```

但不属于 v0.1。

---

# 25. Effect Integration

如果 Registry 本身由 Fiber Activation 创建：

Registry registration 必须作为 Runtime Effect 管理。

例如：

```text
Activation
   ↓
Create Registry
   ↓
Provide Registry
   ↓
Effect registered
```

Activation unwind：

```text
Registry members cleanup
   ↓
Registry unregistered
```

必须遵循 Kernel Effect LIFO。

---

# 26. Failure Semantics

Registry mutation 失败：

```text
Add
Remove
Replace
```

不得破坏 Registry 当前一致状态。

例如：

```text
Replace(A, B)
```

如果 B 不合法：

最终仍然：

```text
A
```

而不是：

```text
Absent
```

Registry mutation 不得产生 partial mutation。

---

# 27. Reentrancy

Registry mutation callback 如果未来支持 callback：

禁止在 Registry internal lock 下执行用户代码。

错误：

```text
lock registry
  ↓
callback
  ↓
callback → registry.Add()
```

可能导致：

```text
deadlock
```

因此：

> Registry lock 与用户 callback 必须完全隔离。

---

# 28. Watch / Subscription

如果实现 Watch：

推荐语义：

```text
Subscribe()
   ↓
initial snapshot
   ↓
changes
```

必须明确：

- 是否 guaranteed initial snapshot
- 是否 guaranteed ordering
- 是否允许 event loss
- slow consumer 如何处理
- unsubscribe 是否 idempotent

v0.1 建议：

> Watch 不承担 durable event 语义。

如果需要 durable event：

使用 Event Extension。

---

# 29. Backpressure

Registry mutation 不应该因为某个 Watcher 不消费而永久阻塞核心 Registry mutation。

因此：

```text
Registry.Add()
```

不能无限等待：

```text
Watcher
```

除非未来明确把 backpressure 纳入 Contract。

---

# 30. Kernel Boundary

Registry Extension：

可以依赖：

```text
Runtime
Fiber
Activation
Context
Effect
Provider
Dependency
Ownership
```

Kernel：

不得依赖 Registry。

即：

```text
Kernel
   ↑
Registry Extension
```

而不是：

```text
Kernel ↔ Registry
```

---

# 31. Forbidden

v0.1 禁止：

1. 修改 Kernel Fiber state machine。
2. Registry Member 自动成为 Kernel Provider。
3. Member churn 导致 Registry Consumer reactivation。
4. 使用多个 Provider 模拟 Registry。
5. 使用 Event Bus 模拟 Dependency。
6. 使用 polling 实现 Registry membership。
7. Registry 内部持有 Runtime global singleton。
8. Registry lock 内执行用户代码。
9. Add/Remove partial mutation。
10. 依赖 map iteration ordering。
11. Registry disappearance 被当成普通 Member removal。
12. Member removal 自动导致 Registry Consumer withdrawal。
13. 为 Registry 引入第二套生命周期状态机。

---

# 32. Contract Tests

必须至少实现：

### R1 — Stable Identity

```text
Registry Active
Add A
Remove A
Add B
```

Registry Provider Identity 不变。

---

### R2 — Consumer Stability

```text
Registry Active
Consumer Active

Add A
Remove A
Replace A
```

Consumer：

```text
Apply count = 1
Inverse count = 0
```

Consumer 不重新激活。

---

### R3 — Duplicate Member

```text
Add A
Add A
```

第二次：

```text
ErrMemberExists
```

Registry 仍保持：

```text
A
```

---

### R4 — Remove

```text
Add A
Remove A
```

最终：

```text
A absent
```

---

### R5 — Replace Atomicity

```text
Add A
Replace A → B
```

任何 Snapshot 都只能观察：

```text
A
```

或：

```text
B
```

不能观察：

```text
Absent
```

---

### R6 — Snapshot Immutability

```text
S1 = Snapshot()

Add A

S1
```

S1 不包含 A。

---

### R7 — Concurrent Mutation

并发：

```text
Add
Remove
Replace
```

最终状态必须等价于某个合法串行执行。

---

### R8 — Registry Disposal

```text
Registry Active
Add A
Add B

Dispose Registry
```

最终：

```text
A absent
B absent
Registry Gone
```

且 Runtime-managed resources 全部 cleanup。

---

### R9 — Registry Dependency Loss

```text
Registry Active
Consumer Active

Dispose Registry
```

Consumer 按 Kernel Dependency Contract：

```text
Active
→ Unloading
→ Pending
```

不能因为 Registry member 消失而产生特殊生命周期。

---

### R10 — Member Churn

执行：

```text
Add
Remove
Add
Replace
Remove
```

大量重复操作。

验证：

```text
Registry identity stable
Consumer activation stable
```

---

### R11 — Watch

如果实现 Watch：

验证：

```text
Add
Remove
Replace
```

事件顺序和取消语义。

---

### R12 — Race

必须：

```bash
go test -race ./...
```

---

# 33. Property Tests

至少：

### Property 1 — Membership Consistency

任何时刻：

```text
MemberID
```

最多对应一个 Member。

### Property 2 — Snapshot Consistency

Snapshot 必须对应一个合法 Registry state。

### Property 3 — Identity Preservation

Member mutation 不改变 Registry identity。

### Property 4 — Consumer Stability

任意合法 Member churn 不触发 Consumer reactivation。

### Property 5 — Replacement Atomicity

Replace 不产生中间 absent state。

### Property 6 — Disposal Completeness

Registry Gone 时不存在 Runtime-managed live member resource。

---

# 34. Implementation Guidance

推荐内部模型：

```go
type Registry[T any] struct {
    // immutable identity

    mu      sync.RWMutex
    members map[MemberID]T
}
```

但这只是建议。

**不要把这个具体 struct layout 当成 Contract。**

真正必须满足的是：

```text
stable identity
atomic mutation
snapshot consistency
consumer stability
```

---

# 35. Recommended API Shape

建议：

```go
type MemberID string

type Registry[T any] interface {
    Add(MemberID, T) error
    Remove(MemberID) error
    Replace(MemberID, T) error

    Get(MemberID) (T, bool)
    Has(MemberID) bool

    Snapshot() Snapshot[T]
}
```

Snapshot：

```go
type Snapshot[T any] interface {
    Get(MemberID) (T, bool)
    Has(MemberID) bool
    Range(func(MemberID, T) bool)
    Len() int
}
```

具体 API 可以根据现有 Runtime 风格调整。

但是不能降低上述语义。

---

# 36. Acceptance Criteria

Registry Extension 只有在以下条件全部满足后才能 PASS：

```text
[ ] Registry 是稳定 Provider
[ ] Member churn 不改变 Registry identity
[ ] Member churn 不触发 Consumer reactivation
[ ] Add 原子
[ ] Remove 原子
[ ] Replace 原子
[ ] Snapshot 一致
[ ] Snapshot 不可变
[ ] Duplicate Member 被拒绝
[ ] Registry disposal 完整清理
[ ] Registry dependency loss 遵循 Kernel
[ ] Ownership 语义明确
[ ] 无锁内用户代码
[ ] 无 polling
[ ] 无第二套生命周期
[ ] Contract Tests 完整
[ ] Property Tests 完整
[ ] go test ./... PASS
[ ] go test -race ./... PASS
[ ] go vet ./... PASS
```

---

# 37. Implementation Rule

Code Agent 不得：

- 自行改变上述语义；
- 为方便实现修改 Kernel Contract；
- 把 Registry 做成简单 global map；
- 为解决测试问题修改生命周期语义；
- 顺手实现 Event；
- 顺手实现 Config；
- 顺手实现 HMR；
- 顺手实现 MCP；
- 顺手实现 WASM。

如果现有 Kernel API 不足以干净实现 Registry：

> **停止实现并报告 API gap。**

不得偷偷修改 Kernel。

---

# 38. Completion Report

实现完成后必须报告：

1. 修改文件。
2. 新增 API。
3. Registry semantic model。
4. Contract Test 列表。
5. Property Test 列表。
6. Kernel 是否修改。
7. 如果修改，为什么。
8. `go test ./...`
9. `go test -race ./...`
10. `go vet ./...`
11. 所有未解决问题。

最终给出：

```text
REGISTRY EXTENSION

PASS
CONDITIONAL PASS
FAIL
```

并说明原因。

本阶段完成后停止。

不要自行进入 Event Extension。