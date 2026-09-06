# Dynamic Composable Runtime — Loader Extension Specification v0.1

## 1. Objective

Loader Extension v0.1 为 Dynamic Composable Runtime 提供**动态实现加载能力**。

Loader 的核心职责是：

```text
Load Request
    ↓
Resolve Artifact
    ↓
Load Implementation
    ↓
Expose Factory / Component Type
```

Loader 不负责：

- Config Desired State
- Config Reconcile
- File Watch
- HMR
- Component 生命周期
- Provider 生命周期
- Dependency Resolution
- Event Bus
- Scheduler
- Runtime Kernel 状态
- 自动 Retry
- 自动 Reload
- 自动替换正在运行的 Component

Loader 是 Extension，不属于 Kernel。

依赖方向：

```text
Loader → Runtime
Loader → Config        （允许通过适配层集成，但 Kernel 不依赖二者）
```

禁止：

```text
Kernel → Loader
Loader → Kernel internal package
```

---

# 2. Core Model

Loader v0.1 引入四个核心概念：

```text
Artifact
Module
Factory
Loader
```

### Artifact

表示一个待加载的实现来源。

```go
type Artifact struct {
    ID      string
    Type    string
    Source  string
    Version string
}
```

要求：

- `ID` 非空
- `Type` 非空
- `Source` 非空
- `Version` 可为空
- `ID` 是 Loader 内部稳定逻辑身份
- `Type` 是对外暴露的 Component Factory Type
- `Source` 是实现来源，不解释其具体格式

v0.1 不规定 Source 必须是：

- `.so`
- `.dll`
- `.wasm`
- HTTP URL
- OCI image
- Git repository

这些属于具体 Loader 实现或未来 Extension。

---

# 3. Loader Interface

核心接口：

```go
type Loader interface {
    Load(ctx context.Context, artifact Artifact) (*Module, error)
}
```

`Load` 是一次显式操作。

它必须：

1. 校验 Artifact
2. 定位 Source
3. 加载实现
4. 验证实现契约
5. 返回 Module

Loader 不得因为某个 Artifact 加载失败而修改已经成功加载的其它 Module。

---

# 4. Module

```go
type Module struct {
    ID      string
    Type    string
    Version string

    Factory Factory
}
```

其中：

```go
type Factory interface {
    Create(config config.ComponentConfig) (runtime.Component, error)
}
```

注意：

Loader 的 Factory 与 Config Extension 使用的 Factory 必须是**同一语义层的 Factory**。

Loader 可以产生 Factory：

```text
Loader
   ↓
Module
   ↓
Factory
   ↓
Config.FactoryRegistry
   ↓
Config.Reconcile
```

但 Loader 不得直接修改 Config Controller 的 Desired/Applied State。

---

# 5. Module Identity

Module Identity：

```go
type ModuleIdentity struct {
    ID      string
    Version string
}
```

必须满足：

```text
同一个 Module ID + Version
    = 同一个加载实例身份
```

但是：

```text
Component Fiber Identity
≠
Module Identity
```

尤其禁止：

```text
Module Reload
    ↓
直接修改 Fiber
```

Module 与 Fiber 是两个生命周期系统。

---

# 6. Module Lifecycle

Loader v0.1 的 Module 生命周期：

```text
Absent
  ↓ Load
Loaded
  ↓ Unload
Absent
```

失败：

```text
Absent
  ↓ Load
Failed
```

Failed Module 不得被假装为 Loaded。

Loader 不负责 Component 生命周期。

---

# 7. Loaded Module Registry

Loader 必须维护一个 Module Registry。

```go
type ModuleRegistry interface {
    Get(id string) (Module, bool)
    Has(id string) bool
    Snapshot() []Module
}
```

要求：

- ID 唯一
- Snapshot 是不可变快照
- Registry mutation 线性化
- 不允许重复 Module ID
- 不允许通过多个 Module 实例伪造同一个 ID

错误：

```go
var (
    ErrModuleExists       = errors.New("module already exists")
    ErrModuleNotFound     = errors.New("module not found")
    ErrLoaderClosed       = errors.New("loader closed")
    ErrInvalidArtifact    = errors.New("invalid artifact")
    ErrLoadFailed         = errors.New("module load failed")
    ErrInvalidModule      = errors.New("invalid module")
)
```

---

# 8. Load Semantics

执行：

