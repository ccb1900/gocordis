> ⚠️ **ARCHIVED（已归档）— 2026-09-08**
>
> 本文档已整体归档，仅作历史参考，**不再作为规范来源或引用依据**。
> 本仓库现以论文原文为唯一规范来源：`docs/论文.pdf`
> （A Programming Paradigm for Spatiotemporal Composability）。
> 当前路线图与论文↔代码对照见 `docs/ROADMAP.md`。
>
> This document is archived for historical reference only and is no longer cited
> as authority. The paper is the sole normative source; see `docs/ROADMAP.md`.

# Dynamic Composable Runtime — End-to-End Integration & Semantic Hardening Specification v0.1

**Status:** Implementation Specification  
**Version:** v0.1  
**Role:** Integration / Verification  
**Goal:** Verify that all existing Runtime layers compose without semantic conflicts.

---

# 1. Objective

本阶段不增加新的 Runtime 能力。

目标是验证：

```text
Watch
  ↓
ConfigWatch
  ↓
Config
  ↓
Loader
  ↓
HMR
  ↓
Runtime
  ↓
Registry / Event / Scheduler
```

组合之后仍满足既有语义。

核心问题：

> 每个 Extension 单独正确，并不意味着组合后正确。

本阶段专门验证跨模块生命周期、依赖、所有权、并发和关闭语义。

---

# 2. Existing Components

本阶段只允许使用现有实现：

```text
runtime
extensions/config
extensions/configwatch
extensions/watch
extensions/loader
extensions/hmr
extensions/registry
extensions/event
extensions/scheduler
```

不得重新实现这些模块。

---

# 3. Non-Goals

本阶段禁止：

```text
❌ 新增 Extension
❌ 修改 Kernel lifecycle semantics
❌ 修改 Dependency semantics
❌ 修改 Config Desired/Applied semantics
❌ 修改 Watch semantics
❌ 修改 Loader semantics
❌ 修改 HMR semantics
❌ 修改 Registry semantics
❌ 修改 Event semantics
❌ 修改 Scheduler semantics
❌ WASM
❌ Dynamic Go plugin
❌ HTTP module loading
❌ Persistence
❌ Retry framework
❌ Distributed coordination
```

如果发现已有 API 无法完成测试：

```text
STOP
```

报告 API GAP，不自行修改既有模块。

---

# 4. Semantic Authorities

全局必须保持以下唯一权威关系：

```text
Kernel
  = Fiber Lifecycle / Dependency / Effect / Ownership

Config
  = Desired / Applied / Reconcile

Watch
  = External Change Observation

ConfigWatch
  = Watch Change → Config Reconcile Adapter

Loader
  = Artifact → Module / Module lifecycle

HMR
  = Module Replacement Policy

Registry
  = Stable Capability + Dynamic Members

Event
  = Notification

Scheduler
  = Time-based Job Execution
```

任何测试发现两个模块同时承担同一语义，必须报告。

---

# 5. Global Invariants

必须验证以下不变量。

## INV-01 Lifecycle Authority

所有 Fiber 状态变化最终必须由 Runtime/Kernel 决定。

禁止：

```text
Config → direct Fiber state mutation
HMR → direct Fiber state mutation
Watch → Fiber mutation
Loader → Fiber mutation
Registry → Fiber mutation
```

---

## INV-02 Configuration Authority

Desired / Applied 只能由 Config Controller 管理。

Adapter 不得拥有第二份 Applied。

HMR 不得修改 Config Applied。

Loader 不得修改 Config Desired。

---

## INV-03 Module Authority

Loader 管理 Module。

Runtime 管理 Fiber。

不得：

```text
Loader Unload → automatically Dispose Fiber
```

除非已有 Loader semantics 明确定义。

---

## INV-04 Replacement Authority

HMR 决定 replacement policy。

Runtime 决定实际 Fiber lifecycle。

---

## INV-05 External Observation

Watch 只报告：

```text
external state may have changed
```

不得把 Watch Event 当成 Runtime command。

---

## INV-06 Stable Registry

Registry member churn：

```text
Add
Remove
Replace
```

不得改变 Registry Provider identity。

不得因此导致 Consumer reactivation。

---

## INV-07 Event Isolation

Event notification 不得成为 Kernel Dependency。

---

## INV-08 Scheduler Isolation

Scheduler Job execution 不得成为 Fiber lifecycle。

---

# 6. Test Architecture

新增测试目录建议：

