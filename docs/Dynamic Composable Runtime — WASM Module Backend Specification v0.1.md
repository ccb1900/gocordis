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
## WASM Module Backend Specification v0.1

**Status:** Implementation Ready  
**Scope:** Loader Extension  
**Dependency:** Runtime Kernel v0.1、Loader Extension v0.1  
**Non-goal:** 不修改 Kernel，不引入第二生命周期系统

---

# 1. 目标

本阶段只验证一个架构事实：

> **WASM 可以作为 Loader 的 Module 实现载体，而不改变 Runtime 对 Component / Fiber 生命周期的唯一控制权。**

目标链路：

```text
.wasm
  ↓
WASM Backend
  ↓
Loader Module
  ↓
Factory
  ↓
Component
  ↓
Runtime.Load()
  ↓
Fiber
  ↓
Active
```

必须证明：

```text
Module ≠ Component ≠ Fiber
```

WASM Backend 只负责：

```text
Artifact → Module
```

不得负责：

```text
Module → Fiber
Module → Provider
Module → Dependency
Module → Lifecycle
```

---

# 2. 架构边界

系统职责严格保持：

```text
Kernel
  └── Fiber / Activation / Dependency / Effect / Ownership

Loader
  └── Artifact → Module

WASM Backend
  └── .wasm → Module implementation

Config
  └── Desired → Component

Runtime
  └── Component → Fiber

HMR
  └── Module replacement policy
```

WASM Backend：

- 可以依赖 Loader
- 可以使用 Loader 的 Module Registry
- 可以实现 Loader Backend 接口
- 不得依赖 Kernel 内部实现
- 不得直接操作 Fiber
- 不得直接操作 Provider
- 不得实现 HMR
- 不得实现 Watch
- 不得实现 Config

---

# 3. v0.1 WASM 模型

v0.1 使用**最小 WASM Contract**。

WASM 模块必须能够被加载并验证。

最低要求：

```text
module is valid WASM
        +
required export exists
```

推荐最小 ABI：

```text
runtime_component_create
runtime_component_destroy
```

但 ABI 的具体编码方式必须封装在 WASM Backend 内部。

**Loader 上层不得知道 WASM ABI 细节。**

---

# 4. Artifact

继续复用 Loader：

```go
type Artifact struct {
    ID      string
    Type    string
    Source  string
    Version string
}
```

WASM Backend 只接受：

```text
Artifact.Type == "wasm"
```

Source 表示 WASM artifact 来源。

v0.1 至少支持：

```text
file:///path/to/module.wasm
```

如果实现为了测试方便支持本地普通路径，也必须保持 Backend 内部处理。

不得把 WASM 文件内容暴露给 Config 或 Runtime。

---

# 5. Module

继续使用 Loader 的 Module：

```go
type Module struct {
    ID      string
    Type    string
    Version string
    Factory config.Factory
}
```

WASM Backend 的核心输出：

```text
WASM artifact
      ↓
Module{
    ID,
    Type,
    Version,
    Factory,
}
```

其中：

```text
Module.Type == "wasm"
```

Factory 是 Runtime/Config 可消费的唯一上层抽象。

---

# 6. WASM Factory

WASM Backend 必须提供一个实现：

```go
type WASMFactory struct {
    // implementation private
}
```

其职责是：

```text
Factory.Create(ComponentConfig)
        ↓
Component
```

Factory 不得：

- 创建 Fiber
- 调用 Runtime.Load
- 修改 Provider
- 修改 Registry
- 修改 Config
- 管理 Component 生命周期之外的 Runtime 生命周期

---

# 7. Component

WASM Component 是 Runtime Component。

其生命周期必须由 Kernel 控制。

模型：

```text
Factory.Create()
       ↓
Component
       ↓
Runtime.Load()
       ↓
Fiber.Loading
       ↓
Fiber.Active
```

WASM Component 可以拥有 Runtime Effect。

例如：

```text
Apply
  ↓
WASM instance 初始化
  ↓
Effect committed
  ↓
Active
```

退出：

```text
Active
  ↓
Unloading
  ↓
Inverse
  ↓
WASM instance destroy
  ↓
Gone
```

**不得由 WASM Backend 自己实现第二套生命周期。**

---

# 8. WASM Instance 与 Activation

WASM instance 属于 Component 的 Activation 资源。

因此：

```text
Fiber
 └── Activation
      └── WASM instance
```

每一次新的 Activation：

```text
Activation #1
    ↓
WASM instance #1
```

再次激活：

```text
Activation #2
    ↓
WASM instance #2
```

不得复用：

```text
WASM instance #1
```

除非未来明确设计实例复用语义。

v0.1 禁止。

---

# 9. Effect 规则

WASM instance 的创建属于 Runtime-managed resource。

必须使用：