```go
Load(ctx, artifact)
```

时：

```text
Validate Artifact
       ↓
Resolve Source
       ↓
Load
       ↓
Validate Module
       ↓
Linearize Registry Mutation
       ↓
Loaded
```

任何一步失败：

```text
Registry unchanged
```

已经 Loaded 的 Module 不得受到影响。

---

# 9. Duplicate Load

同一个 Module ID 已经 Loaded 时：

```go
Load(same ID)
```

必须返回：

```go
ErrModuleExists
```

不得：

- 覆盖旧 Module
- 自动 Unload
- 自动 Reload
- 自动替换 Factory
- 修改 Config
- 修改 Runtime Fiber

例如：

```text
M1 Loaded
     ↓
Load(M1)
     ↓
ErrModuleExists
```

M1 保持完全有效。

---

# 10. Unload

Loader 必须提供：

```go
Unload(ctx context.Context, id string) error
```

语义：

```text
Loaded Module
      ↓
Unload
      ↓
Absent
```

但是存在一个非常重要的约束：

> Loader 不得 Unload 一个仍被 Config Controller / Runtime Component 使用的 Module。

因此 v0.1 必须定义使用关系检查。

如果 Module 仍然被使用：

```go
ErrModuleInUse
```

并保持 Module Loaded。

---

# 11. Module Usage

Loader 不应该通过“扫描 Fiber”猜测 Module 是否使用中。

必须建立显式 usage registration。

```go
type ModuleUse struct {
    ModuleID string
    OwnerID  string
}
```

接口：

```go
type ModuleUsage interface {
    Acquire(moduleID, ownerID string) error
    Release(moduleID, ownerID string) error
    InUse(moduleID string) bool
}
```

要求：

```text
Acquire
Release
```

均线性化。

同一个：

```text
ModuleID + OwnerID
```

重复 Acquire 应该返回错误，而不是增加隐藏引用计数。

Release 不存在的 ownership 应返回：

```go
ErrModuleUseNotFound
```

---

# 12. Loader 与 Config Integration

Loader 与 Config 的正确关系：

```text
                Loader
                  │
                  ▼
              Module
                  │
                  ▼
               Factory
                  │
                  ▼
        Config.FactoryRegistry
                  │
                  ▼
              Reconcile
                  │
                  ▼
               Runtime
```

Loader 可以提供：

```go
RegisterFactories(registry config.FactoryRegistry) error
```

但该操作必须是显式的。

禁止：

```text
Load Module
    ↓
偷偷修改 Config Registry
```

除非调用方明确执行 RegisterFactories。

---

# 13. Factory Registration Identity

Factory Registry 中：

```text
Type
```

必须唯一。

因此：

```text
Module A
    Type = "foo"

Module B
    Type = "foo"
```

不能同时向同一个 FactoryRegistry 注册。

返回：

```go
config.ErrFactoryExists
```

原 Factory 保持有效。

Loader 不得覆盖已有 Factory。

---

# 14. Module / Factory / Component Identity

三种 Identity 必须严格区分：

```text
Module Identity
    = 加载实现的身份

Factory Type
    = 创建 Component 的逻辑类型

Fiber Identity
    = Runtime Component 实例身份
```

例如：

```text
module:
    ID = camera-driver
    Version = 1.2

factory:
    Type = camera

component:
    ID = camera-front

fiber:
    FiberID = F-123
```

不能互相混用。

---

# 15. Module Version

v0.1 支持 Version 字段，但：

> Version 不产生自动升级语义。

例如：

```text
camera-driver@1.0
camera-driver@1.1
```

不能因为发现 1.1 就自动：

```text
Unload 1.0
Load 1.1
```

也不能自动修改 Config。

Version 在 v0.1 中只用于：

- Module Identity
- 信息展示
- 显式 Load/Resolve

升级策略留给未来 HMR / Deployment Extension。

---

# 16. Source Resolution

Loader 核心只定义：

```go
Source string
```

不定义具体协议。

允许未来实现：

```text
file:///...
http://...
oci://...
wasm://...
builtin://...
```

但 v0.1 至少必须提供一个确定的 Loader 实现。

推荐首先实现：

```text
Builtin / In-Process Loader
```

即：

```go
type BuiltinLoader struct {
    ...
}
```

它从已经注册的 Go Factory 中创建 Module。

