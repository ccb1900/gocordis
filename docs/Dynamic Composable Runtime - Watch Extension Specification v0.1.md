> ⚠️ **ARCHIVED（已归档）— 2026-09-08**
>
> 本文档已整体归档，仅作历史参考，**不再作为规范来源或引用依据**。
> 本仓库现以论文原文为唯一规范来源：`docs/论文.pdf`
> （A Programming Paradigm for Spatiotemporal Composability）。
> 当前路线图与论文↔代码对照见 `docs/ROADMAP.md`。
>
> This document is archived for historical reference only and is no longer cited
> as authority. The paper is the sole normative source; see `docs/ROADMAP.md`.

# Dynamic Composable Runtime — Watch Extension Specification v0.1

## 1. Objective

Watch Extension v0.1 为 Dynamic Composable Runtime 提供**外部变化观察能力**。

核心职责：

```text
External Source
      ↓
    Watch
      ↓
 Change
      ↓
 Consumer
```

Watch 只负责：

> 观察外部资源，并将观察到的变化转换为标准化 Change。

Watch 不负责：

- Config Reconcile
- Loader
- Module Load / Unload
- HMR
- Runtime Fiber Lifecycle
- Dependency Resolution
- Provider Replacement
- Event Bus
- Scheduler
- Retry Policy
- Persistence
- Desired State 管理

因此：

```text
Watch ≠ Config
Watch ≠ Loader
Watch ≠ HMR
Watch ≠ Event
```

---

# 2. Architecture Position

完整链路：

```text
External World
      │
      ▼
    Watch
      │
      ▼
    Change
      │
      ├──────────────→ Event / Notification
      │
      └──────────────→ Config Desired State
                              │
                              ▼
                          Reconcile
                              │
                              ▼
                           Runtime
```

Watch 不应该直接连接：

```text
Watch → Runtime
Watch → Loader
Watch → HMR
```

---

# 3. Core Concepts

Watch v0.1 定义：

```text
WatchSource
Watch
Change
Subscription
```

---

# 4. WatchSource

```go
type Source struct {
    ID   string
    Kind string
    URI  string
}
```

要求：

- `ID` 非空
- `Kind` 非空
- `URI` 非空
- `ID` 是 Watch 内部稳定逻辑身份
- `Kind` 描述观察对象类型
- `URI` 是不透明资源标识

例如：

```text
ID = "config-main"
Kind = "file"
URI = "file:///etc/app/config.toml"
```

v0.1 不限制 Kind。

可以存在：

```text
file
directory
http
git
registry
builtin
```

但 concrete implementation 本阶段只要求：

```text
file
```

---

# 5. Change

核心模型：

```go
type Change struct {
    SourceID string
    Kind     ChangeKind
    URI      string

    Previous Revision
    Current  Revision

    ObservedAt time.Time
}
```

其中：

```go
type ChangeKind uint8

const (
    ChangeAdded ChangeKind = iota
    ChangeModified
    ChangeRemoved
)
```

---

# 6. Revision

Watch 不应该把“内容”本身作为 Change 的核心语义。

定义：

```go
type Revision struct {
    Exists bool
    ID     string
}
```

例如：

```text
file revision
    = content hash
```

或者：

```text
remote revision
    = ETag
```

或者：

```text
git revision
    = commit SHA
```

Watch 核心只要求：

> Revision 能够判断当前观察对象是否发生了变化。

v0.1 不强制具体 Revision 算法。

---

# 7. Change Semantics

Change 描述：

```text
Previous → Current
```

因此：

### Added

```text
Previous.Exists = false
Current.Exists  = true
```

### Modified

```text
Previous.Exists = true
Current.Exists  = true
Previous.ID != Current.ID
```

### Removed

```text
Previous.Exists = true
Current.Exists  = false
```

非法状态必须被拒绝。

例如：

```text
Added:
    Previous.Exists == true
```

属于：

```go
ErrInvalidChange
```

---