```go
ctx.Effect(...)
```

进行生命周期绑定。

语义：

```text
Apply
  ├── create WASM instance
  ├── initialize
  └── register inverse

Unloading
  └── inverse
       └── destroy WASM instance
```

如果创建过程中发生：

```text
create
 ↓
initialize
 ↓
failure
```

已经产生的 Runtime-managed resource 必须立即清理。

不得留下孤儿 WASM instance。

---

# 10. Failure Semantics

以下任何阶段失败：

```text
Artifact validation
WASM decode
WASM instantiate
ABI validation
Factory creation
Component Load
Apply
```

均不得产生：

```text
Active Fiber
```

如果 Component 已经开始 Apply：

```text
Apply failure
    ↓
Kernel unwind
    ↓
Failed
```

WASM Backend 不得自行决定 Fiber 最终状态。

---

# 11. Module 生命周期

WASM Module 遵循 Loader Module 生命周期：

```text
Absent
  ↓
Loaded
  ↓
Absent
```

它与 Fiber 完全独立。

例如：

```text
WASM Module Loaded
        ↓
Component Created
        ↓
Fiber Active
```

此时：

```text
Module = Loaded
Fiber  = Active
```

卸载 Module 时，如果 Fiber 仍在使用：

```text
Unload(Module)
      ↓
ErrModuleInUse
```

不得自动 Dispose Fiber。

---

# 12. Module Usage

WASM Module 必须继续遵循 Loader v0.1 的显式 Usage 机制：

```go
usage.Acquire(module.ID)
```

使用关系：

```text
Module
  ↑
  │ usage
  │
HMR / Component owner / integration
```

v0.1 不允许隐式引用计数。

---

# 13. Factory Registration

WASM Module 加载成功后，可以显式注册 Factory：

```text
WASM Loader
    ↓
Module
    ↓
RegisterFactories
    ↓
FactoryRegistry
```

不得自动修改 FactoryRegistry，除非明确调用：

```go
loader.RegisterFactories(...)
```

这与 Builtin Loader v0.1 保持一致。

---

# 14. WASM Backend 接口

建议：

```go
type Backend interface {
    Load(ctx context.Context, artifact loader.Artifact) (loader.Module, error)
}
```

如果需要资源释放：

```go
type Backend interface {
    Load(ctx context.Context, artifact loader.Artifact) (loader.Module, error)
    Unload(ctx context.Context, module loader.Module) error
    Close() error
    CloseContext(ctx context.Context) error
}
```

但必须注意：

> Backend 的 Module Unload 不能替代 Loader 的 Module 生命周期管理。

最终 Module Registry 的状态仍由 Loader 控制。

---

# 15. Loader 集成

Loader v0.1 当前只有：

```text
Builtin Backend
```

本阶段增加：

```text
WASM Backend
```

最终：

```text
Loader
 ├── Builtin Backend
 └── WASM Backend
```

Loader 负责：

```text
Artifact
  ↓
Backend selection
  ↓
Backend.Load()
  ↓
Module validation
  ↓
Module Registry commit
```

Backend 不得直接写 Module Registry。

---

# 16. Backend Selection

选择依据：

```text
Artifact.Type
```

例如：

```text
builtin → BuiltinBackend
wasm    → WASMBackend
```

不得根据：

```text
文件扩展名
Source 字符串
Config Type
Fiber Type
```

在 Loader 外部隐式决定 Module 类型。

---

# 17. WASM ABI 隔离

WASM ABI 必须位于：

```text
extensions/loader/wasm/
```

或等价的 Backend 私有包。

禁止：

```text
runtime/
config/
hmr/
registry/
event/
scheduler/
```

直接依赖 WASM ABI。

目标：

```text
              ┌─────────────┐
              │    Kernel   │
              └──────┬──────┘
                     │
                 Component
                     │
              ┌──────┴──────┐
              │   Loader    │
              └──────┬──────┘
                     │
              ┌──────┴──────┐
              │ WASM Backend│
              └──────┬──────┘
                     │
                  .wasm
```

---

# 18. v0.1 不实现 WASI

明确禁止：

```text
filesystem
network
stdin/stdout
environment
process
clock
random
```

等 WASI capability。

原因：

本阶段验证的是：

```text
Module abstraction
```

不是：

```text
WASM sandbox/security model
```

---

# 19. v0.1 不实现 Host API

暂时禁止设计复杂 Host API：

```text
host.log()
host.http()
host.fs()
host.database()
host.registry()
host.event()
```

如果测试需要通信，仅允许最小 ABI。

不要提前设计 Capability 系统。

---

# 20. v0.1 不实现 HMR

不得添加：

```text
WASM → HMR
```

本阶段只需要：

```text
WASM v1
   ↓
Loader
   ↓
Module
   ↓
Factory
   ↓
Component
   ↓
Fiber Active
```

