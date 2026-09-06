# Dynamic Composable Runtime Kernel Review

你现在不是开发者，而是这个项目的 **Principal Runtime Engineer / Architecture Reviewer**。

项目已经按照既定的 Dynamic Composable Runtime 规范完成第一版实现。

你的任务不是重新设计架构，也不是为了让代码“看起来更漂亮”，而是：

> **验证当前实现是否真正满足 Runtime Contract，并找出实现与 Contract 之间的偏差。**

---

## 一、Review 原则

严格遵守：

1. 不重新设计整体架构。
2. 不引入新的核心抽象，除非证明现有实现无法满足 Contract。
3. 不因为测试难写就降低语义要求。
4. 不使用 sleep、retry、全局锁等方式掩盖生命周期/并发问题。
5. 不把实现细节当成 Contract。
6. 如果发现规范和实现冲突，优先指出冲突，不要擅自选择一个。
7. Review 阶段原则上不进行大规模重构。
8. 所有结论必须能够定位到：
   - 具体代码
   - 具体测试
   - 或缺失的测试
9. 特别关注并发、生命周期和资源所有权问题。
10. 不要为了“通过测试”修改测试来迎合当前实现。

---

# 二、首先阅读整个项目

先完整理解：

- go.mod
- package structure
- Runtime 核心代码
- Fiber
- Activation
- Context
- Effect
- Provider
- Dependency
- Ownership
- Orchestrator / Coordinator
- Lifecycle
- Runtime Close
- 所有现有测试

然后建立一张：

```text
Package
  ↓
Type
  ↓
Responsibility
  ↓
Lifecycle relation
  ↓
Concurrency boundary
```

不要马上修改代码。

---

# 三、核心 Contract

## 1. Fiber / Activation / Context

必须满足：

```text
Fiber
  = 长生命周期逻辑对象

Activation
  = 一次具体激活周期

Context
  = Activation 私有上下文
```

要求：

- Fiber 可以经历多次 Activation。
- 每次 Activation 都创建新的 Context。
- Context 不得跨 Activation 复用。
- Async completion 必须能够识别属于哪个 Activation。
- stale completion 不得修改当前 Fiber 状态。

重点检查：

```text
FiberID
ActivationID
Context lifetime
completion routing
```

---

# 四、Lifecycle Contract

Fiber 状态：

```text
Pending
Loading
Active
Unloading
Failed
Gone
```

Desired Intent：

```text
Mounted
Unmounted
```

核心状态转换：

```text
Pending + Mounted + dependency unsatisfied
    → Pending

Pending + Mounted + dependency satisfied
    → Loading

Pending + Unmounted
    → Gone

Loading + Mounted + satisfied
    → Active

Loading + dependency lost
    → Unloading

Loading + Unmounted
    → Unloading

Active + Mounted + satisfied
    → Active

Active + dependency lost
    → Unloading

Active + Unmounted
    → Unloading

Unloading + Mounted + satisfied
    → Loading

Unloading + Mounted + unsatisfied
    → Pending

Unloading + Unmounted
    → Gone

Failed + Mounted + satisfied
    → 不自动 Retry

Failed + Unmounted
    → Gone

Gone + Mounted + satisfied
    → Loading

Gone + Mounted + unsatisfied
    → Pending
```

检查实现是否真正符合上述语义。

特别检查：

> Fiber 状态是否只能由 Runtime Lifecycle Coordinator 修改。

Component 不得直接修改 Fiber 状态。

---

# 五、Lifecycle Concurrency

必须满足：

```text
同一个 Fiber：
最多同时存在一个 lifecycle transition。

不同 Fiber：
可以并发 transition。
```

必须检查：

```text
Load()
Dispose()
Load()
Dispose()
```

以及：

```text
Load()
    ↓
Loading
    ↓
Dispose()
    ↓
Apply completes
```

要求存在明确的 linearization point。

不能出现：

```text
Apply 和 Inverse 同时运行
```

也不能：

```text
Dispose()
导致 Fiber Gone
之后旧 Apply completion
又把 Fiber 变成 Active
```

重点寻找 stale completion bug。

---

# 六、Effect Contract

Effect 是 Runtime 最重要的正确性机制之一。

检查：

```go
Effect(install func() (Effect, error)) error
```

必须满足：

### 1. Install 成功

```text
reserve
  ↓
install
  ↓
commit effect
```