这样可以先验证 Loader 抽象，而不提前引入：

- Go plugin
- WASM
- RPC
- 动态链接
- ABI
- 安全沙箱

---

# 17. Builtin Loader

Builtin Loader API：

```go
type BuiltinFactory func() Factory

type BuiltinLoader struct {
    ...
}

func RegisterBuiltin(
    source string,
    factory BuiltinFactory,
) error
```

例如：

```text
Source:
builtin://camera

Factory:
CameraFactory
```

Load：

```text
Artifact
    Source = builtin://camera
         ↓
Builtin Registry
         ↓
Factory
         ↓
Module
```

Builtin Registry 与 Module Registry 是两个不同 Registry。

---

# 18. Why Builtin First

v0.1 禁止直接把动态 `.so/.dll` 加载作为 Kernel/Loader 的基础语义。

原因：

动态链接涉及：

- ABI
- Go runtime compatibility
- symbol resolution
- process isolation
- unload semantics
- memory ownership
- crash isolation

这些都不是 Loader 抽象本身。

因此：

```text
Loader abstraction
        ≠
Go plugin implementation
```

先完成语义，再增加具体 Artifact Loader。

---

# 19. Loader Concurrency

Loader 必须支持：

```text
Load × Load
Load × Unload
Unload × Unload
Load × Close
```

并且：

> 同一个 Module ID 的状态变更必须线性化。

不同 Module ID 可以并发加载。

禁止：

```text
Loader mutex
    ↓
调用 Factory / user code
```

即：

> 不得持有 Loader state lock 执行用户代码。

正确：

```text
lock
  ↓
snapshot / reserve
  ↓
unlock
  ↓
actual load
  ↓
lock
  ↓
commit
```

如果 commit 时状态已经发生冲突：

```text
discard loaded result
return conflict
```

不得破坏已有 Module。

---

# 20. Load Failure

如果加载过程中：

```text
Resolve 成功
Load 成功
Validate Module 失败
```

或者：

```text
Load implementation panic
```

都必须：

```text
Module Registry unchanged
```

panic 必须被隔离：

```go
ErrModuleLoadPanic
```

Loader 自身不能因为一个 Module panic 而崩溃。

---

# 21. Factory Validation

Module Loaded 前必须验证：

```text
Module.ID != ""
Module.Type != ""
Module.Factory != nil
```

否则：

```go
ErrInvalidModule
```

不得进入 Registry。

---

# 22. Close Semantics

Loader：

```text
Running
   ↓ Close
Closing
   ↓
Closed
```

Close 必须：

1. 禁止新的 Load
2. 等待正在进行的 Load 完成或取消
3. Unload 所有当前无使用的 Module
4. 对仍然 In Use 的 Module 返回清理错误
5. 不谎报全部 Module 已经 Gone
6. Close 幂等

如果存在：

```text
Module A → InUse
```

则：

```text
Close()
```

不能偷偷删除 Module Registry entry 并假装 unload 成功。

---

# 23. CloseContext

提供：

```go
CloseContext(ctx context.Context) error
```

如果 Context 超时：

```text
return ctx.Err()
```

但 Loader 状态仍然：

```text
Closing
```

而不是：

```text
Closed
```

除非所有关闭条件真实完成。

---

# 24. Cancellation

Loader 所有可能阻塞的操作必须接收 Context：

```go
Load(ctx, ...)
Unload(ctx, ...)
CloseContext(ctx)
```

Context cancellation 是 cooperative cancellation。

禁止：

- goroutine kill
- unsafe termination
- 强制释放仍在执行代码使用的 Module
- 在代码仍可能执行时卸载其依赖资源

---

# 25. Resource Ownership

Loader 自己产生的 Runtime-managed resources 必须遵循 Effect semantics。

例如：

```text
Load Module
   ↓
打开资源
   ↓
成功
```

如果资源属于 Loader Activation，则必须：

```go
ctx.Effect(...)
```

登记 inverse。

禁止：

```text
Load failed
    ↓
部分资源留在系统中
```

---

# 26. Loader Is Not HMR

明确禁止：

```text
Load A
    ↓
发现 A'
    ↓
自动替换 A
```

这属于 HMR。

Loader 只负责：

```text
A → Loaded
A → Unloaded
```

HMR 才负责：

```text
A → A'
```

以及：

```text
旧 Module / Factory / Component
        ↓
replacement policy
```

