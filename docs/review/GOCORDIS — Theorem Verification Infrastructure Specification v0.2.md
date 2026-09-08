> ⚠️ **ARCHIVED（已归档）— 2026-09-08**
>
> 本文档已整体归档，仅作历史参考，**不再作为规范来源或引用依据**。
> 本仓库现以论文原文为唯一规范来源：`docs/论文.pdf`
> （A Programming Paradigm for Spatiotemporal Composability）。
> 当前路线图与论文↔代码对照见 `docs/ROADMAP.md`。
>
> This document is archived for historical reference only and is no longer cited
> as authority. The paper is the sole normative source; see `docs/ROADMAP.md`.

# GOCORDIS — Theorem Verification Infrastructure Specification v0.2

## 0. 文档定位

本规范用于指导 Code Agent 为 `gocordis` 实现论文级定理验证基础设施。

规范的第一性来源：

1. 论文：**A Programming Paradigm for Spatiotemporal Composability**
2. `stc-go`：仅作为工程验证策略参考
3. `cordiverse/cordis`：仅作为实现参考

**严禁反过来以 `stc-go` 或 `cordis` 的实现定义论文语义。**

目标不是增加更多 scenario test，而是建立：

```text
Semantic Model
      ↓
Semantic Observer
      ↓
Deterministic Driver
      ↓
Legal Scheduler
      ↓
Quiescence Oracle
      ↓
Theorem Properties
      ↓
Fuzz / Replay / Shrink
```

最终目标：

```text
T59 Preservation       → 可机械验证
T61 Recovery Exactness → 可机械验证
T63 Ordering           → 可机械验证
T66 Progress           → 可机械验证
T73 Confluence         → 可机械验证
```

---

# 1. 非目标

本阶段禁止：

- 重写 Runtime 核心生命周期模型
- 为了让 property test 通过而修改论文语义
- 用 `time.Sleep` 证明定理
- 用 `Ready(timeout)` 证明 Progress
- 用 `Gone(timeout)` 定义 Quiescence
- 用事件日志完全代替最终语义状态
- 直接把 Runtime 私有字段复制成“Oracle”
- 将 `stc-go` 行为直接视为规范
- 为了 T73 而放宽论文的前置条件
- 将所有失败都归类为 Runtime bug

测试失败必须能够区分：

```text
Theorem precondition violation
Implementation violation
Verification infrastructure violation
```

---

# 2. 总体架构

新增测试基础设施建议组织为：

```text
runtime/
    theorem/
        model.go
        observation.go
        observer.go
        equivalence.go

        driver.go
        scheduler.go
        schedule.go

        quiescence.go
        bound.go

        generators.go
        shrink.go
        replay.go

        t59_test.go
        t61_test.go
        t63_test.go
        t66_test.go
        t73_test.go
```

具体目录可以根据现有工程调整。

原则：

> theorem package 不应成为 Runtime 的第二套实现。

它只负责：

```text
生成输入
观察状态
控制执行
判断性质
```

不负责重新实现：

```text
dependency resolution
provider registry
reconcile
effect execution
```

---

# 3. Verification Architecture

必须建立以下五个核心组件：

```text
┌──────────────────────────────┐
│ Semantic Model               │
│                              │
│ Components                   │
│ Capabilities                 │
│ Dependencies                │
│ Operations                   │
└──────────────┬───────────────┘
               │
               ▼
┌──────────────────────────────┐
│ Legal Scheduler              │
│                              │
│ Partial Order                │
│ Enabled Steps                │
│ Randomized Selection         │
└──────────────┬───────────────┘
               │
               ▼
┌──────────────────────────────┐
│ Deterministic Driver         │
│                              │
│ One lifecycle step at a time │
└──────────────┬───────────────┘
               │
               ▼
┌──────────────────────────────┐
│ Runtime Under Test           │
└──────────────┬───────────────┘
               │
               ▼
┌──────────────────────────────┐
│ Semantic Observer            │
│                              │
│ Quiescence                   │
│ Final State                  │
│ Equivalence                  │
└──────────────────────────────┘
```

