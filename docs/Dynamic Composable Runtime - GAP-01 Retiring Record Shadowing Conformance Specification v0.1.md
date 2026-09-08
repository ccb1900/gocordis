> ⚠️ **ARCHIVED（已归档）— 2026-09-08**
>
> 本文档已整体归档，仅作历史参考，**不再作为规范来源或引用依据**。
> 本仓库现以论文原文为唯一规范来源：`docs/论文.pdf`
> （A Programming Paradigm for Spatiotemporal Composability）。
> 当前路线图与论文↔代码对照见 `docs/ROADMAP.md`。
>
> This document is archived for historical reference only and is no longer cited
> as authority. The paper is the sole normative source; see `docs/ROADMAP.md`.

# Dynamic Composable Runtime — GAP-01 Retiring Record Shadowing Conformance Specification v0.1

## 0. Purpose

本规范用于关闭 `gocordis` Convergence Matrix 中唯一当前 P0 `PARTIAL`：

> GAP-01 — Retiring-record shadowing semantics is implemented but not explicitly frozen by a direct conformance test and corresponding semantic documentation.

目标不是新增 Runtime 能力，而是：

1. 冻结当前已经存在的 dependency resolution 语义；
2. 明确 `Retiring` provider 对 ancestor provider 的 shadowing 行为；
3. 增加一个直接、最小、不可歧义的 conformance test；
4. 将该语义与 Paper / stc-go 的对应关系记录下来；
5. 不改变其他 Runtime lifecycle / dependency / ownership / activation 语义。

---

# 1. Authority

语义判断必须遵循以下优先级：

```text
Paper
  ↓
stc-go
  ↓
JS Cordis
  ↓
gocordis existing specifications
  ↓
gocordis implementation
```

其中：

- Paper 是第一性语义来源；
- stc-go 是 Paper 的 Go verification/reference implementation；
- JS Cordis 是工程实现参考；
- gocordis 当前实现不能反向定义理论语义。

如果发现当前实现与上述语义冲突：

> 不得为了让测试通过而修改测试语义。

必须先报告冲突，再决定是否需要 Runtime 修正。

---

# 2. Scope

本任务只处理：

```text
Dependency resolution
+
Retiring provider
+
Ancestor fallback
+
Lifecycle transition to Gone
```

允许修改：

- dependency resolution conformance tests；
- theorem/conformance test harness；
- 相关语义注释；
- progress / convergence matrix / completion documentation；
- 为测试提供必要的最小 helper。

原则：

> Test-first semantic closure.

---

# 3. Explicit Non-Goals

本任务禁止：

- 新增 Provider 类型；
- 新增 Registry；
- 新增 Fiber lifecycle；
- 修改 Activation 模型；
- 修改 HMR；
- 修改 WASM；
- 修改 Loader；
- 修改 Config；
- 修改 Watch；
- 修改 Scheduler；
- 修改 Observation；
- 修改 deterministic runtime；
- 修改 dependency resolution algorithm，除非测试证明当前实现违反 Paper/stc-go 已冻结语义；
- 为了通过测试而修改既有 Runtime 行为；
- 顺手修复其他发现的问题。

如果在实施过程中发现其他问题：

```text
STOP
↓
记录为新的 GAP
↓
不要在本任务中修复
```

---

# 4. Semantic Rule To Freeze

必须明确冻结以下规则：

## 4.1 Nearest record wins

对于某个 dependency key `K`：

```text
resolve(K, realm)
```

必须首先考虑当前 realm 中距离 consumer 最近的 provider record。

如果最近 record 存在，但其 lifecycle state 为：

```text
Retiring
```

则该 record 仍然占据 `K` 的当前 resolution position。

---

# 5. Retiring MUST Shadow Ancestor

核心规则：

```text
Ancestor Realm
    K → Provider A
    state = Active

Child Realm
    K → Provider B
    state = Retiring

Consumer
    located in Child Realm
```

执行：

```text
resolve(K, Child)
```

结果：

```text
NOT Provider A
```

即：

```text
Child.Retiring(K)
        │
        ├── shadows ──→ Ancestor.Active(K)
        │
        ↓
   dependency unavailable
```

禁止：

```text
Child.Retiring(K)
        ↓
fallback
        ↓
Ancestor.Active(K)
```

这条规则必须作为明确的 semantic invariant 存在。

---

# 6. Reason

`Retiring` 不是“provider 不存在”。

它表示：

> 当前最近的 provider binding 正处于退出过程。

因此：

```text
Retiring ≠ Absent
```

如果将 Retiring 当成 Absent 并继续向 ancestor fallback：

```text
Child Provider B
       ↓
   Retiring
       ↓
   fallback
       ↓
Ancestor Provider A
```

会产生两个同时有效的 provider binding interpretation：

```text
K → B (retiring)
K → A (active)
```

这会破坏 nearest-binding 的确定性，并可能导致：

- dependency unexpectedly rebound；
- replacement 期间错误复用 ancestor provider；
- lifecycle transition 与 dependency resolution 脱节；
- spatial composability 的边界不再稳定。

因此在 replacement / withdrawal window 内：