---

# 27. Loader Is Not Watch

Loader 不监听：

- 文件系统
- Git
- HTTP
- 配置文件
- Registry

例如：

```text
camera.so 修改
```

Loader 不应该自动重新 Load。

这是 Watch + HMR 的职责。

---

# 28. Loader Is Not Config

Loader 不读取：

```text
config.toml
config.yaml
config.json
```

也不负责：

```text
Desired State
Reconcile
Component Add/Remove
```

Config Extension 负责这些。

---

# 29. Loader Is Not Dependency Manager

Loader 不处理：

```text
A requires B
```

依赖关系由：

```text
Kernel Dependency Graph
```

处理。

Module dependency 与 Component dependency 必须分开。

v0.1 不实现 Module dependency graph。

---

# 30. Registry Stability

Module Registry 自身应保持稳定 identity。

Module churn：

```text
Add Module
Remove Module
```

不得导致使用 Module Registry 的 Consumer Fiber 自动重新激活。

因此推荐：

```text
Stable ModuleRegistry Provider
        │
        └── dynamic Module members
```

与 Registry Extension 的稳定 capability 模式一致。

---

# 31. Snapshot Semantics

```go
Snapshot() []Module
```

必须对应明确的 Registry linearization point。

Snapshot：

- 不得暴露内部可变 slice
- 不得暴露内部 Module Registry
- 返回后 Registry mutation 不得修改已有 Snapshot

Module 本身应视为 immutable descriptor。

---

# 32. No Callback Under Lock

Loader/ModuleRegistry 内部锁期间：

禁止调用：

- Factory
- Component
- User callback
- Context cancellation callback
- 外部 Loader
- 文件系统
- 网络
- IPC

正确模型：

```text
Lock
 ↓
capture state
 ↓
Unlock
 ↓
external operation
 ↓
Lock
 ↓
commit
 ↓
Unlock
```

---

# 33. Error Model

至少定义：

```go
var (
    ErrLoaderClosed       = errors.New("loader closed")
    ErrModuleExists        = errors.New("module already exists")
    ErrModuleNotFound      = errors.New("module not found")
    ErrModuleInUse         = errors.New("module in use")
    ErrInvalidArtifact     = errors.New("invalid artifact")
    ErrInvalidModule       = errors.New("invalid module")
    ErrLoadFailed          = errors.New("module load failed")
    ErrModuleLoadPanic     = errors.New("module load panic")
    ErrModuleUseNotFound   = errors.New("module use not found")
)
```

错误必须支持：

```go
errors.Is(err, ErrModuleExists)
```

不得要求调用方依赖错误字符串。

---

# 34. Ownership Isolation

Loader 只能管理：

```text
Loader-owned Modules
```

不得：

```text
Unload foreign Module
```

如果 Module Registry 是外部注入的：

```text
foreign ownership
```

必须保持隔离。

---

# 35. Relationship With Runtime

Loader 可以创建：

```text
Factory
```

但不得直接创建长期运行 Component Fiber，除非明确由上层调用。

因此禁止：

```text
Loader.Load()
    ↓
自动 Runtime.Load()
```

正确：

```text
Loader.Load()
    ↓
Module.Factory
    ↓
Config Controller
    ↓
Runtime.Load()
```

这样三个生命周期保持正交。

---

# 36. Relationship With Registry Extension

可以使用 Registry Extension 实现：

```text
Module Registry
```

但不得改变 Registry Extension 的语义。

推荐：

```text
Loader
  │
  └── Registry[Module]
```

但：

```text
Module Registry
≠
Factory Registry
```

两者职责不同。

---

# 37. Relationship With Event Extension

v0.1 不要求 Event Integration。

禁止 Loader 自动 Publish：

```text
ModuleLoaded
ModuleUnloaded
```

如果未来需要：

```text
Loader Event Adapter
```

再实现。

Loader 核心不依赖 Event。

---

# 38. Relationship With Scheduler

v0.1 不使用 Scheduler。

禁止：

```text
Scheduler → periodic Load
```

Loader 没有轮询机制。

---

# 39. Module Discovery

v0.1 不实现自动 Discovery。

例如：

```text
扫描目录
扫描 plugins/
扫描网络
```

均不属于核心 Loader。

未来可以：

```text
Discovery Extension
        ↓
Artifact
        ↓
Loader
```