---

# 4. Semantic Model

实现一个独立的测试语义模型。

不得直接复用 Runtime 的：

```go
Fiber
Activation
providerRecord
dependencyGraph
reconcileState
```

作为测试模型。

可以复用公开 ID / Key 类型，但测试模型必须拥有自己的状态表示。

---

## 4.1 Component Model

至少支持：

```go
type ComponentSpec struct {
    Name       string
    Provides   []Capability
    Requires   []Capability
    Children   []ComponentSpec
}
```

测试模型必须能够表达：

```text
Provider
Consumer
Multiple Consumers
Provider Replacement
Independent Components
Owned Children
```

---

# 5. Operation Model

定义抽象操作：

```go
type OperationKind int

const (
    OpLoad OperationKind = iota
    OpDispose
    OpClose
)
```

操作必须是：

```go
type Operation struct {
    Kind      OperationKind
    Component ComponentID
}
```

后续可以增加：

```text
Provider replacement
Child creation
Capability registration
```

但第一阶段不要扩展。

---

# 6. Schedule Model

这是本规范最重要的部分之一。

不能只随机化：

```text
Load(P)
Load(C)
```

而必须能够随机化 Runtime 内部的：

```text
command
ApplyDone
UnwindDone
dependency transition
withdrawal completion
```

---

## 6.1 Schedule

定义：

```go
type Step struct {
    ID   StepID
    Kind StepKind
}

type Schedule []Step
```

Step 至少包括：

```go
type StepKind int

const (
    StepCommand
    StepApplyDone
    StepUnwindDone
)
```

如果 Runtime 当前架构无法直接控制这些步骤：

> 必须增加测试专用 deterministic control point。

不得使用 sleep 代替。

---

# 7. Deterministic Driver

必须实现：

```go
type Driver interface {
    Step() (Step, error)
    EnabledSteps() []Step
    Quiescent() bool
}
```

要求：

> 每次 `Step()` 最多推进一个可观察 lifecycle transition。

Driver 必须能够：

```text
暂停
选择下一合法 step
执行
观察
继续
```

---

# 8. 禁止时间驱动证明

以下方式禁止用于 T66 / T73：

```go
time.Sleep(...)
```

以及：

```go
Ready(time.Second)
Gone(time.Second)
```

作为 theorem oracle。

允许用于：

```text
ordinary integration tests
race tests
```

但不得出现在：

```text
t66_test.go
t73_test.go
```

的核心证明路径中。

---

# 9. Enabled Step

Driver 必须提供：

```go
EnabledSteps() []Step
```

它代表：

> 当前状态下所有合法、可执行的 lifecycle transitions。

例如：

```text
Provider ApplyDone
Consumer ApplyDone
Provider UnwindDone
```

如果 Consumer 依赖 Provider 尚未 Ready：

```text
Consumer ApplyDone
```

不得出现在 EnabledSteps 中。

---

# 10. Legal Schedule

Schedule 不允许随便排列。

必须满足 Runtime 当前状态下的：

```text
dependency precedence
activation validity
activation generation
withdrawal ordering
```

因此：

```go
func IsLegal(step Step, state State) bool
```

必须能够判断。

---

# 11. Partial Order

测试基础设施必须显式表达 precedence。

至少包括：

```text
Provider activation
    ≺
Consumer activation
```

以及：

```text
Consumer withdrawal
    ≺
Provider withdrawal
```

如果：

```text
P provides X
C requires X
```

则：

```text
P_apply
    ≺
C_apply
```

Provider withdrawal：

```text
C_unwind
    ≺
P_unwind
```

---

# 12. Acyclic Precedence

T66 / T73 使用的 workload 必须满足 precedence graph acyclic。

必须提供：

```go
func CheckAcyclicPrecedence(...) error
```

要求：

```text
合法输入：
    DAG

非法输入：
    dependency cycle
```

