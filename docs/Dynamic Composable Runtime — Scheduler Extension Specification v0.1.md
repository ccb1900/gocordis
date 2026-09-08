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
## Scheduler Extension Specification v0.1

**Status:** Implementation Specification  
**Target:** Code Agent  
**Dependency:** Kernel v0.1  
**Scope:** Scheduler Extension only  
**Out of Scope:** Event, Registry, HTTP, MCP, LLM, WASM, Persistence

---

# 1. Objective

Scheduler Extension 为 Runtime 提供：

> 在指定时间条件满足时，触发一次受控的任务执行。

Scheduler 解决的是：

```text
When should something run?
```

它不负责：

```text
What should run?
Why should it run?
How should it run?
What does the task do?
```

因此 Scheduler 不属于 Kernel。

---

# 2. 核心边界

必须严格区分：

```text
Kernel
    ↓
生命周期与依赖

Scheduler
    ↓
时间与触发

Component
    ↓
业务执行
```

Scheduler 不得自行实现：

- Fiber lifecycle
- Dependency resolution
- Provider registry
- Component loading
- Retry policy
- Business workflow
- Event Bus

---

# 3. Package

实现位置：

```text
dynamic-runtime/
└── extensions/
    └── scheduler/
```

必须满足：

```text
runtime
    ↑
scheduler
```

Kernel 不得 import：

```go
extensions/scheduler
```

Scheduler 可以依赖 Kernel API。

---

# 4. v0.1 调度模型

v0.1 只支持两种 Schedule：

```text
Once
Interval
```

不实现：

```text
Cron
Calendar
Timezone-aware calendar
Distributed scheduling
Persistent scheduling
Misfire policy
Priority queue API
```

这些必须留给后续版本。

---

# 5. Schedule API

定义：

```go
type Schedule interface {
    Next(after time.Time) (time.Time, bool)
}
```

v0.1 提供：

```go
type Once struct {
    At time.Time
}
```

以及：

```go
type Interval struct {
    Every time.Duration
}
```

`Every` 必须满足：

```text
Every > 0
```

否则返回明确错误。

---

# 6. Once Semantics

```text
Once{At: T}
```

语义：

> 当 Scheduler 时间达到 T 后，最多触发一次。

如果 Scheduler 在：

```text
T - ε
```

运行：

```text
wait
```

如果时间已经：

```text
>= T
```

则应立即进入执行流程。

Once 触发后：

```text
scheduled
    ↓
running
    ↓
completed
```

不会再次触发。

---

# 7. Interval Semantics

```text
Interval{Every: 1s}
```

语义：

> 按固定 duration 产生理论触发时间序列。

第一次理论触发时间：

```text
Start + Every
```

后续：

```text
Start + 2*Every
Start + 3*Every
...
```

必须基于**理论时间线**，而不是：

```text
previous actual execution completion + Every
```

因此：

```text
Every = 1s

tick1 = T+1
tick2 = T+2
tick3 = T+3
```

即使 tick1 执行耗时 500ms，也不能变成：

```text
T+1
T+2.5
T+4
```

---

# 8. Start Time

Interval 必须具有明确的起始时间。

推荐 API：

```go
type Interval struct {
    Start time.Time
    Every time.Duration
}
```

如果 `Start.IsZero()`：

> 使用 Schedule 被注册时的当前时间作为 Start。

但必须在注册 linearization point 确定 Start。

不能每次 Next 调用重新读取：

```go
time.Now()
```

作为起点。

---

# 9. Missed Interval Ticks

v0.1 明确采用：

> skip missed ticks

例如：

```text
Every = 1s

理论：
T1
T2
T3
T4
```

如果 Scheduler 因为暂停直到：

```text
T4 + 500ms
```

才恢复：

不得执行：

```text
T1
T2
T3
T4
```

作为四次 backlog execution。

只产生当前有效触发。

---

# 10. No Catch-up Storm

尤其禁止：