```text
integration/
```

或：

```text
extensions/integration/
```

具体位置由项目现有结构决定。

测试可以使用：

```text
real Runtime
real Config
real Watch
real ConfigWatch
real Loader
real HMR
real Registry
real Event
real Scheduler
```

允许使用 fake Component / fake Factory / fake Module。

测试重点是**真实模块组合**，不是重复单元测试。

---

# 7. Test Fixtures

建立统一测试 Fixture：

```go
type Fixture struct {
    Runtime    *runtime.Runtime
    Config     *config.Controller
    Watch      watch.Watch
    ConfigWatch *configwatch.Adapter
    Loader     loader.Loader
    HMR        hmr.HMR
    Registry   *registry.Registry[...]
    Event      *event.Bus
    Scheduler  *scheduler.Scheduler
}
```

具体类型必须根据现有 API 调整。

不得为了 Fixture 修改生产 API。

---

# 8. Test Component Model

定义最小测试 Component：

```text
Provider
Consumer
Independent
RegistryOwner
```

测试 Component 必须能够记录：

```text
Apply count
Inverse count
Fiber ID
Activation ID
```

记录器只能用于测试观察。

不得改变 Runtime semantics。

---

# 9. E2E-01 Configuration Lifecycle

验证完整链：

```text
TOML
 ↓
Watch
 ↓
ConfigWatch
 ↓
Config
 ↓
Factory
 ↓
Runtime
 ↓
Fiber
 ↓
Active
```

流程：

1. 创建配置文件
2. 创建 Watch
3. 创建 Config Controller
4. 创建 ConfigWatch Adapter
5. 启动 Adapter
6. 等待 Fiber Active

PASS：

```text
Config Applied == source Config
Fiber == Active
```

---

# 10. E2E-02 Configuration Replacement

初始：

```text
Component A
Fiber F1
Activation A1
Active
```

修改配置：

```text
Component B
```

必须：

```text
F1 != F2
A1 != A2
```

最终：

```text
F1 == Gone
F2 == Active
```

不得复用：

```text
F1
A1
Context A1
```

---

# 11. E2E-03 Invalid Configuration Preservation

初始：

```text
Valid A
 ↓
Applied A
 ↓
Fiber Active
```

修改：

```text
Invalid B
```

必须：

```text
Applied == A
Fiber remains governed by A
```

不得出现：

```text
Applied = B
Fiber disposed
Applied = empty
```

---

# 12. E2E-04 Provider Replacement

建立：

```text
Provider P
Consumer C
```

其中：

```text
C depends on P
```

配置修改 Provider。

验证：

```text
Old Provider
      ↓
Consumer withdrawal
      ↓
Old Provider Gone
      ↓
New Provider
      ↓
Consumer recovery
```

必须验证：

```text
consumer-first withdrawal
```

不得出现：

```text
Provider Gone
Consumer still Active
```

---

# 13. E2E-05 Multi-Level Dependency

建立：

```text
P
↑
A
↑
B
```

P 为 Provider。

A 依赖 P。

B 依赖 A。

撤销 P。

必须：

```text
B withdraw
 ↓
A withdraw
 ↓
P withdraw
```

恢复：

```text
P recover
 ↓
A recover
 ↓
B recover
```

具体恢复顺序必须符合现有 Kernel dependency semantics。

---

# 14. E2E-06 Registry Member Churn

建立：

```text
Registry Provider
      ↓
Consumer
```

然后：

```text
Member A Add
Member A Remove
Member B Add
Member B Replace
```

验证：

```text
Registry Provider Identity unchanged
Consumer Activation unchanged
Consumer Fiber unchanged
```

除非 Consumer 自己显式依赖某个其他 Provider。

---

# 15. E2E-07 Registry Provider Replacement

与 E2E-06 区分。

这里替换的是：

```text
Registry Provider
```

而不是：

```text
Registry Member
```

必须：

```text
old Registry Provider identity
        !=
new Registry Provider identity
```

因此依赖它的 Consumer 必须按照 Kernel dependency semantics withdrawal/recovery。

证明：

> Stable Registry Provider 与 Registry Member churn 是两个不同层级。

---

# 16. E2E-08 HMR Basic

建立：

```text
Module V1
Component V1
Fiber F1
```

执行 HMR：

```text
Artifact V2
 ↓
Loader
 ↓
Module V2
 ↓
HMR
 ↓
new Component
 ↓
Fiber F2
```