cycle 不得被测试框架静默忽略。

必须产生明确分类：

```text
PreconditionViolation:
    cyclic precedence graph
```

---

# 13. Quiescence Definition

必须定义真正的 Semantic Quiescence。

禁止：

```text
所有 Fiber == Gone
```

作为 Quiescence 定义。

因为：

```text
Active + no pending transition
```

同样可以是 quiescent。

---

## 13.1 Quiescent Conditions

至少要求：

```text
pending lifecycle commands == 0

running Apply == 0

running Unwind == 0

pending withdrawal == 0

unresolved withdrawal gates == 0

unresolved dependency transition == 0

no transitional Fiber
```

定义：

```go
type QuiescenceOracle interface {
    IsQuiescent() bool
}
```

---

# 14. Quiescence Must Be Semantic

Quiescence 判断不得依赖：

```text
sleep
timer
wall clock
eventual polling
```

而应该检查：

```text
当前状态是否还有 Enabled Step
```

理想定义：

```go
func Quiescent() bool {
    return len(EnabledSteps()) == 0
}
```

如果 Runtime 有内部非 lifecycle background activity：

> 必须明确区分 lifecycle step 与非 theorem-relevant background activity。

---

# 15. Progress Bound

T66 需要有限步数证明。

必须定义：

```go
func Bound(model Model) int
```

第一阶段可以使用：

```text
N = c * (#fibers + #dependencies + #operations)
```

其中 `c` 必须是明确常数。

禁止：

```text
N = 1_000_000
```

这种没有语义依据的 magic bound。

---

# 16. T66 Property

实现：

```go
func TestT66Progress(...)
```

流程：

```text
Generate legal model
        ↓
Check preconditions
        ↓
Create Runtime
        ↓
Apply operation set
        ↓
Randomly select enabled legal steps
        ↓
Execute at most Bound(model) steps
        ↓
Require Quiescent
```

伪代码：

```go
for i := 0; i < bound; i++ {
    if driver.Quiescent() {
        return PASS
    }

    enabled := driver.EnabledSteps()

    if len(enabled) == 0 {
        fail("non-quiescent state has no executable step")
    }

    step := scheduler.Select(enabled)
    driver.Execute(step)
}

if !driver.Quiescent() {
    fail("T66 progress bound exceeded")
}
```

---

# 17. T66 Failure Classification

失败必须输出：

```text
seed
model
operations
precedence graph
step history
final runtime state
enabled steps
quiescence snapshot
```

例如：

```text
T66 FAILURE

seed: 928173

components:
    P provides X
    C requires X

operations:
    Load(C)
    Load(P)

precedence:
    P -> C

steps:
    1. Load(C)
    2. Load(P)
    3. ApplyDone(P)

final:
    P = Active
    C = Loading

enabled:
    ApplyDone(C)

bound:
    8

reason:
    progress bound exceeded
```

---

# 18. Semantic Observer

必须实现独立 Observer：

```go
type Observation struct {
    Fibers       []FiberObservation
    Providers    []ProviderObservation
    Dependencies []DependencyObservation
    Effects      []EffectObservation
    Children     []ChildObservation
}
```

Observer 只关心：

> 论文语义可观察状态。

不得把：

```text
mutex
goroutine ID
channel address
memory address
internal queue pointer
```

放入 Observation。

---

# 19. Observation Normalization

Observation 必须 canonicalize。

例如：

```text
Fiber 顺序
Provider 顺序
Dependency edge 顺序
Effect 顺序
```

都必须稳定排序。

否则：

```text
map iteration order
```

会导致假 Confluence failure。

---

# 20. Observational Equivalence

实现：

```go
func Equivalent(a, b Observation) bool
```

必须明确：

```text
哪些状态属于 semantic observable
哪些属于 implementation detail
```

不能简单：

```go
reflect.DeepEqual(runtimeA, runtimeB)
```

也不能：

```go
eventsA == eventsB
```

---