WASM HMR 是下一阶段。

---

# 21. v0.1 不实现 Dependency

WASM Component 可以作为普通 Component 被 Runtime 加载。

但本阶段不要求：

```text
WASM Component
    ↓
Provider
    ↓
Consumer
```

Dependency 仍然由 Kernel 管理。

WASM 不得拥有自己的 dependency resolver。

---

# 22. Close

WASM Backend 必须支持关闭。

关闭过程中：

```text
Running
  ↓
Closing
  ↓
Closed
```

不得：

- 强杀 WASM 执行
- 强制终止 goroutine
- 直接修改 Fiber
- 绕过 Loader Usage

如果 WASM Module 仍被使用：

```text
ErrModuleInUse
```

必须保持 Loader 的既有语义。

---

# 23. Context Cancellation

WASM Backend 必须尊重：

```go
context.Context
```

至少在：

```text
Load 前
Decode 前
Instantiate 前
Commit 前
```

检查 cancellation。

但：

> WASM 执行一旦进入不可抢占阶段，不要求 v0.1 实现强制中断。

不得通过 goroutine kill 模拟 cancellation。

---

# 24. Panic Isolation

WASM Backend 内部发生 panic：

```text
panic
  ↓
recover
  ↓
ErrWASMBackendPanic
```

不得：

- 崩溃 Loader
- 破坏 Module Registry
- 影响其他 Module
- 修改 Fiber 状态

---

# 25. Atomicity

WASM Module Load 必须遵循：

```text
validate
   ↓
decode
   ↓
instantiate/prepare
   ↓
construct Module
   ↓
validate Module
   ↓
commit Registry
```

只有全部成功后：

```text
Module = Loaded
```

任何失败：

```text
Module = Absent
```

不得出现：

```text
Registry 中存在半初始化 Module
```

---

# 26. Error Model

至少定义：

```go
var (
    ErrInvalidWASM
    ErrWASMSourceNotFound
    ErrWASMABI
    ErrWASMInstantiate
    ErrWASMFactory
    ErrWASMBackendClosed
)
```

错误必须保留底层原因：

```go
fmt.Errorf("decode wasm: %w", err)
```

不得只返回：

```text
"wasm load failed"
```

---

# 27. 测试用 WASM

仓库必须包含最小测试 fixture。

至少：

```text
valid.wasm
invalid.wasm
missing-export.wasm
```

测试 fixture 不得依赖网络。

推荐直接生成或提交极小 WASM binary。

不要引入大型 WASM 示例项目。

---

# 28. 必须实现的测试

## W-01 Valid WASM

```text
valid.wasm
    ↓
Load
    ↓
Module Loaded
```

PASS。

---

## W-02 Invalid WASM

```text
invalid.wasm
    ↓
Load
    ↓
ErrInvalidWASM
```

Registry 不得变化。

---

## W-03 Missing ABI

```text
valid WASM
    ↓
required export missing
    ↓
ErrWASMABI
```

Module 不得进入 Registry。

---

## W-04 Duplicate Module

加载相同 Module ID：

```text
first → success
second → ErrModuleExists
```

原 Module 不变。

---

## W-05 Factory

验证：

```text
Module.Factory
    ↓
Create(ComponentConfig)
    ↓
Component
```

成功。

---

## W-06 Runtime Integration

必须使用真实 Runtime：

```text
.wasm
 ↓
WASM Loader
 ↓
Module
 ↓
Factory
 ↓
Component
 ↓
Runtime.Load
 ↓
Fiber.Ready
```

PASS。

---

## W-07 Fiber Lifecycle

验证：

```text
Load
 ↓
Active
 ↓
Dispose
 ↓
Gone
```

WASM instance 必须同步遵循 Activation 生命周期。

---

## W-08 Activation Freshness

同一个 Fiber：

```text
Activation #1
    ↓
Gone
    ↓
Activation #2
```

必须产生不同 WASM instance。

禁止实例复用。

---

## W-09 Apply Failure

WASM Component Apply 失败：

```text
Apply
 ↓
failure
 ↓
partial cleanup
 ↓
Failed
```

不得泄漏实例。

---

## W-10 Module/Fiber Orthogonality

验证：

```text
Module Loaded
Fiber Active
```

然后：

```text
Module Unload
```

必须被：

```text
ErrModuleInUse
```

拒绝。

不得自动 Dispose Fiber。

---

## W-11 Usage

验证：

```text
Acquire
 ↓
Unload → ErrModuleInUse

Release
 ↓
Unload → success
```

---

## W-12 Loader Isolation

WASM Load 失败：

```text
Builtin Module
```

仍然正常。

Builtin Load 失败：

```text
WASM Module
```

仍然正常。

---

## W-13 Close

