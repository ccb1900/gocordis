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
## Module Identity & Artifact Type Separation Specification v0.1

**Status:** Implementation Ready  
**Scope:** Loader / Config / WASM Backend compatibility  
**Purpose:** 解耦 Artifact Backend Type 与 Module Logical Type  
**Kernel:** 不修改  
**Lifecycle:** 不修改  
**前置：** Loader v0.1、Config v0.1、WASM Backend v0.1

---

# 1. 目标

当前模型存在一个架构问题：

```text
Artifact.Type
    ↓
Backend Selection
    ↓
Module.Type
    ↓
FactoryRegistry
```

当前实现中 `Artifact.Type == Module.Type == "wasm"`，导致多个 WASM Module 无法自然拥有不同的 Factory Type。

本规范正式拆分两个概念：

```text
Backend Type
    = 如何加载 Artifact

Logical Module Type
    = Module 在 Runtime/Application 层代表什么
```

目标：

```text
camera.wasm
    Artifact.BackendType = "wasm"
             ↓
       WASM Backend
             ↓
    Module.Type = "industrial.camera"
             ↓
       FactoryRegistry
             ↓
        Component
             ↓
          Fiber
```

---

# 2. 非目标

本阶段不实现：

- WASM HMR
- WASI
- Capability Security
- 网络
- 文件系统
- Dependency Resolver
- Module Dependency
- Module Version Upgrade
- Registry 自动发现
- 新生命周期系统
- Kernel API 重构

---

# 3. 核心原则

必须成立：

```text
BackendType ≠ ModuleType
```

并且：

```text
Artifact → Backend → Module → Factory → Component → Fiber
```

每一层只消费自己职责范围内的身份信息。

---

# 4. Backend Type

定义：

```go
type BackendType string
```

语义：

> BackendType 描述 Artifact 应由哪一种 Loader Backend 解释。

例如：

```text
builtin
wasm
```

未来可以存在：

```text
http
oci
remote
native
```

但本阶段只要求：

```text
builtin
wasm
```

---

# 5. Logical Module Type

定义：

```go
type ModuleType string
```

语义：

> ModuleType 描述 Loader 产生的 Module 在应用语义上的逻辑类型。

例如：

```text
industrial.camera
industrial.sensor
automotive.can
storage.s3
agent.tool
```

ModuleType 不参与 Backend 选择。

---

# 6. Artifact

将 Artifact 语义明确为：

```go
type Artifact struct {
    ID          string
    BackendType BackendType
    Source      string
    Version     string
}
```

如果为了兼容已有 API 暂时保留 `Type` 字段：

```go
type Artifact struct {
    ID          string
    Type        string // deprecated compatibility field
    BackendType BackendType
    Source      string
    Version     string
}
```

则必须定义唯一规范值：

```text
BackendType = canonical source
Type        = compatibility alias
```

不得让两者长期出现不同值。

---

# 7. Artifact Type Compatibility

如果保留旧字段 `Artifact.Type`：

### Case A

```text
Type = ""
BackendType = "wasm"
```

允许。

### Case B

```text
Type = "wasm"
BackendType = ""
```

允许，并转换为：

```text
BackendType = "wasm"
```

### Case C

```text
Type = "wasm"
BackendType = "wasm"
```

允许。

### Case D

```text
Type = "wasm"
BackendType = "builtin"
```

必须：

```text
ErrArtifactTypeConflict
```

不得猜测用户意图。

---

# 8. Module

Module 必须使用 Logical Module Type：

```go
type Module struct {
    ID      string
    Type    ModuleType
    Version string
    Factory config.Factory
}
```

这里的：

```text
Module.Type
```

**不再表示 Backend Type。**

例如：

```text
Artifact:
    BackendType = "wasm"

Module:
    Type = "industrial.camera"
```

这是合法且期望的状态。

---

# 9. Module Identity

Module Identity 仍然：

```go
type ModuleIdentity struct {
    ID      string
    Version string
}
```

本阶段不改变 Loader 对 Module ID 唯一性的既有语义。

即：

```text
(ID, Version)
```

用于描述 Module identity。

但 Registry 的唯一性仍遵循 Loader v0.1：

```text
Module.ID unique
```

Version 不允许绕过 duplicate ID 规则。

---

# 10. Backend Identity

Backend 本身由：

```text
BackendType
```

标识。

Loader 中：

```text
BackendRegistry[BackendType]
```

例如：