# 21. T61 Recovery Exactness

实现：

```text
R0:
    Initial
    ↓
    Never Load C
    ↓
    Observe(R0)

R1:
    Initial
    ↓
    Load C
    ↓
    Apply
    ↓
    Dispose C
    ↓
    Quiescence
    ↓
    Observe(R1)
```

要求：

```go
Equivalent(R0, R1) == true
```

---

# 22. T61 不允许只检查 Gone

以下测试不足：

```go
if fiber.State() != Gone {
    fail()
}
```

因为：

```text
Gone
```

只能证明 lifecycle state。

不能证明：

```text
side effects completely reverted
```

T61 必须至少覆盖：

```text
provider registration
effect registration
owned children
dependency edges
runtime-managed resources
```

---

# 23. T63 Activation Ordering

保留现有：

```text
Consumer Load first
Provider Load second
```

测试。

要求：

```text
Provider Apply
    ≺
Consumer Apply
```

并验证：

```text
Consumer Require()
```

在 activation 中不会观察到尚未 ready 的 dependency。

---

# 24. T63 Withdrawal Ordering

新增独立测试：

```text
P provides X
C requires X

Load(P)
Load(C)

Dispose(P)
```

要求：

```text
C Unwind
    ≺
P Unwind
```

并且：

```text
C Cleanup
    ≺
P Cleanup
```

必须通过 observable event oracle 验证。

---

# 25. T59 Preservation

每一个合法 lifecycle step 后执行：

```go
CheckWellFormed()
```

至少验证：

### Provider uniqueness

```text
一个 capability
    →
最多一个 active provider
```

### Provider identity

```text
旧 activation inverse
不能删除新 activation provider
```

### Dependency validity

```text
每个 active dependency
必须指向有效 provider generation
```

### Graph validity

```text
没有非法 dangling edge
```

### Ownership validity

```text
child owner 必须存在
```

---

# 26. T59 Failure Output

任何 invariant failure 必须输出：

```text
step
operation
fiber states
provider registry
dependency graph
activation identities
effect stack
ownership graph
```

而不是只输出：

```text
expected X, got Y
```

---

# 27. T73 Confluence

T73 不得再使用：

```text
scheduleA = [c1,c2,p]
scheduleB = [p,c2,c1]
```

这种简单 API invocation order 作为主要证明。

它只能作为普通 replacement regression test。

---

# 28. T73 正确模型

生成一个 operation set：

```text
Load(P)
Load(C1)
Load(C2)
Dispose(...)
```

然后生成多个：

```text
legal schedules
```

例如：

```text
Schedule A:
Load(P)
Load(C1)
Load(C2)
ApplyDone(P)
ApplyDone(C1)
ApplyDone(C2)

Schedule B:
Load(P)
Load(C1)
Load(C2)
ApplyDone(P)
ApplyDone(C2)
ApplyDone(C1)

Schedule C:
Load(P)
Load(C2)
Load(C1)
ApplyDone(P)
ApplyDone(C1)
ApplyDone(C2)
```

只要 schedule 满足：

```text
partial order
```

就是合法 schedule。

---

# 29. T73 Final Observation

每个 schedule：

```text
Run schedule
    ↓
Drive to quiescence
    ↓
Observe
```

得到：

```text
O1
O2
O3
...
```

最终：

```go
Equivalent(O1, O2)
Equivalent(O1, O3)
...
```

必须成立。

---

# 30. T73 不比较中间事件顺序

禁止：

```go
eventsA == eventsB
```

作为主要 Confluence 判断。

因为：

```text
Schedule A:
P Apply
C1 Apply
C2 Apply

Schedule B:
P Apply
C2 Apply
C1 Apply
```

中间事件顺序不同：

```text
不是 confluence violation
```

只要：

```text
quiescent semantic state
```

等价即可。

---

# 31. T73 Preconditions

每个 T73 workload 必须明确记录：

```text
precedence graph acyclic
provider uniqueness
component totality
pairwise independence
```

