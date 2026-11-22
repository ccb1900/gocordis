# gocordis → .NET 迁移总计划 v1.0

状态：**执行中**（2026-11-22 批准）。本文件只定义方向与验收，不描述实现细节；
实现细节见 `semantic-mapping.md` 与 `migration-coverage.md`。

## 0. 目标与范围

**目标**：把 gocordis 从 Go 插件框架迁移为一个成熟的 .NET 动态插件框架；
语义继承论文，实现使用 .NET 成熟能力，最终只维护 C# 版本。

**范围**：第一阶段只迁移 `gocordis`（本仓库）。`industrial-data-collector` 保持原样，不在本次迁移内。

**三条原则**：

1. **语义继承** —— 保留已验证的架构成果，不重新发明 Cordis。
2. **实现升级** —— 用 .NET 成熟 Runtime 能力替换 Go 中受限或重复的实现。
3. **一次收敛** —— Go 版本作为迁移参照，最终只维护 C# 版本，不长期维护两套框架。

## 1. 语义优先级（强制）

| 优先级 | 参照 | 用途 |
| --- | --- | --- |
| 1 | Dynamic Composable Runtime 论文（`docs/论文.pdf`） | 决定框架语义；唯一裁决来源 |
| 2 | JavaScript Cordis（`docs/JS-CORDIS-ALIGNMENT.md`，基线 `5e2e6c17…` / 4.0.0-rc.13） | 成熟实现与能力覆盖参照 |
| 3 | 当前 gocordis Go 实现 | 代码、测试、实现经验的迁移参照 |

**冲突规则**：论文 > JS Cordis > Go。不能为了照搬 Go 代码而保留错误语义；
也不能为了追逐 JS API 而破坏论文的 LIFO 恢复、隔离与调和语义。逐条裁决见 `semantic-mapping.md` §4。

## 2. 最终技术方向

普通 .NET 托管运行时，目标框架 **.NET 10**（LTS，而非追逐最新语言特性）。
本机 SDK：`10.0.115`（见 `migration-baseline.md` §1）。

| 领域 | 技术选择 | 目的 |
| --- | --- | --- |
| 语言 | C# | 主体实现 |
| Runtime | .NET 10 | 托管运行环境 |
| 原生插件 | `AssemblyLoadContext` | 动态程序集加载 |
| 依赖解析 | `AssemblyDependencyResolver` | 插件依赖隔离 |
| 生命周期 | `Task`、`CancellationToken`、`IAsyncDisposable` | 异步生命周期管理 |
| WASM | 独立 WASM Runtime Adapter | 保留跨平台插件；不自行编写虚拟机 |
| Process | IPC/RPC | 保留进程插件与故障隔离 |
| 测试 | xUnit | 单元与集成测试 |
| UI | 插件贡献接口 | Core 不依赖具体 UI |

**必须记住的边界**：`AssemblyLoadContext.Unload()` 是**协作式**的，不能保证强制卸载；
ALC **不是安全沙箱**。因此进程隔离保留独立价值，不得因为有了 ALC 就删掉 Process 后端。

## 3. 最终架构

```
gocordis .NET
├── Core            Context · Component · Fiber · Effect · Event · Dependency · Ownership · Reconciliation
├── Plugin System   Manifest · Discovery · Registry · Dependency Resolution · Lifecycle
├── Runtime Adapters
│   ├── Managed     C# Assembly / AssemblyLoadContext
│   ├── WASM        WASM VM Adapter
│   └── Process     IPC/RPC
└── Extensions      业务插件 · UI 插件 · Console/API 插件 · 基础设施插件
```

**历史计划注记**：早期“不增加 Application Package”已由 `Gocordis.Packaging` + CLI supersede；当前仍不增加第二个 Plugin Registry 或第二套插件生命周期。
Go 的 Native Runtime 不原样移植，由 C# Managed Runtime 接替。
三种执行模式保留，但插件的身份、Manifest、依赖、生命周期与贡献语义**不因执行后端改变**。

目标目录结构（阶段 1 落地）：

```
gocordis/
├── src/
│   ├── Gocordis.Core/
│   ├── Gocordis.Plugins/
│   ├── Gocordis.Runtime.Managed/
│   ├── Gocordis.Runtime.Wasm/
│   └── Gocordis.Runtime.Process/
├── tests/
│   ├── Gocordis.Core.Tests/
│   ├── Gocordis.Conformance.Tests/
│   └── Gocordis.Runtime.Tests/
├── specs/          ← docs/migration/ 的 C# 可执行规格镜像
└── samples/
```

不为每个领域概念拆独立程序集；先保证依赖方向正确：**Core 不依赖任何具体 Runtime**。

## 4. 七阶段与结束条件

阶段验收通过后**冻结**，不允许回头重新审查已通过模块；发现缺陷按阶段 0 的语义优先级走修正流程。

### 阶段 0：冻结 Go 基线（准备）

- 工作：固定 Git commit；保留测试/Coverage 清单/示例；记录三种插件机制；建立三层语义对照表。
- 交付：`migration-baseline.md`、`semantic-mapping.md`、`migration-coverage.md`。
- 验收：迁移范围与语义标准明确；基线可用 `verify-baseline.sh` 独立复核；不再因后续代码变化改变基线。

### 阶段 1：C# 项目骨架与边界（基础建设）

- 工作：建立 .NET Solution；明确 Core / Plugin System / Runtime Adapter 的依赖关系。
- 验收：`dotnet build` 与 `dotnet test` 独立通过；Core 项目不引用任何 Runtime Adapter 或 UI/Console 项目。

### 阶段 2：迁移 Core 语义内核（最高优先级）