验证：

```text
Backend.Close()
```

之后：

```text
Load → ErrWASMBackendClosed
```

---

## W-14 Race

必须通过：

```bash
go test -race ./...
```

---

# 29. E2E 必须增加

在现有 Integration Suite 中增加至少：

```text
E2E-WASM-01
WASM Artifact → Module

E2E-WASM-02
WASM Module → Factory

E2E-WASM-03
WASM Factory → Component

E2E-WASM-04
Component → Runtime Fiber

E2E-WASM-05
Fiber Active → WASM Instance Active

E2E-WASM-06
Fiber Dispose → WASM Instance Destroy

E2E-WASM-07
Module Unload 被 Usage 阻止

E2E-WASM-08
WASM Failure Isolation

E2E-WASM-09
Activation Identity Isolation

E2E-WASM-10
Builtin/WASM Backend Isolation
```

---

# 30. 必须验证的架构性质

## P-WASM-01 Module/Fiber Orthogonality

```text
Module lifecycle ≠ Fiber lifecycle
```

---

## P-WASM-02 Runtime Authority

WASM Backend 不得改变 Fiber 状态。

---

## P-WASM-03 Activation Ownership

WASM instance 必须属于一个 Activation。

---

## P-WASM-04 Recovery

Fiber Unloading 后：

```text
WASM instance → destroyed
```

---

## P-WASM-05 Failure Conservation

WASM Load failure：

```text
Module Registry unchanged
```

---

## P-WASM-06 Backend Isolation

Backend failure 不得污染 Loader 其他 Backend。

---

# 31. 目录建议

建议：

```text
extensions/
└── loader/
    ├── loader.go
    ├── builtin.go
    ├── usage.go
    └── wasm/
        ├── backend.go
        ├── module.go
        ├── factory.go
        ├── abi.go
        ├── errors.go
        └── wasm_test.go
```

如果现有 Loader 包结构不同，可以保持现有架构，不得为了 WASM 大规模重构 Loader。

---

# 32. 依赖选择

允许引入成熟 WASM Runtime。

但必须满足：

1. 支持当前 Go 版本
2. 不修改 Kernel
3. 不引入第二生命周期系统
4. 不要求 WASI
5. 不要求网络
6. 可以离线测试
7. 能够执行最小 WASM fixture

如果依赖选择存在多个方案：

> Code Agent 不自行扩展架构，应报告候选方案及影响，由架构层决定。

---

# 33. 禁止事项

本阶段禁止：

```text
❌ 修改 Kernel 生命周期
❌ 修改 Fiber 状态模型
❌ 新增 WASM Fiber
❌ 新增 WASM Scheduler
❌ 新增 WASM Dependency Resolver
❌ 新增 WASM Event Bus
❌ 新增 WASM Registry
❌ 新增 WASM HMR
❌ 新增 WASI
❌ 新增网络能力
❌ 新增文件系统能力
❌ 新增权限系统
❌ 新增 Security Policy
❌ 自动 Runtime.Load
❌ 自动 Fiber.Dispose
❌ Backend 直接修改 Module Registry
❌ Backend 直接修改 Provider
❌ 第二套生命周期系统
```

---

# 34. STOP 条件

Code Agent 遇到以下情况必须停止，不得自行修改 Kernel：

```text
1. Loader API 不足以支持 Backend
2. Runtime Component API 不足以绑定 WASM instance
3. Effect 语义无法正确绑定 WASM instance
4. Module Usage 语义无法满足 WASM 生命周期
5. 需要修改 Fiber 状态机
6. 需要修改 Dependency semantics
7. 需要引入第二生命周期系统
8. WASM Runtime 迫使架构违反现有边界
```

报告：

```text
BLOCKED

Reason:
...

Required architectural decision:
...
```

---

# 35. 完成标准

只有同时满足：

```text
go test ./...
go test -race ./...
go vet ./...
```

并且：

```text
W-01 ~ W-14 PASS
E2E-WASM-01 ~ E2E-WASM-10 PASS
P-WASM-01 ~ P-WASM-06 PASS
```

才可以宣布：

```text
WASM MODULE BACKEND v0.1
PASS
```

---

# 36. 下一阶段

WASM Backend v0.1 完成后，不立即继续增加 WASI 或安全能力。

下一阶段优先：

```text
WASM v1
   ↓
Loader
   ↓
Module v1
   ↓
HMR
   ↓
Module v2
   ↓
New Fiber
```

即：

**WASM + HMR Integration Specification v0.1**

届时重点验证：

```text
Module replacement
+
Fiber replacement
+
Activation identity
+
Provider replacement
+
old Module usage release
```

仍然由：

```text
Runtime = 生命周期权威
Loader  = Module 权威
HMR     = Replacement Policy
```

三者保持严格分离。