PASS：

```text
F1 != F2
F1 == Gone
F2 == Active
```

Old Module 必须在：

```text
F1 Gone
```

之后才允许 Release/Unload。

---

# 17. E2E-09 HMR Provider Replacement

建立：

```text
Provider V1
Consumer
```

HMR Provider：

```text
V1 → V2
```

必须最终：

```text
old Provider Gone
new Provider Active
Consumer recovered
```

重点：

HMR 不得自行实现 Consumer withdrawal。

必须由 Kernel 完成。

---

# 18. E2E-10 HMR Failure Preservation

建立：

```text
V1 Active
```

HMR V2 故意失败：

```text
Factory Create failure
```

或者：

```text
New Fiber fails
```

必须：

```text
V1 remains Active
Binding remains V1
```

不得：

```text
V1 Gone
Binding empty
```

---

# 19. E2E-11 Config + HMR Separation

同时存在：

```text
configuration change
module implementation change
```

必须保持：

```text
Config change → Config pipeline

Module change → HMR pipeline
```

禁止：

```text
ConfigWatch → HMR
Loader → Config
Watch → Runtime.Load
```

测试通过代码审查和运行时行为共同确认。

---

# 20. E2E-12 Watch + HMR

如果测试环境允许：

```text
Module artifact file
       ↓
Watch
       ↓
HMR Replace
```

可以通过一个明确的测试 Adapter 连接。

注意：

该 Adapter 只能存在于 Integration Test。

不得把：

```text
Watch → HMR
```

实现进入 Watch 或 HMR core。

---

# 21. E2E-13 Loader / Config Separation

验证：

```text
Loader.Load
```

只产生：

```text
Module
```

不会产生：

```text
Fiber
```

同时：

```text
Config.Reconcile
```

才产生：

```text
Component → Fiber
```

证明 Module lifecycle 与 Fiber lifecycle 正交。

---

# 22. E2E-14 Module In-Use

建立：

```text
Module M
Fiber F
```

Module Usage：

```text
Acquire(M)
```

尝试：

```text
Unload(M)
```

必须：

```text
ErrModuleInUse
```

Fiber 不受影响。

之后：

```text
Fiber Gone
Release(M)
Unload(M)
```

必须成功。

---

# 23. E2E-15 Event Independence

建立：

```text
Provider
Consumer
Event Bus
```

Publish Event。

验证：

```text
Event notification
```

不会改变：

```text
Fiber lifecycle
Provider identity
Dependency state
```

除非测试显式建立一个外部 Event → Config/HMR Adapter。

---

# 24. E2E-16 Scheduler Independence

建立：

```text
Scheduler Job
Fiber
```

Job 执行：

```text
start
finish
```

验证 Scheduler 不会自动：

```text
Load Fiber
Dispose Fiber
Change Provider
```

除非测试显式建立 Integration Adapter。

---

# 25. E2E-17 Concurrent Config Changes

快速修改：

```text
A
B
C
D
E
```

Watch 可能产生：

```text
A
C
E
```

甚至合并事件。

最终必须：

```text
Applied == E
```

如果 E 是最终稳定合法状态。

---

# 26. E2E-18 Concurrent HMR

同一个 Target：

```text
Replace(V2)
Replace(V3)
Replace(V4)
```

必须串行。

禁止同一个 Target：

```text
V2 replacement running
+
V3 replacement running
```

同时发生。

最终状态必须符合 HMR 当前 serialized semantics。

---

# 27. E2E-19 Different Target Concurrency

两个不同 Target：

```text
Target A → V2
Target B → V2
```

允许并发。

A 失败：

```text
B must continue
```

---

# 28. E2E-20 Registry + HMR

Component 提供 Registry：

```text
Module V1
 ↓
Fiber
 ↓
Registry Provider
```

HMR Component：

```text
V1 → V2
```

必须：

```text
old Registry Provider Gone
new Registry Provider Active
```

如果 Consumer 依赖 Registry：

```text
Consumer withdrawal
 ↓
Provider replacement
 ↓
Consumer recovery
```

Member churn 不得被错误地等价为 Provider replacement。

---

# 29. E2E-21 Close During Config Reconcile

执行：

```text
Config.Reconcile
```

同时：

```text
Runtime.Close()
```

必须：

```text
no panic
no deadlock
no leaked Fiber
```

如果 Close 超时：

```text
Runtime != Closed
```

不得错误报告 Closed。

---

