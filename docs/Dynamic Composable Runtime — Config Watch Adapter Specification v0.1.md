# Dynamic Composable Runtime — Config Watch Adapter Specification v0.1

**Status:** Implementation Specification  
**Version:** v0.1  
**Role:** Extension / Integration Adapter  
**Primary Goal:** Connect external configuration changes to the existing Config Controller and Runtime without introducing a second lifecycle or configuration state machine.

---

# 1. Objective

实现：

```text
External Configuration
        ↓
      Watch
        ↓
    Change Fact
        ↓
Config Watch Adapter
        ↓
 Read / Parse / Validate
        ↓
 Config.Reconcile
        ↓
     Runtime
        ↓
    Fiber State
```

Adapter 的唯一核心职责：

> 将 Watch 产生的外部变化事实转换为 Config Controller 的 Reconcile 请求。

Adapter 不拥有 Component 生命周期，也不拥有 Config Applied State。

---

# 2. Architectural Position

```text
┌───────────────────────────────┐
│           Watch               │
│ external change observation   │
└───────────────┬───────────────┘
                │ Change
                ▼
┌───────────────────────────────┐
│    Config Watch Adapter       │
│                               │
│ Resolve                       │
│ Read                          │
│ Parse                         │
│ Validate                      │
│ Reconcile                     │
└───────────────┬───────────────┘
                │ Config
                ▼
┌───────────────────────────────┐
│        Config Controller      │
│ desired/applied/reconcile     │
└───────────────┬───────────────┘
                │ Runtime API
                ▼
┌───────────────────────────────┐
│            Kernel             │
└───────────────────────────────┘
```

Adapter 不得绕过 Config Controller 直接进入 Kernel。

---

# 3. Explicit Non-Goals

v0.1 禁止实现：

```text
❌ 修改 Kernel
❌ 修改 Fiber lifecycle
❌ 修改 Dependency semantics
❌ 修改 Provider Registry
❌ 直接 Runtime.Load
❌ 直接 Fiber.Dispose
❌ 直接操作 Provider
❌ 实现新的 Config Controller
❌ 实现新的 Watch system
❌ 实现 HMR
❌ 调用 Loader 进行模块替换
❌ 自动执行 HMR
❌ Polling
❌ Retry framework
❌ Persistent queue
❌ Event sourcing
❌ Rollback transaction
❌ Blue/Green deployment
❌ Canary deployment
❌ 第二套 Desired/Applied state
❌ 第二套 lifecycle state
```

---

# 4. Existing Components

Adapter 必须复用已有：

```text
extensions/watch
extensions/config
runtime
```

不得复制其核心语义。

依赖方向：

```text
Watch ───────────────┐
                     ▼
               Config Adapter
                     │
                     ▼
                   Config
                     │
                     ▼
                  Runtime
```

推荐：

```text
extensions/configwatch
```

或者等价命名。

---

# 5. Core Model

## 5.1 Source

Adapter 对一个外部配置源建立绑定。

```go
type Source struct {
    ID      string
    Path    string
    Format  Format
}
```

约束：

- `ID` 非空
- `Path` 非空
- `Format` 必须受支持
- ID 是逻辑身份，不得使用数组位置
- 一个 Adapter Binding 对应一个逻辑配置源

---

# 6. Format

v0.1 至少支持 TOML。

```go
type Format string

const (
    FormatTOML Format = "toml"
)
```

不得为了 v0.1 引入 YAML。

解析器必须将外部文件转换成：

```go
config.Config
```

而不是直接生成 Runtime 操作。

---

# 7. Source Reader

Adapter 应将读取抽象为：

```go
type Reader interface {
    Read(ctx context.Context, source Source) ([]byte, error)
}
```

Reader 只负责读取。

Reader 不得：

```text
❌ Reconcile
❌ Load
❌ Dispose
❌ HMR
❌ 修改 Config Controller
```

---

# 8. Parser

```go
type Parser interface {
    Parse(ctx context.Context, source Source, data []byte) (config.Config, error)
}
```

