> ⚠️ **ARCHIVED（已归档）— 2026-09-08**
>
> 本文档已整体归档，仅作历史参考，**不再作为规范来源或引用依据**。
> 本仓库现以论文原文为唯一规范来源：`docs/论文.pdf`
> （A Programming Paradigm for Spatiotemporal Composability）。
> 当前路线图与论文↔代码对照见 `docs/ROADMAP.md`。
>
> This document is archived for historical reference only and is no longer cited
> as authority. The paper is the sole normative source; see `docs/ROADMAP.md`.

# gocordis Plugin Runtime Platform Completion Specification v0.1

## 0. 文档定位

本规范用于指导 `gocordis` 从当前 Runtime Kernel 演进为完整的、可承载 “Everything is a Plugin” 应用的 Go Runtime Platform。

本规范的第一性依据：

1. Dynamic Composable Runtime 论文
2. `stc-go`
3. JS Cordis
4. DeepSeek Harness 对 Cordis 的实际使用方式

优先级严格遵循：

```text
论文
 ↓
stc-go
 ↓
JS Cordis
 ↓
DeepSeek Harness
```

DeepSeek Harness 仅用于验证：

> 一个真实复杂应用如何利用 Cordis Runtime 组织 Plugin、Service、Dependency、Event、Effect、Scope、Loader 和动态组合。

不得因为兼容 DeepSeek Harness 而违反论文语义。

---

# 1. 总体目标

gocordis 最终应能够自然承载如下模型：

```text
Application
    │
    └── Bootstrap
          │
          ↓
      gocordis Runtime
          │
          ├── Plugin A
          │     ├── Service
          │     ├── Event
          │     └── Effects
          │
          ├── Plugin B
          │     ├── Service
          │     └── Dependency
          │
          ├── Plugin C
          │
          └── Plugin D
```

Plugin 是 Runtime 中最基本的可组合能力单元。

Plugin 必须能够：

```text
install
activate
provide capability
require capability
register event handler
register effects
create child composition
withdraw
dispose
reload
replace
```

并且所有上述行为必须服从 Runtime 的：

```text
ownership
dependency
lifecycle
quiescence
reconciliation
```

语义。

---

# 2. 非目标

本阶段禁止：

```text
❌ 工业采集业务
❌ CSV/XLSX
❌ Oracle/MySQL
❌ Agent
❌ Scheduler 业务实现
❌ Developer Console UI
❌ Web UI
❌ MCP
❌ LLM
❌ Domain-specific Service
❌ 工业 Scope
❌ 工业 Pipeline
```

这些全部属于上层 Application / Extension。

本阶段也禁止为了“看起来像 Cordis”而复制 JS Cordis 的 API。

---

# 3. 当前 Kernel 保持不变的核心原则

以下语义已经属于 gocordis 核心资产，不得为了 API 美化而破坏：

```text
Component
Fiber
Activation
Context
Effect
Provider
Dependency
Realm
Orchestrator
Quiescence
Reconciliation
Ownership
LIFO disposal
Provider generation identity
```

尤其不得：

```text
❌ Component 直接操作其他 Fiber
❌ Component 直接修改 Registry
❌ Component 直接修改 Orchestrator 状态
❌ HMR 直接修改 Fiber 内部字段
❌ Extension 绕过 Runtime public API
❌ 用 service existence 代替 ownership invariant
```

Kernel 必须继续保持：

> Runtime 是 lifecycle authority。

---

# 4. 本阶段只实现三个能力域

```text
P1 Event Runtime
P2 Public Plugin/Context/Service Model
P3 Declarative Composition / Loader
```

最终形成：

```text
Plugin
  │
  ↓
Context
  │
  ├── Service
  ├── Dependency
  ├── Event
  ├── Effect
  └── Scope
       │
       ↓
Runtime Reconciliation
       │
       ↓
Quiescence
       │
       ↓
Lifecycle
```

---

# 5. P1 — Event Runtime

## 5.1 目标

Event 必须成为 Plugin Composition 的正式 Runtime primitive。

Event 不能只是普通 Go callback registry。

必须具有明确的：

```text
ownership
lifecycle
scope
ordering
dispatch semantics
disposal
```

---

# 6. Event 类型

Runtime 至少支持以下四种语义：

```text
Emit
Parallel
Serial
Waterfall
```

可以额外支持：

```text
Bail
```

但不得把不同 dispatch semantics 混为一个 API。

---

# 7. Emit

语义：

```text
emit(event, payload)
```

表示：

> 广播通知，不依赖 listener 返回值。

特点：

```text
fire-and-observe
```

不得依赖 handler 的返回值决定结果。

Listener 的生命周期属于当前 Context。

Context dispose 后 listener 必须自动移除。

---

# 8. Parallel