# 8. Watch Interface

```go
type Watch interface {
    Watch(context.Context, Source) (Subscription, error)
    Close() error
    CloseContext(context.Context) error
}
```

`Watch` 表示：

> 为指定 Source 建立一个独立观察实例。

---

# 9. Subscription

```go
type Subscription interface {
    Changes() <-chan Change
    Close() error
}
```

Subscription 是：

```text
Watch
  └── Subscription
         └── Changes
```

每一个 Source 可以拥有独立 Subscription。

---

# 10. Subscription Identity

每个 Subscription 都拥有内部唯一 identity。

但：

```text
Subscription Identity
≠
Source Identity
≠
Module Identity
≠
Fiber Identity
```

同一个 Source 可以先后创建多个 Subscription。

---

# 11. Duplicate Watch

同一个 Watch 实例上：

```go
Watch(source)
```

是否允许重复？

v0.1：

> **允许。**

每次调用创建独立 Subscription。

例如：

```text
Watch(file)
    ↓
S1

Watch(file)
    ↓
S2
```

S1/S2 各自拥有独立事件流。

一个 Subscription Close 不得影响另一个。

---

# 12. Initial State

建立 Subscription 时：

> 默认不发送一个伪造的 Added Change。

也就是说：

```text
Watch(existing file)
```

不会自动：

```text
Added
```

原因：

Watch 的职责是观察变化，而不是报告当前状态。

如果消费者需要初始状态：

```text
Read Current State
        +
Watch Future Changes
```

由上层组合。

---

# 13. Change Ordering

单个 Subscription 必须满足：

```text
Change N
    →
Change N+1
    →
Change N+2
```

发送顺序与 Watch 的观察线性化顺序一致。

例如：

```text
A → B → C
```

不得向同一个 Subscription 观察到：

```text
A → C → B
```

---

# 14. Concurrent Sources

不同 Source 可以并发观察。

例如：

```text
S1 → file A
S2 → file B
S3 → file C
```

不得要求全局串行。

但：

> 同一个 Subscription 的 Change 顺序必须保持。

---

# 15. Backpressure

Watch v0.1 使用**有界缓冲**。

默认：

```go
DefaultBufferSize = 64
```

Watch producer：

> 不得因为单个消费者阻塞而永久阻塞整个 Watch。

因此不能使用：

```text
unbounded queue
```

也不能因为：

```text
consumer 不读取
```

导致：

```text
Watch goroutine 永久泄漏
```

---

# 16. Overflow Policy

v0.1 采用：

> **coalescing / latest-state semantics**

而不是无限堆积所有底层事件。

例如底层文件系统产生：

```text
Modified
Modified
Modified
Modified
```

如果消费者来不及读取，可以合并成最终状态：

```text
Modified
```

但必须保证：

```text
最终 Current Revision
```

不会丢失。

---

# 17. Why Coalescing

Watch 的目标是：

```text
State Changed
```

而不是：

```text
每一个底层 OS event 都必须被业务消费
```

例如：

```text
editor save
    ↓
temp file
    ↓
rename
    ↓
write
    ↓
chmod
```

底层可能产生大量事件。

上层真正关心的通常是：

```text
最终资源状态发生变化
```

因此 Watch v0.1 采用状态变化语义，而不是 raw OS event replay。

---

# 18. No Event Loss of Final State

允许丢弃中间变化：

```text
A → B → C
```

变成：

```text
A → C
```

但不得：

```text
A → B → C
```

最终消费者只看到：

```text
A
```

而不知道 C 已经存在。

因此 coalescing 必须以：

```text
latest observable revision
```

为准。

---

# 19. Event vs Watch Change

Watch Change 与 Event Bus Event 必须区分。

```text
Watch Change:
    “某个外部资源发生了变化”

Event:
    “系统内部某个事实已经发生”
```

Watch 可以被适配成 Event：

```text
Watch
  ↓
Adapter
  ↓
Event.Publish
```