Parser 负责：

```text
bytes
 ↓
syntax parsing
 ↓
Config
```

Parser 不负责：

```text
❌ Runtime.Load
❌ Runtime.Dispose
❌ Reconcile
❌ Watch
```

---

# 9. Validator

Parser 成功后必须进行 Config 级验证。

Adapter 不得复制 Config Controller 的 Desired/Applied 语义。

可以执行 source-level validation，例如：

```text
格式合法
Source 必填
文件可读
```

Config 本身的业务约束必须交由已有 Config Controller / Config validation 机制处理。

如果现有 Config API 已经包含完整 Validate，则直接调用它。

不得创建第二套 Config validation semantics。

---

# 10. Change Handling

Watch 发出的 Change 表示：

> 外部世界发生了一个可能影响配置的变化。

Change 本身不是 Config。

Adapter 不得假定：

```text
Change = Config
```

必须执行：

```text
Change
 ↓
Read current source
 ↓
Parse current source
 ↓
Validate
 ↓
Reconcile
```

因此 Adapter 使用的是：

> latest external state

而不是：

> event payload state。

---

# 11. Revision Semantics

如果 Watch Change 已经提供 Revision，Adapter 应利用 Revision 做最基本的重复抑制。

逻辑：

```text
Change.Revision == lastProcessedRevision
        ↓
      ignore
```

但：

> Revision 不能替代 Config Controller 的 Applied equality。

最终是否产生实际 Runtime mutation，仍由 Config Controller 决定。

因此：

```text
Watch Revision
    ↓
Adapter duplicate suppression
    ↓
Config.Reconcile
    ↓
Config Applied comparison
```

是两层不同语义。

---

# 12. Event Coalescing

Watch 可能快速产生：

```text
A
B
C
D
```

Adapter 不要求每个 Change 都触发一次 Reconcile。

允许：

```text
A B C D
   ↓
 latest
   ↓
Reconcile(D)
```

但必须满足：

> Adapter 不能因为 coalescing 丢失最终外部状态。

最终读取必须重新读取 Source。

---

# 13. Serialization

同一个 Adapter 实例的 Reconcile 请求必须串行化。

禁止：

```text
Change A
    ↓
Reconcile(A)

Change B
    ↓
Reconcile(B)
```

形成并发 Reconcile。

必须：

```text
Change A → Reconcile(A)
                 ↓
Change B → Reconcile(B)
```

不同 Adapter 实例可以并发。

Config Controller 自身已经有串行 Reconcile 语义；Adapter 不得假设调用者会替自己串行化。

---

# 14. Latest-State Guarantee

假设外部变化：

```text
Revision 1
Revision 2
Revision 3
```

Adapter 在处理 Revision 1 时又收到 2、3。

允许：

```text
Reconcile(1)
Reconcile(3)
```

跳过 2。

不允许最终状态停留在：

```text
Revision 1
```

如果 Revision 3 已经稳定可读取。

因此 Adapter 的目标是：

> eventual convergence to the latest successfully readable and valid external configuration.

---

# 15. Invalid Configuration

外部配置变为非法：

```text
Watch
 ↓
Read
 ↓
Parse/Validate
 ↓
Error
```

必须：

```text
Applied configuration unchanged
Current Runtime state unchanged
```

Adapter 不得自行：

```text
❌ unload old components
❌ partially apply new configuration
❌ clear Config Applied
❌ modify Runtime
```

错误只作为一次 failed reconciliation attempt 返回/记录。

---

# 16. Read Failure

如果文件暂时不可读取：

```text
Read → error
```

Adapter 必须保留现有 Runtime 状态。

这是正常 external-world transient failure。

不得将 Fiber 标记为 Failed。

不得主动 Dispose 现有 Fiber。

---

# 17. Delete Semantics

v0.1 必须明确支持配置源删除。

删除后 Adapter 必须执行预先定义的 Source policy。

默认策略：

