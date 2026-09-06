# Dynamic Composable Runtime
## Framework Boundary & Completeness Specification v0.1

**Status:** Architecture Authority  
**Scope:** Framework boundary, Kernel boundary, Infrastructure boundary, Application boundary, completeness criteria, migration rules  
**Audience:** Architect / Code Agent / Review  
**Normative terms:** MUST / MUST NOT / SHOULD / MAY

---

# 1. Purpose

本规范重新定义 Dynamic Composable Runtime 的框架边界，并建立：

1. Framework 的正式定义；
2. Kernel 的最小职责；
3. Runtime Infrastructure 的职责与边界；
4. Integration / Backend 的职责；
5. Application / Business Capability 的职责；
6. 已实现模块的重新分类；
7. Framework “实现完整”的判定标准；
8. 后续代码迁移与冻结规则。

本规范是此前所有具体 Extension Specification 的**上位架构约束**。

如果既有实现或既有 Extension Specification 与本规范冲突，以本规范为准。

---

# 2. Framework Definition

Dynamic Composable Runtime 的正式定义：

> **Dynamic Composable Runtime 是一个负责在运行期间动态装配、激活、撤销、替换和恢复 Component，并对其生命周期、依赖关系、资源 Effect、Provider Identity、Ownership 和并发一致性提供确定语义的通用运行时框架。**

Framework 的核心对象不是：

- HTTP
- MCP
- WASM
- Database
- LLM
- Camera
- CAN
- Agent
- ETL

Framework 的核心对象是：

```text
Component
Fiber
Activation
Context
Effect
Provider
Dependency
Ownership
Lifecycle
```

因此 Framework 的核心问题是：

```text
How can a running system safely change its composition?
```

而不是：

```text
What business capabilities does the system provide?
```

---

# 3. Fundamental Boundary

整个系统 MUST 分成以下四个逻辑层次：

```text
┌──────────────────────────────────────────────────────────┐
│ Application / Business Capability                        │
│                                                          │
│ Agent / MCP / HTTP / Camera / CAN / LLM / DB / ETL ... │
└───────────────────────────┬──────────────────────────────┘
                            │
                            │ Component / Capability API
                            ▼
┌──────────────────────────────────────────────────────────┐
│ Runtime Infrastructure                                   │
│                                                          │
│ Registry / Event / Scheduler / Config / Loader / HMR ...│
└───────────────────────────┬──────────────────────────────┘
                            │
                            │ Runtime API
                            ▼
┌──────────────────────────────────────────────────────────┐
│ Kernel                                                   │
│                                                          │
│ Context / Component / Fiber / Activation / Effect       │
│ Dependency / Provider Identity / Ownership / Lifecycle  │
└──────────────────────────────────────────────────────────┘
```

此外存在横向的：

```text
Integration / Backend
```

用于连接 Runtime、Implementation 和外部世界。

---

# 4. Layer 0 — Kernel

Kernel 是 Framework 的不可替代语义核心。

Kernel MUST only define mechanisms required to make dynamic composition semantically correct.

Kernel MUST own:

## 4.1 Component

Component 是 Runtime 可以管理的最小运行单元。

Kernel MUST NOT know the domain meaning of a Component.

---

## 4.2 Fiber

Fiber 是 Component 在 Runtime 中的稳定生命周期实体。

Fiber MUST have a stable logical identity.

Fiber state MUST NOT be publicly mutable.

---

## 4.3 Activation

Activation 表示 Fiber 的一次实际运行周期。

每一次重新激活 MUST 获得新的 Activation Identity。

旧 Activation 的异步完成不得影响新 Activation。

---

## 4.4 Context

Context 是 Activation-local runtime context。

Context MUST provide:

- Effect registration;
- Child ownership;
- Provider access;
- cancellation / withdrawal state;
- activation identity.

Context MUST NOT become an unrestricted mutable key-value store.

---

## 4.5 Effect

Runtime-managed mutable state MUST be represented by Effect.

Effect MUST provide:

```text
install
+
inverse
```

并满足：

```text
successful activation
    ↓
effects committed
    ↓
unwind in reverse order
```

Effect MUST be owned by exactly one Activation.

---

## 4.6 Dependency

Kernel MUST own runtime dependency semantics.

Dependency is a persistent runtime condition.

It is NOT merely:

```text
startup ordering
```

Dependency loss MUST trigger withdrawal semantics rather than being represented as an arbitrary failure.

---

## 4.7 Provider Identity

Provider identity MUST distinguish:

```text
same capability
```

from:

```text
same provider activation
```

Minimum identity:

```text
ProviderIdentity{
    FiberID,
    ActivationID,
}
```

Provider replacement MUST therefore be observable as identity replacement.

---

## 4.8 Ownership

Ownership MUST remain distinct from Dependency.

Dependency:

```text
A requires B
```

Ownership:

```text
A owns C
```

The former controls availability.

The latter controls cleanup responsibility.

---

## 4.9 Lifecycle Coordinator

All Fiber lifecycle decisions MUST have a single authoritative decision domain.

Kernel MUST prevent:

- overlapping Apply / Inverse on one Fiber;
- stale async completion;
- duplicate lifecycle systems;
- user code executing under Runtime lifecycle locks.

---

## 4.10 Runtime Close

Kernel MUST own Runtime shutdown semantics.

Close MUST be truthful:

```text
Running
   ↓
Closing
   ↓
Closed
```

A timeout MUST NOT be represented as successful Closed.

---

# 5. Kernel Exclusions

Kernel MUST NOT contain:

```text
HTTP
MCP
WASM
Database
LLM
Camera
CAN
MQTT
Kafka
Filesystem
TOML
YAML
JSON
Agent
ETL
HMR policy
Watch backend
business events
business schemas
```

Kernel MUST NOT contain protocol-specific logic.

Kernel MUST NOT contain transport-specific logic.

Kernel MUST NOT contain external-resource-specific logic.

Kernel MUST NOT contain business capability definitions.

---

# 6. Layer 1 — Runtime Infrastructure

Runtime Infrastructure consists of mechanisms that are:

1. domain-independent;
2. useful across many Components;
3. concerned with composition, observation, scheduling, implementation loading, or runtime control;
4. built on top of Kernel;
5. not themselves business capabilities.

Infrastructure MUST NOT redefine Kernel lifecycle semantics.

Infrastructure MUST use Kernel public semantics rather than directly mutating Kernel internals.

---

# 7. Registry

Generic Registry MAY belong to Runtime Infrastructure.

Its responsibility is:

```text
stable capability
+
dynamic member set
```

Registry MUST NOT become a hidden second lifecycle system.

Registry members MUST NOT automatically become Kernel Providers.

Business registries MAY be built using the generic Registry mechanism.

Example:

```text
Registry[T]              Framework Infrastructure

CameraRegistry           Application Capability
ToolRegistry             Application / Agent Capability
ConnectionRegistry       Application Capability
```

Thus the generic mechanism belongs to Framework Infrastructure; domain-specific registries do not.

---

# 8. Event

Generic Event mechanism MAY belong to Runtime Infrastructure.

Event MUST represent notification semantics only.

The Runtime MUST NOT define business event meaning.

Therefore:

```text
Event Bus                  Infrastructure
EventType mechanism        Infrastructure

OrderCreated               Application
CameraConnected            Application
ToolInvoked                Application
```

Event subscription MUST NOT become a Kernel Dependency.

---

# 9. Scheduler

Generic Scheduler MAY belong to Runtime Infrastructure.

Scheduler provides:

```text
when to execute
```

It MUST NOT define:

```text
what the business task means
```

Therefore:

```text
Scheduler                  Infrastructure
Job execution mechanism    Infrastructure

DailyCameraSync            Application
RetryBillingJob            Application
LLMHeartbeat               Application
```

---

# 10. Config

Config requires a strict distinction.

## 10.1 Config Model

The generic desired-state model MAY belong to Runtime Infrastructure.

```text
Desired Component Tree
        ↓
Reconcile
        ↓
Runtime commands
```

## 10.2 Configuration Format

Formats MUST NOT belong to Runtime.

Therefore:

```text
Config Reconciler          Infrastructure
ComponentConfig            Infrastructure

TOML                       Integration
YAML                       Integration
JSON                       Integration

Application Config Schema  Application
```

A parser MUST NOT be required by Kernel.

---

# 11. Loader

Loader MAY belong to Runtime Infrastructure as an implementation-loading mechanism.

Its responsibility is:

```text
Artifact
   ↓
Backend
   ↓
Module
   ↓
Factory / Component implementation
```

Loader MUST NOT own:

- Fiber lifecycle;
- dependency resolution;
- HMR policy;
- Watch;
- business configuration;
- application semantics.

---

# 12. Backend

A Backend is an implementation mechanism used by Loader.

Examples:

```text
Builtin Backend
WASM Backend
Go Plugin Backend
Remote Module Backend
```

Backend MUST NOT be treated as a Framework capability.

For example:

```text
WASM
```

is not a Runtime capability.

Correct classification:

```text
Loader
  └── WASM Backend
```

