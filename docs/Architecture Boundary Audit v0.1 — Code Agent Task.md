# Dynamic Composable Runtime
## Architecture Boundary Audit v0.1

### Mode

**AUDIT ONLY**

本任务禁止修改生产代码。

允许：
- 阅读代码；
- 阅读已有 tests；
- 阅读 docs/specs；
- 建立依赖关系；
- 建立 package/import 分析；
- 新增临时审计脚本；
- 输出审计报告。

禁止：
- 修改生产代码；
- 移动 package；
- 修改 API；
- 修改 Kernel；
- 新增功能；
- 修复发现的问题；
- 重构；
- 删除已有实现。

---

# 1. Architecture Authority

以：

`Dynamic Composable Runtime — Framework Boundary & Completeness Specification v0.1`

作为本次审计的最高架构依据。

重点验证以下边界：

```text
Kernel
Runtime Infrastructure
Integration
Backend
Application / Business Capability
```

---

# 2. Audit Scope

审计现有：

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
Integration tests
Examples
```

以及它们之间的 import / dependency / lifecycle / semantic 关系。

---

# 3. Kernel Audit

检查 Kernel 是否满足：

```text
Kernel
 ├── Component
 ├── Fiber
 ├── Activation
 ├── Context
 ├── Effect
 ├── Provider Identity
 ├── Dependency
 ├── Ownership
 ├── Lifecycle Coordinator
 └── Runtime Close
```

Kernel MUST NOT directly depend on:

```text
HTTP
MCP
WASM
Database
LLM
Camera
CAN
Watch
Config format
HMR
business Event
```

检查实际 Go import graph。

报告：

```text
K-01 Kernel imports upward
K-02 Kernel contains domain knowledge
K-03 Kernel contains protocol knowledge
K-04 Kernel contains transport knowledge
K-05 Kernel contains implementation-backend knowledge
K-06 Kernel contains configuration-format knowledge
K-07 Kernel contains hidden secondary lifecycle
```

每项：

```text
PASS / FAIL / CONDITIONAL
```

并提供具体 package/file evidence。

---

# 4. Infrastructure Audit

逐项检查：

```text
Registry
Event
Scheduler
Config
Loader
HMR
```

对于每个 package 输出：

```text
Classification
Responsibility
Depends on
Depended by
Kernel dependency
Domain knowledge
Lifecycle ownership
Mandatory/Optional
```

重点判断：

> 它是否是 Runtime Infrastructure，还是实际上已经包含 Application Capability？

---

# 5. Loader / Backend Audit

特别检查：

```text
Loader
WASM Backend
Builtin Backend
Module
Factory
```

确认：

```text
Loader → Module
Module → Factory
Factory → Component
Component → Runtime
```

而不是：

```text
Loader → Fiber lifecycle
Loader → HMR
Loader → Config
Loader → Watch
```

WASM 必须被分类为：

```text
Loader Backend / Integration
```

而不是 Runtime primitive。

---

# 6. Watch Audit

检查 Watch 是否：

```text
External World
      ↓
Observation
      ↓
Normalized Change
```

并确认 Watch 没有隐式：

```text
Config.Reconcile
HMR.Replace
Runtime.Load
Fiber.Dispose
Provider mutation
```

---

# 7. HMR Audit

检查 HMR 是否只是：

```text
Replacement Policy
```

并确认：

```text
HMR → Kernel public lifecycle API
```

而不是：

```text
HMR → private Fiber mutation
HMR → private Provider mutation
```

同时确认 HMR 是否应该：

```text
optional infrastructure / contrib
```

而不是：

```text
mandatory Runtime core
```

这里不要修改代码，只给判断。

---

# 8. HTTP Audit

检查现有 HTTP 实现。

目标分类：

```text
HTTP Server Component
    ↓