```text
Source deleted
      ↓
Config becomes empty Config
      ↓
Config.Reconcile(empty)
      ↓
Owned Components removed
```

即：

> 删除配置文件意味着该 Source 的 desired configuration 为空。

但是：

**只有 Adapter 自己拥有的 Config Controller scope 才允许被删除。**

不得删除其他 Controller 管理的 Fiber。

---

# 18. Initial Load

创建 Adapter 时必须支持：

```text
Initial Sync
```

流程：

```text
Source
 ↓
Read
 ↓
Parse
 ↓
Validate
 ↓
Config.Reconcile
```

Initial Sync 不依赖先收到 Watch Event。

推荐 API：

```go
Sync(ctx context.Context) error
```

---

# 19. Watch Integration

Adapter 应接收已有 Watch Subscription。

概念 API：

```go
type Adapter interface {
    Sync(ctx context.Context) error
    Run(ctx context.Context) error
    Close() error
    CloseContext(ctx context.Context) error
}
```

如果已有 Watch API 的 Subscription 结构不同，应适配现有 API。

**不得为了 Adapter 修改 Watch 核心 API，除非存在明确 API gap。**

---

# 20. Run Semantics

`Run` 负责消费 Watch Changes。

生命周期：

```text
Running
   │
   ├── Change → process
   │
   ├── Watch closed → stop
   │
   └── Context cancelled → stop
```

`Run` 不得自行创建 Runtime lifecycle。

---

# 21. Startup Ordering

推荐启动顺序：

```text
1. Construct Adapter
2. Initial Sync
3. Subscribe Watch
4. Run event processing
```

但是 Initial Sync 与 Subscribe 之间存在事件窗口。

因此实现必须避免丢失变化。

推荐语义：

```text
Subscribe
   ↓
Initial Sync
   ↓
Process queued/latest Change
```

即：

> 先建立观察，再同步当前状态。

最终模型：

```text
Subscribe
   ↓
Sync current state
   ↓
Drain/coalesce pending changes
   ↓
steady state
```

不得出现：

```text
Sync
 ↓
Subscribe
```

导致窗口期间变化丢失。

---

# 22. Race: Change During Sync

假设：

```text
Sync reads Revision 1

external file → Revision 2

Watch emits Revision 2

Sync finishes
```

Adapter 必须最终处理 Revision 2。

允许：

```text
Sync(1)
Reconcile(1)
Reconcile(2)
```

也允许：

```text
Sync reads 2
Reconcile(2)
```

但不能：

```text
Sync(1)
Reconcile(1)
ignore Revision 2
```

---

# 23. Change During Reconcile

假设：

```text
Reconcile(1)
    ↓
external change
    ↓
Revision 2
```

Adapter 必须保留 pending indication。

完成当前 Reconcile 后：

```text
Reconcile(1)
    ↓
detect pending
    ↓
Read latest
    ↓
Reconcile(2)
```

不得因为当前 Reconcile 正在执行而丢弃变化。

---

# 24. Backpressure

Adapter 不得无限增长内存。

不允许：

```text
unbounded []Change
```

推荐：

```text
pending = bool
```

或 bounded latest-state queue。

因为 Adapter 只需要保证：

> 至少保留“有更新尚未处理”这一事实。

不要求保存每一个历史 Change。

---

# 25. Watch Event Ordering

Adapter 不得依赖 Watch Event 的历史完整性。

它只依赖：

```text
Change indicates source may have changed
```

因此：

```text
Change 3
Change 1
Change 2
```

也必须通过重新读取 Source 得到当前状态。

Revision 可以用于 duplicate suppression，但不能要求所有 Revision 连续。

---

# 26. Reconcile Ownership

Adapter 必须绑定一个 Config Controller。

```go
type Adapter struct {
    source    Source
    controller *config.Controller
}
```

Adapter 只调用：

```go
controller.Reconcile(...)
```

不得直接访问：

```text
controller.applied
controller.desired
controller.components
```

除非这些本来就是已有公开 API。

---