如果 workload 不满足 theorem precondition：

```text
不要执行 Equivalent assertion
```

而应报告：

```text
PRECONDITION VIOLATION
```

---

# 32. Randomized Scheduler

实现：

```go
type Scheduler interface {
    Select([]Step) Step
}
```

随机选择必须使用：

```go
math/rand/v2
```

或项目当前采用的 deterministic RNG。

禁止使用：

```text
time.Now()
```

作为 seed。

---

# 33. Seed

每次 property run 必须记录：

```text
seed
```

例如：

```text
seed=928173
```

失败后可以：

```bash
go test ./runtime/theorem -run TestT73 -seed=928173
```

具体 CLI 形式可以根据 Go test 体系设计。

核心要求：

> 任意失败必须可 deterministic replay。

---

# 34. Schedule Recording

每次 fuzz run 必须记录：

```text
seed
model
operation sequence
schedule
```

例如：

```text
seed: 928173

operations:
  1. Load(C1)
  2. Load(C2)
  3. Load(P)

schedule:
  1. cmd.Load(P)
  2. cmd.Load(C1)
  3. cmd.Load(C2)
  4. ApplyDone(P)
  5. ApplyDone(C2)
  6. ApplyDone(C1)
```

---

# 35. Shrinking

T73 / T66 failure 必须支持 shrink。

优先 shrink：

```text
1. component count
2. operation count
3. dependency count
4. schedule length
5. unnecessary steps
```

目标：

```text
最小失败模型
+
最小失败 schedule
```

---

# 36. Example

原始失败：

```text
P1
P2
C1
C2
C3
C4

17 operations
31 schedule steps
```

Shrink 后：

```text
P
C

2 operations
6 schedule steps
```

最终报告：

```text
minimal counterexample
```

这比打印几千行日志有价值。

---

# 37. Fuzz Entry Point

必须增加：

```go
func FuzzT73Confluence(f *testing.F)
```

或者：

```go
func FuzzInterleaving(f *testing.F)
```

要求：

```text
seed corpus
+
random generation
+
legal schedule validation
+
deterministic replay
```

---

# 38. Corpus

至少加入：

```text
provider + consumer
provider + 2 consumers
provider replacement
independent providers
independent consumers
consumer loaded before provider
provider loaded before consumer
provider withdrawal
```

---

# 39. Race Test 与 Theorem Test 分离

保留：

```text
TestPropertyRaceLoadClose
```

这种测试。

但明确：

```text
Race Test
≠
T66
≠
T73
```

Race test 验证：

```text
data race
concurrent API safety
```

Theorem test 验证：

```text
semantic properties
```

不能混在一起。

---

# 40. Existing Tests

现有测试不得删除。

尤其保留：

```text
property_contract_test.go
property_internal_test.go
phase*_contract_test.go
waitinactive*
domain_contract_test.go
```

原有测试属于：

```text
regression / contract suite
```

新的 theorem infrastructure 属于：

```text
semantic verification suite
```

二者并存。

---

# 41. 现有 P4 修改要求

当前：

```text
TestPropertyProgressToQuiescence
```

不得删除。

但是必须降级其语义名称，例如：

```text
TestLifecycleConvergesAfterDispose
```

它继续作为：

```text
scenario regression
```

存在。

不得再将：

```text
all fibers Gone
```

写成：

```text
T66 quiescence
```

---

# 42. 现有 P5 修改要求

当前：

```text
TestPropertyConfluenceReplacement
```

保留。

但明确改名为：

```text
TestProviderReplacementConverges
```

它是：

```text
provider replacement regression
```

不是论文 T73。

新增：

```text
FuzzT73Confluence
```

作为真正的 theorem test。

---

# 43. Runtime 不得为了测试暴露过多内部 API

优先采用：

```text
test-only instrumentation
```

而不是修改 public API。

推荐：

```go
//go:build theorem
```

或 internal test hooks。

如果必须修改 Runtime：

> 修改必须保持 public API backward compatible。