```text
Scheduler resumed
    ↓
10000 missed ticks
    ↓
spawn 10000 goroutines
```

Scheduler 必须保证：

> 时间落后不会造成无限补偿执行。

---

# 11. Task

Scheduler 调度的对象定义为：

```go
type Task func(context.Context) error
```

Scheduler 不得知道 Task 的业务含义。

---

# 12. Job

Schedule 与 Task 的绑定称为 Job。

建议：

```go
type JobID string

type Job struct {
    ID       JobID
    Schedule Schedule
    Task     Task
}
```

JobID 在一个 Scheduler 实例内唯一。

---

# 13. Add

核心 API：

```go
func (s *Scheduler) Add(job Job) error
```

重复 JobID：

```text
ErrJobExists
```

不得覆盖原 Job。

失败 Add 不得改变已有 Scheduler 状态。

---

# 14. Remove

```go
func (s *Scheduler) Remove(id JobID) error
```

不存在：

```text
ErrJobNotFound
```

Remove 的语义：

> 从未来调度集合中移除该 Job。

---

# 15. Remove 与 Running Task

如果：

```text
Job A
   ↓
Task A already running
```

此时：

```go
Remove(A)
```

不得强杀 Task A。

语义：

```text
Remove
    ↓
future execution cancelled
    ↓
current execution continues
```

如果希望 Task 停止，必须使用 Context cancellation。

---

# 16. Task Context

Task 必须收到 Context：

```go
Task func(context.Context) error
```

Scheduler 必须为每次 execution 创建独立 execution context。

不能复用前一次 execution context。

---

# 17. Execution Context Ownership

执行 Context 属于：

```text
one execution
```

生命周期：

```text
trigger
   ↓
create context
   ↓
Task
   ↓
return
   ↓
cancel context
```

不得跨 execution 重用。

---

# 18. Cancellation

Scheduler 必须支持：

```text
Job removal
Scheduler Close
```

触发 Task 的 cooperative cancellation。

禁止：

```text
runtime.Goexit
goroutine killing
unsafe termination
```

Scheduler 不能保证 Task 一定停止。

它只能：

```go
cancel()
```

并要求 Task 合作退出。

---

# 19. Task Concurrency

v0.1 必须定义：

> 同一个 Job 的 Task execution 不允许重叠。

例如：

```text
Every = 1s
Task duration = 3s
```

理论触发：

```text
T1
T2
T3
```

不能产生：

```text
Task1 running
Task2 running
Task3 running
```

而是：

```text
T1 → Task running

T2 → skipped
T3 → skipped
```

当 Task1 完成后，继续等待后续理论 tick。

---

# 20. Global Concurrency

不同 Job 可以并发执行：

```text
Job A → Task A
Job B → Task B
```

不得因为 A 正在执行而阻塞 B。

因此：

```text
per-job serialization
```

但：

```text
cross-job concurrency allowed
```

---

# 21. Overrun Semantics

例如：

```text
Every = 1s
Task = 3s
```

在 Task1 执行期间发生：

```text
T2
T3
```

均跳过。

Task1 完成后：

> 不执行已经错过的 T2/T3。

下一次执行使用理论时间线中下一个尚未过期的 tick。

---

# 22. Task Return Error

Task 返回 error：

```go
return err
```

Scheduler 不得自动：

```text
retry
```

也不得：

```text
remove job
```

除非 Job 自己被显式移除或 Scheduler 关闭。

v0.1：

```text
Task error ≠ Scheduler failure
```

---

# 23. Task Panic

Task panic 不得导致 Scheduler 主循环退出。

Scheduler 必须隔离 Task panic。

要求：

```text
Task panic
    ↓
execution ends
    ↓
Scheduler continues
```

如果实现提供 panic reporting，可以记录：

```text
panic value
stack
job id
```

但不得让 panic 穿透 Scheduler goroutine。

---

# 24. Task Result

v0.1 不要求同步等待 API。

Add 后 Scheduler 自主运行。

如果需要结果观察：

