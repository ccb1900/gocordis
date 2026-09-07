# Dynamic Composable Runtime — Convergence & Completion Specification v0.1

## 1. Purpose

本规范定义 `gocordis` Dynamic Composable Runtime 的最终收敛标准。

目标不是继续扩展功能，而是确认：

1. 论文定义的 Dynamic Composable Runtime 核心语义是否完整实现；
2. JS Cordis 中属于论文语义或 Runtime 核心能力的部分是否存在对应实现；
3. `stc-go` 中已经验证的论文语义是否在 `gocordis` 中得到等价验证；
4. 当前实现是否存在尚未闭合的语义缺口；
5. 在所有必要项闭合后，冻结 Runtime Kernel，停止无依据的功能扩张。

本规范本身不是语义来源。

---

# 2. Authority Hierarchy

所有收敛判断必须遵循以下优先级：

```text
Paper
  >
stc-go / JS Cordis
  >
gocordis existing specification
  >
gocordis implementation
```

其中：

### Level 1 — Paper

论文是唯一的第一性语义依据。

论文定义的：

- Component
- Context
- Effect
- Coeffect
- Spatial Composability
- Temporal Composability
- Dynamic Composition
- Runtime semantics
- Metatheory

具有最高优先级。

任何 gocordis specification、代码、测试，如果与论文语义冲突，都必须修改。

---

### Level 2 — stc-go

`stc-go` 是论文语义的 Go 参考实现。

其作用：

- 验证论文语义如何落地到 Go；
- 提供 theorem verification 参考；
- 提供 Runtime lifecycle / dependency / replacement / registry 等实现语义参考；
- 识别论文文字中不够具体的工程边界。

`stc-go` 不能反向改变论文语义。

---

### Level 3 — JS Cordis

JS Cordis 是成熟工程实现参考。

其作用：

- 验证论文概念在真实 Runtime 中如何组织；
- 提供 Loader、Config、HMR、Registry、Event 等工程能力参考；
- 判断 gocordis 是否遗漏成熟 Runtime 所需要的工程机制。

Cordis 特有、且论文没有要求的 JS / Node ecosystem 能力，不自动成为 gocordis 的 requirement。

---

### Level 4 — gocordis Existing Specification

现有 gocordis specification 只用于：

- 固化已经确定的设计；
- 指导实现；
- 定义 Go API / package boundary；
- 定义测试和工程约束。

如果发现其与 Paper 冲突，必须以 Paper 为准。

---

# 3. Scope

本规范只收敛 `gocordis` Runtime Framework。

目标结构：

```text
gocordis
│
├── Runtime Kernel
│
├── Runtime Public API
│
└── Runtime Extensions
```

以下项目不属于本次 Runtime Completion：

```text
gocordis-console
industrial-data-collector
CAN analyzer
PLC / Modbus
CSV / Excel
Dashboard
Business Workflow
Specific industrial application
```

这些项目可以作为 Runtime Consumer，但不得反向决定 Kernel 语义。

---

# 4. Completion Definition

`gocordis` v0.1 只有在以下条件全部满足时，才允许宣布完成：

```text
Paper Semantic Coverage
        AND
Theorem Verification Coverage
        AND
Core Cordis Correspondence
        AND
Required stc-go Correspondence
        AND
Runtime Extension Boundary
        AND
Known Semantic Gaps = 0
```

其中：

```text
P0 required gap = 0
P1 required gap = 0
```

允许存在：

```text
N/A
Intentional Difference
Future Extension
```

但必须有明确理由和记录。

---

# 5. Convergence Matrix

必须建立唯一的 Convergence Matrix。

每一个 capability 必须至少包含：

| Field | Meaning |
|---|---|
| ID | 唯一编号 |
| Capability | 能力名称 |
| Paper Reference | 论文章节/定义 |
| Cordis Reference | JS Cordis 对应能力 |
| stc-go Reference | Go 参考实现 |
| gocordis Implementation | 当前代码 |
| gocordis Tests | 当前验证 |
| Status | PASS / PARTIAL / MISSING / N/A |
| Priority | P0 / P1 / P2 |
| Gap | 当前缺口 |
| Action | 必须采取的动作 |

---

# 6. Status Definition

## PASS

必须同时满足：

1. 存在实际实现；
2. 实现符合 Paper semantics；
3. 与 stc-go / Cordis 的对应语义没有已知冲突；
4. 有测试或其他可验证证据；
5. 不存在未解决的 P0/P1 semantic issue。