按依赖顺序迁移：

| 顺序 | 模块 | 解决的问题 |
| --- | --- | --- |
| 1 | Context / Realm | 执行上下文与作用域 |
| 2 | Ownership | 资源归属和清理 |
| 3 | Component | 组件定义和组合 |
| 4 | Fiber | 组件运行实例及执行状态 |
| 5 | Activation | 激活和停用 |
| 6 | Effect | 可撤销的副作用 |
| 7 | Coeffect / Dependency | 响应式依赖及依赖解析 |
| 8 | Capability | 能力声明和提供 |
| 9 | Event | 事件传播和监听 |
| 10 | Reconciliation | 状态变化与运行时协调 |

不要求照搬 Go 类型/函数签名，只保证论文定义的行为与不变量。
验收：Core 语义测试通过，不依赖插件加载器也能独立运行。

### 阶段 3：迁移 Plugin System

Manifest、Discovery、Registry、Dependency Resolution、Plugin Lifecycle、Activation/Deactivation、与 Core 集成。
现有 Manifest 能力**继承而非重新设计**。
验收：能发现、解析依赖、注册、激活、停用插件；插件管理不产生第二套生命周期。

### 阶段 4：实现 C# Managed Runtime（本次迁移核心收益）

- 每个插件独立、可回收的 `AssemblyLoadContext`。
- `AssemblyDependencyResolver` 解析依赖；共享稳定的插件契约程序集。
- 插件独立编译与安装；停止后清理事件订阅、资源、后台任务。
- 验证程序集**实际回收**；支持不同插件依赖不同版本私有依赖。

验收：插件无需重编宿主；两个插件可用不同版本私有依赖；插件可停用/卸载/重载；
`WeakReference` 证明 ALC 可回收；Windows 环境通过测试。**不能只验证 `Unload()` 调用成功。**

### 阶段 5：迁移 WASM 与 Process Runtime

- WASM：保留插件契约、Manifest 与依赖声明，重新实现运行时适配，复用成熟 WASM Runtime。
- Process：保留进程插件模型、IPC/RPC 协议语义，统一生命周期与错误处理，保留故障隔离。
- 验收：三个 Runtime 共用统一插件管理与生命周期语义。

### 阶段 6：UI、Console、API 与整体收敛

UI Contribution API、Console 插件、API 插件、运行时状态查询、插件管理与诊断、示例插件与开发文档。
UI 不进入 Core，但 UI 插件是正式插件，与其他 Managed 插件共享生命周期。
验收：通过插件扩展 UI 与 Console 不需要修改 Core。

## 5. 工作量控制（硬性约束）

| 约束 | 具体要求 |
| --- | --- |
| 不逐行翻译 | Go 实现只作参考 |
| 不重新造轮子 | 优先使用 .NET Runtime 与标准库 |
| 不过度拆分 | 不为每个概念建立一个项目 |
| 不提前做 UI | 先完成 UI Contribution 契约 |
| 不迁移工业应用 | `industrial-data-collector` 保持原样 |
| 不维护双版本 | Go 只作为迁移基线 |
| 不无限审查 | 阶段验收通过后冻结 |

## 6. 分支与收敛策略

```
main              Go 最终基线，迁移期间冻结（HEAD ed2ef724a243…）
migration/dotnet  C# 迁移工作（当前分支）
```

迁移验收通过后，C# 成为正式主线；Go 版本通过 Git tag 或历史分支保留。
不新建第二个仓库，也不让两个版本长期并行演进。

**基线叠加层注意**：冻结时工作区存在未提交但被已跟踪代码引用的文件
（`extensions/loader/proc/transport/`、`extensions/console/webui/listen.go`）。
它们已按 SHA-256 记入 `baseline.sha256`。在决定提交叠加层之前，任何 fresh clone 都不能独立构建；
这不是可以忽略的细节，见 `migration-baseline.md` §2。

## 7. 最终验收标准

不以"代码写完多少"衡量，而以以下指标衡量：

| 验收领域 | 标准 |
| --- | --- |
| 论文语义 | 已覆盖语义全部符合论文（逐条对照 `semantic-mapping.md`） |
| JS Cordis | 目标能力逐项对照，无遗漏；差异必须显式记录 |
| Core | 不依赖具体插件 Runtime |
| Managed | Windows 动态加载、依赖隔离、可回收（WeakReference 证据） |
| WASM | 保留原有目标能力 |
| Process | 保留原有目标能力 |
| Plugin | 统一 Manifest、依赖与生命周期 |
| UI | 可通过插件贡献扩展 |
| 测试 | 核心语义与 Runtime 测试通过（含 race） |
| 构建 | 可重复构建、独立发布 |

**未完成的能力必须逐项记录为未完成**，不得用总体 Coverage 百分比掩盖缺失。

## 8. 明确不在本次迁移范围内

- 不迁移 `industrial-data-collector`。
- 不设计第二个 Host。
- Application Package 已实现（原计划 superseded）；见 `final-calibration.md`。
- 不开发插件市场服务端。
- 不开发云端插件管理平台。
- 不开发新的 WASM 虚拟机。
- 不自行实现程序集加载器。
- 不引入 NativeAOT。
- 不为 DDD 把框架拆成大量微型项目。

这些都不是完成 gocordis C# 迁移的前置条件。

## 9. 路线图（执行顺序）

```
基线冻结 → C# 骨架 → Core → Plugin System → Managed Runtime
        → WASM / Process → UI / Console / API → 最终验收
```

阶段 0 已完成；当前入口是阶段 1（骨架），随后按 `migration-coverage.md` 清单推进。
