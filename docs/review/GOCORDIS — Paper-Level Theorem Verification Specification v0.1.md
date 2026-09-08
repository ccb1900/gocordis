> ⚠️ **ARCHIVED（已归档）— 2026-09-08**
>
> 本文档已整体归档，仅作历史参考，**不再作为规范来源或引用依据**。
> 本仓库现以论文原文为唯一规范来源：`docs/论文.pdf`
> （A Programming Paradigm for Spatiotemporal Composability）。
> 当前路线图与论文↔代码对照见 `docs/ROADMAP.md`。
>
> This document is archived for historical reference only and is no longer cited
> as authority. The paper is the sole normative source; see `docs/ROADMAP.md`.

# GOCORDIS — PAPER-LEVEL THEOREM VERIFICATION SPECIFICATION v0.1

## 0. Status

```text
SPECIFICATION
```

## 1. Objective

本规范用于指导 Code Agent 对 `gocordis` Runtime 进行修改，使其从：

```text
scenario-level contract verification
```

提升到：

```text
paper-level theorem verification
```

目标不是重写 Runtime，不是新增 Extension，也不是复制 `stc-go`。

目标是：

```text
Paper §4.4
    ↓
T59 Preservation
T61 Recovery Exactness
T63 Ordering
T66 Progress
T73 Confluence
    ↓
Executable property specification
    ↓
Randomized model / schedule generation
    ↓
Runtime execution
    ↓
Invariant / observational-equivalence oracle
```

完成后，`gocordis` 必须能够通过：

```text
go test -race ./...
go test -run Property -fuzz FuzzInterleaving -fuzztime 10s ./...
```

并且测试失败时能够指出违反的是哪一个 theorem/invariant。

---

# 2. Non-Goals

本阶段禁止：

1. 新增 WASM 功能。
2. 新增 HMR 功能。
3. 新增 Config Watch 功能。
4. 修改 Extension API，仅为了让测试通过。
5. 将 Cordis TypeScript API 作为 correctness oracle。
6. 直接复制 `stc-go` 实现。
7. 为了通过 property test 而弱化 Runtime invariant。
8. 将 timeout、sleep、retry 等测试技巧当成 Progress theorem 的证明。
9. 将“Fiber 最终变成 Gone”单独作为 Confluence 的充分条件。
10. 删除现有 contract/E2E 测试。

---

# 3. First Principle

必须始终遵循：

```text
Paper
  >
gocordis semantic model
  >
implementation
  >
tests
```

而不是：

```text
Cordis
  >
gocordis
```

`stc-go` 可以用于：

```text
test strategy reference
property testing reference
```

不能作为：

```text
semantic authority
```

---

# 4. Current Runtime Must Be Preserved

现有 Runtime 已具备以下重要机制，不得无理由重写：

```text
Context
Fiber
Activation
Dependency Snapshot
Provider Identity / Generation
Revertible Effect
LIFO Unwind
Consumer-first Withdrawal
Ownership
Orchestrator serialization
Stale completion fencing
```

这些机制是当前 Runtime 正确性的基础。

修改必须优先增加：

```text
verification
model
oracle
property tests
```

而不是重新设计 Runtime。

---

# 5. Required Verification Architecture

新增：

```text
runtime/
    theorem_model_test.go
    theorem_oracle_test.go
    theorem_generator_test.go
    theorem_property_test.go
```

或者根据现有项目测试组织方式调整为：

```text
runtime/
    property/
        model.go
        generator.go
        oracle.go
        t59_test.go
        t61_test.go
        t63_test.go
        t66_test.go
        t73_test.go
```

Code Agent 必须首先检查现有测试结构。

如果已有等价文件：

```text
property_contract_test.go
property_internal_test.go
```

则优先扩展现有结构，不制造重复测试体系。

---

# 6. Test Model

必须建立一个**独立于 Runtime 实现的数据模型**。

禁止：

```go
model = runtime.internalState
```

