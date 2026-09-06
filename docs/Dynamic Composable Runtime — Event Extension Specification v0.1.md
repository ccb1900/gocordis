# Dynamic Composable Runtime
## Event Extension Specification v0.1

**Status:** Implementation Specification  
**Target:** Code Agent  
**Dependency:** Kernel v0.1, Registry Extension v0.1  
**Scope:** Event Extension only  
**Out of Scope:** Kernel modification, Registry modification, Scheduler, HTTP, MCP, LLM, WASM

---

# 1. Objective

Event Extension 为 Runtime 提供通用的事件发布与订阅能力。

它解决的是：

> 一个组件产生某个事实/事件，其他组件可以按照事件类型订阅并接收通知。

Event Extension **不是 Kernel 生命周期机制**，也不是 Dependency Provider 本身。

Kernel 负责：

- Fiber
- Activation
- Context
- Effect
- Provider
- Dependency
- Ownership
- Lifecycle

Event Extension 负责：

- Event
- Topic / Event Type
- Publish
- Subscribe
- Subscription lifecycle
- Delivery semantics
- Backpressure policy
- Event ordering semantics

---

# 2. 核心边界

必须严格区分以下三个概念：

```text
Kernel Dependency
    ↓
“我需要这个能力存在”

Registry
    ↓
“我需要一个动态成员集合”

Event
    ↓
“某件事情发生了，请通知我”
```

因此：

```text
Dependency ≠ Event
Registry Watch ≠ Event Bus
Event Subscription ≠ Runtime Dependency
```

尤其禁止：

> 用 Event 来模拟 Dependency。

例如：

```text
Provider disappeared
    ↓
publish "provider_removed"
    ↓
consumer 收到事件
    ↓
consumer 自己决定 Unload
```

这是错误的。

Provider disappearance 必须由 Kernel Dependency 机制处理。

---

# 3. Event Extension 不属于 Kernel

Event Package 必须位于：

```text
dynamic-runtime/
└── extensions/
    └── event/
```

Kernel 不得 import Event Extension。

依赖方向必须保持：

```text
                 ┌──────────────┐
                 │    Kernel    │
                 └──────────────┘
                        ↑
                        │
              ┌─────────┴─────────┐
              │                   │
        Registry Extension   Event Extension
```

Event Extension 可以使用 Kernel API。

Kernel 不得依赖：

```go
extensions/event
```

---

# 4. Event 的定义

Event 是一个已经发生的事实。

最小模型：

```go
type Event struct {
    Type      string
    Payload   any
    Timestamp time.Time
}
```

但实现不得依赖 `Timestamp` 实现排序语义。

必须额外具有 Extension 内部唯一序号：

```go
type Sequence uint64
```

内部事件记录至少包含：

```go
type eventRecord struct {
    sequence Sequence
    event    Event
}
```

`Sequence` 用于定义同一 Event Bus 实例中的发布顺序。

---

# 5. Event Type

`Type` 是事件类型标识。

v0.1 使用：

```go
type EventType string
```

例如：

```text
"device.connected"
"device.disconnected"
"job.completed"
"can.frame.detected"
```

Event Type 是普通数据标识，不是 Kernel Capability Key。

禁止：

```text
EventType = Provider Key
```

二者语义必须保持独立。

---

# 6. Payload

Event Payload 类型：

```go
any
```

Event Bus 不负责：

- 序列化
- schema validation
- persistence
- deep copy
- schema evolution

因此：

> Payload 的并发安全与生命周期由 Publisher / Subscriber 共同负责。

Event Bus 不得声称 Payload 自动 immutable。

---

# 7. Event Bus

核心对象：

```go
type Bus struct
```

构造：

```go
func New() *Bus
```

核心 API：

```go
func (b *Bus) Publish(event Event) error
```

订阅：

```go
func (b *Bus) Subscribe(
    eventType EventType,
) (*Subscription, error)
```

Subscription：

```go
type Subscription struct {
    // internal
}

func (s *Subscription) Events() <-chan Event
func (s *Subscription) Close() error
```

`Close()` 必须幂等。

---

# 8. Subscription 生命周期

Subscription 不属于 Kernel Fiber 生命周期。

它自身不是第二套 Runtime Lifecycle。

其状态仅允许实现内部使用：

```text
Active
Closed
```

这是一个资源状态，而不是 Runtime Fiber lifecycle。

禁止引入：

```text
Pending
Loading
Unloading
Failed
Gone
```

等 Kernel 状态。