---

## PARTIAL

存在实现，但：

- 语义覆盖不完整；
- 测试不足；
- 与参考实现存在未解释差异；
- 边界行为未冻结；
- failure / concurrency / lifecycle 场景未覆盖。

PARTIAL 不能作为 completion。

---

## MISSING

论文或 required reference capability 明确要求，但 gocordis 没有对应实现。

必须进入 implementation queue。

---

## N/A

能力明确不属于 gocordis Runtime scope。

必须记录原因。

不得仅以“目前没做”作为 N/A。

---

## INTENTIONAL DIFFERENCE

gocordis 与 Cordis / stc-go 不同，但差异：

1. 不违反 Paper semantics；
2. 有明确技术原因；
3. 有测试；
4. 已记录为 intentional difference。

---

# 7. Priority

## P0 — Paper Semantic Requirement

直接影响论文核心语义：

```text
Component
Context
Effect
Reversible Effect
Coeffect
Dependency
Fiber
Activation
Spatial Composition
Temporal Composition
Recovery
Ordering
Progress
Confluence
```

P0 不允许存在 PARTIAL / MISSING。

---

## P1 — Required Runtime Engineering Capability

论文实现或 stc-go / Cordis 核心 Runtime 所需：

```text
Provider
Ownership
Registry
Loader
Config Reconciliation
Watch
HMR
WASM Backend
Provider Replacement
Failure Handling
Runtime Observation Boundary
```

只有当该能力被确认属于 gocordis v0.1 scope 时才作为 completion blocker。

---

## P2 — Ecosystem / Application Capability

例如：

```text
Console UI
Industrial Collector
CAN
PLC
HTTP application
Database
MCP
Business workflow
```

不得成为 Runtime completion blocker。

---

# 8. Paper Semantic Closure

以下语义必须逐项确认。

## 8.1 Component

必须确认：

```text
Component
    ↓
Runtime representation
    ↓
Fiber
```

Component identity、application identity 不得混淆。

---

## 8.2 Context

必须确认 Context 是 Effect 与 Coeffect 的统一语义载体。

不得仅以存在 `Context` struct 判定完成。

必须验证：

```text
Context
 ├── Effect
 └── Coeffect
```

两者能够共同参与 Dynamic Composition。

---

## 8.3 Effect

必须支持：

```text
Effect
    ↓
Apply
    ↓
Inverse
    ↓
Unwind
```

Inverse 必须具有明确生命周期语义。

---

## 8.4 Temporal Composability

必须验证：

```text
Composition
    ↓
Effects
    ↓
Unload
    ↓
Inverse effects
    ↓
Recovery
```

并验证：

- cleanup ordering；
- LIFO；
- partial application；
- failure；
- repeated activation。

---

## 8.5 Coeffect / Dependency

必须验证：

```text
Dependency
    ↓
Unsatisfied
    ↓
Satisfied
    ↓
Active
```

以及：

```text
Provider disappears
    ↓
Dependency becomes unsatisfied
    ↓
Dependent unwinds
```

---

## 8.6 Spatial Composability

必须验证多个 Component 的组合不是简单顺序执行，而是由 Context / Dependency satisfaction 驱动。

---

## 8.7 Activation

必须明确：

```text
Fiber identity
        ≠
Activation identity
```

不同 activation cycle 的异步 completion 不得互相污染。

---

## 8.8 Ownership

必须明确：

```text
Parent
 ├── Child A
 └── Child B
```

Parent unload 时：

```text
Child A
Child B
    ↓
cleanup
    ↓
Parent cleanup
```

具体 ordering 必须与 Paper / stc-go semantic contract 一致。

---

# 9. Theorem Closure

以下 theorem 必须逐项建立 theorem-to-theorem correspondence：

```text
Paper
  ↓
stc-go
  ↓
gocordis
```

## T59 — Preservation

必须证明 Runtime 每个合法 transition 后仍满足 invariant。

---

## T61 — Recovery Exactness

必须证明：

```text
load
→
apply
→
unload
→
cleanup
```

最终恢复到与未加载状态 observationally equivalent 的状态。

测试不得只证明“Fiber Gone”。

必须证明 semantic state recovery。

---

## T63 — Ordering

必须证明 dependency prerequisite 未满足时，不允许提前进入要求 prerequisite 的 lifecycle state。