因为这样无法发现 Runtime 自身实现错误。

Model 至少包含：

```go
type Model struct {
    Fibers       map[FiberID]*ModelFiber
    Providers    map[ProviderKey]ModelProvider
    Dependencies map[FiberID]map[ProviderKey]ModelProvider
    Effects      map[FiberID][]ModelEffect
    Ownership    map[FiberID][]ModelOwnedResource
}
```

Model Fiber：

```go
type ModelFiber struct {
    ID         FiberID
    Parent     FiberID
    State      ModelFiberState
    Activation uint64
}
```

状态至少：

```text
Pending
Loading
Active
Withdrawing
Unloading
Gone
```

具体状态名称必须与现有 Runtime 适配。

---

# 7. Operation Model

建立独立 operation：

```go
type Op interface {
    Apply(*Model) error
}
```

至少支持：

```text
CreateFiber
LoadFiber
DisposeFiber
ReloadFiber
Provide
Require
Resolve
WithdrawProvider
ReplaceProvider
CreateChild
DisposeChild
RegisterEffect
```

不是所有 operation 都必须直接暴露给 Runtime API。

可以通过：

```text
high-level scenario
```

生成合法操作。

---

# 8. Operation Trace

每次 property test 必须产生：

```text
Seed
Operation sequence
Expected model state
Actual runtime observations
```

失败时必须输出完整 replay 信息：

```text
seed: 123456789

step 0: create fiber A
step 1: create fiber B
step 2: B requires service X
step 3: load B
step 4: load A
step 5: A provides X
step 6: dispose A
step 7: reload A
...
```

要求：

```text
failure must be reproducible
```

---

# 9. T59 — Preservation

## 9.1 Requirement

每一个合法 Runtime operation 后，都必须保持 Registry Well-Formedness。

必须验证：

### P1 — Parent validity

任何非-root Fiber：

```text
fiber.parent exists
```

### P2 — Provider uniqueness

对于任意 provider key：

```text
at most one active provider identity
```

provider identity 必须包含 activation generation。

禁止仅以：

```text
FiberID
```

判断 provider identity。

### P3 — Dependency validity

对于 Active Fiber：

```text
every required dependency is satisfied
```

### P4 — Provider state validity

Active consumer 的 provider 必须是合法 Active provider。

### P5 — Snapshot consistency

Consumer dependency snapshot 中的 provider identity：

```text
ProviderID + ActivationID
```

必须仍然对应合法 provider。

---

# 10. T59 Property

实现：

```go
func CheckPreservation(m *Model) error
```

每个随机 operation：

```text
model.Apply(op)
runtime.Apply(op)
CheckPreservation(model)
CheckPreservation(runtime)
```

如果 Runtime 不满足 invariant：

```text
FAIL
```

输出：

```text
T59 Preservation violated
seed
operation index
operation
model state
runtime state
```

---

# 11. T61 — Recovery Exactness

## 11.1 Requirement

定义：

```text
Load(F)
Dispose(F)
```

的最终 observable state 必须等价于：

```text
F never loaded
```

必须使用**独立 baseline**。

---

# 12. T61 Baseline Test

构造两个 Runtime：

```text
R0
R1
```

R0：

```text
initial configuration
never load F
```

R1：

```text
initial configuration
load F
perform arbitrary effects
dispose F
```

最终：

```text
Observable(R0) == Observable(R1)
```

---

# 13. Observable State

禁止直接比较 Runtime private fields。

定义：

```go
type ObservableState struct {
    ActiveFibers      ...
    Providers         ...
    Dependencies      ...
    Registrations     ...
    OwnedResources    ...
    EffectResidue     ...
}
```

必须只包含用户可观察的 Runtime semantic state。

不得包含：

```text
goroutine id
pointer address
mutex state
channel address
internal timestamp
implementation-specific counters
```

除非该字段本身属于论文语义。

---

# 14. Effect Recovery

对每个 effect：

```text
install
inverse
```

必须验证：