---

# 9. Publish 语义

一次：

```go
Publish(event)
```

必须具有明确的 linearization point。

成功 Publish 后：

> Event 已进入 Event Bus 的可见事件序列。

Event Bus v0.1 不要求 durable persistence。

因此：

```text
Publish success
```

不代表：

```text
Event 永久保存
```

也不代表：

```text
所有 Subscriber 都已经处理完成
```

---

# 10. Delivery 语义

v0.1 使用：

> best-effort asynchronous delivery

Publish 不直接执行 Subscriber 用户代码。

禁止：

```go
subscriber.callback(event)
```

在 Publish 临界区中执行。

推荐模型：

```text
Publish
   ↓
lock
   ↓
确定 sequence
确定当前 subscribers
   ↓
unlock
   ↓
非阻塞/异步 delivery
```

---

# 11. Subscriber 不允许阻塞 Publisher

这是 Event Extension 的核心要求。

慢 Subscriber 不得阻塞：

```go
Publish()
```

因此 Subscription 必须具有有界缓冲区。

默认：

```text
buffer = 64
```

可以允许构造参数配置，但必须存在有限上界。

禁止无限增长。

---

# 12. Backpressure

v0.1 明确定义：

> 慢消费者允许丢事件。

因此，当 Subscription buffer 已满：

```text
Publish
    ↓
Subscription buffer full
    ↓
drop event for this subscription
```

不得：

```text
block publisher
```

不得：

```text
无限扩容
```

不得：

```text
启动无限 goroutine
```

---

# 13. Event Loss

事件丢失必须是：

> per-subscription loss

例如：

```text
Publisher
   │
   ├── S1 → received
   │
   ├── S2 → dropped
   │
   └── S3 → received
```

不能因为 S2 慢而导致 S1/S3 无法接收。

---

# 14. Ordering

对于单个 Subscription：

> 成功进入该 Subscription delivery queue 的事件必须保持 Publish sequence 顺序。

例如：

```text
Publish A seq=1
Publish B seq=2
Publish C seq=3
```

Subscription 不得观察：

```text
A C B
```

但如果：

```text
A delivered
B dropped
C delivered
```

则允许：

```text
A C
```

因为 B 已经丢失。

---

# 15. 全局 Ordering

v0.1 不提供跨 Subscription 的全局 delivery order。

例如：

```text
S1: A → B
S2: B → A
```

在不同 Subscription 上允许存在。

唯一保证：

```text
单 Subscription
    ↓
delivery queue 顺序保持 Publish sequence
```

---

# 16. Concurrent Publish

多个 goroutine 可以同时：

```go
Publish()
```

Event Bus 必须为它们建立明确的线性化顺序。

例如：

```text
G1 Publish(A)
G2 Publish(B)
```

最终必须存在：

```text
A seq=10
B seq=11
```

或者：

```text
B seq=10
A seq=11
```

不能出现：

```text
A/B partial visibility
```

即一次 Publish 不得被其他 Publish 观察为半完成状态。

---

# 17. Subscribe Linearization

Subscribe 必须具有明确的线性化点。

Subscribe 之后发生的 Publish：

> 必须按照定义的线性化顺序决定是否进入该 Subscription。

例如：

```text
Publish A
Subscribe S
Publish B
```

如果 Subscribe 的 linearization point 位于 A 和 B 之间：

```text
S receives B
S does not receive A
```

不能出现：

```text
随机收到 A
```

---

# 18. Subscribe 与 Publish 并发

以下操作：

```text
G1: Subscribe(S)
G2: Publish(A)
```

最终必须等价于某个合法线性化顺序：

```text
Subscribe → Publish
```

或者：

```text
Publish → Subscribe
```

不得产生第三种不可解释状态。

---

# 19. Close Subscription

```go
sub.Close()
```

必须：

1. 幂等
2. 最终关闭 `Events()` channel
3. 不再向 channel 发送事件
4. 不阻塞 Publisher
5. 不导致其他 Subscription 失效

允许：

```text
Close()
↓
已经进入 queue 的 event 被丢弃
↓
channel close
```

v0.1 不要求 Drain。

---

# 20. Close 与 Publish 并发

以下：

```text
G1: Publish(A)
G2: sub.Close()
```

必须具有合法线性化结果。

允许：

```text
A received
```

或者：

```text
A dropped because subscription already closed
```

但禁止：

```text
send on closed channel panic
```

必须通过明确的内部状态/同步机制解决。

---