但 Watch 不依赖 Event。

---

# 20. Watch vs Polling

Watch Extension 的抽象：

```text
Watch
```

并不强制 concrete implementation 必须使用 OS native notification。

但是：

> v0.1 concrete File Watcher 禁止固定周期轮询作为主要实现机制。

例如禁止：

```go
for {
    time.Sleep(time.Second)
    os.Stat(...)
}
```

如果平台提供 native watcher，应优先使用 native watcher。

---

# 21. File Watcher

v0.1 concrete implementation：

```text
FileWatcher
```

支持：

```text
file:///path/to/file
```

以及必要时：

```text
directory observation
```

但对外仍然只产生标准 `Change`。

---

# 22. Atomic Save

File Watcher 必须正确处理常见 atomic save：

```text
write temp
   ↓
rename temp → target
```

消费者最终应该观察：

```text
Modified(target)
```

而不是要求消费者理解：

```text
temp-created
temp-written
rename
delete
```

因此 OS-level events 属于 implementation detail。

---

# 23. File Removal

如果被观察文件消失：

```text
Exists:
    true → false
```

产生：

```text
ChangeRemoved
```

但 Subscription 不应自动关闭。

即：

```text
file removed
    ↓
Subscription remains active
```

这样文件可以重新出现。

---

# 24. File Reappearance

例如：

```text
file exists
    ↓
removed
    ↓
created again
```

应该：

```text
Modified/Removed
    ↓
Added
```

具体 ChangeKind 必须基于：

```text
Previous.Exists
Current.Exists
```

计算。

因此重新出现：

```text
false → true
```

必然是：

```text
Added
```

---

# 25. Duplicate OS Events

底层可能产生：

```text
Modified
Modified
Modified
```

如果 Revision 没有变化：

```text
Previous.ID == Current.ID
```

则不得向消费者重复发送逻辑 Change。

因此：

> Watch 必须做 logical deduplication。

---

# 26. Revision Deduplication

例如：

```text
OS event 1
OS event 2
OS event 3
```

全部最终得到：

```text
Revision = abc
```

只发送一次：

```text
Current = abc
```

而不是三次 Modified。

---

# 27. File Revision

对于 File Watcher，推荐：

```text
Revision.ID = SHA-256(content)
```

但：

> v0.1 不强制必须使用 SHA-256。

允许实现使用：

```text
mtime + size
```

等方式。

但是必须保证：

> 如果 Revision 机制不能可靠区分两个不同内容，则实现必须明确报告其一致性边界。

对于 Config/HMR 等高可靠消费者，推荐 content hash。

---

# 28. Read Semantics

Watch 需要获得：

```text
Current Revision
```

但：

> Watch 不负责解析文件内容。

例如 Config 文件：

```text
file
 ↓
Watch
 ↓
Change
 ↓
Config parser
```

而不是：

```text
Watch
 ↓
TOML parser
 ↓
Config
```

---

# 29. No Config Integration

Watch v0.1 不实现：

```text
Watch → Config.Reconcile
```

因此：

```text
config.toml changed
```

Watch 只产生：

```text
ChangeModified
```

不会自动：

```text
parse
validate
reconcile
```

---

# 30. No Loader Integration

同理：

```text
plugin changed
```

不得自动：

```text
Loader.Unload
Loader.Load
```

---

# 31. No HMR

Watch 不执行：

```text
old implementation
        ↓
new implementation
```

替换。

HMR 是未来 Extension。

---

# 32. Subscription Close

```go
sub.Close()
```

必须：

- 幂等
- 关闭 Changes channel
- 停止后续 Change delivery
- 不影响其它 Subscription

---

# 33. Close Ordering

Subscription Close 必须保证：

```text
Close returns
    ↓
no future Change can be observed
```

不能：

```text
Close()
return
    ↓
goroutine sends Change
```

产生：

```text
send-on-closed-channel
```

---

# 34. Watch Close

```go
watch.Close()
```

必须：