---

# 40. Security Boundary

Loader 不得假设：

```text
Source 是可信的
```

但 v0.1 不实现完整 sandbox/security model。

因此 Loader API 必须把：

```text
Source
```

视为不可信输入，并至少做到：

- 不 panic
- 不接受空 Source
- 不因非法 Source 修改 Registry
- Load failure 不污染已有 Module

具体签名验证、沙箱、权限模型留给未来 Security / WASM Loader。

---

# 41. Builtin Registry Semantics

Builtin Registry：

```go
type BuiltinRegistry interface {
    Register(source string, factory BuiltinFactory) error
    Lookup(source string) (BuiltinFactory, bool)
}
```

要求：

- Source 唯一
- Duplicate → `ErrBuiltinExists`
- 原 Factory 保持有效
- Register/Lookup 并发安全
- Lookup 不执行 Factory
- Factory 创建发生在锁外

---

# 42. Builtin Factory Panic

执行：

```go
factory()
```

时必须 recover panic。

结果：

```go
ErrModuleLoadPanic
```

而不是 Loader crash。

---

# 43. Load Atomicity

一次 Load 操作的 Registry 可观察结果只能是：

```text
Load Before:
    Absent

Load After:
    Loaded
```

或者：

```text
Load failed:
    Absent
```

禁止观察到：

```text
Partially Loaded
```

Module Registry 层面必须 atomic。

---

# 44. Unload Atomicity

一次 Unload：

```text
Loaded
   ↓
Absent
```

如果 Unload 失败：

```text
Loaded
```

保持不变。

不能：

```text
remove registry entry
    ↓
cleanup failed
```

导致 Registry 认为已经 Unloaded。

---

# 45. Usage and Unload Ordering

必须满足：

```text
Release last user
        ↓
Unload
```

不能：

```text
Unload
  ↓
Component still using Factory
```

因此：

```text
Module InUse
```

是 Loader Unload 的硬约束。

---

# 46. Config Integration Rule

如果 Config Controller 使用 Loader Module：

```text
Config Component
    Type = "camera"
        ↓
FactoryRegistry
        ↓
Factory from Module
```

那么 Module Usage 应当由明确的上层 integration adapter 管理。

Loader 不得通过：

```text
Config.Applied
```

反向推断 usage。

Config 和 Loader 的 ownership graph 必须保持独立。

---

# 47. Replacement Boundary

v0.1：

```text
Module A
```

和：

```text
Module A'
```

即使：

```text
ID 相同
Version 不同
```

也不得通过：

```go
Load(A')
```

直接替换 A。

必须显式：

```text
Unload(A)
Load(A')
```

或者由未来：

```text
HMR / Deployment
```

执行 replacement transaction。

---

# 48. No Implicit Reload

以下行为全部禁止：

```text
Load 同 Source
Load 同 ID
Source timestamp changed
Version changed
Factory changed
```

自动导致 Reload。

所有 Reload 必须显式。

---

# 49. Lifecycle Independence

Loader Module 生命周期与 Kernel Fiber 生命周期完全独立：

```text
Module:
Absent → Loaded → Absent

Fiber:
Pending → Loading → Active → Unloading → Gone
```

禁止建立隐含：

```text
Module Loaded == Fiber Active
```

或：

```text
Module Unloaded == Fiber Gone
```

二者没有这种等价关系。

---

# 50. Failure Isolation

一个 Module：

```text
Load Failure
```

不能影响：

- 其它 Module
- Factory Registry 中已有 Factory
- Config Desired State
- Config Applied State
- Runtime Fiber
- Kernel Provider Identity

Loader failure isolation 是 v0.1 硬约束。

---

# 51. Close Failure

Close 时如果：

```text
Module A unload success
Module B unload failure
Module C unload success
```

结果必须：

```text
A = Absent
B = Loaded
C = Absent
```

并返回聚合错误。

不能因为 B 失败而停止清理 C。

---

# 52. Testing — Contract Tests

必须实现：

### L1 — Load

Absent Module → Loaded。

### L2 — Load Failure

失败后 Registry 不变。

### L3 — Duplicate Load

Duplicate ID → `ErrModuleExists`，原 Module 不变。

### L4 — Invalid Artifact

非法 Artifact 不产生 mutation。

### L5 — Invalid Module

非法 Factory / Type / ID 不进入 Registry。