The Runtime MUST remain unaware of WASM-specific execution semantics.

---

# 13. Watch

Watch is Integration Infrastructure.

Its responsibility is:

```text
External World
      ↓
Observation
      ↓
Normalized Change
```

Watch MUST NOT directly:

- reconcile Config;
- perform HMR;
- mutate Fiber;
- load Modules;
- alter Provider state.

Those are explicit adapters/policies.

Therefore:

```text
Filesystem Watch       Integration
Kubernetes Watch       Integration
Git Watch              Integration

Watch abstraction      Infrastructure boundary
```

---

# 14. HMR

HMR is NOT a Kernel primitive.

HMR is a replacement policy built on Kernel + Loader.

Conceptually:

```text
Loader
   ↓
New Module

Kernel
   ↓
New Fiber

HMR
   ↓
Replacement policy
```

HMR MUST NOT introduce a second lifecycle system.

HMR MUST NOT directly mutate Fiber state.

HMR MUST use Kernel public lifecycle semantics.

HMR MAY be shipped as:

```text
contrib/hmr
```

or equivalent optional infrastructure package.

It MUST NOT be required for Kernel correctness.

---

# 15. Layer 2 — Integration / Backend

Integration connects the Runtime to an external system or implementation.

Examples:

```text
Filesystem Watcher
TOML Parser
WASM Loader Backend
HTTP Transport
Kubernetes Adapter
OCI Adapter
```

Integration MAY depend on external libraries.

Integration MUST NOT modify Kernel semantics.

Integration MUST expose external facts/resources through Component or Infrastructure APIs.

---

# 16. Layer 3 — Application / Business Capability

Application capabilities define what the system actually does.

Examples:

```text
Agent
MCP
HTTP API
Camera
CAN
LLM
Database
MQTT
Kafka
ETL
Industrial Controller
Business Workflow
```

These MUST NOT be treated as Framework primitives.

They SHOULD be implemented as Components or Component compositions.

For example:

```text
MCP Server
    ↓
runtime.Component
    ↓
ctx.Effect(...)
ctx.Provide(...)
ctx.Child(...)
```

The Runtime knows only that it is managing a Component.

It does NOT know that the Component happens to implement MCP.

---

# 17. MCP Classification

MCP is explicitly classified as:

```text
Application / Agent Capability
```

MCP MUST NOT be added to Kernel.

MCP MUST NOT be required by Runtime Infrastructure.

MCP MAY use:

```text
Component
Context
Effect
Provider
Registry
Event
Scheduler
HTTP / transport infrastructure
```

but the dependency direction MUST remain:

```text
MCP
 ↓
Runtime
```

and never:

```text
Runtime
 ↓
MCP
```

The same rule applies to HTTP, CAN, Camera, LLM, Agent, etc.

---

# 18. HTTP Classification

HTTP Server MUST be treated as an Application/Integration capability implemented using Runtime Components.

A generic HTTP transport implementation MAY be infrastructure.

A concrete HTTP Server Component is application capability.

Therefore:

```text
HTTP protocol/transport mechanism
        ↓
Integration

HTTP Server Component
        ↓
Application

Runtime
        ↓
knows neither
```

The Runtime MUST NOT contain HTTP-specific lifecycle primitives.

---

# 19. WASM Classification

WASM MUST be classified as an implementation backend.

Correct:

```text
Loader
  └── WASM Backend
        └── Module
              └── Component
```

Incorrect:

```text
Runtime
  └── WASM subsystem
```

The WASM backend MUST NOT define independent lifecycle semantics.

---

# 20. Dependency Direction

The architecture MUST obey:

```text
Application
      ↓
Runtime Infrastructure
      ↓
Kernel
```

and:

```text
Integration
      ↓
Infrastructure / Kernel public API
```

The reverse direction is prohibited.

In particular:

```text
Kernel → MCP              FORBIDDEN
Kernel → HTTP             FORBIDDEN
Kernel → WASM             FORBIDDEN
Kernel → Config format    FORBIDDEN
Kernel → Watch            FORBIDDEN

Registry → Kernel internals       FORBIDDEN
HMR → Fiber private mutation      FORBIDDEN
Loader → Fiber lifecycle mutation FORBIDDEN
Watch → Config implicit mutation  FORBIDDEN
```

---

# 21. Framework API Stability Principle

The Framework MUST expose a small semantic API.

A new application capability MUST NOT require adding a new Kernel primitive unless the capability reveals a genuinely missing generic runtime semantic.

The test is:

> Can this capability be implemented as a Component using existing Runtime semantics?

If YES:

```text
Do not modify Kernel.
```