# 21. Bus Close

Bus 必须提供：

```go
func (b *Bus) Close() error
```

语义：

```text
Running
   ↓
Closed
```

Close 幂等。

Close 后：

```go
Publish()
```

必须返回明确错误：

```go
ErrBusClosed
```

Subscribe 同样必须失败：

```go
ErrBusClosed
```

---

# 22. Bus Close 的 Subscriber 行为

Bus Close 必须关闭所有 Subscription。

因此：

```text
Bus.Close()
    ↓
all subscriptions closed
    ↓
Events() eventually closes
```

Bus Close 不得等待用户消费完事件。

剩余 queue 可以直接丢弃。

---

# 23. Subscription Ownership

如果 Event Bus 被 Runtime Component 创建：

```text
Component Activation
       │
       └── Event Bus
```

则 Bus 必须通过：

```go
ctx.Effect(...)
```

登记资源清理。

例如语义：

```text
install Bus
inverse Bus.Close
```

Subscription 同理。

禁止依赖：

```text
runtime finalizer
GC
```

保证 Runtime-managed resource cleanup。

---

# 24. Event Bus 作为 Runtime Capability

Event Bus 可以作为稳定 Provider：

```text
Provider Fiber
      ↓
Event Bus
      ↓
Consumer
```

此时 Consumer Dependency 的语义是：

> 我需要 Event Bus capability。

而不是：

> 我订阅了某个 EventType。

因此：

```text
Subscribe("device.connected")
```

不是 Kernel Dependency。

---

# 25. Event Bus Provider Replacement

如果提供 Event Bus 的 Fiber 被替换：

```text
Provider identity:
    P1 → P2
```

Consumer 的 Kernel Dependency 必须按照 Kernel Provider identity/generation 规则重新激活。

Event Extension 不得绕过这一机制。

---

# 26. Subscription 不改变 Provider Identity

以下：

```text
Subscribe(A)
Subscribe(B)
Close(S1)
Subscribe(C)
```

不得改变：

```text
ProviderIdentity
```

也不得导致依赖 Event Bus 的 Consumer：

```text
Active
→ Unloading
→ Loading
```

Subscription churn 是 Event 内部资源变化。

---

# 27. Event Bus 与 Registry Watch

两者必须保持明确区别。

Registry Watch：

```text
Registry member changed
```

特点：

- 绑定特定 Registry
- 事件来源固定
- 服务于 Registry member observation
- 可以丢事件
- Snapshot + changes

Event Bus：

```text
general event happened
```

特点：

- 通用
- EventType 路由
- 多 Publisher
- 多 Subscriber

禁止把：

```go
Registry.Subscribe()
```

简单 alias 成 Event Bus。

---

# 28. Event Schema

v0.1 不实现：

- JSON Schema
- Protobuf schema
- Avro
- schema registry
- version negotiation

这些属于未来 Extension。

Event Type 字符串不承担 schema registry 职责。

---

# 29. Persistence

v0.1 不实现：

- WAL
- disk persistence
- replay
- event sourcing
- durable queue

因此：

```text
Process restart
```

之后事件全部丢失是允许的。

---

# 30. Replay

禁止声称：

```go
Subscribe()
```

可以收到历史事件。

v0.1：

```text
Subscribe
    ↓
future events only
```

如果未来实现 replay，必须作为独立语义明确设计。

---

# 31. Wildcard Subscription

v0.1 不实现 wildcard。

例如：

```text
device.*
*
```

均不支持。

Subscription 必须精确匹配：

```go
Subscribe("device.connected")
```

只接收：

```text
device.connected
```

---

# 32. Filtering

v0.1 不提供任意 Predicate：

```go
Subscribe(func(Event) bool)
```

原因：

- 用户代码执行位置不明确
- backpressure 语义复杂
- 锁与回调风险
- ordering 更复杂

如果需要过滤，未来设计 Filter Extension。

---

# 33. User Callback

v0.1 使用 channel：

```go
Events() <-chan Event
```

不支持：

```go
Subscribe(func(Event))
```

因此 Event Bus 不执行 Subscriber 用户代码。

---

# 34. Panic Isolation

Event Bus 本身不执行用户 callback，因此不存在：

```text
subscriber callback panic
```

如果未来增加 callback API，必须单独定义 panic isolation contract。

v0.1 禁止提前实现。

---

# 35. Payload Ownership

Event Bus 不复制：

```go
Payload
```