> 属于未来 Event/Execution History Extension。

不要为了 v0.1 增加复杂 result store。

---

# 25. Scheduler Lifecycle

Scheduler 自身不是 Kernel Fiber。

内部生命周期只有：

```text
Running
Closed
```

禁止引入：

```text
Pending
Loading
Active
Unloading
Failed
Gone
```

---

# 26. New

```go
func New() *Scheduler
```

New 后：

```text
Running
```

Scheduler 应该立即具备接受 Job 的能力。

---

# 27. Start

v0.1 不提供：

```go
Start()
```

`New()` 即可运行。

原因：

避免：

```text
New
Add
Start
```

这种额外状态机。

---

# 28. Add Before Execution

Scheduler 必须保证：

```text
Add(job)
```

返回成功后，Job 已经进入调度集合。

之后发生的 trigger 必须按照 linearization order 处理。

---

# 29. Remove Linearization

`Remove(id)` 必须具有明确 linearization point。

如果：

```text
Add
Remove
```

已经完成：

> 未来不得再启动新的 execution。

但如果 execution 已经启动：

> Remove 不负责强杀。

---

# 30. Add / Remove Concurrent

并发：

```text
Add(A)
Remove(A)
```

必须等价于某个合法线性化顺序：

```text
Add → Remove
```

或者：

```text
Remove → Add
```

不得产生无法解释的中间状态。

---

# 31. Trigger / Remove Race

如果：

```text
trigger(A)
Remove(A)
```

并发：

允许两种合法结果：

```text
trigger linearized first
    ↓
execution starts
    ↓
Remove
```

或者：

```text
Remove linearized first
    ↓
execution does not start
```

但一旦 execution 已经启动，Remove 不得声称可以取消已经执行的 Task。

---

# 32. Scheduler Close

```go
func (s *Scheduler) Close() error
```

必须幂等。

Close：

```text
Running
   ↓
Closed
```

Close 后：

```text
Add
Remove
```

都必须返回：

```text
ErrSchedulerClosed
```

---

# 33. Close Behavior

Close 必须：

1. 停止未来 trigger
2. cancel 所有正在运行的 Task Context
3. 不强杀 goroutine
4. 等待 Scheduler 内部 goroutine 退出

对于 Task：

> Scheduler 可以等待 Task 返回，但不得无限期假装已经 Closed。

---

# 34. Close Timeout

v0.1 提供：

```go
func (s *Scheduler) CloseContext(ctx context.Context) error
```

或者等价机制。

如果 Task 不响应 cancellation：

```text
CloseContext(ctx)
    ↓
ctx deadline
    ↓
return ctx.Err()
```

但 Scheduler 状态：

```text
仍然 Closed / Closing semantics 必须保持真实
```

不得因为超时谎报所有 goroutine 已经停止。

---

# 35. Recommended Close State

虽然公开生命周期只有 Running/Closed，但实现内部允许：

```text
Running
Closing
Closed
```

其中：

```text
Closing
```

表示：

> 已拒绝新 Job，正在等待内部资源退出。

如果 CloseContext 超时：

```text
Closing
```

可以继续存在。

不得：

```text
Closing → Closed
```

而实际 Task 仍然由 Scheduler 管理且尚未结束。

---

# 36. No Task Goroutine Killing

绝对禁止：

```text
kill goroutine
unsafe termination
runtime.Goexit on foreign goroutine
```

Scheduler 只能：

```go
cancel()
```

---

# 37. Timer Model

不得使用：

```text
time.Sleep()
```

作为 Scheduler 主循环的长期调度机制。

推荐：

```go
time.Timer
```

或者：

```go
time.NewTimer
```

根据最近 Job trigger 动态等待。

目的：

```text
Add/Remove
    ↓
wake scheduler
    ↓
recalculate next deadline
```

不能因为当前没有 Job 而永久睡眠导致 Add 后无法及时唤醒。

---

# 38. No Polling

禁止：