```text
"builtin" → Builtin Backend
"wasm"    → WASM Backend
```

两个 Backend Type 不得冲突。

---

# 11. Backend Selection

Loader Load 流程必须改成：

```text
Artifact
   ↓
Normalize Artifact
   ↓
Resolve BackendType
   ↓
Backend Registry Lookup
   ↓
Backend.Load()
   ↓
Module Validation
   ↓
Module Registry Commit
```

Backend 选择只能使用：

```text
Artifact.BackendType
```

不能使用：

```text
Module.Type
Factory Type
Component Type
File extension
```

---

# 12. Backend 输出约束

Backend 必须明确产生：

```text
Module.Type
```

例如 WASM：

```text
Artifact.BackendType = "wasm"

WASM Backend
    ↓

Module{
    ID: "...",
    Type: "industrial.camera",
    Version: "...",
    Factory: ...
}
```

Backend 可以从：

- Artifact metadata
- WASM manifest
- Backend-specific metadata
- 显式配置

确定 ModuleType。

但不得把：

```text
BackendType
```

自动复制成：

```text
ModuleType
```

因此禁止：

```text
Module.Type = Artifact.BackendType
```

作为通用 Loader 行为。

---

# 13. Module Type 来源

v0.1 必须定义一种确定性的来源。

推荐 WASM 使用最小 manifest metadata。

例如逻辑概念：

```text
ModuleType = industrial.camera
```

具体 manifest 编码属于 WASM Backend 内部实现。

Loader 不解析 WASM manifest。

---

# 14. Builtin Backend

Builtin Backend 也必须适配新的语义。

例如：

```text
Artifact.BackendType = "builtin"
```

产生：

```text
Module.Type = "industrial.camera"
```

而不是：

```text
Module.Type = "builtin"
```

这样 Builtin 和 WASM 在 Module 上层完全统一。

---

# 15. Factory Registry

Factory Registry 的 key 必须是：

```text
Module.Type
```

即：

```go
FactoryRegistry.Register(
    "industrial.camera",
    factory,
)
```

不得使用：

```text
Artifact.BackendType
```

作为 Factory Registry key。

---

# 16. 多 WASM Module

以下场景必须合法：

```text
camera.wasm
BackendType = wasm
ModuleType  = industrial.camera

sensor.wasm
BackendType = wasm
ModuleType  = industrial.sensor

can.wasm
BackendType = wasm
ModuleType  = automotive.can
```

三个 Module 可以同时 Loaded：

```text
Module Registry
├── camera
├── sensor
└── can
```

三个 Factory Type：

```text
industrial.camera
industrial.sensor
automotive.can
```

不得产生 Factory Type 冲突。

---

# 17. 同 Logical Module Type 冲突

以下场景：

```text
camera-a.wasm
ModuleType = industrial.camera

camera-b.wasm
ModuleType = industrial.camera
```

允许两个 Module Loaded。

但是：

```text
RegisterFactories
```

到同一 Factory Type 时：

```text
FactoryRegistry.Register("industrial.camera", ...)
```

第二次必须遵循 Config v0.1：

```text
config.ErrFactoryExists
```

并保留第一个 Factory。

Module Registry 不得因此损坏。

---

# 18. Module Registry 与 Factory Registry 独立

必须保持：

```text
Module Registry
    ≠
Factory Registry
```

因此：

```text
Module Load
```

不等于：

```text
Factory Register
```

仍然需要显式：

```go
loader.RegisterFactories(...)
```

或等价 API。

---

# 19. Module Load 不自动注册 Factory

禁止：

```text
Load Artifact
    ↓
Module Loaded
    ↓
自动 FactoryRegistry.Register()
```

原因：

Loader 与 Config 的职责必须继续分离。

正确：

```text
Load
 ↓
Module

显式 RegisterFactories
 ↓
FactoryRegistry
```

---

# 20. Config Type

Config 中：

```go
type ComponentConfig struct {
    ID     string
    Type   string
    Config map[string]any
}
```

这里的：

```text
ComponentConfig.Type
```

必须解释为：

```text
Logical Component / Factory Type
```

因此它应该与：

```text
Module.Type
```

兼容。

例如：

```text
Module.Type
    = "industrial.camera"

ComponentConfig.Type
    = "industrial.camera"
```

合法。

---

# 21. Config 不知道 Backend Type

Config 不得依赖：

```text
wasm
builtin
http
oci
```

Config 只知道：

```text
industrial.camera
```

因此：

```text
Config → Factory
```