---

# 44. Deterministic Runtime Mode

如果当前 orchestrator 使用 goroutine/channel 导致无法 deterministic control：

必须增加：

```go
type RuntimeMode int

const (
    RuntimeNormal
    RuntimeDeterministic
)
```

Deterministic mode：

```text
不依赖 wall clock
不自动抢跑 lifecycle completion
所有 theorem-relevant completion 由 Driver 注入
```

Normal mode：

```text
保持现有生产行为
```

---

# 45. 禁止“双 Runtime”

不要实现：

```text
Production Runtime
Theorem Runtime
```

两套不同 lifecycle semantics。

Deterministic mode 必须只是：

```text
same Runtime
+
controlled scheduling
```

否则 theorem test 证明的是另一个系统。

---

# 46. Semantic Oracle Independence

这是整个规范的核心要求。

禁止：

```go
func Equivalent(a, b *Runtime) bool {
    return a.internalState == b.internalState
}
```

也禁止：

```go
func IsQuiescent(r *Runtime) bool {
    return r.someInternalFlag
}
```

如果 Oracle 直接读取 Runtime 内部 flag：

> 必须证明该 flag 本身不是被待验证性质定义出来的。

优先：

```text
semantic projection
```

而不是：

```text
implementation state comparison
```

---

# 47. Theorem Preconditions

建立统一结构：

```go
type Preconditions struct {
    AcyclicPrecedence bool
    ProviderUnique    bool
    PairwiseIndependent bool
    ComponentTotal    bool
}
```

每个 theorem test 在执行前：

```go
CheckPreconditions(model)
```

失败分类：

```text
PRECONDITION_VIOLATION
```

不是：

```text
THEOREM_FAILURE
```

---

# 48. Theorem Matrix

最终生成：

```text
┌──────┬───────────────────────┬──────────────┐
│ T59  │ Preservation          │ PASS/FAIL    │
│ T61  │ Recovery Exactness    │ PASS/FAIL    │
│ T63  │ Ordering              │ PASS/FAIL    │
│ T66  │ Progress              │ PASS/FAIL    │
│ T73  │ Confluence            │ PASS/FAIL    │
└──────┴───────────────────────┴──────────────┘
```

同时列出：

```text
test count
fuzz count
seed
counterexamples
precondition exclusions
```

---

# 49. CI Requirements

至少：

```bash
go test ./...
go test -race ./...
```

Theorem suite：

```bash
go test ./runtime/theorem/...
```

Fuzz smoke：

```bash
go test -run=^$ -fuzz=FuzzT73Confluence -fuzztime=10s ./runtime/theorem/...
```

具体 package path 按最终目录调整。

---

# 50. CI 不得依赖随机不稳定结果

CI fuzz：

```text
固定 seed corpus
固定基础 seed
```

开发环境可以：

```text
随机 seed
```

但一旦发现 failure：

```text
seed
+
model
+
schedule
```

必须加入 regression corpus。

---

# 51. Failure Regression

任何最小 counterexample 都必须能够转换成：

```text
TestT66Regression_xxx
```

或者：

```text
testdata/counterexamples/xxx.json
```

以后每次 CI 都重新验证。

---

# 52. Implementation Order

Code Agent 必须严格按照以下顺序实施。

## Phase A

实现：

```text
Semantic Observation
Quiescence Oracle
Precondition Checker
```

不要先做 fuzz。

---

## Phase B

实现：

```text
Deterministic Driver
EnabledSteps
Step execution
```

确认能够：

```text
手工执行一个完整生命周期
```

---

## Phase C

实现：

```text
T61 baseline equivalence
T63 activation ordering
T63 withdrawal ordering
T59 invariant checks
```

---

## Phase D

实现：

```text
T66 bounded progress
```

必须先做到：

```text
deterministic
non-time-based
replayable
```

---

## Phase E

实现：

```text
Legal Scheduler
Randomized Schedule
```

---

## Phase F

实现：