语义：

```text
parallel(event, payload)
```

所有 handler 执行，并等待全部完成。

逻辑：

```text
       ┌── Handler A
Event ─┼── Handler B
       └── Handler C
             ↓
          await all
```

要求：

```text
- handlers independent
- await all
- lifecycle-owned
- cancellation-aware
```

不得保证 handler 执行顺序。

---

# 9. Serial

语义：

```text
serial(event, payload)
```

handler 按明确顺序执行：

```text
A
↓
B
↓
C
```

要求：

```text
- deterministic ordering
- await previous before next
- handler lifecycle owned
```

顺序必须有定义。

不得依赖 map iteration order。

---

# 10. Waterfall

Waterfall 是本阶段最高优先级 Event 能力。

语义：

```text
A
 ↓ next
B
 ↓ next
C
 ↓
result
```

它允许 Plugin 对已有行为进行：

```text
observe
modify
wrap
short-circuit
```

典型模型：

```go
ctx.Waterfall("request", initial, func(next Next, value Value) Value {
    value = modify(value)
    return next(value)
})
```

具体 Go API 不要求与 JS Cordis 相同。

必须满足：

### W-01

Handler 可以修改输入。

### W-02

Handler 可以决定是否调用 downstream。

### W-03

最终存在明确 result。

### W-04

执行顺序 deterministic。

### W-05

handler disposal 自动解除注册。

### W-06

Waterfall execution 必须服从 Context cancellation。

### W-07

Waterfall handler 本身必须属于 Effect ownership。

---

# 11. Event 与 Effect 的关系

Event registration：

```text
ctx.On(...)
```

本质必须等价于：

```text
effect registration
+
inverse unregister
```

因此：

```text
Activate Plugin
      ↓
register handler
      ↓
Plugin unload
      ↓
effect unwind
      ↓
handler removed
```

禁止存在：

```text
Plugin disposed
     ↓
event handler still alive
```

---

# 12. Scoped Event

Event 必须支持 Scope / Realm visibility。

至少需要：

```text
Global Event
Scoped Event
```

例如：

```text
Root
 ├── Plugin A
 │
 └── Scope X
      ├── Plugin B
      └── Plugin C
```

Scope X 中注册的 Event Handler：

```text
visible inside X
```

不得泄漏到 sibling scope。

如果设计允许 ancestor propagation，则必须定义：

```text
capture
bubble
inheritance
shadowing
```

具体语义在实现前必须形成明确规范，不允许凭直觉实现。

---

# 13. P2 — Public Plugin / Context / Service Model

## 13.1 目标

当前 Kernel API 已经具备：

```text
Provide
Require
Effect
Child
```

本阶段需要在其上形成真正面向 Plugin 作者的公共编程模型。

最终开发者看到的是：

```go
func Apply(ctx *Context) {
    ...
}
```

而不是：

```go
直接操作 Runtime 内部 registry
```

---

# 14. Plugin Model

Plugin 至少具备：

```text
Identity
Apply
Lifecycle
Dependencies
Provided capabilities
```

概念模型：

```go
type Plugin interface {
    Apply(ctx *Context) error
}
```

如果当前 Component 已经能够表达该模型，不得重复创建第二套 Plugin Runtime。

应优先：

```text
Plugin
    ↓
Component
    ↓
Fiber
```

即：

> Plugin 是 Application-facing abstraction，Component 是 Runtime-facing abstraction。

---

# 15. Service / Capability Model

必须明确区分：

```text
Service
Capability
Provider
```

建议语义：

```text
Capability
=
Plugin 对 Runtime 暴露的可消费能力

Provider
=
Capability 在某个 activation generation 中的具体拥有者

Service
=
Application-facing capability abstraction
```

不得让 Service 直接成为：

```text
global singleton
```

---

# 16. Service Identity

Service 必须具有稳定 identity。

例如概念上：

```text
ServiceKey[T]
```

或者：

```text
Capability[T]
```

具体 API 可以根据现有 gocordis 类型系统确定。

要求：

```text
- type safe
- stable identity
- scope aware
- provider generation aware
```

不得通过：

```go
map[string]any
```

作为核心 Service API。

---

# 17. Service Injection

Plugin 应声明：

```text
requires A
requires B
provides C
```

Runtime 根据 dependency graph 决定：

```text
Pending
→ Active
→ Gone
```

而不是 Plugin 自己：

```text
if service == nil {
    retry()
}
```

禁止业务 Plugin 自己轮询依赖。

---

# 18. Dependency 与 Service 不得重复实现

必须确保：

```text
Service dependency
        ↓
gocordis Dependency
```

只有一套真正的 dependency semantics。

禁止出现：

```text
ServiceRegistry dependency
+
Kernel dependency
```