而不是：

```text
Config → WASM Backend
```

---

# 22. Loader 不知道 ComponentConfig

Loader 不得为了解决 Type 映射而依赖 Config Component semantics。

禁止：

```text
Loader
  ↓
Config.ComponentConfig
```

Loader 只负责：

```text
Artifact → Module
```

---

# 23. WASM Manifest

WASM Backend 可以定义最小 metadata：

```text
module_type
```

例如：

```text
module_type = "industrial.camera"
```

必须满足：

```text
non-empty
valid logical type
deterministic
```

如果缺失：

```text
ErrInvalidModuleType
```

或者等价明确错误。

不得 fallback：

```text
"wasm"
```

因为这会重新混淆两个 namespace。

---

# 24. Logical Type Namespace

ModuleType 使用独立 namespace。

建议约束：

```text
^[a-z][a-z0-9]*(\.[a-z0-9_-]+)+$
```

例如：

```text
industrial.camera
industrial.sensor
automotive.can
storage.s3
```

本阶段可以采用更宽松实现，但必须：

- 非空
- 去除首尾空白后不能为空
- 不允许隐式 canonicalization 导致冲突

如果决定采用严格 grammar，必须补齐测试。

---

# 25. Canonicalization

v0.1 不自动：

```text
lowercase
trim
replace
normalize
```

例如：

```text
Industrial.Camera
industrial.camera
```

视为不同 Logical Type。

这样避免隐藏语义转换。

如果后续需要 canonicalization，应单独定义规范。

---

# 26. HMR 兼容性

本阶段不实现 HMR，但必须保证未来兼容。

未来：

```text
Module v1
Type = industrial.camera
```

替换为：

```text
Module v2
Type = industrial.camera
```

应该能够：

```text
old Fiber
    ↓
new Fiber
```

而不因为 BackendType：

```text
wasm
```

参与 Component Type 判定而产生错误。

---

# 27. Backend Replacement

未来可以：

```text
camera.wasm
BackendType = wasm
ModuleType  = industrial.camera
```

替换为：

```text
camera.builtin
BackendType = builtin
ModuleType  = industrial.camera
```

从 Loader/Config 语义看，这是：

```text
same logical type
different implementation backend
```

本阶段不实现替换策略，但模型必须允许该状态。

---

# 28. Provider Identity 不受 Type 解耦影响

Kernel Provider Identity 仍然：

```go
ProviderIdentity{
    FiberID,
    ActivationID,
}
```

不能改成：

```text
BackendType
ModuleType
```

Provider replacement 仍由 Fiber/Activation identity 决定。

---

# 29. Dependency 不使用 BackendType

Dependency Key：

```text
Capability Key
```

不得：

```text
dependsOn("wasm")
```

来表达：

```text
dependsOn("industrial.camera")
```

Backend Type 永远不是 Capability identity。

---

# 30. API 兼容策略

优先选择：

```text
新增明确字段
+
保持旧 API 编译兼容
```

但不能永久保留双语义。

推荐：

```go
type Artifact struct {
    ID          string
    Type        string
    BackendType BackendType
    Source      string
    Version     string
}
```

并定义：

```text
Type
    = deprecated compatibility alias

BackendType
    = canonical field
```

如果现有仓库已经可以接受 breaking API，则可以直接删除旧 `Type`。

**Code Agent 不得自行决定 breaking 与 non-breaking 策略。**

如果实现判断需要 breaking change：

```text
STOP
```

并报告影响范围。

---

# 31. Loader Backend Registry

推荐内部模型：

```go
type BackendType string

type Backend interface {
    Load(ctx context.Context, artifact Artifact) (Module, error)
}
```

Loader：

```go
RegisterBackend(
    backendType BackendType,
    backend Backend,
) error
```

必须保持：

```text
duplicate BackendType
    → ErrBackendExists
```

---

# 32. Validation

Artifact validation：

```text
ID non-empty
BackendType non-empty
Source non-empty
```

Module validation：

```text
ID non-empty
Type non-empty
Version valid according to existing semantics
Factory non-nil
```

BackendType 和 ModuleType 不得互相替代。

---

# 33. Error Model

至少新增：

```go
ErrArtifactTypeConflict
ErrInvalidBackendType
ErrInvalidModuleType
```

已有：

```text
ErrInvalidBackend
ErrBackendExists
ErrModuleExists
```

继续保持。

错误必须可以通过：

```go
errors.Is
```

判断。

---