```go
for {
    time.Sleep(10 * time.Millisecond)
    scanJobs()
}
```

Scheduler 必须使用：

```text
timer + wake signal
```

而不是固定周期 polling。

---

# 39. Clock

Scheduler 使用：

```go
time.Time
time.Duration
```

时间计算必须集中管理。

如果为了测试注入 Clock：

```go
type Clock interface {
    Now() time.Time
    NewTimer(time.Duration) Timer
}
```

可以作为实现选择。

但不要为了测试引入复杂的时间框架。

---

# 40. Timer Precision

v0.1 不保证实时系统级精度。

允许：

```text
scheduled at T
actual start at T + δ
```

其中 δ 由：

- OS scheduling
- Go runtime
- Scheduler contention

决定。

Contract 只要求：

> 不在理论触发时间之前执行。

测试必须允许合理调度误差。

---

# 41. Monotonic Time

如果使用 Go `time.Time`，必须注意 Go monotonic clock 语义。

不要把：

```go
time.Now().UnixNano()
```

作为 Scheduler 内部唯一时间基准，以避免不必要的 wall-clock arithmetic 问题。

---

# 42. Wall Clock Changes

v0.1 不提供复杂 wall-clock adjustment semantics。

Schedule 计算基于 Scheduler 获取到的 `time.Time`。

测试不得假设跨 NTP 调整仍然具有绝对 wall-clock 保证。

未来如需要：

```text
monotonic scheduler
calendar scheduler
timezone scheduler
```

单独设计。

---

# 43. Memory Safety

Remove Job 后：

```text
Job
Task
Schedule
```

不得继续被 Scheduler 主循环无意义持有。

已运行 Task 的 execution context 除外。

---

# 44. No Global Scheduler

禁止：

```go
var DefaultScheduler *Scheduler
```

不得创建 package-level singleton。

---

# 45. Runtime Integration

Scheduler 可以由 Runtime Component 创建。

例如：

```text
Component Activation
      ↓
scheduler.New()
      ↓
ctx.Effect(
    install scheduler,
    inverse scheduler.Close
)
```

Scheduler 的关闭属于 Activation ownership。

---

# 46. Scheduler Provider

如果 Scheduler 作为 Runtime Capability：

```text
Provider Fiber
      ↓
Scheduler
      ↓
Consumer
```

Consumer Dependency 依赖的是：

```text
Scheduler capability
```

而不是：

```text
Job
```

Job 不得自动成为 Kernel Provider。

---

# 47. Job Churn

以下：

```text
Add Job
Remove Job
Add Job
```

不得改变 Scheduler Provider identity。

也不得导致依赖 Scheduler capability 的 Consumer：

```text
Active
→ Unloading
→ Loading
```

---

# 48. Runtime Provider Replacement

如果 Scheduler Provider Activation 被替换：

```text
P1 → P2
```

必须完全遵循 Kernel Provider identity replacement contract。

Scheduler Extension 不得自行实现 replacement semantics。

---

# 49. Ownership

必须明确：

```text
Runtime Activation owns Scheduler
```

则：

```text
Activation unload
    ↓
Scheduler.Close()
```

必须通过：

```go
ctx.Effect(...)
```

登记。

不要依赖：

```text
GC
finalizer
package shutdown
```

---

# 50. Error Model

至少定义：

```go
var (
    ErrSchedulerClosed = errors.New("scheduler closed")
    ErrJobExists        = errors.New("job already exists")
    ErrJobNotFound      = errors.New("job not found")
    ErrInvalidSchedule  = errors.New("invalid schedule")
)
```

具体错误包装可以使用：

```go
fmt.Errorf("...: %w", Err...)
```

调用方可以使用：

```go
errors.Is
```

判断。

---

# 51. Invalid Schedule

必须拒绝：

```text
Interval Every <= 0
```

以及无法产生有效 Next 时间的 Schedule。

例如：

```text
Next(after) -> invalid time / impossible result
```