# 27. No Hidden State Machine

Adapter 内部最多允许存在：

```text
Running
Closing
Closed
```

不得存在：

```text
Desired
Applied
Loading
Active
Unloading
Failed
```

这些属于其他层。

尤其禁止：

```text
AdapterFiberState
AdapterComponentState
AdapterAppliedState
```

作为第二套生命周期系统。

---

# 28. Close

Close 必须：

```text
Running
   ↓
Closing
   ↓
stop accepting new work
   ↓
drain current processing
   ↓
Closed
```

Close 必须 idempotent。

不得：

```text
close channel twice
send on closed channel
panic during event storm
```

---

# 29. Close During Reconcile

如果 Close 发生在 Reconcile 执行期间：

```text
Close
   ↓
stop new changes
   ↓
current Reconcile may finish
   ↓
drain
   ↓
Closed
```

不得强杀 goroutine。

Context cancellation 采用 cooperative cancellation。

---

# 30. CloseContext

```go
CloseContext(ctx context.Context) error
```

如果 timeout：

```text
return ctx.Err()
```

但：

```text
Adapter state != Closed
```

不得谎报 Closed。

后台 cleanup 可以继续。

---

# 31. Error Semantics

建议错误：

```go
var (
    ErrAdapterClosed       = errors.New("config watch adapter closed")
    ErrInvalidSource       = errors.New("invalid config source")
    ErrUnsupportedFormat   = errors.New("unsupported config format")
)
```

具体错误应保留 cause：

```go
fmt.Errorf("read config source %q: %w", source.ID, err)
```

不得吞掉：

```text
Read error
Parse error
Validate error
Reconcile error
```

---

# 32. Error Isolation

一个 Source 的错误不能影响其他 Adapter。

例如：

```text
Adapter A → invalid TOML
Adapter B → valid TOML
```

必须：

```text
A failed
B continues
```

不得使用 global error state。

---

# 33. Logging

日志必须描述：

```text
source ID
revision
phase
error
```

例如：

```text
config source read failed
config source parse failed
config reconcile failed
```

不得将完整配置内容默认写入日志。

---

# 34. Security Boundary

v0.1 不实现：

```text
签名验证
加密配置
权限系统
沙箱
```

但 Adapter 不得因为方便而执行配置中的任意代码。

Parser 只产生数据结构。

---

# 35. TOML Mapping

TOML 应映射为现有：

```go
config.Config
```

例如：

```toml
[[components]]

id = "camera"
type = "builtin.camera"

[components.config]
device = "cam-01"
interval = "1s"
```

映射：

```go
config.Config{
    Components: []config.ComponentConfig{
        {
            ID:   "camera",
            Type: "builtin.camera",
            Config: map[string]any{
                "device":   "cam-01",
                "interval": "1s",
            },
        },
    },
}
```

Adapter 不解释：

```text
builtin.camera
```

的运行时含义。

Type 的解释仍由 Config FactoryRegistry 负责。

---

# 36. Factory Resolution

配置：

```text
Type = "builtin.camera"
```

Adapter 不得：

```text
Lookup Factory
Create Component
Runtime.Load
```

这些已经属于 Config Controller。

完整链路必须保持：

```text
TOML
 ↓
Config
 ↓
Config Controller
 ↓
Factory
 ↓
Component
 ↓
Runtime.Load
```

---

# 37. HMR Boundary

v0.1 不自动执行 HMR。

例如配置：

```text
type = "camera.v1"
```

变成：

```text
type = "camera.v2"
```

如果这是 Config Component 的 Type 变化：

```text
Config.Reconcile
```

由 Config Controller 按其现有 replacement semantics 处理。

Adapter 不得判断：

```text
"这是代码变化，所以应该 HMR"
```

HMR 是另一条 replacement pipeline。

---

# 38. Loader Boundary

Loader 不参与配置文件变化。

Adapter 不得：

```text
Watch
 ↓
Loader.Load
```

除非未来明确建立：

```text
Watch → Loader → Config
```