If NO:

```text
First prove that the missing behavior is domain-independent
and fundamental to dynamic composition.
```

Only then MAY Kernel be extended.

---

# 22. The New Primitive Test

Before adding anything to Kernel, answer:

### A. Is it domain-independent?

If no → Application.

### B. Does it define dynamic composition semantics?

If no → Infrastructure/Application.

### C. Does it need to control Fiber lifecycle?

If no → not Kernel.

### D. Can it be implemented using existing Component/Context/Effect/Provider semantics?

If yes → do not extend Kernel.

### E. Would removing the feature make the generic Runtime lifecycle semantically incomplete?

If no → not Kernel.

This test is mandatory for future Kernel changes.

---

# 23. Framework Completeness

The Framework is NOT complete when every integration is implemented.

The Framework is complete when its generic semantic model is closed.

Formally:

> **Framework Completeness = Semantic Closure + Lifecycle Closure + Composition Closure + Boundary Closure**

---

# 24. Semantic Closure

Kernel MUST have fully defined semantics for:

```text
Component
Fiber
Activation
Context
Effect
Provider
Dependency
Ownership
Lifecycle
Cancellation
Failure
Recovery
Replacement
Close
```

For each concept there MUST be:

- state model;
- transition rules;
- concurrency semantics;
- failure semantics;
- cleanup semantics;
- identity semantics;
- testable invariants.

No unresolved semantic ambiguity may remain in these areas.

---

# 25. Lifecycle Closure

For every Runtime-managed Fiber:

```text
Mount
→ Load
→ Active
→ Withdraw / Unload
→ Gone
```

must have deterministic semantics.

The Runtime MUST define:

- activation identity;
- stale completion behavior;
- dependency loss;
- dependency recovery;
- duplicate provider;
- provider replacement;
- partial Apply failure;
- partial cleanup failure;
- cancellation;
- concurrent Load / Dispose;
- Runtime Close.

---

# 26. Composition Closure

The Framework MUST demonstrate that independently implemented Components can compose without introducing special-case lifecycle logic.

Minimum proof:

```text
Provider
   ↓
Consumer
   ↓
Consumer-of-Consumer
```

and:

```text
Provider replacement
   ↓
dependency withdrawal
   ↓
consumer recovery
```

must work without domain-specific Kernel code.

---

# 27. Resource Closure

Any Runtime-managed external resource MUST be representable as:

```text
Activation
   ↓
Effect
   ↓
inverse
```

Examples:

```text
listener
file handle
subscription
timer
WASM instance
network connection
```

The Kernel need not know what the resource is.

It only needs to guarantee ownership and cleanup semantics.

---

# 28. Extension Closure

Runtime Infrastructure MUST be optional.

A minimal Runtime installation SHOULD be able to operate with:

```text
Kernel
+
Component
```

without requiring:

```text
Registry
Event
Scheduler
Config
Loader
Watch
HMR
WASM
HTTP
MCP
```

This is a critical completeness criterion.

If removing an optional subsystem makes Kernel lifecycle semantics invalid, that subsystem has leaked into Kernel.

---

# 29. Application Capability Closure

The Framework MUST demonstrate that new capabilities can be added without Kernel modification.

At minimum, examples SHOULD include at least three structurally different capabilities:

```text
resource server
external device/resource
agent-facing capability
```

For example:

```text
HTTP
Camera
MCP
```

But these are **validation applications**, not Framework modules.

Their existence proves composability; their code MUST NOT become part of Kernel semantics.

---

# 30. Framework Completion Criteria

The Framework MAY be declared **v0.1 Complete** only when all conditions below hold:

### C-01 Kernel Semantic Closure

All Kernel semantics are specified and tested.

### C-02 Lifecycle Closure

All legal lifecycle transitions and failure paths are tested.

### C-03 Dependency Closure

Dependency loss/recovery/replacement semantics are proven.

### C-04 Effect Closure

Effect installation, partial failure, reverse cleanup and cancellation are proven.

### C-05 Identity Closure

Fiber / Activation / Provider identity and stale completion semantics are proven.

### C-06 Ownership Closure

Ownership and dependency remain distinct and cleanup ordering is proven.

### C-07 Concurrency Closure

Race tests and adversarial concurrent lifecycle tests pass.

### C-08 Shutdown Closure

Runtime Close is truthful under normal, concurrent and timeout conditions.

### C-09 Extension Isolation

Infrastructure can be removed without modifying Kernel semantics.

### C-10 Application Independence

At least several structurally different application capabilities can be implemented without Kernel changes.

### C-11 Boundary Audit