---

## T66 — Progress

必须证明在满足 theorem assumptions 时，Runtime 最终能够达到 quiescent state。

不得以无限等待作为实现策略。

---

## T73 — Confluence

必须证明不同合法 scheduling/interleaving 最终得到相同 quiescent semantic state。

---

# 10. Required Lifecycle Conformance

至少必须验证：

```text
Load
Apply
Active
Unload
Cleanup
Gone
```

以及：

```text
Apply Failure
Cleanup Failure
Dependency Loss
Dependency Recovery
Repeated Activation
Concurrent Lifecycle
Runtime Close
```

---

# 11. Provider Replacement Contract

必须明确 Provider replacement 的生命周期 barrier。

默认目标语义：

```text
Old Provider
      ↓
Unwind / Dispose
      ↓
Gone
      ↓
New Provider
      ↓
Activation
```

禁止：

```text
Old Provider = still live
New Provider = already active
```

如果 Paper / stc-go 语义允许例外，必须记录依据。

必须增加 conformance test：

```text
old provider
→ dependent active
→ old provider removed
→ old provider Gone
→ new provider introduced
→ dependent rebinds
```

---

# 12. Failed Fiber Contract

必须冻结 Failed lifecycle semantics。

至少明确：

```text
Apply Failure
      ↓
Failed
```

之后：

- Registry membership；
- dependency visibility；
- ownership；
- Ready semantics；
- Gone semantics；
- replacement；
- Runtime.Close；

必须全部有确定行为。

不得由具体实现偶然决定。

---

# 13. Multi-Activation Contract

必须验证：

```text
Activation A
    ↓
Unwind
    ↓
Activation B
```

以下 A 的 completion：

```text
ApplyDone(A)
UnwindDone(A)
AsyncCompletion(A)
```

不得改变 B。

必须存在专门的 regression/conformance test。

---

# 14. Registry Contract

必须区分：

```text
Kernel Registry
```

与：

```text
Registry Extension
```

Kernel Registry 是 Runtime lifecycle 的权威状态来源。

必须明确：

- Fiber membership；
- ordering；
- Failed；
- Gone；
- disposal；
- snapshot；
- replacement。

Registry Extension 不得改变 Kernel lifecycle semantics。

---

# 15. Loader Contract

Loader 属于 Runtime Engineering Layer。

必须支持：

```text
Entry
 ↓
Component
 ↓
Fiber
 ↓
Activation
```

Loader 不得直接修改 Kernel internal state。

Loader 必须通过 Public Runtime API / composition mechanism 操作 Runtime。

---

# 16. Config Reconciliation Contract

必须遵循：

```text
External State
      ↓
Watch
      ↓
Adapter
      ↓
Desired State
      ↓
Reconcile
      ↓
Runtime Composition
```

禁止 Adapter：

```text
direct Fiber mutation
direct lifecycle scheduling
direct HMR implementation
```

Parse failure 不得无条件破坏当前 applied state。

---

# 17. HMR Contract

HMR 必须被视为 transactional composition update。

至少验证：

```text
Old
 ↓
Prepare / Backup
 ↓
Update
 ├── Success → New
 │
 └── Failure → Old remains valid
```

Failure 时不得留下：

```text
half-old
half-new
duplicate provider
stale activation
lost cleanup
```

HMR 不得绕过 Runtime lifecycle。

---

# 18. WASM Contract

WASM 是 Runtime Backend，不是 Kernel semantic primitive。

必须保持：

```text
WASM Backend
      ↓
Loader / Runtime Public API
      ↓
Kernel
```

不得：

```text
WASM Backend
      ↓
direct Kernel mutation
```

WASM 必须符合普通 Component/Fiber/Activation/Dependency/Effect semantics。

因此：

> WASM Component 与 native Component 在 Runtime semantic model 上必须等价。

---

# 19. Stable Registry Contract

如果 `extensions/registry` 纳入 v0.1，必须验证：

```text
Stable Registry Provider
        ↓
Members dynamically change
        ↓
Consumer remains valid
```

Member registration 必须可逆。

旧 Member 的 inverse 不得误伤新 Member。

如果该能力被判定为非 v0.1 requirement，则必须标记 N/A，而不是留下 PARTIAL。

---

# 20. Observation Contract

Observation 属于 Runtime Engineering API，不属于 Paper P0 semantic primitive。