### 2. Install 失败

```text
reserve
  ↓
install error
  ↓
没有 effect committed
```

### 3. Context 正在 Unwind

如果：

```text
Effect install
```

执行过程中 Context 已经开始 unwind：

不能把这个 Effect 留在 Context 中。

必须：

```text
install complete
    ↓
发现 Context 已经 unwinding
    ↓
立即执行 inverse
```

### 4. Exactly Once

一个 Effect 的 inverse：

```text
只能执行一次
```

不能：

```text
double inverse
```

### 5. LIFO

如果：

```text
Effect A
Effect B
Effect C
```

必须：

```text
C inverse
B inverse
A inverse
```

---

# 七、Apply Failure

检查：

```text
Apply
 ├── Effect A committed
 ├── Effect B committed
 ├── Effect C failed
```

必须：

```text
C
B inverse
A inverse
```

然后：

```text
Fiber → Failed
```

不得泄漏 Runtime-managed resources。

---

# 八、Cleanup Error

如果：

```text
C inverse → error
B inverse → error
A inverse → success
```

不能：

```text
C error
    ↓
停止 cleanup
```

而必须：

```text
C inverse
B inverse
A inverse
    ↓
aggregate errors
```

检查错误聚合实现。

---

# 九、Provider Contract

Provider identity 必须能够区分不同 Activation。

逻辑上必须类似：

```text
ProviderIdentity {
    FiberID
    ActivationID / Generation
}
```

Dependency snapshot 必须记录：

```text
provider key
provider identity
```

不能只记录：

```text
provider exists
```

---

# 十、Provider Replacement

对于 exclusive capability：

```text
Provider A Active
```

此时：

```text
Provider B Load
```

不得直接覆盖 A。

必须：

```text
A
↓
Unloading
↓
Gone

B
↓
Loading
↓
Active
```

特别检查：

> A 的旧 cleanup 是否可能误删 B 新建的 Provider registration。

必须使用 identity validation。

---

# 十一、Dependency Withdrawal

这是整个 Runtime 最容易出错的地方。

如果：

```text
Provider P
    ↑
Consumer A
    ↑
Consumer B
```

P 消失时不能：

```text
P → Gone
```

然后才处理 A/B。

必须保证：

```text
B → Unloading
A → Unloading
P → Unloading
```

也就是说：

> Provider activation 在进入最终撤销阶段之前，依赖它的 Active/Loading consumers 必须已经进入撤销流程，不能继续进入 Active。

检查多级依赖：

```text
P ← A ← B
```

以及：

```text
P ← A
P ← B
```

以及多个依赖同时消失。

强依赖循环在 v0.1 中应当被禁止。

---

# 十二、Ownership

Dependency Graph 与 Ownership Graph 是两张不同的图。

检查：

```text
Parent owns Child
```

是否明确记录。

要求：

```text
Parent Gone
```

之前：

```text
Owned Child → Gone
```

Context.Child 本身不能自动等价于 Fiber ownership，除非项目明确实现了这种语义。

---

# 十三、Stale Completion

重点审查所有异步 completion。

必须至少能够识别：

```text
FiberID
ActivationID
```

例如：

```text
Fiber A
Activation 1
    ↓
Apply async

Activation 1 unwind
    ↓
Activation 2
    ↓
Apply async
```

此时 Activation 1 completion 到达：

```text
必须被忽略
```

不能影响 Activation 2。

搜索所有：

```text
goroutine
channel
callback
completion
future
async
```

确认没有 stale completion 漏洞。

---

# 十四、Runtime Close

Runtime：

```text
Running
  ↓
Closing
  ↓
Closed
```

要求：

### Running

允许 Load。

### Closing

拒绝新的 Load。

已有 Apply：

```text
cooperative cancellation
```

Active Fiber：

```text
Unload
```

最后：

```text
drain coordinator
```

才能：

```text
Closed
```

不能通过：

```text
close(command channel)
```

导致晚到 completion panic。

不能通过 timeout：

```text
直接把 Runtime 标记成 Closed
```

如果 timeout 但仍有工作：

```text
Runtime = Closing
```

而不是虚假的 Closed。

---

# 十五、Mutex / User Code

这是必须重点检查的项目。

Runtime mutex 内：

```text
禁止调用：

Component.Apply()
Component.Inverse()
user callback
user code
external IO
```

正确结构应该类似：