Every package has an explicit classification:

```text
Kernel
Infrastructure
Integration
Backend
Application
Example/Test
```

### C-12 No Semantic Debt

No known unresolved contradiction exists in:

```text
state machine
dependency semantics
Effect semantics
identity semantics
ownership
shutdown
concurrency
```

Only documented non-semantic limitations MAY remain.

---

# 31. What Does NOT Block Framework Completion

The following are NOT Framework completion requirements:

```text
MCP implementation
HTTP implementation
WASM implementation
CAN implementation
Camera implementation
LLM implementation
Database integration
Kubernetes integration
MQTT integration
```

These are capabilities/integrations that demonstrate Framework usefulness.

They are not part of the definition of Framework completeness.

---

# 32. Current Project Reclassification

Existing work MUST be reclassified as follows:

| Existing work | Final classification |
|---|---|
| Kernel | Framework Core |
| Registry | Runtime Infrastructure |
| Event | Runtime Infrastructure |
| Scheduler | Runtime Infrastructure |
| Config Reconciler | Runtime Infrastructure / Control Plane |
| Loader | Runtime Infrastructure |
| WASM Backend | Loader Backend / Integration |
| Watch | Integration Infrastructure |
| Config Watch Adapter | Integration |
| HMR | Optional Replacement Policy / Infrastructure |
| HTTP Server Component | Application / Example Capability |
| MCP | Application / Agent Capability |
| CAN | Application Capability |
| Camera | Application Capability |
| LLM | Application Capability |
| Agent | Application |
| ETL | Application |

This classification does NOT imply that existing implementation must be deleted.

It determines:

- dependency direction;
- repository/package ownership;
- release requirements;
- API stability;
- whether the component is mandatory.

---

# 33. Migration Rule

Existing implementation MUST NOT be rewritten merely to satisfy naming.

First perform:

```text
classification
↓
dependency audit
↓
API boundary audit
↓
semantic audit
↓
only then structural migration
```

No mass rewrite is authorized by this specification.

---

# 34. Immediate Required Action

Before implementing MCP or any other new capability:

## Phase A — Architecture Audit

Audit:

```text
Kernel
Registry
Event
Scheduler
Config
Loader
Watch
ConfigWatch
HMR
WASM
HTTP
```

For each package record:

```text
1. Classification
2. Public dependency direction
3. Kernel dependency
4. Domain knowledge
5. Lifecycle ownership
6. Whether it is mandatory
7. Whether it can be implemented as a Component
8. Whether it should remain in core repository
```

---

## Phase B — Kernel Boundary Audit

Verify:

```text
Kernel imports no application capability.

Kernel imports no protocol implementation.

Kernel imports no concrete transport.

Kernel imports no serialization format.

Kernel does not know Loader/Watch/HMR/Event/Registry/Scheduler semantics.
```

Infrastructure MAY depend on Kernel.

Kernel MUST NOT depend upward.

---

## Phase C — Package Boundary Decision

Every package MUST receive one of:

```text
core/
infrastructure/
integration/
backend/
contrib/
examples/
```

The exact directory layout MAY differ, but the architectural classification MUST be explicit.

---

# 35. Definition of Done for the Framework

The final Framework v0.1 release is reached when:

```text
                ┌──────────────────────┐
                │ Semantic Specification│
                └──────────┬───────────┘
                           │
                           ▼
                ┌──────────────────────┐
                │ Kernel Implementation │
                └──────────┬───────────┘
                           │
                           ▼
                ┌──────────────────────┐
                │ Property / Race Tests│
                └──────────┬───────────┘
                           │
                           ▼
                ┌──────────────────────┐
                │ Boundary Audit       │
                └──────────┬───────────┘
                           │
                           ▼
                ┌──────────────────────┐
                │ Application Proofs   │
                └──────────┬───────────┘
                           │
                           ▼
                 FRAMEWORK v0.1 COMPLETE
```

At that point:

```text
MCP can be built.
HTTP can be built.
CAN can be built.
Camera can be built.
Agent can be built.
```

without modifying Kernel semantics.

That is the actual proof that the Framework is complete.

---

# 36. Final Architectural Principle

The Framework is successful when it can say:

> **“I don't know what your application does. I only know how to compose it, activate it, withdraw it, replace it, and clean it up correctly.”**

If the Runtime starts knowing:

```text
MCP
HTTP
WASM
Camera
CAN
LLM
Agent
```

then the Framework boundary has already failed.

The goal is therefore not:

```text
Framework = everything
```

but:

```text
Framework = the smallest semantic machine
             capable of safely composing everything else.
```