1. 禁止新的 Watch
2. Close 所有 Subscription
3. 停止所有 underlying watchers
4. 等待内部 goroutines 完成
5. 幂等

---

# 35. CloseContext

```go
CloseContext(ctx)
```

语义：

```text
Closing
   ↓
wait
   ↓
Closed
```

如果：

```text
ctx.Done()
```

先发生：

```go
return ctx.Err()
```

但 Watch 仍然处于：

```text
Closing
```

不得谎报：

```text
Closed
```

---

# 36. Context Cancellation

建立 Watch：

```go
Watch(ctx, source)
```

如果 ctx 已取消：

```go
ErrContextCanceled
```

不得建立半成品 Subscription。

运行过程中 Context 被取消：

```text
ctx cancelled
    ↓
subscription closes
```

属于 cooperative cancellation。

---

# 37. Subscription Lifecycle

内部：

```text
Active
  ↓ Close
Closing
  ↓
Closed
```

外部 API 不要求暴露 State。

---

# 38. Ownership

每个 Subscription 必须明确属于一个 Watch。

```text
Watch
 ├── S1
 ├── S2
 └── S3
```

Watch Close：

```text
S1 → Closed
S2 → Closed
S3 → Closed
```

但：

```text
S1.Close()
```

不能导致：

```text
Watch Closed
```

---

# 39. Source Ownership

Source descriptor 本身不属于 Subscription 的生命周期。

例如：

```text
Source S
    ↓
Subscription A
    ↓ Close

Source S
    ↓
Subscription B
```

合法。

---

# 40. Resource Cleanup

Native File Watcher 等底层资源：

```text
OS handle
goroutine
channel
file descriptor
```

必须在 Subscription Close / Watch Close 后释放。

禁止：

```text
Close()
    ↓
goroutine 永久等待
```

---

# 41. No Goroutine Leak

必须保证：

```text
Watch
    ↓
Close
```

最终所有 Watch-owned goroutines 退出。

测试必须检测：

- repeated create/close
- concurrent close
- context cancellation
- source removal
- source reappearance

---

# 42. Error Model

至少定义：

```go
var (
    ErrWatchClosed        = errors.New("watch closed")
    ErrSubscriptionClosed = errors.New("subscription closed")
    ErrInvalidSource      = errors.New("invalid watch source")
    ErrInvalidChange      = errors.New("invalid watch change")
    ErrSourceNotFound     = errors.New("watch source not found")
)
```

错误必须支持：

```go
errors.Is(...)
```

---

# 43. Watch Registry

Watch 内部可以维护：

```go
type SourceRegistry interface {
    Get(id string) (Source, bool)
    Has(id string) bool
    Snapshot() []Source
}
```

但：

> Source Registry 不是 Runtime Registry。

它不提供 Capability。

也不得进入 Kernel Provider Graph。

---

# 44. Source Identity

Source ID 必须稳定。

例如：

```text
config-main
```

不能因为：

```text
URI changed
```

自动认为是新的 Watch Subscription。

Source Identity 与 URI 分离。

---

# 45. Source Replacement

如果需要把：

```text
Source A
```

换成：

```text
Source B
```

必须显式：

```text
Close A
Watch B
```

v0.1 不提供 atomic source replacement。

---

# 46. Multiple Watchers

多个 Watch 实例可以观察相同 URI：

```text
Watch1 → file A
Watch2 → file A
```

两者完全独立。

一个关闭：

```text
Watch1.Close()
```

不得影响：

```text
Watch2
```

---

# 47. Thread Safety

所有 public API 必须并发安全：

```text
Watch × Watch
Watch × Close
Subscription × Subscription.Close
Change delivery × Close
```

特别禁止：

```text
send on closed channel
double close
use after close
```

---

# 48. Lock Rule

Watch state lock 中禁止调用：

- filesystem
- OS watcher API
- user callback
- parser
- Config
- Loader
- Event
- Runtime

正确：