```text
inverse executes exactly once
```

同时：

```text
LIFO order
```

必须验证。

例如：

```text
e1
e2
e3
```

必须：

```text
e3^-1
e2^-1
e1^-1
```

---

# 15. T61 Failure Classification

如果失败，区分：

```text
RECOVERY_STATE_MISMATCH
RECOVERY_DOUBLE_INVERSE
RECOVERY_MISSING_INVERSE
RECOVERY_WRONG_ORDER
RECOVERY_LATE_EFFECT_LEAK
```

不能统一报：

```text
fiber not gone
```

---

# 16. T63 — Ordering

## Requirement

Fiber 只有在其依赖全部满足后才能进入 Loading / Apply。

验证：

```text
Consumer requires Provider
Provider inactive
    ↓
Consumer cannot Apply
```

Provider become Active 后：

```text
Provider Active
    ↓
Consumer may enter Loading
    ↓
Consumer Apply
```

---

# 17. T63 Property

随机生成：

```text
provider
consumer
dependency
load order
```

允许：

```text
consumer first
provider first
provider reload
consumer reload
provider withdraw
provider reappear
```

但必须保证：

```text
consumer.Apply()
```

发生时：

```text
all dependencies satisfied
```

---

# 18. T63 Event Oracle

Runtime 必须能够产生测试事件：

```text
FiberLoading
FiberApply
FiberActive
FiberWithdrawing
FiberUnloading
FiberGone
ProviderAvailable
ProviderUnavailable
```

如果已有事件机制，直接使用。

不得为了测试新增与生产语义无关的第二套 lifecycle dispatcher。

测试检查：

```text
Apply(consumer)
```

之前最近一次 provider state 必须为：

```text
Active
```

---

# 19. T66 — Progress

## Requirement

Progress 不是：

```text
eventually with time.Sleep
```

也不是：

```text
wait until timeout
```

而是：

> 对合法状态和合法 transition sequence，orchestrator 最终达到 quiescent state。

---

# 20. Quiescent Definition

定义：

```go
func IsQuiescent(state ObservableState) bool
```

Quiescent 至少意味着：

```text
no pending lifecycle transition
no unresolved dependency transition
no pending activation completion
no pending unwind completion
no pending withdrawal gate
```

不能简单定义为：

```text
all fibers Gone
```

因为合法系统可能包含：

```text
Active fibers
```

仍然属于 quiescent。

---

# 21. Bounded Progress

Runtime 必须提供测试可用的：

```go
Step()
```

或等价的 deterministic orchestrator driving mechanism。

测试不得使用：

```go
time.Sleep()
```

作为 theorem proof。

定义：

```text
N = bounded number of enabled transitions
```

在：

```text
N + constant
```

步以内必须达到 quiescence。

如果当前 Runtime 是异步 API：

```text
production async API
```

可以保留。

但必须增加：

```text
deterministic test driver
```

使 property test 可以逐步推进 orchestrator。

---

# 22. T66 Property

生成随机合法 operation sequence：

```text
load
dispose
reload
provide
withdraw
replace
child create
child dispose
```

执行：

```text
for step := 0; step < bound; step++ {
    runtime.Step()
    if IsQuiescent(runtime) {
        PASS
    }
}
FAIL
```

bound 必须根据：

```text
number of fibers
number of dependencies
number of effects
number of pending transitions
```

计算。

禁止使用任意巨大固定值掩盖 Progress 问题。

---

# 23. T66 Failure Output

失败必须输出：

```text
T66 Progress violated

seed:
operations:
step count:
bound:

pending fibers:
pending activations:
pending dependencies:
pending withdrawal gates:
pending effects:
```

这样 Code Agent 才能定位真正的 livelock/deadlock。

---

# 24. T73 — Confluence

## Requirement

对于相同初始 semantic state 和相同 operation set：

```text
不同合法 interleaving
```

最终 quiescent observable state 必须 observationally equivalent。

---