两套互相独立的依赖系统。

---

# 19. Context 的最终职责

Public Context 至少应该提供：

```text
ctx.Effect()
ctx.Provide()
ctx.Require()
ctx.On()
ctx.Emit()
ctx.Serial()
ctx.Parallel()
ctx.Waterfall()
ctx.Child()
ctx.Scope()
```

注意：

这不是要求所有 API 都必须直接挂在 Context 上。

如果现有架构已经有合理的 typed interfaces，可以通过：

```text
Context
 ├── Effect API
 ├── Dependency API
 ├── Event API
 └── Composition API
```

组合。

目标是：

> Plugin 作者只需要 Context，而不需要了解 Runtime 内部结构。

---

# 20. Context 不得成为 God Object

Context 不得承担：

```text
scheduler
database
HTTP server
logger implementation
configuration parser
business logic
```

Context 只负责：

```text
composition
capability access
dependency
events
effects
lifecycle
scope
```

Application capability 必须通过 Service 注入。

---

# 21. P3 — Declarative Composition / Loader

## 21.1 目标

最终允许：

```text
Composition Definition
        ↓
Loader
        ↓
Plugin Graph
        ↓
Runtime Reconciliation
```

Loader 负责：

```text
discover
resolve
instantiate
activate
update
replace
disable
remove
```

但：

> Loader 不拥有 Lifecycle。

Lifecycle 仍然属于 Runtime。

---

# 22. Loader 与 Runtime 的边界

正确：

```text
Loader
  ↓
告诉 Runtime：
“这里应该存在 Plugin A”
  ↓
Runtime
  ↓
reconcile
```

错误：

```text
Loader
  ↓
自己启动 Fiber
自己 dispose Fiber
自己修改 Provider Registry
```

Loader 不得成为第二个 Orchestrator。

---

# 23. Composition State

Loader 至少应该能够表达：

```text
Plugin identity
Plugin implementation
Configuration
Enabled / Disabled
Dependencies
Scope
```

最终 Runtime 处理：

```text
desired state
       ↓
actual state
       ↓
diff
       ↓
reconciliation
```

---

# 24. Config Watch

配置变化必须转换成：

```text
Composition Change
```

而不是：

```text
Config changed
↓
直接修改 Plugin struct
```

正确模型：

```text
Config
  ↓
Desired Composition
  ↓
Composition Diff
  ↓
Runtime Reconciliation
  ↓
Quiescence
  ↓
Replacement / Activation / Disposal
```

---

# 25. HMR

HMR 必须继续保持 Extension 身份。

HMR 不得成为 Kernel 内部特殊路径。

正确：

```text
HMR Extension
      ↓
Loader / Composition API
      ↓
Runtime
      ↓
Reconciliation
```

禁止：

```text
HMR
 ↓
直接操作 Fiber
```

---

# 26. WASM

WASM 同样必须保持 Extension / Plugin Backend。

最终应该允许：

```text
Go Plugin
WASM Plugin
Remote Plugin
Built-in Plugin
```

都映射成：

```text
Plugin
   ↓
Component
   ↓
Runtime
```

Runtime 不应该知道：

```text
Go
WASM
Remote
```

这些实现细节。

---

# 27. Dynamic Composition

Runtime 最终必须支持：

```text
install plugin
remove plugin
replace plugin
change dependency
change configuration
change scope
```

而不需要重新创建整个 Runtime。

核心过程：

```text
Desired Composition
        ↓
Reconciliation
        ↓
Dependency Resolution
        ↓
Quiescence
        ↓
Effect Unwind
        ↓
Activation
        ↓
Stable State
```

---

# 28. 一个完整 Plugin 的生命周期

最终必须能够自然表达：

```text
Declared
   ↓
Loading
   ↓
Pending
   ↓
Activating
   ↓
Active
   ↓
Withdrawn
   ↓
Unwinding
   ↓
Gone
```

失败必须具有明确状态。

不得出现：

```text
半 Active
半 Gone
```

之类无法定义的状态。

---

# 29. Plugin 示例

以下只是语义示例，不要求 API 完全采用此形式：

```go
type DatabasePlugin struct{}

func (DatabasePlugin) Apply(ctx *Context) error {
    db := NewDatabase()

    ctx.Effect(func() {
        db.Close()
    })

    ctx.Provide(DatabaseCapability, db)

    return nil
}
```

另一个 Plugin：

```go
type RepositoryPlugin struct{}

func (RepositoryPlugin) Apply(ctx *Context) error {
    db := ctx.Require(DatabaseCapability)

    repo := NewRepository(db)

    ctx.Provide(RepositoryCapability, repo)

    return nil
}
```

最终：

```text
DatabasePlugin
      │
      │ provides Database
      ↓
RepositoryPlugin
      │
      │ provides Repository
      ↓
ConsumerPlugin
```