```text
T73 Confluence
Fuzz
Seed
Replay
Shrink
```

---

# 53. Acceptance Criteria

本规范完成的最低标准：

### A. Semantic Observer

```text
PASS
```

能够独立产生 canonical semantic observation。

### B. Deterministic Driver

```text
PASS
```

可以逐 step 推进 Runtime。

### C. Quiescence

```text
PASS
```

不依赖 timeout / sleep。

### D. T59

```text
PASS
```

每个合法 lifecycle step 后 invariant 成立。

### E. T61

```text
PASS
```

Loaded → Unloaded 与 Never Loaded semantic observation 等价。

### F. T63

```text
PASS
```

Activation 与 Withdrawal ordering 都验证。

### G. T66

```text
PASS
```

在 theorem preconditions 下：

```text
within finite bound
→
quiescence
```

### H. T73

```text
PASS
```

至少多个不同 legal schedules：

```text
Schedule A
Schedule B
Schedule C
...
```

最终：

```text
Equivalent(Observe(A), Observe(B))
```

成立。

---

# 54. Final Report

Code Agent 完成后必须输出：

```text
# THEOREM VERIFICATION REPORT

## T59 Preservation
PASS

cases:
    ...

## T61 Recovery Exactness
PASS

baseline comparisons:
    ...

## T63 Ordering
PASS

activation:
    PASS

withdrawal:
    PASS

## T66 Progress
PASS

max steps:
    ...

bound:
    ...

models:
    ...

## T73 Confluence
PASS

schedules:
    ...

fuzz iterations:
    ...

seeds:
    ...

## Preconditions
acyclic precedence:
    PASS

provider uniqueness:
    PASS

component totality:
    PASS

pairwise independence:
    PASS

## Counterexamples
none

## Final Verdict

T59 PASS
T61 PASS
T63 PASS
T66 PASS
T73 PASS

THEOREM VERIFICATION:
PASS
```

---

# 55. 最终判定规则

不能因为：

```text
go test ./...
```

通过，就宣布：

```text
Paper Compliance: PASS
```

必须满足：

```text
T59 PASS
AND
T61 PASS
AND
T63 PASS
AND
T66 PASS
AND
T73 PASS
```

并且：

```text
无未解释的 theorem precondition violation
无 flaky theorem test
所有 failure 可 replay
```

才允许最终声明：

```text
PAPER-LEVEL VERIFICATION: PASS
```

否则：

```text
CONDITIONAL PASS
```

或者：

```text
NOT VERIFIED
```

---

# 56. 最重要的设计原则

最终代码应该形成：

```text
              PAPER
                │
                ▼
        Semantic Model
                │
                ▼
        Legal Schedules
                │
                ▼
       Deterministic Driver
                │
                ▼
          GoCordis Runtime
                │
                ▼
       Semantic Observer
                │
                ▼
       Theorem Properties
```

而不是：

```text
GoCordis Runtime
       │
       ├── 看内部字段
       ├── sleep
       ├── timeout
       ├── 比较 event log
       └── 猜测论文定理
```

**论文是规范。**

**Runtime 是被测系统。**

**Verification Infrastructure 是独立裁判。**

这三者必须保持边界。

---

# 57. Code Agent 执行要求

不要一次性重构整个项目。

必须按照：

```text
Phase A
↓
go test ./...
↓
Phase B
↓
go test ./...
↓
Phase C
↓
go test ./...
↓
Phase D
↓
T66
↓
Phase E
↓
Phase F
↓
T73
```

逐阶段提交。

每个阶段完成后报告：

```text
changed files
tests added
tests passed
semantic decisions
known deviations
```

如果发现论文语义与当前 Runtime 架构冲突：

> **停止修改 Runtime，先报告冲突。**

不得为了让测试通过而自行修改论文语义。

最终目标不是：

> “把测试做绿”。

而是：

> **证明当前 GoCordis 实现确实满足论文定义下的 T59/T61/T63/T66/T73。**