# 25. Schedule Model

不要手写：

```text
A B C
B C A
C A B
```

必须生成：

```text
partial order DAG
```

例如：

```text
P ready
C requires P
C load depends on P active
```

合法 schedule 必须满足：

```text
dependency precedence
```

但除此之外：

```text
independent operations
```

允许任意交错。

---

# 26. Randomized Interleaving

实现：

```go
GenerateSchedules(trace, seed)
```

或者更优：

```go
ExploreInterleavings(trace, seed, budget)
```

每次 property run：

```text
same initial state
same logical operations
different legal schedules
```

至少运行：

```text
N >= 32
```

个 randomized schedules。

Fuzz 模式下允许增加到更高数量。

---

# 27. Schedule Determinism

所有 schedule 必须由：

```text
math/rand/v2
```

或项目已有 deterministic RNG 产生。

禁止：

```text
global rand
time.Now()
goroutine timing
```

决定测试 schedule。

必须可以通过：

```text
seed
```

完全重现。

---

# 28. Observational Equivalence

不能简单：

```go
reflect.DeepEqual(runtimeA, runtimeB)
```

必须：

```go
Observe(runtimeA) == Observe(runtimeB)
```

比较：

```text
active fibers
providers
dependency bindings
registrations
ownership
observable resources
```

忽略：

```text
internal pointer
goroutine
mutex
channel
generation counters
```

除非 generation 本身具有 semantic meaning。

---

# 29. Confluence and Duplicate Provider

如果论文 theorem 对 duplicate-provider 情形存在前提限制：

必须明确编码为：

```go
Precondition(trace)
```

而不是：

```go
skip silently
```

如果不满足 theorem precondition：

```text
property = not applicable
```

必须记录原因。

禁止把 unsupported case 当成 PASS。

---

# 30. Confluence and Effect Independence

对于两个 component：

```text
A
B
```

只有当其 effects 满足论文规定的 independence / observational equivalence 前提时，才要求 schedule convergence。

必须把：

```text
independence precondition
```

编码到 generator / oracle。

不能简单宣称：

```text
all effects are confluent
```

---

# 31. Required Fuzz Entry Point

必须增加：

```go
func FuzzInterleaving(f *testing.F)
```

seed corpus 至少包括：

```text
single provider
provider + consumer
consumer-first
provider replacement
provider withdrawal
nested child
effect + dependency
reload
concurrent withdrawal/reload
```

Fuzz case 必须：

```text
decode seed
generate legal operation graph
generate legal schedules
execute
check T59/T61/T63/T66/T73
```

---

# 32. Race Verification

必须通过：

```bash
go test -race ./...
```

特别关注：

```text
Context
Fiber
Activation
Provider registry
Dependency graph
Orchestrator
Effect unwind
```

Race detector failure：

```text
FAIL
```

不能作为 flaky test 忽略。

---

# 33. Existing Tests

现有：

```text
contract tests
property tests
E2E tests
WASM tests
HMR tests
```

全部保留。

新测试必须与现有测试共存。

如果旧测试与论文 theorem 冲突：

```text
do not silently change theorem
```

必须报告：

```text
semantic discrepancy
```

并判断：

```text
implementation bug
test bug
paper interpretation issue
```

---

# 34. Required Theorem Matrix

最终必须新增：

```text
docs/theorem-verification.md
```

包含：

| Theorem | Definition | Runtime Mechanism | Property | Status |
|---|---|---|---|---|
| T59 | Preservation | registry/dependency/provider invariants | randomized invariant check | PASS/FAIL |
| T61 | Recovery | effect inverse/LIFO | baseline equivalence | PASS/FAIL |
| T63 | Ordering | dependency gating | event ordering | PASS/FAIL |
| T66 | Progress | orchestrator transitions | bounded quiescence | PASS/FAIL |
| T73 | Confluence | legal interleaving | observational equivalence | PASS/FAIL |

---

# 35. Required Failure Taxonomy