# 30. E2E-22 Close During HMR

执行：

```text
HMR.Replace
```

同时：

```text
HMR.Close()
```

要求：

```text
no panic
no double cleanup
no send-on-closed
```

如果 HMR CloseContext 超时：

```text
HMR remains Closing
```

---

# 31. E2E-23 Runtime Close During Watch Storm

产生：

```text
hundreds/thousands of Watch notifications
```

同时：

```text
Runtime.Close()
```

必须：

```text
no goroutine explosion
no panic
no send-on-closed
no deadlock
```

最终 Runtime 必须满足既有 Close semantics。

---

# 32. E2E-24 Cross-Extension Isolation

同时制造：

```text
Config failure
HMR failure
Registry mutation
Event publishing
Scheduler execution
```

要求：

```text
failure in A
does not corrupt B
```

不得存在 global error state。

---

# 33. E2E-25 Ownership Verification

建立：

```text
Controller A → Fiber A
Controller B → Fiber B
```

A replacement/removal：

```text
A → remove
```

必须：

```text
Fiber B unchanged
```

HMR A 也不得影响 B。

Registry A member removal 不得影响 B。

---

# 34. E2E-26 Activation Identity

验证：

```text
initial activation A1
replacement activation A2
```

必须：

```text
A1 != A2
```

旧 Context 不得重新使用。

同时验证：

```text
WaitInactive(A1)
```

不会被：

```text
A2 Active
```

错误地当成 A1 completion。

---

# 35. E2E-27 Stale Async Completion

构造：

```text
Fiber F
Activation A1
```

触发异步 operation。

随后：

```text
A1 replaced
A2 created
```

A1 completion 到达。

必须：

```text
A1 completion ignored
```

不得改变：

```text
A2 state
```

这是 Kernel stale-completion invariant 的组合验证。

---

# 36. E2E-28 Repeated Lifecycle Churn

循环：

```text
Load
Active
Replace
Gone
Load
Active
Dispose
Gone
```

至少运行：

```text
100 cycles
```

验证：

```text
no leaked goroutines
no leaked effects
no stale provider
no stale dependency
no stale registry member
```

---

# 37. E2E-29 Full Chain

最终必须有一个完整测试：

```text
External TOML
     ↓
Native Watch
     ↓
ConfigWatch
     ↓
Config Controller
     ↓
Factory
     ↓
Runtime
     ↓
Provider
     ↓
Registry
     ↓
Consumer
     ↓
Active
```

随后：

```text
TOML change
+
Provider replacement
```

最终：

```text
new Provider
+
new Consumer activation
+
correct Registry
```

---

# 38. E2E-30 Full Shutdown

最终组合：

```text
Watch
ConfigWatch
Config
Loader
HMR
Registry
Event
Scheduler
Runtime
```

全部 Running。

然后执行：

```text
Close
```

验证：

```text
Runtime
ConfigWatch
HMR
Loader
Event
Scheduler
```

均最终进入自己的终止状态。

不得出现：

```text
Fiber Active
Activation leaked
Module usage leaked
Watch subscription leaked
Scheduler task leaked
Event subscription leaked
```

注意：

各 Extension 的关闭顺序必须遵守它们现有 API semantics。

不要为了测试而发明新的 shutdown protocol。

---

# 39. Concurrency Matrix

至少测试以下组合：

| Operation A | Operation B |
|---|---|
| Config Reconcile | Config Reconcile |
| Config Reconcile | Runtime Close |
| Watch Change | Config Reconcile |
| Watch Change | Adapter Close |
| HMR Replace | HMR Replace |
| HMR Replace | HMR Close |
| HMR Replace | Runtime Close |
| Registry mutation | Consumer lifecycle |
| Event Publish | Event Close |
| Scheduler Task | Scheduler Close |
| Scheduler Task | Runtime Close |

---

# 40. Race Requirements

运行：

```bash
go test -race ./...
```

至少连续：

```bash
-count=3
```

所有结果必须 PASS。

---

# 41. Vet

运行：

```bash
go vet ./...
```

必须 PASS。

---

# 42. Leak Detection

至少验证：

```text
goroutine count
active fibers
active activations
module usages
watch subscriptions
event subscriptions
scheduler executions
```

不得出现单调增长。

测试不得依赖极其脆弱的固定 goroutine 数字。

推荐使用：

```text
eventual assertion
bounded timeout
```

---

# 43. No Sleep-Based Correctness

禁止：