# 34. Concurrency

必须保持 Loader v0.1 的并发语义：

- Backend Registry mutation linearizable
- Module Registry mutation linearizable
- Factory Registry mutation linearizable
- 不得持有 Loader lock 调用 Backend user code
- 不得持有 Registry lock 调用 Factory user code
- Backend Load 可以并发
- 不同 Backend 可以并发
- 同 Module ID 的最终 commit 必须线性化

---

# 35. Failure Isolation

如果：

```text
WASM Backend.Load()
```

失败：

```text
Builtin Backend
Module Registry
Factory Registry
```

不得被污染。

如果：

```text
FactoryRegistry.Register()
```

失败：

```text
Module Registry
```

不得回滚。

继续遵循：

```text
Module lifecycle
≠
Factory lifecycle
```

---

# 36. WASM Backend 修正

当前 WASM Backend 中：

```text
Module.Type = "wasm"
```

必须修改。

必须改成：

```text
Module.Type = logical module type
```

来源由 WASM Backend manifest/metadata 决定。

如果测试 fixture 没有 logical type metadata：

> 必须增加 fixture metadata，而不是 fallback 到 `"wasm"`。

---

# 37. Builtin Backend 修正

当前 Builtin 测试 fixture 必须明确：

```text
BackendType = builtin
ModuleType = test.camera
```

不得使用：

```text
ModuleType = builtin
```

作为默认语义。

---

# 38. HMR 现有 API 兼容

本阶段检查 HMR：

```text
Target.Artifact
```

如果 HMR 只消费：

```text
Artifact
```

则不应该自行解析：

```text
Module.Type
```

HMR 仍然：

```text
Artifact → Loader
```

然后通过 Loader Module 获得 Logical Type。

如果现有 HMR API 假定：

```text
Artifact.Type == Module.Type
```

则：

```text
STOP
```

不要在 HMR 包内偷偷增加映射规则。

---

# 39. Config 现有 API 兼容

检查 Config：

```text
ComponentConfig.Type
```

必须继续表示 Factory Type。

如果 Config 当前依赖：

```text
Artifact.Type
```

则：

```text
STOP
```

并报告调用链。

不得通过：

```text
if type == "wasm"
```

临时修补。

---

# 40. 测试要求

## T-01 Backend Selection

```text
Artifact.BackendType = wasm
```

必须选择 WASM Backend。

---

## T-02 Logical Module Type

```text
Artifact.BackendType = wasm
Module.Type = industrial.camera
```

必须成功。

---

## T-03 Backend ≠ Module Type

明确断言：

```text
BackendType != ModuleType
```

可以同时成立。

---

## T-04 Multiple WASM Modules

同时加载：

```text
camera.wasm → industrial.camera
sensor.wasm → industrial.sensor
can.wasm    → automotive.can
```

全部成功。

---

## T-05 Factory Registration

三个 Module：

```text
industrial.camera
industrial.sensor
automotive.can
```

分别注册 Factory。

全部成功。

---

## T-06 Same Logical Type

两个不同 WASM Module：

```text
camera-v1.wasm → industrial.camera
camera-v2.wasm → industrial.camera
```

Module Registry 均可 Loaded。

Factory 注册第二个时：

```text
ErrFactoryExists
```

原 Factory 保留。

---

## T-07 Backend Independence

```text
builtin → industrial.camera
wasm    → industrial.sensor
```

同时 Loaded。

无冲突。

---

## T-08 Same Logical Type Different Backend

```text
builtin → industrial.camera
wasm    → industrial.camera
```

两个 Module 可以 Loaded。

Factory Registry 冲突遵循既有 Factory semantics。

---

## T-09 Config Integration

```text
Module.Type
    ↓
FactoryRegistry
    ↓
ComponentConfig.Type
    ↓
Factory.Create
    ↓
Runtime.Load
    ↓
Fiber.Active
```

必须成功。

---

## T-10 WASM Runtime Integration

真实链路：

```text
.wasm
 ↓
WASM Backend
 ↓
Module
 ↓
Factory
 ↓
Component
 ↓
Runtime
 ↓
Fiber
```

Module.Type 必须是 Logical Type。

---

## T-11 HMR Compatibility

现有 HMR 测试全部保持通过。

如果需要修改 HMR：

必须保持：

```text
HMR = replacement policy
```

而不是 Type resolver。

---

## T-12 Backward Compatibility

所有既有 Loader/Builtin/Config/HMR 测试必须保持通过。