Database 消失：

```text
Database
   ↓
Repository withdraw
   ↓
Consumer withdraw
   ↓
effects unwind
   ↓
Gone
```

不得通过业务代码手工维护该链。

---

# 30. 组合示例

最终 gocordis 应能够承载：

```text
Root Context
│
├── Config Plugin
│
├── Event Plugin
│
├── Database Plugin
│
├── Scheduler Plugin
│
└── Machine Scope
     │
     ├── CSV Source Plugin
     ├── Parser Plugin
     └── Oracle Sink Plugin
```

这里：

```text
CSV
Parser
Oracle
Scheduler
```

都不是 gocordis Kernel。

它们只是：

```text
Plugin
```

---

# 31. Acceptance Criteria

## A — Event

必须满足：

```text
A-01 Emit
A-02 Parallel
A-03 Serial
A-04 Waterfall
A-05 cancellation
A-06 lifecycle disposal
A-07 deterministic ordering
A-08 scoped ownership
A-09 no handler leak
```

---

## B — Plugin / Context

必须满足：

```text
B-01 Plugin 可以仅通过 Context 工作
B-02 Plugin 无需访问 Runtime 内部结构
B-03 Service dependency 使用 Kernel Dependency
B-04 Service lifecycle 使用 Kernel lifecycle
B-05 Effect 自动 unwind
B-06 Provider generation 正确
B-07 Scope 正确
B-08 Plugin 不拥有 Lifecycle Authority
```

---

## C — Composition

必须满足：

```text
C-01 Plugin install
C-02 Plugin remove
C-03 Plugin replace
C-04 Plugin disable
C-05 dependency change
C-06 configuration change
C-07 scoped composition change
C-08 reconciliation
C-09 quiescence
C-10 no direct Fiber mutation
```

---

## D — Loader

必须满足：

```text
D-01 Declarative composition
D-02 Runtime reconciliation
D-03 loader 不拥有 lifecycle
D-04 loader 不操作 registry internals
D-05 loader 支持 update
D-06 HMR 可以建立在 Loader/Composition API 上
```

---

# 32. 最终架构

完成以后，gocordis 应收敛为：

```text
                    Application
                         │
                         ↓
                  Plugin Composition
                         │
              ┌──────────┴──────────┐
              ↓                     ↓
           Loader               Native API
              │                     │
              └──────────┬──────────┘
                         ↓
                    gocordis
                         │
        ┌────────────────┼────────────────┐
        ↓                ↓                ↓
      Scope          Dependency        Events
        │                │                │
        └────────────────┼────────────────┘
                         ↓
                    Orchestrator
                         │
                 ┌───────┴───────┐
                 ↓               ↓
              Fiber           Effect
                 │               │
                 └───────┬───────┘
                         ↓
                    Quiescence
                         ↓
                  Stable Runtime
```

---

# 33. 最终判断标准

不要以：

```text
“API 有多少”
“代码多少”
“Extension 有多少”
```

判断完成度。

最终只问一个问题：

> **能不能把一个真实复杂应用完全拆成 Plugin，并仅通过 Context / Service / Dependency / Event / Effect / Scope / Composition 组合起来，而不需要向 gocordis Kernel 添加领域业务代码？**

如果答案是：

```text
YES
```

则：

> **gocordis Runtime Platform v1 的核心目标达成。**

如果必须添加：

```text
IndustrialCollector
AgentManager
LLMManager
SchedulerManager
DatabaseManager
```

这种“业务总管”才能工作：

> **说明 Runtime 仍然没有完成。**

---

# 34. 本阶段严格执行顺序

不要同时实现所有内容。

按照：

```text
P1 Event
   ↓
P1.1 Emit
P1.2 Serial
P1.3 Parallel
P1.4 Waterfall
   ↓
P2 Context / Service
   ↓
P2.1 Public Plugin API
P2.2 Service abstraction
P2.3 Injection
P2.4 Context-facing Event API
   ↓
P3 Composition
   ↓
P3.1 Desired Composition
P3.2 Diff
P3.3 Reconciliation
P3.4 Loader
P3.5 Dynamic Update
   ↓
P4 Integration
   ↓
HMR
WASM
ConfigWatch
```

每一步都必须优先复用已有 Kernel。

**禁止为了实现新 API 而重新设计 Fiber / Effect / Dependency / Realm。**

---

# 35. 完成后的能力边界

最终：

```text
gocordis Kernel
=
“如何让动态组合安全地存在”
```

```text
gocordis Platform
=
“如何让 Plugin 使用这种动态组合”
```

```text
Application
=
“组合哪些 Plugin 来解决什么问题”
```

三者必须严格分层。

这就是本阶段的最终收敛目标。