```text
lock
 ↓
capture state
 ↓
unlock
 ↓
external operation
 ↓
lock
 ↓
commit
```

---

# 49. Callback Model

v0.1：

> Watch 不接受用户 callback。

只提供：

```go
Changes() <-chan Change
```

原因：

- 避免用户代码运行于 Watch 内部 goroutine
- 简化 backpressure
- 简化 lifecycle
- 保持 Watch 与 Event Bus 解耦

未来如需要 callback：

```text
Adapter
```

解决。

---

# 50. No Polling Scheduler

Watch 不得依赖 Scheduler：

```text
Scheduler
   ↓
periodic scan
```

如果 concrete Source 只能 polling：

```text
Polling Watcher
```

也必须是独立实现，不得把 Scheduler 引入 Watch 核心。

---

# 51. Deterministic Change Semantics

对于同一个 Source：

```text
State A
   ↓
State B
```

只产生：

```text
A → B
```

无论底层产生多少重复 OS events。

因此：

> Watch 对外暴露的是 logical state transition，而不是 OS event stream。

---

# 52. Recovery

如果：

```text
file exists
    ↓
filesystem watcher error
```

v0.1 不要求自动恢复。

必须：

- 报告错误
- Subscription 不得静默伪造 Change
- 不得把 watcher error 当作 Modified
- 不得自动重建 watcher，除非 concrete implementation 明确规定且不改变 Watch 抽象语义

推荐 v0.1：

```text
watcher fatal error
    ↓
Subscription closes / reports terminal error
```

但是必须避免静默数据丢失。

---

# 53. Error Delivery

`Changes()` 只传：

```text
Change
```

不混入：

```text
error
```

因此如果发生 terminal watcher error：

Subscription 必须关闭。

如需要暴露错误，提供：

```go
type Subscription interface {
    Changes() <-chan Change
    Err() error
    Close() error
}
```

`Err()`：

- Closed normally → nil
- Context cancelled → context error
- Watcher failure → underlying error

---

# 54. Recommended Subscription API

最终推荐：

```go
type Subscription interface {
    Changes() <-chan Change
    Err() error
    Close() error
}
```

`Err()` 只有在 Subscription 已结束后才保证最终值。

调用者：

```go
for change := range sub.Changes() {
    ...
}

if err := sub.Err(); err != nil {
    ...
}
```

---

# 55. File Watcher Concrete API

推荐：

```go
type FileWatcher struct {
    ...
}

func NewFileWatcher(...) *FileWatcher
```

使用：

```go
source := Source{
    ID:   "config-main",
    Kind: "file",
    URI:  "file:///path/config.toml",
}

sub, err := watcher.Watch(ctx, source)
```

---

# 56. URI Semantics

v0.1 File Watcher 至少支持：

```text
file:///absolute/path
```

URI 必须：

- 可解析
- scheme 正确
- path 有效

禁止把任意字符串直接当 filesystem path 而不验证。

---

# 57. Symlink Semantics

v0.1 必须明确：

> 默认观察最终解析后的 target，而不是 symlink inode 本身。

如果具体实现不支持这一语义：

```text
STOP
```

并报告实现限制，不得默默采用不同语义。

---

# 58. Directory Semantics

如果观察：

```text
directory
```

Change 必须指向发生变化的具体 child URI：

```text
directory/
    ↓
file A changed
    ↓
Change.URI = file A
```

而不是永远：

```text
Change.URI = directory
```

---

# 59. Atomicity

一次 Change 必须是不可变值。

发送：

```go
Change
```

后：

> Watch 内部不能再修改该 Change。

消费者持有 Change 后必须安全。

---

# 60. Snapshot

Watch v0.1 不提供历史 replay。

Subscription 建立后：

```text
future changes only
```

不存在：

```text
replay previous changes
```

因此：

```text
Watch
```

不是 Event Store。

---

# 61. Watch + Config Recommended Composition

未来上层可以：