所有 theorem failure 必须属于：

```text
T59_PRESERVATION
T61_RECOVERY
T61_LIFO
T61_EXACTLY_ONCE
T63_ORDERING
T66_PROGRESS
T66_DEADLOCK
T66_LIVELOCK
T73_CONFLUENCE
T73_PRECONDITION
```

错误消息必须包含：

```text
theorem
seed
operation index
operation trace
runtime state
expected state
actual state
```

---

# 36. Shrinking

Property test 必须支持 shrinking。

例如：

```text
100 operations
```

失败后自动缩减为：

```text
7 operations
```

仍然能够复现 failure。

最终输出：

```text
minimal counterexample
```

Code Agent 必须优先实现 deterministic shrink，而不是只增加 fuzz duration。

---

# 37. Counterexample Format

失败时输出：

```text
=== THEOREM FAILURE ===

theorem: T73 Confluence
seed: 91827364

initial:
  fibers: [A B C]

operations:
  0: create A
  1: create B
  2: B require X
  3: create C
  4: A provide X
  5: withdraw A

schedule A:
  ...

schedule B:
  ...

result A:
  ...

result B:
  ...

first divergent observation:
  provider[X]

minimal reproducer:
  ...
```

---

# 38. No Sleep-Based Proof

以下模式禁止作为 theorem verification：

```go
time.Sleep(...)
Eventually(...)
EventuallyWithTimeout(...)
retry until it passes
```

可以在 E2E 测试中存在。

但：

```text
T59/T61/T63/T66/T73
```

必须使用 deterministic driving / synchronization。

---

# 39. No Implementation-Coupled Oracle

禁止：

```go
if runtime.internalState == expectedInternalState
```

必须：

```text
semantic observation
```

否则测试可能：

```text
implementation A
```

错误地验证：

```text
implementation A
```

---

# 40. No Cordis-Copy Requirement

不要为了测试增加：

```text
Cordis-compatible internal behavior
```

论文语义相同即可。

API compatibility 不是本任务目标。

---

# 41. stc-go Reference

可以参考 `stc-go`：

```text
property_test.go
contract_test.go
FuzzInterleaving
```

尤其参考：

```text
T59 → invariant property
T61 → baseline recovery
T63 → dependency ordering
T66 → bounded quiescence
T73 → randomized schedule
```

但是：

```text
DO NOT COPY IMPLEMENTATION
```

如果 `stc-go` 的某个行为与论文产生冲突：

```text
paper wins
```

`stc-go` 自身也明确将论文作为 specification，并把 Cordis 仅作为 reference/test scenario source。

---

# 42. Implementation Order

Code Agent 必须严格按照以下顺序：

## Phase 1 — Model

实现：

```text
Model
ObservableState
Operation
Trace
```

不得修改 Runtime。

---

## Phase 2 — Oracle

实现：

```text
CheckPreservation
CheckRecovery
CheckOrdering
CheckProgress
CheckConfluence
```

先让 oracle 自身通过已有固定测试。

---

## Phase 3 — T59

增加：

```text
randomized operation
registry invariant
dependency invariant
provider invariant
```

---

## Phase 4 — T61

增加：

```text
baseline runtime
effect trace
inverse trace
observable equivalence
```

---

## Phase 5 — T63

增加：

```text
random dependency graph
event ordering oracle
consumer-first scenarios
```

---

## Phase 6 — T66

增加：

```text
deterministic orchestrator driver
quiescence detector
bounded transition count
```

如果 Runtime 当前没有 deterministic step API：

优先增加**仅测试可用的内部 driver**，不要改变 production semantics。

---

## Phase 7 — T73

增加：

```text
DAG
legal scheduler
random interleavings
observational equivalence
```

---

## Phase 8 — Fuzz

实现：

```text
FuzzInterleaving
```

并加入 corpus。

---

## Phase 9 — Race

运行：

```bash
go test -race ./...
```

修复所有 race。

---

## Phase 10 — Documentation