的独立适配器。

v0.1 不实现。

---

# 39. Multiple Sources

v0.1 可以支持多个 Source：

```text
source-A.toml
source-B.toml
```

但必须明确 ownership。

推荐每个 Source 对应一个 Config Controller。

即：

```text
Source A → Controller A
Source B → Controller B
```

而不是：

```text
多个 Adapter
      ↓
共享 Controller
```

除非现有 Config API 已明确支持多 Source scope。

不得自行发明 merge semantics。

---

# 40. Configuration Merge

v0.1 **禁止自动 merge**。

例如：

```text
a.toml
b.toml
```

不得默认：

```text
A + B → merged Config
```

除非 Config Controller 已有明确的 merge contract。

原因：

> Merge 会产生新的 ownership、冲突、删除、优先级和 Applied semantics。

这些不属于 Adapter v0.1。

---

# 41. File Deletion and Empty Config

如果 Source 删除：

```text
Deleted
 ↓
Config{}
 ↓
Reconcile
```

该行为必须只影响该 Source Controller 所拥有的组件。

如果现有 Config Controller 不支持 empty Config，则：

**STOP，不修改 Config。**

报告 API gap。

---

# 42. File Recreation

如果：

```text
config.toml deleted
```

随后：

```text
config.toml recreated
```

必须能够：

```text
empty desired
   ↓
new desired
   ↓
new Active state
```

不允许复用已经 Gone 的 Fiber。

Runtime/Config 的既有 Fiber identity semantics 必须保持。

---

# 43. Temporary Invalid Write

常见编辑器行为：

```text
write partial file
write complete file
```

因此可能产生：

```text
Revision A
Revision B invalid
Revision C valid
```

Adapter：

```text
A → Reconcile
B → parse error
C → Reconcile
```

即可。

不需要 Adapter 自己实现 debounce/retry。

但允许 bounded latest-state coalescing。

---

# 44. Convergence

Adapter 的核心 correctness property：

> 如果外部 Source 在某个时间点之后稳定为合法配置 C，并且 Watch 最终通知该变化，则 Adapter 最终应使对应 Config Controller 观察到 C。

注意：

这不是：

```text
最终 Fiber 一定 Active
```

因为：

```text
Factory failure
Dependency unsatisfied
Runtime failure
```

可能导致 Component 非 Active。

Adapter 的职责只到：

```text
Config Controller received desired C
```

---

# 45. Failure Separation

必须区分：

```text
Source failure
    ↓
Adapter error

Config failure
    ↓
Reconcile error

Runtime failure
    ↓
Fiber lifecycle failure
```

Adapter 不得把 Runtime Failure 转换成自己的 Fiber state。

---

# 46. Observability

建议提供最小统计：

```text
changes_received
changes_coalesced
sync_success
sync_failed
reconcile_success
reconcile_failed
```

但统计不是 correctness state。

统计丢失不得影响功能。

---

# 47. Testing Requirements

必须实现单元测试 + 集成测试 + 并发测试。

---

# 48. Functional Tests

## A1 Initial Sync

```text
valid TOML
 ↓
Sync
 ↓
Component Active
```

PASS 条件：

- Config Applied 正确
- Fiber Active
- 无直接 Runtime.Load bypass

---

## A2 Change

```text
TOML A
 ↓
Sync

TOML B
 ↓
Watch Change
 ↓
Reconcile
```

PASS：

```text
Applied == B
```

---

## A3 Duplicate Revision

```text
Change(R1)
Change(R1)
```

不得产生两次必要 Reconcile。

---

## A4 Coalescing

```text
R1
R2
R3
R4
```

允许少于 4 次 Reconcile。

最终：

```text
Applied == R4
```

---

## A5 Invalid Configuration

```text
valid A
 ↓
invalid B
```

PASS：

```text
Applied == A
Runtime == A
```

---

## A6 Read Failure

```text
valid A
 ↓
read failure
```

PASS：

```text
Applied == A
```

---

## A7 Delete