```text
File Watch
    ↓
Change
    ↓
Read file
    ↓
Parse Config
    ↓
Validate
    ↓
Config.Reconcile
```

注意：

```text
Read file
Parse
Validate
Reconcile
```

全部属于上层 Adapter / Controller。

Watch 只负责：

```text
Change
```

---

# 62. Watch + Loader Recommended Composition

未来：

```text
Artifact Source
    ↓
Watch
    ↓
Change
    ↓
Deployment/HMR Policy
    ↓
Loader
```

而不是：

```text
Watch
 ↓
Loader
```

---

# 63. Watch + HMR

未来 HMR 可以订阅：

```text
Watch Change
```

并执行：

```text
old Module
   ↓
Load new Module
   ↓
validate
   ↓
replacement
```

这部分明确留给 HMR。

---

# 64. Testing — Contract Tests

必须实现：

### W1 — Watch Existing Source

建立 Subscription 成功，不产生初始伪 Added。

### W2 — File Modification

修改文件产生 Modified。

### W3 — File Removal

删除文件产生 Removed。

### W4 — File Reappearance

Removed → Added。

### W5 — Duplicate OS Events

多个底层事件但 Revision 不变，只产生一次 logical Change。

### W6 — Change Validation

非法 Previous/Current 状态被拒绝。

### W7 — Ordering

同一 Subscription 的 Change 顺序正确。

### W8 — Subscription Isolation

关闭 S1 不影响 S2。

### W9 — Duplicate Watch

同一个 Source 可以建立多个独立 Subscription。

### W10 — Close

Subscription Close 后不再收到 Change。

### W11 — Watch Close

Watch Close 后所有 Subscription 关闭。

### W12 — Context Cancellation

Context cancel 正确终止 Subscription。

### W13 — No Initial Replay

Watch existing source 不发送初始 Change。

### W14 — Coalescing

快速连续修改最终至少得到最新 Revision。

### W15 — Revision Deduplication

内容未变化不得重复发送。

### W16 — Remove/Reappear

文件 remove → recreate 正确产生 Removed → Added。

### W17 — Atomic Save

temp + rename 最终产生目标文件 Modified/Added 语义。

### W18 — Error Reporting

Watcher fatal error 能通过 `Err()` 观察。

### W19 — Module/Fiber Independence

Watch 不直接操作 Loader/Runtime Fiber。

### W20 — Config Independence

Watch 不自动调用 Config.Reconcile。

---

# 65. Property Tests

### P1 — Ordering Preservation

同一 Subscription：

```text
Observed revision sequence
```

保持逻辑顺序。

### P2 — Deduplication

相同 Revision 不产生重复 Logical Change。

### P3 — Latest State Preservation

Coalescing 后最终可观察 Revision 不丢失。

### P4 — Subscription Isolation

任意 Subscription Close 不影响其它 Subscription。

### P5 — Close Idempotence

重复 Close 无副作用。

### P6 — Watch Close Conservation

Watch Close 后：

```text
all subscriptions = Closed
```

### P7 — No Goroutine Leak

Repeated Watch/Close 最终没有 Watch-owned goroutine 泄漏。

### P8 — Concurrency Safety

并发：

```text
Watch
Close
Subscription.Close
Change delivery
```

不产生 race / panic / deadlock。

---

# 66. Acceptance Requirements

必须满足：

```text
Contract W1–W20       PASS
Property P1–P8        PASS
go test ./...          PASS
go test -race ./...    PASS
go vet ./...           PASS
Kernel unchanged       PASS
Config unchanged       PASS
Loader unchanged       PASS
Event unchanged        PASS
Scheduler unchanged    PASS
```

---

# 67. Forbidden Behaviors

以下任一项均 FAIL：