实现应明确处理，而不是让 scheduler loop busy-loop。

---

# 52. Schedule Contract

对于合法 Schedule：

```go
next, ok := schedule.Next(after)
```

必须满足：

```text
ok == true
next > after
```

对于已经结束的 Schedule：

```text
ok == false
```

Once：

```text
Next(after >= At) => false
```

Interval：

```text
Next(after) > after
```

并且返回理论时间线上的下一个 tick。

---

# 53. Scheduler Main Loop

推荐逻辑：

```text
loop
  ↓
lock
  ↓
find earliest next trigger
  ↓
unlock
  ↓
wait timer OR wake signal OR close
  ↓
wake
  ↓
lock
  ↓
determine due jobs
  ↓
advance theoretical schedule
  ↓
unlock
  ↓
start eligible executions
  ↓
repeat
```

禁止在 global scheduler lock 下执行 Task。

---

# 54. Critical Lock Rule

绝对禁止：

```text
Scheduler mutex
    ↓
Task(ctx)
```

也禁止：

```text
Scheduler mutex
    ↓
Task cleanup callback
```

用户 Task 永远不能在 Scheduler global lock 内执行。

---

# 55. Per-Job Execution State

每个 Job 至少需要能够判断：

```text
scheduled
running
next theoretical trigger
```

实现可以使用其他内部结构。

禁止让 Job 自身成为第二套 Fiber lifecycle。

---

# 56. Race Safety

必须通过：

```bash
go test -race ./...
```

重点覆盖：

```text
Add × Remove
Add × Close
Remove × Close
Publish/Trigger × Remove
Trigger × Close
multiple Jobs concurrently
same Job overrun
```

---

# 57. Contract Tests

必须实现：

### S1 — Once

```text
Once(T)
→ exactly one execution
```

### S2 — Interval

```text
Every(10ms)
→ repeated execution
```

允许 timing tolerance。

### S3 — Interval Theoretical Timeline

Task 人为 sleep：

```text
Every = 20ms
Task = 50ms
```

证明：

```text
execution intervals
```

不是：

```text
previous completion + Every
```

### S4 — Overrun Skip

同一 Job 不允许 execution overlap。

### S5 — Job Isolation

Job A running：

```text
Job B
```

仍可以执行。

### S6 — Add

Add 成功后 Job 可以被触发。

### S7 — Duplicate Add

重复 JobID：

```text
ErrJobExists
```

且原 Job 不受影响。

### S8 — Remove

Remove 后：

```text
no future execution
```

### S9 — Remove Running Job

Running Task：

```text
Remove
```

Task 不被强杀，但其 Context 被取消。

### S10 — Scheduler Close

Close 后：

```text
no future execution
Add -> ErrSchedulerClosed
Remove -> ErrSchedulerClosed
```

### S11 — Close Idempotence

```text
Close()
Close()
Close()
```

必须安全。

### S12 — Task Panic Isolation

一个 Task panic：

```text
Scheduler continues
```

### S13 — Concurrent Add/Remove

高并发：

```text
no race
no lost internal state
```

### S14 — Trigger/Remove Race

必须符合合法线性化语义。

### S15 — Runtime Ownership

Runtime Activation unload：

```text
Scheduler.Close()
```

并最终停止未来 trigger。

### S16 — Provider Stability

Job churn：

```text
Add
Remove
Add
```

不改变 Scheduler Provider identity。

### S17 — Provider Replacement

Scheduler Provider replacement 遵循 Kernel identity replacement。

### S18 — CloseContext Timeout

Task 不响应 cancellation：

```text
CloseContext(timeout)
→ ctx.Err()
```

但 Scheduler 不得虚假宣称内部任务已经停止。

---

# 58. Property Tests

至少实现：

### P1 — No Overlap

对于任意 Job：

```text
activeExecutions <= 1
```

始终成立。

### P2 — Job Isolation

一个 Job 的执行状态不会阻塞其他 Job。

### P3 — Monotonic Theoretical Schedule