```text
A
 ↓
Delete
 ↓
empty Config
```

PASS：

```text
owned components removed
```

---

## A8 Recreate

```text
Delete
 ↓
Create B
```

PASS：

```text
B becomes desired
B reaches normal Config/Runtime lifecycle
```

---

# 49. Race Tests

必须覆盖：

```text
Change vs Reconcile
Change vs Close
Sync vs Change
Sync vs Close
Close vs Run
multiple Change producers
```

运行：

```bash
go test -race ./...
```

至少连续运行 3 次。

---

# 50. Property Tests

## P1 Latest State

对于：

```text
R1 R2 ... Rn
```

最终稳定：

```text
Applied == Rn
```

---

## P2 Invalid Preservation

任意：

```text
Valid(A) → Invalid(B)
```

必须：

```text
Applied == A
```

---

## P3 Duplicate Safety

重复相同 Revision 不产生额外必要 mutation。

---

## P4 Close Safety

任意 Change/Close 交错：

```text
no panic
no send-on-closed
no deadlock
```

---

## P5 Isolation

Adapter A failure 不影响 Adapter B。

---

## P6 Ownership

Adapter A 不能删除 Adapter B 的 Config-owned components。

---

# 51. Integration Test

必须至少有一条完整链路：

```text
TOML file
   ↓
Watch
   ↓
Config Watch Adapter
   ↓
Config Controller
   ↓
Factory
   ↓
Component
   ↓
Runtime Fiber
   ↓
Active
```

然后：

```text
modify TOML
   ↓
Watch Change
   ↓
Adapter
   ↓
Config.Reconcile
   ↓
old Fiber replacement
   ↓
new Fiber Active
```

必须验证 Fiber identity 改变，而不是复用旧 Fiber。

---

# 52. Dependency Integration

至少验证：

```text
Provider
   ↑
Consumer
```

配置变化导致 Provider replacement 时：

```text
old Provider
    ↓
consumer withdrawal
    ↓
old Provider Gone
    ↓
new Provider
    ↓
consumer recovery
```

Adapter 本身不得实现这些逻辑。

验证目的：

> Config Watch Adapter 不破坏 Kernel 已有 dependency semantics。

---

# 53. Registry Integration

如果 Component 通过 Registry 暴露动态成员：

```text
Config
 ↓
Component
 ↓
Registry
```

成员变化不得导致 Adapter 自己重新 Reconcile。

Registry member churn 仍由 Registry semantics 管理。

---

# 54. Event Integration

v0.1 不要求 Adapter 使用 Event。

Watch Change 不得自动转换成 Event，除非未来存在明确 adapter。

---

# 55. Scheduler Integration

v0.1 不使用 Scheduler。

不得用 Scheduler 做：

```text
polling
debounce loop
retry
```

---

# 56. No Polling

绝对禁止：

```go
for {
    time.Sleep(...)
    os.ReadFile(...)
}
```

Adapter 必须以 Watch 作为变化驱动。

---

# 57. Concurrency Model

推荐内部：

```text
Watch
  ↓
bounded/latest pending signal
  ↓
single processing loop
  ↓
Read
  ↓
Parse
  ↓
Reconcile
```

处理逻辑串行。

不得：

```text
one goroutine per Change
```

因为这会导致 Reconcile overlap。

---

# 58. Locking

Adapter mutex 不得在持有期间调用：

```text
Reader.Read
Parser.Parse
Config.Reconcile
```

原则：

```text
lock
 ↓
capture state
 ↓
unlock
 ↓
user/external operation
```

所有外部代码调用必须在锁外。

---

# 59. Context

每次：

```text
Sync
```

以及每次实际 processing operation 都必须接受 Context。

Adapter 不得保存一个长期共享的 request Context 作为所有操作的 Context。

---

# 60. Cancellation

Cancellation 是 cooperative。

不得：

```text
runtime.Goexit
goroutine kill
unsafe termination
```

如果 Read/Parse/Reconcile 支持 context，则向下传递。