允许：

```text
RuntimeSnapshot
RuntimeEvent
Snapshot()
Subscribe()
```

但必须满足：

```text
Observation
    ↓
read-only semantic projection
```

不得：

```text
Console requirement
    ↓
Kernel semantic change
```

Console UI 不属于 gocordis。

---

# 21. Extension Boundary

Extension 必须通过 Runtime Public Contract 工作。

禁止：

```text
Extension
    ↓
direct Fiber mutation
direct activation mutation
direct Kernel scheduler manipulation
```

允许：

```text
Extension
    ↓
Public Runtime API
    ↓
Kernel
```

新增 Extension 不应要求修改 Kernel lifecycle semantics。

如果必须修改 Kernel 才能支持 Extension，应重新审查：

```text
是否属于真正的 Runtime primitive？
```

---

# 22. Prohibited Scope Expansion

收敛阶段禁止新增与 Convergence Matrix 无关的功能。

特别禁止以以下理由修改 Kernel：

```text
“Console 以后需要”
“工业采集以后需要”
“某个 Demo 更方便”
“某个 Extension 更好写”
“API 看起来更漂亮”
```

任何新增 Kernel API 必须回答：

1. Paper 是否要求？
2. stc-go 是否证明需要？
3. Cordis 是否证明属于 Runtime core？
4. 当前已有 Public API 是否无法表达？
5. 是否会改变已有 semantic contract？

如果全部不能回答“是”，不得进入 Kernel。

---

# 23. Implementation Procedure

Code Agent 必须按照以下流程工作：

```text
1. Read Paper
      ↓
2. Read corresponding stc-go implementation
      ↓
3. Read corresponding Cordis implementation
      ↓
4. Inspect gocordis current implementation
      ↓
5. Identify exact gap
      ↓
6. Classify P0/P1/P2/N/A
      ↓
7. Write/modify test
      ↓
8. Implement minimum change
      ↓
9. Run targeted tests
      ↓
10. Run full runtime tests
      ↓
11. Run race tests
      ↓
12. Update Convergence Matrix
```

禁止跳过第 1～4 步直接修改代码。

---

# 24. Gap Closure Rule

每一个 Gap 必须具有唯一 ID，例如：

```text
GAP-01
GAP-02
...
```

一个 Gap 只有在：

```text
Implementation
+
Test
+
Reference correspondence
+
Documentation
```

全部完成后才能标记：

```text
CLOSED
```

不得以：

```text
“代码看起来没问题”
```

关闭 Gap。

---

# 25. Completion Gate

最终必须满足：

```text
P0:
  PASS = 100%
  PARTIAL = 0
  MISSING = 0

Required P1:
  PASS = 100%
  PARTIAL = 0
  MISSING = 0

P2:
  不作为 completion blocker

Intentional Difference:
  全部有依据

Known Semantic Bug:
  0

Unverified Semantic Boundary:
  0
```

然后执行：

```text
go test ./...
go test -race ./...
```

并完成最终 Convergence Matrix。

---

# 26. Final Completion Decision

只有出现以下状态时，才允许宣布：

```text
gocordis v0.1 — COMPLETE
```

最终报告必须明确列出：

```text
Paper Coverage
Cordis Coverage
stc-go Coverage

P0 PASS
P1 PASS
N/A
Intentional Differences

Remaining Gaps = 0
```

---

# 27. Definition of Done

最终 Runtime 必须满足：

```text
                 PAPER
                   │
                   ▼
          Semantic Contract
                   │
          ┌────────┴────────┐
          ▼                 ▼
       stc-go            Cordis
          │                 │
          └────────┬────────┘
                   ▼
              gocordis
                   │
        ┌──────────┼──────────┐
        ▼          ▼          ▼
      Kernel    Extension   Public API
        │          │          │
        └──────────┼──────────┘
                   ▼
            External Consumer
```

此时：

```text
gocordis
```

已经不再需要 Console、工业采集或其他业务项目来证明自身“有什么功能”。

它只需要证明：

> **论文定义的 Dynamic Composable Runtime 语义已经被正确实现，并且参考实现中的必要工程能力已经以 Go Runtime 的方式闭合。**

达到该条件后，Runtime Kernel 进入 **Feature Freeze**。

后续需求必须优先作为：

```text
Extension
Consumer
Application
```

实现，而不是继续扩大 Kernel。