因此 Publisher 不得在 Publish 返回后修改一个 Subscriber 仍然使用的共享可变对象，除非其自身提供并发安全。

Event Bus 不对此负责。

测试不得错误要求：

```text
deep immutable payload
```

---

# 36. Memory Safety

必须避免：

```text
subscription leak
```

Subscription 只能通过：

```go
Close()
```

释放。

v0.1 不实现自动 GC subscription。

如果用户忘记 Close：

> Subscription 可以长期存在。

这属于使用方 ownership 问题。

---

# 37. No Global Singleton

禁止：

```go
var GlobalBus *Bus
```

禁止 package-level singleton。

必须显式：

```go
bus := event.New()
```

然后通过 Kernel Capability 或组件依赖传递。

---

# 38. No Polling

禁止：

```go
for {
    if bus.HasEvent(...) {
        ...
    }
}
```

Event delivery 必须基于 Subscription channel。

---

# 39. Locking

严格禁止：

> 持有 Event Bus mutex 时执行用户代码。

v0.1 因为没有 callback API，天然满足这一点。

Channel send 也不得因为慢 Subscriber 而持有 Bus global lock。

推荐：

```text
lock
  snapshot subscribers
  assign sequence
unlock

deliver
```

或者其他等价设计。

---

# 40. Race Safety

以下必须通过：

```bash
go test -race ./...
```

尤其覆盖：

```text
Publish × Publish
Publish × Subscribe
Publish × Close(subscription)
Publish × Bus.Close
Subscribe × Bus.Close
Close × Close
```

---

# 41. Error Model

至少定义：

```go
var (
    ErrBusClosed      = errors.New("event bus closed")
    ErrSubscriptionClosed = errors.New("subscription closed")
)
```

`Close()` 的幂等性意味着：

```go
err := Close()
```

第二次调用可以返回：

```go
nil
```

推荐：

```text
Close → nil
repeated Close → nil
Publish after Bus.Close → ErrBusClosed
Subscribe after Bus.Close → ErrBusClosed
```

---

# 42. Snapshot Semantics

Event Bus v0.1 不提供 Event Snapshot。

不要为了“完整性”增加：

```go
Events()
Current()
Snapshot()
Replay()
```

这些会把 Event Bus 推向 durable/event-store 语义。

v0.1 保持简单：

```text
future notification only
```

---

# 43. Contract Tests

必须实现以下 Contract Tests。

### E1 — Basic Publish

```text
Subscribe(T)
Publish(T)
→ receives exactly one event
```

### E2 — Type Isolation

```text
Subscribe(A)
Publish(B)
→ S does not receive B
```

### E3 — Multiple Subscribers

```text
S1(A)
S2(A)
Publish(A)
→ both receive A
```

### E4 — Slow Subscriber Isolation

```text
S1 buffer full
S2 available
Publish(A)
→ S2 still receives
→ Publish does not block
```

### E5 — Subscription Ordering

```text
Publish A
Publish B
Publish C

→ S observes A B C
```

在未发生丢弃的前提下。

### E6 — Subscription Close

```text
S.Close()
→ channel eventually closes
→ no further event
```

### E7 — Idempotent Close

```text
Close()
Close()
Close()
```

必须安全。

### E8 — Bus Close

```text
Bus.Close()
→ all subscriptions close
→ Publish returns ErrBusClosed
→ Subscribe returns ErrBusClosed
```

### E9 — Concurrent Publish

多个 goroutine Publish：

```text
no panic
no race
each event gets unique sequence
```

### E10 — Publish × Close

并发：

```text
Publish
Close(subscription)
```

必须：

```text
no send-on-closed-channel
no race
```

### E11 — Subscribe × Publish

并发 Subscribe/Publish 必须符合合法线性化顺序。

### E12 — Runtime Integration

Event Bus 作为 Runtime Provider：

```text
Provider Active
    ↓
Consumer Active
```

Subscription churn：

```text
Subscribe
Close
Subscribe
```

不得触发 Consumer reactivation。

### E13 — Provider Replacement

Event Bus Provider：

```text
P1 → P2
```

Consumer 必须遵循 Kernel dependency identity replacement。

### E14 — Runtime Disposal

如果 Event Bus 由 Activation 创建：

```text
Activation unload
    ↓
Bus.Close()
    ↓
all subscriptions closed
```

不得泄漏。

---

# 44. Property Tests

至少实现：

### P1 — Sequence Uniqueness

同一 Bus 中：

```text
sequence unique
```

### P2 — Per Subscription Ordering

对未丢弃事件：