```text
lock
read/update internal state
unlock

call user code

lock
process result
unlock
```

检查所有 Runtime mutex 的持有范围。

---

# 十六、Dispose

Dispose 必须：

```text
idempotent
```

即：

```text
Dispose()
Dispose()
Dispose()
```

不会：

- double inverse
- double unregister
- 状态损坏
- panic

并检查：

```text
Load() concurrent with Dispose()
```

是否存在明确的线性化结果。

---

# 十七、Required Tests

检查当前测试是否覆盖：

### Preservation

所有 committed Effect 在正常生命周期结束后都被 inverse。

### Recovery Exactness

```text
Load
→ Active
→ dependency lost
→ Gone
→ dependency returns
→ Active
```

状态和 Effect 数量正确。

### Ordering

验证：

```text
Effect C
Effect B
Effect A
```

inverse：

```text
C
B
A
```

### Progress

没有因为内部状态机错误导致：

```text
Pending 永久 Pending
Loading 永久 Loading
Unloading 永久 Unloading
```

### Confluence

对合法操作序列：

```text
Load A
Load B
Dispose A
Dispose B
```

以及不同交错顺序进行验证。

注意：

> Confluence 只要求 Runtime-managed observable state 一致。

### Duplicate Provider

第二个 exclusive provider 失败：

```text
Provider A remains Active
```

### Provider Replacement

验证：

```text
A gone
B active
```

并验证旧 A cleanup 不会删除 B。

### Dependency Recovery

```text
Provider disappears
Consumer withdraws
Provider returns
Consumer recovers
```

### Idempotent Dispose

多次 Dispose。

### Child Ownership

Parent Dispose：

```text
Child Gone
↓
Parent Gone
```

### Race

必须通过：

```bash
go test -race ./...
```

---

# 十八、你必须实际执行

至少执行：

```bash
go test ./...
go test -race ./...
go vet ./...
```

如果项目已有 lint：

```bash
golangci-lint run
```

不要只静态阅读。

---

# 十九、测试缺口

如果 Contract 有要求但没有测试：

不要立刻大规模修改实现。

先建立：

```text
Contract
    ↓
Current Implementation
    ↓
Existing Test
    ↓
Missing Test
    ↓
Risk
```

并为每个缺口给出：

```text
P0 = correctness / lifecycle / race
P1 = important semantic gap
P2 = maintainability / robustness
```

---

# 二十、最终输出格式

最终不要输出泛泛而谈的“代码整体不错”。

必须输出：

## 1. Executive Summary

```text
PASS / CONDITIONAL PASS / FAIL
```

并说明最关键的原因。

## 2. Critical Findings

表格：

| ID | Severity | Contract | Location | Problem | Consequence |
|----|----------|----------|----------|---------|-------------|

## 3. Lifecycle Review

逐项：

```text
Fiber/Activation/Context       PASS/FAIL
Lifecycle state machine        PASS/FAIL
Stale completion               PASS/FAIL
Effect exactly-once            PASS/FAIL
Effect LIFO                    PASS/FAIL
Apply rollback                 PASS/FAIL
Provider identity              PASS/FAIL
Provider replacement           PASS/FAIL
Dependency withdrawal          PASS/FAIL
Ownership                      PASS/FAIL
Dispose                        PASS/FAIL
Runtime Close                  PASS/FAIL
Concurrency                    PASS/FAIL
```

## 4. Test Coverage Matrix

| Contract | Existing Test | Missing Test | Risk |
|----------|---------------|--------------|------|

## 5. Race / Concurrency Findings

单独列出。

## 6. Required Fixes

按照：

```text
P0
P1
P2
```

排序。

## 7. Recommended Next Step

只能给出下一阶段建议，不要自行开始实现 Registry。

---

# 二十一、重要

除非发现明确的 P0 correctness bug：

**本轮不要修改核心架构。**

如果发现 P0：

1. 指出问题。
2. 给出最小修复方案。
3. 添加能够复现问题的测试。
4. 修复。
5. 重新运行：

```bash
go test ./...
go test -race ./...
go vet ./...
```

如果没有 P0：

**不要重构。**

Review 完成后停止，等待下一步指令。

最终目标不是让代码“看起来优秀”，而是证明：

> **这个 Runtime Kernel 的生命周期、依赖、Effect、Ownership 和并发语义，在真实 Go 实现中成立。**