```go
time.Sleep(1 * time.Second)
```

作为唯一 synchronization mechanism。

应优先使用：

```text
Ready
Gone
WaitInactive
channels
explicit test signals
eventual assertions
```

Scheduler timing tests 可以使用时间窗口，但不能依赖固定 sleep 才能证明 lifecycle correctness。

---

# 44. Failure Injection

测试 Component / Factory / Module Loader 必须支持故障注入：

```text
Apply failure
Inverse failure
Factory failure
Module Load failure
Module Unload failure
Read failure
Parse failure
Reconcile failure
```

验证错误不会跨层污染。

---

# 45. Recovery Exactness

必须验证：

```text
Apply
Inverse
```

最终符合既有 Effect semantics。

尤其：

```text
Apply partial effects
→ error
→ inverse partial effects
```

不得出现 cleanup leak。

---

# 46. No Hidden Cross-Layer Mutation

通过代码审查确认：

```text
Watch
```

没有：

```text
runtime import
```

除非已有 Integration Test Adapter。

确认：

```text
runtime
```

没有：

```text
configwatch
hmr
loader
watch
```

反向依赖。

---

# 47. Dependency Direction

必须保持：

```text
Kernel
   ↑
Extensions
   ↑
Integration adapters
```

而不是：

```text
Kernel
 ↓
HMR
 ↓
Watch
 ↓
Config
```

Kernel 对任何 Extension 都无感知。

---

# 48. No Second Lifecycle System

全局搜索并确认不存在：

```text
AdapterFiber
ModuleFiber
WatchFiber
RegistryFiber
SchedulerFiber
HMRFiber
```

这些如果只是测试名称可以存在，但不得成为 production lifecycle abstraction。

---

# 49. Property P-01 — Lifecycle Conservation

任意合法 replacement：

```text
Old Fiber
New Fiber
```

最终不得同时留下两个同一 logical component 的 Active Fiber。

---

# 50. Property P-02 — Dependency Conservation

对于：

```text
P → C
```

任意 Provider replacement：

```text
old P
new P
```

不得出现：

```text
C Active while required P unavailable
```

---

# 51. Property P-03 — Configuration Conservation

任意：

```text
Valid A
Invalid B
```

最终：

```text
Applied == A
```

---

# 52. Property P-04 — Module/Fiber Orthogonality

Module Unload 不应自动改变不属于 Loader semantics 的 Fiber。

Fiber Gone 之后 Module Usage 才可以 Release。

---

# 53. Property P-05 — Registry Stability

任意 Member churn：

```text
Add
Remove
Replace
```

Registry Provider identity 不变。

---

# 54. Property P-06 — Event Non-Interference

Event Publish 不应自动产生 lifecycle transition。

---

# 55. Property P-07 — Scheduler Non-Interference

Scheduler execution 不应自动产生 lifecycle transition。

---

# 56. Property P-08 — Close Truthfulness

任意：

```text
CloseContext(timeout)
```

如果底层仍存在 active work：

```text
state != Closed
```

---

# 57. Property P-09 — Ownership Isolation

Controller A 只能影响：

```text
A-owned components
```

不得删除：

```text
B-owned components
```

---

# 58. Property P-10 — Stale Completion Isolation

旧 Activation completion 不得改变新 Activation。

---

# 59. Property P-11 — No Duplicate Lifecycle

同一个 Fiber：

```text
Apply
Inverse
```

不得重叠执行。

同一个 HMR Target replacement 不得重叠。

同一个 Config Controller Reconcile 不得重叠。

---

# 60. Property P-12 — Convergence

如果：

```text
External source
```

最终稳定为：

```text
Valid Config C
```

并且 Watch 最终产生变化通知，则：

```text
Config Applied → C
```

如果 Runtime 本身满足依赖与 Factory 条件，则最终：

```text
Runtime → corresponding stable state
```

---

# 61. Observability

测试可以提供统一 Trace：

```go
type TraceEvent struct {
    Time       time.Time
    Layer      string
    Operation  string
    FiberID    runtime.FiberID
    Activation runtime.ActivationID
}
```

Trace 只用于测试验证。

不得成为 production lifecycle system。

---

# 62. Expected Trace

例如 Provider replacement：

```text
Config.Reconcile
    ↓
old Consumer Unloading
    ↓
old Provider Unloading
    ↓
old Provider Gone
    ↓
new Provider Loading
    ↓
new Provider Active
    ↓
Consumer Loading
    ↓
Consumer Active
```