### L6 — Factory Panic

Factory panic → `ErrModuleLoadPanic`。

### L7 — Unload

Loaded → Absent。

### L8 — Unload Missing

Missing → `ErrModuleNotFound`。

### L9 — Unload In Use

In-use Module 无法 Unload。

### L10 — Usage Acquire

Acquire 后 Module 为 InUse。

### L11 — Usage Release

最后一个 owner Release 后 Module 可 Unload。

### L12 — Duplicate Usage

相同 ModuleID + OwnerID 重复 Acquire → error。

### L13 — Usage Isolation

Release foreign owner 不影响合法 owner。

### L14 — Snapshot

Snapshot 不受后续 Registry mutation 影响。

### L15 — Factory Registration

Factory 可以显式注册到 Config FactoryRegistry。

### L16 — Factory Duplicate

相同 Type 注册失败，原 Factory 保持有效。

### L17 — Module Identity

Module ID/Version identity 正确。

### L18 — Module / Fiber Independence

Module Load/Unload 不自动改变 Fiber 生命周期。

### L19 — Load Isolation

一个 Module Load failure 不影响其它 Module。

### L20 — Close Cleanup

Close 清理全部可清理 Module。

---

# 53. Concurrency Tests

至少：

### P1 — Concurrent Load

不同 Module 可以并发 Load。

### P2 — Same ID Linearization

并发 Load 相同 ID：

```text
最多一个成功
其余 ErrModuleExists
```

### P3 — Load × Unload

并发 Load/Unload 后 Registry 状态满足某个合法线性化顺序。

### P4 — Usage Race

并发 Acquire/Release 无 ownership corruption。

### P5 — Close Race

Load × Close 不产生：

- panic
- 永久阻塞
- registry corruption

### P6 — Snapshot Race

Snapshot 与 mutation 并发无 race。

### P7 — Race Detector

```bash
go test -race ./...
```

---

# 54. Property Tests

### Property P1 — Registry Preservation

失败操作不改变已有 Module 集合。

### Property P2 — Duplicate Preservation

Duplicate Load 不改变原 Module。

### Property P3 — Ownership Conservation

```text
Acquire count == Release count
```

在合法操作序列结束后成立。

### Property P4 — Snapshot Isolation

旧 Snapshot 永远不随 Registry 后续 mutation 改变。

### Property P5 — Module/Fiber Separation

任何 Loader 操作都不会直接修改 Kernel Fiber State。

### Property P6 — Failure Isolation

任意 Module Load Failure 不影响其它 Loaded Modules。

### Property P7 — Close Idempotence

重复 Close 不产生额外 mutation 或 panic。

### Property P8 — Linearizability

并发 Module Registry 操作等价于某个合法串行执行顺序。

---

# 55. Required API

Code Agent 至少实现：

```go
type Artifact struct {
    ID      string
    Type    string
    Source  string
    Version string
}

type Module struct {
    ID      string
    Type    string
    Version string
    Factory Factory
}

type Loader interface {
    Load(context.Context, Artifact) (*Module, error)
    Unload(context.Context, string) error
    Close() error
    CloseContext(context.Context) error
}

type ModuleRegistry interface {
    Get(string) (Module, bool)
    Has(string) bool
    Snapshot() []Module
}

type ModuleUsage interface {
    Acquire(moduleID, ownerID string) error
    Release(moduleID, ownerID string) error
    InUse(moduleID string) bool
}
```

Builtin：

```go
type BuiltinFactory func() Factory

type BuiltinRegistry interface {
    Register(string, BuiltinFactory) error
    Lookup(string) (BuiltinFactory, bool)
}
```

---

# 56. Package Boundary

推荐：

```text
extensions/
    loader/
        loader.go
        builtin.go
        registry.go
        usage.go
        loader_test.go
        loader_concurrency_test.go
```

禁止：

```text
runtime/
    loader/
```

Kernel 不得 import：

```text
extensions/loader
```

---

# 57. Implementation Constraint

Code Agent 必须：

1. 优先复用现有 Runtime / Registry / Config public API。
2. 不修改 Kernel。
3. 不修改 Registry/Event/Scheduler/Config 已完成语义。
4. 如果发现 Runtime API 不足：
   - STOP
   - 报告 API gap
   - 不自行修改 Kernel。