```text
nearest Retiring record
```

必须继续 shadow ancestor。

---

# 7. Required Conformance Scenario

必须增加一个直接测试。

建议测试名称：

```text
TestRetiringProviderShadowsAncestor
```

或遵循现有 repository naming convention 的等价名称。

---

# 8. Test Topology

测试至少构造以下结构：

```text
Root / Ancestor Realm
│
└── Provider A
      key = K
      state = Active
      │
      └── Child Realm
            │
            ├── Provider B
            │     key = K
            │     state = Retiring
            │
            └── Consumer C
                  requires K
```

要求：

```text
A and B use the SAME provider key K.
B is nearer to C than A.
B is explicitly Retiring.
```

---

# 9. Assertion A — No Ancestor Fallback

当 B 为 `Retiring` 时：

```text
resolve(C, K)
```

不得得到 A。

必须满足：

```text
resolved provider != A
```

如果 Runtime 使用 Pending / unavailable / unsatisfied 等具体状态：

必须断言实际 repository contract 对应的状态。

测试不得仅断言：

```text
not panic
```

也不得只检查日志。

必须验证 dependency resolution 的实际结果。

---

# 10. Assertion B — Retiring Record Remains Visible For Resolution Semantics

测试必须能够证明：

```text
B exists
B is Retiring
B shadows A
```

不能通过“直接删除 B”模拟该状态。

禁止：

```text
remove B
resolve K
expect no A
```

因为这测试的是：

```text
Absent
```

而不是：

```text
Retiring
```

---

# 11. Assertion C — Ancestor Provider Is Still Valid Outside Child Shadow

测试必须同时证明 ancestor provider A 并没有被全局删除。

例如：

```text
Root consumer requiring K
```

仍然可以 resolve 到：

```text
A
```

即：

```text
Root → K → A
Child → K → B(Retiring) → unavailable
```

而不是：

```text
Root → K → unavailable
```

这样可以证明：

> Shadowing 是 realm-local 的，而不是 provider-global invalidation。

---

# 12. Assertion D — After Gone, Ancestor May Become Visible

必须覆盖 lifecycle transition：

```text
B: Retiring
        ↓
B: Gone
        ↓
B removed from active dependency resolution
        ↓
resolve(K, Child)
        ↓
A becomes eligible
```

最终允许：

```text
Child → K → A
```

但必须严格区分两个阶段：

### Phase 1

```text
B = Retiring

Child.resolve(K)
    != A
```

### Phase 2

```text
B = Gone / removed

Child.resolve(K)
    == A
```

这个 transition 是本测试最重要的第二部分。

---

# 13. Required State Timeline

测试应尽可能形成如下确定性时间线：

```text
T0
Ancestor A registered
A = Active

T1
Child B registered
B = Active

T2
Child consumer C binds to B

T3
B enters Retiring

T4
resolve(C, K)

EXPECTED:
    B shadows A
    A is NOT selected

T5
B reaches Gone

T6
resolve(C, K)

EXPECTED:
    A becomes eligible
```

如果当前 Runtime 的 lifecycle transition API 不允许直接人工设置 `Retiring`：

必须通过真实 Runtime lifecycle transition 产生该状态。

禁止直接修改内部 state field，仅为了制造测试状态。

---

# 14. Test Must Be Deterministic

该测试不得依赖：

- sleep；
- wall-clock timing；
- goroutine scheduling；
- arbitrary retry loops；
- flaky polling。

优先使用 repository 已有：

- deterministic driver；
- lifecycle barrier；
- Gone barrier；
- existing test synchronization primitive。

测试必须能够确定地控制：

```text
Active
→ Retiring
→ Gone
```

---

# 15. Provider Replacement Interaction

如果现有 Runtime 已经具有 Provider replacement contract：

测试可以复用现有 replacement path。

重点验证：

```text
Old Provider B
      ↓
   Retiring
      ↓
New Provider / ancestor visibility
```

但不得因为本任务而重新设计 replacement protocol。

本任务只验证：

> 在 old provider 尚未 Gone 时，其 Retiring record 仍然 shadows ancestor。

---

# 16. Conformance To stc-go

必须在测试或相关 documentation 中明确记录对应的 stc-go semantic reference。

至少说明：

```text
stc-go defines nearest provider binding semantics across
the lifecycle transition and does not treat a retiring nearest
binding as equivalent to an absent binding for ancestor fallback.
```

如果实际 stc-go 对应实现/测试的具体名称不同：

必须以实际源码为准填写。

禁止凭印象填写测试名称。

---

# 17. Conformance To Paper

必须检查 Paper 对以下概念的正式定义：

- dependency resolution；
- component lifecycle；
- withdrawal / disposal；
- spatial composability；
- dependency availability during lifecycle transition。

最终 documentation 必须说明：

```text
Paper semantic principle
        ↓
nearest dependency binding
        ↓
lifecycle-aware visibility
        ↓
Retiring shadows ancestor
```

如果 Paper 没有直接规定“Retiring shadows ancestor”这一具体 operational rule：

必须明确写：

> This is an operational refinement required to preserve the Paper's nearest-binding / lifecycle semantics, verified against stc-go.