Application / Capability
```

而不是：

```text
Runtime Core HTTP
```

检查：

1. 是否可以作为普通 Component；
2. 是否依赖 Kernel public API；
3. Kernel 是否知道 HTTP；
4. HTTP 是否引入 Runtime 特殊语义；
5. 是否应该归入 examples / capability package / application package。

---

# 9. MCP Audit

当前项目中如果不存在 MCP：

报告：

```text
NOT IMPLEMENTED
```

不要实现 MCP。

如果存在 MCP：

按照 Application / Agent Capability 审计。

---

# 10. Dependency Direction

建立实际依赖图。

至少输出：

```text
Kernel
↑
Infrastructure
↑
Integration / Backend
↑
Application
```

同时标记所有违反：

```text
Kernel → Infrastructure
Kernel → Integration
Kernel → Application
```

的依赖。

特别关注：

```text
Kernel → Registry
Kernel → Event
Kernel → Scheduler
Kernel → Config
Kernel → Loader
Kernel → Watch
Kernel → HMR
```

这些即使没有直接 import，也要检查是否存在 semantic coupling。

---

# 11. Lifecycle Authority Audit

整个仓库只能有一个 Fiber lifecycle authority。

检查是否存在：

```text
second lifecycle
manual Fiber state mutation
extension-owned activation state
duplicate cancellation system
duplicate ownership system
duplicate dependency system
```

特别检查：

```text
Registry
Config
Loader
HMR
HTTP
WASM
```

是否偷偷实现了第二套 lifecycle。

---

# 12. Application Independence Test

任选现有 capability，判断：

> 如果删除该 capability，Kernel 是否仍然语义完整？

至少检查：

```text
HTTP
WASM
HMR
Config
Registry
```

并给出：

```text
Kernel semantic dependency
or
optional capability dependency
```

---

# 13. Package Classification

给仓库中的主要 package 建立最终分类：

```text
Kernel
Infrastructure
Integration
Backend
Application
Example/Test
```

输出完整表格：

| Package | Classification | Reason | Kernel Coupling | Domain Knowledge | Action |
|---|---|---|---|---|---|

其中 Action 只能是：

```text
KEEP
RECLASSIFY
MOVE LATER
SPLIT LATER
REVIEW
```

不要实际执行 Action。

---

# 14. Completeness Audit

根据 Framework Boundary & Completeness Specification v0.1 检查：

```text
C-01 Kernel Semantic Closure
C-02 Lifecycle Closure
C-03 Dependency Closure
C-04 Effect Closure
C-05 Identity Closure
C-06 Ownership Closure
C-07 Concurrency Closure
C-08 Shutdown Closure
C-09 Extension Isolation
C-10 Application Independence
C-11 Boundary Audit
C-12 No Semantic Debt
```

每项：

```text
PASS
FAIL
CONDITIONAL
NOT PROVEN
```

注意：

“测试通过”不能直接证明架构边界正确。

---

# 15. Critical Distinction

报告必须区分：

### Semantic correctness

例如：

```text
Provider replacement is correct.
```

和：

### Architectural correctness

例如：

```text
Provider replacement is implemented in the correct layer.
```

两者不能混为一谈。

---

# 16. Required Final Report

最终报告必须包含：

## A. Executive Summary

```text
Architecture Status:
GREEN / YELLOW / RED
```

## B. Kernel Boundary

## C. Infrastructure Boundary

## D. Integration / Backend Boundary

## E. Application Boundary

## F. Dependency Violations

## G. Lifecycle Authority Violations

## H. Package Classification Table

## I. Existing Implementation Reclassification

## J. Required Changes

将 Required Changes 分成：

```text
P0 — Must fix before Framework v0.1
P1 — Should fix before Framework v0.1
P2 — Can defer
```

## K. Completeness Gate

输出：

```text
C-01 ... C-12
```

逐项判定。

## L. Final Recommendation

只能从以下选择：

```text
A. Framework boundary is sound; proceed to completion gate
B. Boundary requires structural correction
C. Kernel boundary requires redesign
D. Architecture is not yet stable; stop implementation
```

---

# 17. Hard Stop

如果发现：

```text
Kernel semantic dependency on application capability
```

或者：

```text
multiple lifecycle authorities
```

或者：

```text
Infrastructure changes Kernel semantics
```

必须明确标记：

```text
ARCHITECTURE BLOCKER
```

不得自行修复。

---

# 18. Success Criteria

本任务成功的唯一标准：

> 获得一份可以据此决定“哪些代码应该留在 Framework、哪些应该降级为 Infrastructure、哪些应该移动到 Application”的客观审计报告。

**本任务不是实现任务。**

**不要实现 MCP。**

**不要修改任何生产代码。**