- 修改 Kernel
- 修改 Config semantics
- 修改 Loader semantics
- Watch 直接调用 Config.Reconcile
- Watch 直接 Load/Unload Module
- Watch 直接修改 Fiber
- Watch 自动执行 HMR
- Watch 自动 Reload
- Watch 自动 Retry
- Watch 使用 Event Bus 作为内部依赖
- Watch 使用 Scheduler 作为核心机制
- 固定周期 polling 作为 File Watcher 主实现
- callback API 作为核心 Watch API
- 无界队列
- 因一个慢消费者阻塞所有其它 Subscription
- Close 后仍发送 Change
- send on closed channel
- goroutine 泄漏
- replay 历史 Change
- 把 OS raw event 直接暴露给消费者
- 重复 Revision 重复通知
- 丢失最终 Revision
- Module Identity 与 Source Identity 混用
- Source URI 与 Source ID 混用
- 将 file content parser 放入 Watch
- 将 Config parser 放入 Watch
- 自动 Discovery
- 自动 HMR
- 第二套生命周期系统
- global singleton

---

# 68. Implementation Constraint

Code Agent 必须：

1. 首先实现 Watch 抽象。
2. concrete implementation 只要求 File Watcher。
3. 优先使用现有平台 native filesystem watcher。
4. 不实现 HMR。
5. 不实现 Config integration。
6. 不实现 Loader integration。
7. 不实现 Event integration。
8. 不修改 Kernel。
9. 不修改 Config。
10. 不修改 Loader。
11. 不修改 Registry/Event/Scheduler。
12. 如果发现现有 API 不足，STOP 并报告 API gap。
13. 不自行修改本 Spec 的核心语义。

---

# 69. Recommended Package

```text
extensions/
    watch/
        watch.go
        file.go
        subscription.go
        revision.go
        watch_test.go
        watch_concurrency_test.go
```

如果需要第三方 filesystem watcher：

> 允许作为 concrete implementation dependency，但必须隔离在 `extensions/watch` 内部。

Kernel 不得依赖该库。

---

# 70. Completion Report

实现完成后必须严格按以下格式报告：

```text
WATCH EXTENSION

PASS / CONDITIONAL PASS / FAIL

1. 修改文件
2. 新增 API
3. Source model
4. Change model
5. Revision semantics
6. Watch semantics
7. Subscription semantics
8. Ordering semantics
9. Deduplication semantics
10. Coalescing semantics
11. File Watcher semantics
12. Error semantics
13. Close semantics
14. Context cancellation
15. Concurrency semantics
16. Contract tests W1–W20
17. Property tests P1–P8
18. Kernel 是否修改
19. Config/Loader/Event/Scheduler/Registry 是否修改
20. go test ./...
21. go test -race ./...
22. go vet ./...
23. 未解决问题

如果发现 P0/P1 semantic conflict，必须明确：

- Spec 条款
- 实现行为
- 冲突原因
- 是否修改实现
- 是否需要修改 Spec

完成 Watch Extension 后立即停止。

不得进入：

- HMR
- WASM
- HTTP Watch
- Git Watch
- Discovery
- Deployment
- Config Watch Adapter
```

# 71. Architect Review Rule

Code Agent 负责：

```text
Implement
Test
Report
```

Architect 负责：

```text
Define semantics
Review implementation
Resolve semantic conflicts
Accept / Reject
```

Code Agent 不得自行重新定义：

- Change semantics
- Revision semantics
- Ordering
- Deduplication
- Coalescing
- Subscription lifecycle
- Close semantics
- Source identity

如果认为 Spec 存在问题：

```text
STOP
 ↓
Report conflict
 ↓
Await architectural decision
```

---

# 72. Completion Criterion

Watch Extension v0.1 只有在：

```text
W1–W20              PASS
P1–P8               PASS
Race                PASS
Vet                 PASS

Kernel              unchanged
Config              unchanged
Loader              unchanged
Event               unchanged
Scheduler           unchanged

No Config integration
No Loader integration
No HMR
No polling core
No lifecycle duplication
```

全部满足后，才能宣布：

```text
WATCH EXTENSION
PASS
```

然后停止，等待 Architect 决定下一阶段。