不得声称 Paper 逐字规定了该规则。

---

# 18. Documentation Requirement

至少更新一处 convergence documentation。

建议在：

```text
docs/
```

现有 convergence/completeness 文档中增加：

```text
GAP-01 — Retiring Record Shadowing
```

内容至少包括：

### Rule

```text
A nearest Retiring provider record shadows ancestor
providers for the same dependency key.
```

### Transition

```text
Retiring
    ↓
Gone
    ↓
ancestor provider may become eligible
```

### Scope

```text
Shadowing is realm-local.
```

### Rationale

解释为什么：

```text
Retiring != Absent
```

---

# 19. Convergence Matrix Update

将：

```text
GAP-01
```

从：

```text
PARTIAL / P0
```

更新为：

```text
PASS / P0
```

前提必须全部满足：

```text
[ ] direct conformance test exists
[ ] Retiring shadows ancestor is asserted
[ ] no-fallback behavior is asserted
[ ] ancestor remains valid outside shadowed realm
[ ] Gone transition is asserted
[ ] deterministic execution
[ ] Paper correspondence documented
[ ] stc-go correspondence documented
[ ] no unrelated semantic change
```

---

# 20. Required Verification

至少运行：

```text
go test ./runtime/...
```

然后：

```text
go test -race ./runtime/...
```

如果 repository 已有更窄的 theorem/conformance test target：

先运行：

```text
targeted GAP-01 test
```

再运行：

```text
full runtime tests
race tests
```

不得只报告 targeted test。

---

# 21. Diff Constraint

完成后检查：

```text
git diff
git status
```

生产 Runtime 修改必须满足：

```text
Expected:
    zero production semantic changes
```

如果必须修改 production code 才能使测试成立：

必须暂停并报告：

```text
1. existing implementation
2. observed contradiction
3. Paper evidence
4. stc-go evidence
5. proposed semantic correction
```

在得到明确结论之前，不得自行修改。

---

# 22. Forbidden Shortcuts

以下方式均视为任务失败：

### Shortcut 1

直接修改 `resolveDependency()`：

```text
if Retiring {
    fallback ancestor
}
```

因为这会改变当前已经审计通过的语义。

---

### Shortcut 2

删除 Retiring provider 后测试 fallback。

这不能证明 Retiring shadowing。

---

### Shortcut 3

使用 sleep 等待 Gone。

这不能形成 deterministic conformance。

---

### Shortcut 4

只测试“不会 panic”。

这不是 semantic assertion。

---

### Shortcut 5

只测试最终 fallback。

必须同时证明：

```text
Retiring → shadow
Gone → fallback eligible
```

---

### Shortcut 6

为了通过本任务顺手修改：

- Registry；
- HMR；
- Loader；
- Activation；
- Fiber；
- deterministic scheduler；
- WASM；
- Config。

全部禁止。

---

# 23. Completion Criteria

GAP-01 只有在以下条件全部满足时才能宣布 PASS：

```text
GAP-01

Semantic:
    Retiring nearest provider shadows ancestor       PASS
    Retiring is not treated as absent                 PASS
    Ancestor remains valid outside child realm        PASS
    Gone permits ancestor visibility                  PASS

Conformance:
    Paper correspondence                              PASS
    stc-go correspondence                             PASS

Verification:
    direct conformance test                           PASS
    runtime test suite                                PASS
    race test                                          PASS

Scope:
    unrelated production semantics unchanged          PASS
    no new framework capability introduced            PASS
```

最终 Matrix：

```text
GAP-01 | P0 | PASS
```

---

# 24. Expected Agent Report

Code Agent 完成后只能按以下结构报告：

```text
# GAP-01 Conformance Closure

## 1. Semantic Rule

[冻结的 Retiring shadowing 规则]

## 2. Evidence

### Paper
[file / section / exact correspondence]

### stc-go
[file / test / implementation / exact correspondence]

### gocordis
[file / function / implementation]

## 3. Test

[test file]
[test name]

### Covered States

Active
→ Retiring
→ Gone

### Assertions

- Retiring does not fallback to ancestor
- Ancestor remains valid outside child realm
- Gone allows ancestor visibility

## 4. Verification

go test ./runtime/...        PASS
go test -race ./runtime/... PASS

## 5. Production Changes

[None / exact files and justification]

## 6. Documentation

[changed files]

## 7. Convergence Matrix

GAP-01: PARTIAL → PASS

## 8. Remaining Issues

[Only issues discovered; do not fix unrelated issues]
```

---

# 25. Final Rule

本任务的最终目的不是：

> “让 gocordis 支持 Retiring provider。”

因为当前实现已经支持。

真正目的：

> **把已经存在但未冻结的语义变成可执行、可重复、可审计的 Conformance Contract。**

完成后，`b09462c` 的：

```text
P0:
11 PASS
1 PARTIAL
```

应收敛为：

```text
P0:
12 PASS
0 PARTIAL
0 MISSING
```

然后才能进入下一轮 Convergence Audit。

**不要在 GAP-01 完成前继续扩展 Runtime 功能。**