实际内部事件顺序必须以既有 Kernel semantics 为准。

测试不能要求 Spec 未定义的微观调度顺序。

---

# 63. Determinism

测试不得依赖：

```text
goroutine scheduling order
map iteration order
OS filesystem event ordering
```

除非对应行为已经被现有 API 明确定义。

---

# 64. Test Timeouts

每个 integration test 必须有 bounded timeout。

例如：

```go
ctx, cancel := context.WithTimeout(...)
defer cancel()
```

不得：

```text
无限等待
```

---

# 65. No Production Semantic Changes

本阶段默认：

```text
runtime: unchanged
config: unchanged
watch: unchanged
configwatch: unchanged
loader: unchanged
hmr: unchanged
registry: unchanged
event: unchanged
scheduler: unchanged
```

如果测试失败：

第一优先级：

```text
确定是测试错误还是既有实现错误
```

不得立即修改生产语义。

---

# 66. API Gap Protocol

发现问题时必须：

```text
API GAP

Affected:
Existing API:
Required semantic:
Why current API is insufficient:
Minimal change:
Would this change alter existing semantics:
```

然后 STOP。

---

# 67. Implementation Constraints

Code Agent：

1. 先阅读全部现有 Extension API。
2. 不修改既有生产实现。
3. 新增 Integration Test / Test Fixture。
4. 允许新增测试辅助代码。
5. 不新增 Runtime abstraction。
6. 不新增 lifecycle system。
7. 不修改已有 Spec 语义。
8. 不为了测试制造特殊 production API。

---

# 68. Acceptance Criteria

必须全部 PASS：

```text
E2E-01  Configuration Lifecycle
E2E-02  Configuration Replacement
E2E-03  Invalid Configuration Preservation
E2E-04  Provider Replacement
E2E-05  Multi-Level Dependency
E2E-06  Registry Member Churn
E2E-07  Registry Provider Replacement
E2E-08  HMR Basic
E2E-09  HMR Provider Replacement
E2E-10  HMR Failure Preservation
E2E-11  Config/HMR Separation
E2E-12  Watch/HMR Isolation
E2E-13  Loader/Config Separation
E2E-14  Module In-Use
E2E-15  Event Independence
E2E-16  Scheduler Independence
E2E-17  Concurrent Config Changes
E2E-18  Concurrent HMR
E2E-19  Different Target Concurrency
E2E-20  Registry/HMR
E2E-21  Close During Config Reconcile
E2E-22  Close During HMR
E2E-23  Close During Watch Storm
E2E-24  Cross-Extension Isolation
E2E-25  Ownership
E2E-26  Activation Identity
E2E-27  Stale Async Completion
E2E-28  Lifecycle Churn
E2E-29  Full Chain
E2E-30  Full Shutdown

P-01   Lifecycle Conservation
P-02   Dependency Conservation
P-03   Configuration Conservation
P-04   Module/Fiber Orthogonality
P-05   Registry Stability
P-06   Event Non-Interference
P-07   Scheduler Non-Interference
P-08   Close Truthfulness
P-09   Ownership Isolation
P-10   Stale Completion Isolation
P-11   No Duplicate Lifecycle
P-12   Convergence

Quality:
go test ./...             PASS
go test -race ./...       PASS
go vet ./...              PASS
```

---

# 69. Completion Report

完成后严格使用：

```text
END-TO-END INTEGRATION & SEMANTIC HARDENING

Status:
PASS / CONDITIONAL PASS / FAIL

Implemented:
- ...

Test Files:
- ...

Integration:
- E2E-01 PASS
- ...

Properties:
- P-01 PASS
- ...

Concurrency:
- ...

Race:
- ...

Vet:
- ...

Production Packages Modified:
- ...

Kernel Modified:
YES / NO

Config Modified:
YES / NO

Watch Modified:
YES / NO

Loader Modified:
YES / NO

HMR Modified:
YES / NO

Registry Modified:
YES / NO

Event Modified:
YES / NO

Scheduler Modified:
YES / NO

Known Issues:
- ...

Semantic Conflicts:
- ...

API Gaps:
- ...

Acceptance:
E2E-01 PASS
...
E2E-30 PASS

P-01 PASS
...
P-12 PASS
```

**Code Agent 不得自行宣布 Runtime 完成。**

本阶段的任务是证明已有架构组合正确；审查完成后再决定下一阶段。