生成：

```text
docs/theorem-verification.md
```

明确：

```text
PASS
PARTIAL
UNPROVEN
FAIL
```

不能把未验证项目写成 PASS。

---

# 43. Acceptance Criteria

本任务只有满足以下全部条件才能报告：

```text
PAPER-LEVEL VERIFICATION: PASS
```

### AC-01

```bash
go test ./...
```

PASS。

### AC-02

```bash
go test -race ./...
```

PASS。

### AC-03

```bash
go test -run Property ./...
```

PASS。

### AC-04

```bash
go test -run Property -fuzz FuzzInterleaving -fuzztime 10s ./...
```

PASS。

### AC-05

T59 有 randomized invariant verification。

### AC-06

T61 有 baseline observational-equivalence verification。

### AC-07

T63 有 randomized dependency-order verification。

### AC-08

T66 不依赖 `time.Sleep`，拥有 deterministic progress driver。

### AC-09

T73 至少拥有 randomized legal interleaving verification。

### AC-10

T73 对 theorem precondition 做显式判断。

### AC-11

property failure 能输出 deterministic seed。

### AC-12

property failure 能 shrink 到最小或接近最小 counterexample。

### AC-13

所有现有 E2E/WASM/HMR 测试保持 PASS。

### AC-14

不得为了测试修改论文语义。

---

# 44. Final Report Format

Code Agent 完成后必须严格输出：

```text
# PAPER-LEVEL THEOREM VERIFICATION

## Result

PASS / CONDITIONAL PASS / FAIL

## T59 Preservation

PASS
- randomized invariant:
- cases:
- failures:

## T61 Recovery Exactness

PASS
- baseline comparison:
- LIFO:
- exactly-once:
- failures:

## T63 Ordering

PASS
- dependency scenarios:
- randomized cases:
- failures:

## T66 Progress

PASS
- deterministic driver:
- max transition bound:
- randomized cases:
- failures:

## T73 Confluence

PASS
- schedules explored:
- randomized schedules:
- precondition handling:
- observational equivalence:
- failures:

## Fuzz

PASS
- fuzz target:
- corpus:
- duration:
- crashes:

## Race

PASS

## Existing Tests

PASS

## Remaining Deviations

NONE
```

如果存在任何未证明 theorem：

```text
Result = CONDITIONAL PASS
```

不得报告：

```text
PASS
```

---

# 45. Critical Rule

**不要为了满足测试而改变 Runtime 语义。**

如果 property test 暴露：

```text
T59 violation
T61 violation
T63 violation
T66 violation
T73 violation
```

Code Agent 必须首先判断：

```text
implementation bug
```

而不是修改：

```text
oracle
```

只有当证明：

```text
oracle incorrectly interprets paper
```

时才能修改 oracle。

---

# 46. Definition of Done

最终 Definition of Done：

```text
                    PAPER
                      │
                      ▼
              ┌──────────────┐
              │ Five Theorems│
              └──────┬───────┘
                     │
          ┌──────────┼──────────┐
          ▼          ▼          ▼
       Model       Oracle     Generator
          │          │          │
          └──────────┼──────────┘
                     ▼
              Runtime execution
                     │
                     ▼
              Observable state
                     │
          ┌──────────┼──────────┐
          ▼          ▼          ▼
        Invariant  Recovery   Schedule
          │          │          │
          └──────────┼──────────┘
                     ▼
              Theorem verified
```

最终必须能够回答：

```text
Q1: Runtime 是否保持 Preservation？
Q2: Fiber unload 后是否 Exact Recovery？
Q3: Dependency Ordering 是否始终成立？
Q4: 任意合法 transition 是否最终 Progress？
Q5: 任意合法 interleaving 是否 Confluent？
```

并且每个答案都有：

```text
executable property
+
deterministic counterexample
+
reproducible seed
```

作为依据。

**在这五项全部 PASS 之前，不得宣布 gocordis 为 paper-level complete。**