对于 Interval：

```text
next1 < next2 < next3
```

理论 trigger 永远递增。

### P4 — Remove Safety

Remove linearized 后，不得启动新的 execution。

### P5 — Close Safety

Close linearized 后，不得启动新的 execution。

### P6 — Panic Isolation

任意 Task panic 不破坏其他 Job scheduling。

### P7 — No Goroutine Explosion

Overrun Job 不得因为 missed ticks 创建与 missed tick 数量线性增长的 goroutine backlog。

### P8 — Idempotent Close

任意：

```text
Close^N
```

均保持一致最终状态。

---

# 59. Timing Test Rules

时间相关测试禁止：

```text
assert exact nanosecond
```

不得写：

```go
if elapsed != 100*time.Millisecond
```

应使用：

```text
lower bound
upper tolerance
eventual execution
```

并避免过短 duration 导致 flaky test。

---

# 60. Deterministic Testing

如果实现 Clock abstraction：

> Contract tests 应优先使用 deterministic clock 验证 Schedule 数学语义。

真实时间测试只验证：

```text
end-to-end integration
```

不要依赖真实 sleep 验证所有属性。

---

# 61. Forbidden Features

v0.1 禁止：

```text
❌ Cron
❌ timezone scheduling
❌ calendar rules
❌ persistent jobs
❌ distributed scheduler
❌ leader election
❌ retry
❌ exponential backoff
❌ priority
❌ dependency-aware scheduling
❌ Event-driven scheduling
❌ wildcard schedule
❌ arbitrary predicate trigger
❌ job history
❌ execution persistence
❌ global singleton
❌ polling
❌ goroutine killing
❌ lock-held Task execution
❌ second Fiber lifecycle
❌ Kernel modification
```

尤其禁止自行加入：

```text
Event → Scheduler
```

的隐式集成。

Event-triggered scheduling 属于未来组合层。

---

# 62. Acceptance Criteria

只有同时满足以下条件才算 PASS：

```text
[ ] Scheduler 独立于 Kernel
[ ] Kernel 不 import Scheduler
[ ] Once semantics 明确
[ ] Interval semantics 明确
[ ] Interval 使用理论时间线
[ ] missed tick 不 catch-up
[ ] 不产生 catch-up storm
[ ] 同一 Job execution 不重叠
[ ] 不同 Job 可以并发
[ ] Remove 不强杀 running Task
[ ] Remove 取消未来 execution
[ ] Task 使用独立 Context
[ ] cancellation cooperative
[ ] Task panic 不破坏 Scheduler
[ ] Task error 不自动 retry
[ ] Add/Remove/Close 具有线性化语义
[ ] Close 幂等
[ ] Close 后拒绝新 Job
[ ] Close 可以取消 running Task
[ ] Close timeout 不虚假报告完成
[ ] 不使用 polling
[ ] 不使用 global singleton
[ ] 不在 global lock 中运行 Task
[ ] Runtime ownership 使用 Effect
[ ] Job churn 不改变 Scheduler Provider identity
[ ] Provider replacement 遵循 Kernel
[ ] no Kernel modification
[ ] go test ./... PASS
[ ] go test -race ./... PASS
[ ] go vet ./... PASS
```

---

# 63. Completion Report

完成后只报告 Scheduler Extension。

必须使用：

```text
SCHEDULER EXTENSION

PASS / CONDITIONAL PASS / FAIL

1. 修改文件
2. 新增 API
3. Schedule semantics
4. Once semantics
5. Interval semantics
6. Overrun semantics
7. Concurrency semantics
8. Cancellation semantics
9. Close semantics
10. Runtime integration
11. Contract tests S1–S18
12. Property tests P1–P8
13. Kernel 是否修改
14. go test ./...
15. go test -race ./...
16. go vet ./...
17. 未解决问题
```

如果存在 P0/P1 semantic conflict，必须明确报告。

完成 Scheduler Extension 后停止，不进入下一个 Extension。