```text
sequence strictly increasing
```

### P3 — Subscriber Isolation

任一 Subscription 的关闭/阻塞不得破坏其他 Subscription。

### P4 — Close Idempotence

任意重复 Close 序列：

```text
Close^N
```

均保持一致最终状态。

### P5 — Publish Linearizability

并发 Publish 的结果必须对应某个合法线性化顺序。

### P6 — Resource Completeness

Bus Close 后：

```text
all subscriptions closed
```

---

# 45. Forbidden Implementations

以下全部禁止：

```text
❌ Event Bus 放入 runtime/kernel
❌ Kernel import extensions/event
❌ Event 代替 Dependency
❌ Event 代替 Registry
❌ EventType 作为 Capability Key
❌ global singleton Bus
❌ unbounded queue
❌ Publish 因慢 Subscriber 阻塞
❌ Publish 执行用户 callback
❌ callback API
❌ wildcard
❌ predicate filter
❌ replay
❌ persistence
❌ event sourcing
❌ polling
❌ goroutine killing
❌ GC 负责资源释放
❌ lock 内调用用户代码
❌ Subscription churn 导致 Provider identity 变化
❌ Subscription churn 导致 Consumer reactivation
❌ 修改 Kernel
```

---

# 46. Implementation Guidance

以下不是额外 Contract，但建议：

```go
type Bus struct {
    mu          sync.RWMutex
    closed      bool
    nextSeq     uint64
    subscribers map[EventType]map[*Subscription]struct{}
}

type Subscription struct {
    mu     sync.Mutex
    events chan Event
    closed bool
}
```

但是 Code Agent 可以选择其他实现。

**不得为了匹配上述结构修改语义。**

---

# 47. Critical Concurrency Requirement

尤其注意：

```go
close(sub.events)
```

与：

```go
sub.events <- event
```

的并发关系。

禁止依赖：

```go
recover()
```

吞掉：

```text
send on closed channel
```

必须从同步设计上证明：

```text
closed channel cannot receive a concurrent send
```

---

# 48. No Kernel Modification

如果实现过程中发现：

> 当前 Kernel API 不足以安全地将 Event Bus 作为 Runtime-managed resource 注册。

Code Agent 必须：

1. 停止修改 Kernel
2. 报告具体 API gap
3. 给出最小缺口
4. 不自行扩大 Kernel API

只有收到新的 Kernel Change Specification 后才能修改。

---

# 49. Acceptance Criteria

Event Extension 只有同时满足以下条件才算 PASS：

```text
[ ] Event Bus 独立于 Kernel
[ ] Kernel 不 import Event
[ ] Publish 有明确 linearization point
[ ] Subscribe 有明确 linearization point
[ ] per-subscription ordering 明确
[ ] 不提供跨 subscription ordering
[ ] 慢 subscriber 不阻塞 publisher
[ ] queue 有界
[ ] 允许 per-subscription event loss
[ ] Subscription Close 幂等
[ ] Bus Close 幂等
[ ] Close 不产生 send-on-closed-channel
[ ] Bus Close 清理所有 subscriptions
[ ] Event Bus Provider identity 稳定
[ ] subscription churn 不影响 Provider identity
[ ] subscription churn 不导致 Consumer reactivation
[ ] Event 不承担 Dependency 语义
[ ] 无 polling
[ ] 无 global singleton
[ ] 无用户 callback
[ ] 无 persistence
[ ] 无 replay
[ ] 无 wildcard
[ ] 无第二套 Kernel lifecycle
[ ] Runtime-managed Bus 使用 Effect 清理
[ ] go test ./... PASS
[ ] go test -race ./... PASS
[ ] go vet ./... PASS
```

---

# 50. Completion Report

完成后必须只报告 Event Extension，不进入下一 Extension。

报告至少包括：

```text
EVENT EXTENSION

PASS / CONDITIONAL PASS / FAIL

1. 修改文件
2. 新增 API
3. Event semantics
4. Ordering semantics
5. Backpressure semantics
6. Subscription lifecycle
7. Runtime integration
8. Contract tests E1–E14
9. Property tests P1–P6
10. Kernel 是否修改
11. go test ./...
12. go test -race ./...
13. go vet ./...
14. 未解决问题
```

如果存在：

```text
P0 / P1 semantic conflict
```

必须明确列出，不得用“实现选择”掩盖。

**完成 Event Extension 后停止。不要自行进入 Scheduler、HTTP、MCP 或其他 Extension。**