---

# 41. Property Tests

## P-TYPE-01

BackendType 决定 Backend。

---

## P-TYPE-02

ModuleType 决定 Factory。

---

## P-TYPE-03

改变 BackendType 不应隐式改变 ModuleType。

---

## P-TYPE-04

改变 ModuleType 不应改变 Backend selection。

---

## P-TYPE-05

多个 Backend 可以产生相同 ModuleType。

---

## P-TYPE-06

一个 Backend 可以产生多个 ModuleType。

---

## P-TYPE-07

Module Registry 与 Factory Registry 独立。

---

## P-TYPE-08

Type conflict 不污染已有 Registry state。

---

# 42. E2E

新增至少：

```text
E2E-TYPE-01
WASM Backend → industrial.camera

E2E-TYPE-02
WASM Backend → industrial.sensor

E2E-TYPE-03
Builtin Backend → industrial.camera

E2E-TYPE-04
WASM + Builtin same logical type

E2E-TYPE-05
Three WASM logical types concurrently

E2E-TYPE-06
Logical type → Config Factory

E2E-TYPE-07
Logical type → Component → Fiber

E2E-TYPE-08
Factory conflict isolation

E2E-TYPE-09
HMR regression

E2E-TYPE-10
Full existing integration regression
```

---

# 43. 禁止事项

```text
❌ Module.Type = BackendType
❌ FactoryRegistry keyed by BackendType
❌ Config 根据 BackendType 创建 Component
❌ WASM Backend 直接操作 FactoryRegistry
❌ Loader 直接操作 Component
❌ Backend 创建 Fiber
❌ ModuleType 参与 Backend selection
❌ BackendType 参与 Dependency identity
❌ BackendType 参与 Provider identity
❌ HMR 自己实现 Type resolver
❌ 通过 "if wasm" 修补架构
❌ 修改 Kernel
❌ 修改 Fiber 状态机
❌ 引入第二生命周期
```

---

# 44. STOP 条件

Code Agent 遇到：

```text
1. 现有 API 无法在不破坏语义的情况下完成解耦
2. HMR 假定 Artifact.Type == Module.Type
3. Config 假定 Artifact.Type == Factory Type
4. Loader 需要直接依赖 Config 才能完成映射
5. 需要修改 Kernel
6. 需要修改 Fiber 生命周期
7. 需要改变 Provider Identity
8. 需要改变 Dependency semantics
```

必须：

```text
STOP
```

并报告：

```text
Conflict:
Current behavior:
Required change:
Affected packages:
Compatibility impact:
```

不得自行发明兼容层语义。

---

# 45. 验收 Gate

### Gate A — Type Semantics

```text
BackendType / ModuleType
```

概念完全分离。

### Gate B — Registry Semantics

```text
Backend Registry
Module Registry
Factory Registry
```

三者职责不混淆。

### Gate C — E2E

```text
Artifact
 ↓
Backend
 ↓
Module
 ↓
Factory
 ↓
Component
 ↓
Fiber
```

完整通过。

### Gate D — Regression

```bash
go test ./...
go test -race ./...
go vet ./...
```

全部 PASS。

---

# 46. 完成判定

必须：

```text
T-01 ~ T-12      PASS
P-TYPE-01 ~ 08   PASS
E2E-TYPE-01 ~ 10 PASS
```

并且：

```text
go test ./...
go test -race ./...
go vet ./...
```

全部 PASS。

最终报告必须明确：

```text
BackendType ≠ ModuleType
```

并提供至少一个：

```text
WASM Backend → ModuleType A
WASM Backend → ModuleType B
```

的真实测试证据。

---

# 47. 最终目标模型

完成后系统应该稳定在：

```text
                         ┌───────────────┐
                         │    Artifact   │
                         └───────┬───────┘
                                 │
                         BackendType
                                 │
                  ┌──────────────┴──────────────┐
                  │                             │
             WASM Backend                 Builtin Backend
                  │                             │
                  └──────────────┬──────────────┘
                                 │
                              Module
                                 │
                             ModuleType
                                 │
                         ┌───────┴───────┐
                         │ FactoryRegistry│
                         └───────┬───────┘
                                 │
                             Component
                                 │
                              Runtime
                                 │
                               Fiber
                                 │
                            Activation
```

最终架构原则：

> **BackendType 决定“怎么加载”；ModuleType 决定“它是什么”；Factory 决定“如何构造 Component”；Runtime 决定“如何运行 Component”。**