---

# 61. API Stability

实现前先检查已有：

```text
Watch API
Config API
Runtime API
```

如果现有 API 足够：

> 不修改已有 API。

如果确实存在缺口：

> STOP。

报告：

```text
API GAP
Affected package:
Missing capability:
Why adapter cannot implement correctly:
Minimal proposed API:
```

不得自行修改 Kernel。

---

# 62. Package Boundary

建议：

```text
extensions/configwatch/
```

依赖：

```text
extensions/watch
extensions/config
runtime
```

不得出现：

```text
runtime → extensions/configwatch
```

Kernel 对 Adapter 必须完全无感知。

---

# 63. Documentation

必须包含：

```text
README.md
```

说明：

1. Adapter 的职责
2. Source → Watch → Adapter → Config → Runtime
3. Initial Sync
4. Delete semantics
5. Error semantics
6. Close semantics
7. 为什么 Adapter 不直接调用 Runtime.Load
8. 为什么 Adapter 不实现 HMR

---

# 64. Forbidden Shortcuts

以下实现即使测试通过，也判定失败：

```text
❌ Adapter 直接 Runtime.Load
❌ Adapter 直接 Fiber.Dispose
❌ Adapter 直接修改 Config Applied
❌ Adapter 保存自己的 Applied Config
❌ Adapter 自己实现 Component lifecycle
❌ Adapter polling
❌ Adapter 调用 Loader 自动加载
❌ Adapter 自动 HMR
❌ Adapter 用 goroutine-per-change
❌ Adapter 无限事件队列
❌ Adapter 在 mutex 中执行 Reconcile
❌ 修改 Kernel API 来适配 Adapter
❌ 为 Adapter 增加第二套生命周期
```

---

# 65. Acceptance Criteria

全部满足才算 PASS：

```text
AC-01  Initial Sync 正确
AC-02  Watch Change 正确进入 Config.Reconcile
AC-03  Adapter 不绕过 Config
AC-04  Duplicate Revision 可抑制
AC-05  高频 Change 可 coalesce
AC-06  Latest State 不丢失
AC-07  Invalid Config 不破坏 Applied
AC-08  Read Failure 不破坏 Applied
AC-09  Delete semantics 正确
AC-10  Recreate 正确
AC-11  Reconcile 不重叠
AC-12  Change/Sync race 安全
AC-13  Close 并发安全
AC-14  无 polling
AC-15  无 goroutine-per-change
AC-16  无第二 lifecycle
AC-17  无 Kernel 修改
AC-18  无 Config 核心语义修改
AC-19  无 Watch 核心语义修改
AC-20  Full integration chain PASS
AC-21  Dependency semantics 未破坏
AC-22  Ownership isolation PASS
AC-23  go test ./... PASS
AC-24  go test -race ./... PASS
AC-25  go vet ./... PASS
```

---

# 66. Implementation Rule

Code Agent 必须遵循：

```text
Spec first
 ↓
Inspect existing APIs
 ↓
Implement Adapter
 ↓
Tests
 ↓
Integration tests
 ↓
Race tests
 ↓
Vet
```

禁止：

```text
发现 API 不够
 ↓
顺手修改 Kernel
```

必须：

```text
发现 API 不够
 ↓
STOP
 ↓
报告 API GAP
```

---

# 67. Completion Report

完成后只需按以下格式报告：

```text
CONFIG WATCH ADAPTER

Status:
PASS / CONDITIONAL PASS / FAIL

Implemented:
- ...

Files:
- ...

API:
- ...

Semantics:
- ...

Tests:
- ...

Integration:
- ...

Race:
- ...

Vet:
- ...

Kernel modified:
YES / NO

Config modified:
YES / NO

Watch modified:
YES / NO

Known Issues:
- ...

API Gaps:
- ...

Acceptance:
AC-01 PASS
...
AC-25 PASS
```

不要自行宣布整个 Runtime 完成。

本阶段完成后，由架构审查决定下一阶段。