5. 不提前实现 Watch。
6. 不提前实现 HMR。
7. 不实现 WASM。
8. 不实现动态 Go plugin。
9. 不实现网络加载。
10. 不引入第二套生命周期系统。

---

# 58. Implementation Preference

v0.1 推荐：

```text
Builtin Loader
```

作为唯一 concrete Loader。

不要为了“动态加载”而过早引入：

```text
plugin.Open
WASM runtime
RPC
gRPC
HTTP
容器
进程管理
```

这些都会把 Loader 的语义问题与执行载体问题混在一起。

首先证明：

```text
Artifact
  ↓
Loader
  ↓
Module
  ↓
Factory
  ↓
Config FactoryRegistry
  ↓
Config Reconcile
  ↓
Runtime
```

这一条链成立。

---

# 59. Architecture Acceptance

Loader Extension v0.1 完成的定义不是：

> “能够加载某种动态库。”

而是：

> “Runtime 可以安全地将一个外部实现转换成具有明确 Identity、Ownership、Usage、Lifecycle 的 Module，并且该 Module 可以显式进入 Config Factory Registry，而不侵入 Kernel 生命周期。”

必须满足：

```text
Kernel
  ↑
Config ← Loader
  ↑
Application
```

而不是：

```text
Kernel
  ├── Loader
  ├── Config
  ├── Watch
  └── HMR
```

Kernel 保持最小。

---

# 60. Forbidden Behaviors

以下任一项出现均视为 FAIL：

- 修改 Kernel 实现以支持 Loader
- Loader 直接修改 Fiber State
- Loader 直接修改 Provider Map
- Load 自动 Runtime.Load
- Unload 自动 Dispose Fiber
- Load 自动 Reload
- Source 改变自动 Reload
- Version 改变自动 Reload
- Watch 目录
- 自动 Discovery
- 自动注册 Config Factory
- Factory 覆盖已有 Type
- Module Registry 与 Factory Registry 混为一体
- Module Dependency 模拟 Component Dependency
- Module Unload 时忽略 InUse
- Unload 失败却删除 Registry entry
- Load 失败却留下半成品 Module
- 用户代码在 Loader lock 内执行
- panic 泄漏到 Loader 外部
- goroutine kill
- polling
- global singleton
- 引入第二套 lifecycle state machine
- 实现 HMR/WASM/HTTP Loader 作为本阶段前置依赖

---

# 61. Completion Report

实现完成后，必须严格按以下格式报告：

```text
LOADER EXTENSION

PASS / CONDITIONAL PASS / FAIL

1. 修改文件
2. 新增 API
3. Artifact / Module model
4. Module Identity semantics
5. Load semantics
6. Unload semantics
7. Usage / Ownership semantics
8. Module Registry semantics
9. Builtin Loader semantics
10. Config integration
11. Concurrency semantics
12. Close semantics
13. Failure isolation
14. Contract tests L1–L20
15. Property tests P1–P8
16. Kernel 是否修改
17. Registry/Event/Scheduler/Config 是否修改
18. go test ./...
19. go test -race ./...
20. go vet ./...
21. 未解决问题

如果发现 P0/P1 级语义冲突，必须明确指出：
- Spec 条款
- 实现行为
- 冲突原因
- 是否修改实现
- 是否需要修改 Spec

完成 Loader Extension 后立即停止。

不得进入：
- Watch
- HMR
- WASM
- HTTP Loader
- RPC Loader
- Discovery
- Deployment
```

# 62. Architect Review Rule

Code Agent 的职责：

```text
Implement
Test
Report
```

Architect 的职责：

```text
Define semantics
Review implementation
Resolve semantic conflicts
Accept / Reject
```

Code Agent 不得自行重新定义：

- Module Identity
- Usage semantics
- Unload semantics
- Config integration semantics
- Replacement semantics
- Lifecycle semantics

如果实现过程中认为 Spec 不合理：

```text
STOP → report → await architectural decision
```

不得自行修改语义后继续实现。

# 63. Completion Criterion

Loader Extension 只有在：

```text
Contract Tests      PASS
Property Tests      PASS
Race Test           PASS
Vet                 PASS
Kernel unchanged    PASS
Config semantics    unchanged
No HMR              PASS
No Watch            PASS
```

全部满足时，才可以宣布：

```text
LOADER EXTENSION
PASS
```

然后由 Architect 决定下一阶段。