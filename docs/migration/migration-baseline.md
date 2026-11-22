# 阶段 0：Go 基线冻结报告

冻结时间：2026-11-22。本文件记录的事实不可变；如需变更，必须显式修订本文件并升级基线版本号，
同时重跑 `verify-baseline.sh`。

## 1. 冻结标识

| 项目 | 值 |
| --- | --- |
| 冻结分支 | `main`（迁移工作分支 `migration/dotnet`） |
| 基线 commit | `ed2ef724a24331ecc3fa21974539315fd90212bf`（`main` 冻结于此；迁移分支在其之上提交） |
| commit 时间 | 2026-11-22T07:53:04+08:00 |
| commit 主题 | `fix(ci): isolate POSIX process-group test from Windows vet` |
| Go 工具链 | `go1.26.8 linux/amd64` |
| .NET SDK | `10.0.115`（`/usr/lib/dotnet/sdk`） |
| Go module | `dynamic-runtime`（`go 1.24.0`） |
| 叠加层快照时间 | `2026-11-22T08:32:25+08:00`（见 §2 漂移记录） |

## 2. 工作区叠加层（关键）

冻结时工作区**不干净**。快照时间 `2026-11-22T08:32:25+08:00`，共 13 个文件被修改或未跟踪，且部分被已跟踪代码直接引用：

| 文件 | 状态 | 为什么重要 |
| --- | --- | --- |
| `extensions/loader/proc/transport/transport.go` | 未跟踪 | 被 `extensions/loader/proc/backend.go:41` 与 `extensions/console/procplugin/client.go:21` import |
| `extensions/loader/proc/transport/transport_test.go` | 未跟踪 | transport 锁不变量测试（目前仅 2 个） |
| `extensions/console/webui/listen.go` | 未跟踪 | 被 `cmd/platform/main.go` 调用；远程监听安全门 |
| `extensions/console/webui/listen_test.go` | 未跟踪 | loopback/remote opt-in 测试 |
| `extensions/console/webui/explorer_provider_test.go` | 未跟踪 | 每请求单一 explorer provider 解析测试 |
| `cmd/platform/listen_test.go` | 未跟踪 | 平台监听端到端测试 |
| `extensions/loader/proc/backend.go` | 已修改 | 协议实现迁移到 transport 后适配 |
| `extensions/loader/proc/backend_internal_test.go` | 未跟踪（重冻结时新增） | backend 内部行为测试；由并行写入者在 08:30 引入 |
| `extensions/console/procplugin/client.go` | 已修改 | 改用 transport 连接 |
| `extensions/console/webui/server.go` | 已修改 | 注入 explorer provider / 监听策略 |
| `cmd/platform/main.go` | 已修改 | `listenAddress()` 远程 opt-in |
| `cmd/platform/README.md` | 已修改 | 对应文档 |
| `docs/guide/user-manual.md` | 已修改 | 对应文档 |

SHA-256 全量清单见 [`baseline.sha256`](baseline.sha256)，由 [`verify-baseline.sh`](verify-baseline.sh) 校验。

**漂移记录（必须保留）**：首次采集（约 08:20）得到 12 个叠加层文件；`verify-baseline.sh`
在 08:30 后检出 5 个文件内容变化，并出现新文件 `backend_internal_test.go`。
工作区在 08:32 后稳定 2 分钟以上，于 `08:32:25+08:00` 重新采集为 13 个文件。
HEAD 未变；变化只发生在未提交叠加层。该事件说明本仓库存在并行写入者，
因此阶段 0 的"冻结"是**时间点快照**，不是对并发修改的保证。后续若再次漂移，必须重复此流程。

**结论**：迁移基线 = HEAD commit + 该时间点叠加层快照。只提交 HEAD 不足以复现基线；
在迁移分支上提交叠加层之前，fresh clone 不能独立构建。这是一项待决策事项，不计入阶段 0 缺陷。

## 3. 代码规模与结构

统计口径：`wc -l` 物理行（含注释/空行），统计对象为冻结时的 Go 源文件。

| 区域 | 非测试文件 | 行数 | 测试文件 | 行数 | 合计文件 | 合计行数 |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| `runtime/` | 25 | 6,656 | 60 | 20,598 | 85 | 27,254 |
| `extensions/` | 93 | 23,637 | 114 | 34,257 | 207 | 57,894 |
| `cmd/` | 22 | 5,104 | 14 | 2,776 | 36 | 7,880 |
| `integration/` | 1 | 248 | 19 | 8,428 | 20 | 8,676 |
| **总计** | **141** | **35,645** | **207** | **66,059** | **348** | **101,704** |

- 含生产 Go 文件的包目录：**61**；其中 32 个目录还有外部测试包（97 个文件）。
- 真正的 `func main()` 入口：**12** 个（`backfill`、`cli-cordis`、`collector`、`create-cordis`、`example`、`host`、`httpd`、`platform`、`plugin`、`procplugindemo`、`procplugindemo/pluginecho`、`wasmhmr`）。
- 无测试文件的包：`cmd/{collector,example,host,procplugindemo,wasmhmr}`、
  `cmd/procplugindemo/pluginecho`、`extensions/fs/contract`、`extensions/fs/memory`（后者由 `extensions/fs` 一致性测试间接覆盖）。
- `runtime/` 生产代码唯一内部 import 是 `runtime/internal/wait`，**不 import 任何 extensions**。
- 唯一测试期方向反转：`runtime/p2_1_plugin_boundary_test.go:29` import `extensions/event`（文件头却声称只 import runtime）；迁移时应修正文档或依赖，不继承这个矛盾。

## 4. 核心模块（阶段 2 参照）

`runtime/` 生产文件 25 个（6,656 行），关键锚点：

| 语义 | Go 锚点 |
| --- | --- |
| Component / Dependency / Capability / `ErrDiverted` | `runtime/component.go:17` |
| Context（Require/Provide/Effect/On/Emit/Child/Unwind） | `runtime/context.go`（880 行） |
| Fiber（每次装载的运行实例） | `runtime/fiber.go:19` |
| 状态与意图 | `runtime/state.go:8` |
| 单一生命周期权威（命令队列/线性化/Close 排空） | `runtime/orchestrator.go:62` |
| target 视图 / quiet 判定 | `runtime/reconcile.go:19`、`runtime/dependency_graph.go:95` |
| relied 守卫（provider 撤回延迟到消费者退出） | `runtime/dependency_graph.go:12`、`runtime/orchestrator.go:455` |
| 声明环检测 | `runtime/dependency_cycle.go:65`、`:151` |
| Realm 隔离 / 逐 key 元数据拦截 | `runtime/providers.go`、`runtime/rehome.go:70` |
| 事件注册表与规范事件词表 | `runtime/event_registry.go`、`runtime/events.go`、`runtime/eventkey.go` |
| 所有权与级联清理 | `runtime/ownership.go:150` |
| 确定性调度 / 观察等价 | `runtime/deterministic.go`、`runtime/observation_equivalence.go` |
| 只读快照 | `runtime/observation.go:41` |

## 5. 三种插件机制快照

### 5.1 Managed / builtin（进程内）

- 契约：`extensions/loader/loader.go`（`Artifact`/`Module`/`Backend`/`ResourceHandle`）、
  `extensions/config/config.go`（`ComponentConfig`/`FactoryRegistry`/`Controller`）。
- 声明：`extensions/configwatch/toml.go`（`[[components]]`、`enabled`）、
  `extensions/pluginmeta/manifest.go`（描述性 Manifest + 校验）。
- 生命周期：Loader 管 Artifact→Module 与 usage；Kernel 管 Fiber；Config Controller 管期望态与
  `Enabled` 开关；`Enabled:false` 保留 entry、`fiber=nil`。
- `pluginmeta.Manifest.Requires/Provides` **只是描述信息**；真正的边只来自 `Component.Inject()/Provide()` 的 typed key。

### 5.2 WASM

- `extensions/loader/wasm/*`：wazero v1.11 解释器，模块 64 MiB 上限。
- ABI：必导出 `runtime_component_create` / `runtime_component_destroy`；逻辑类型来自自定义段 `module_type`，**不**来自 `Artifact.Type`。
- **无 host imports、无 WASI**：导入型模块可编译但无法实例化；无文件/网络/时钟/随机能力。
- 生命周期：每次激活新实例 + 一个 `ctx.Effect`；`moduleGeneration` + `Commit/Abandon/Release` 保证过期尝试不能释放新世代资源。
- HMR：`extensions/hmr` 管 target/binding/usage；warm 优先、withdraw-then-load 兜底；无零停机承诺、无自动回滚。

### 5.3 Process

- `extensions/loader/proc/backend.go` + 未跟踪的 `transport/transport.go`：
  `GORIDIS-PROC-PLUGIN 1` 握手 + 行分隔 JSON-RPC 2.0（stdin/stdout），单写锁、单并发调用。
- `extensions/loader/proc/serve.go`：插件侧 `Serve`/`Handler`，SIGINT/SIGTERM，shutdown hook 测试缝。
- 生命周期：每次激活起新进程；`ctx.Effect` 安装 = spawn+handshake，逆 = 优雅 shutdown → 有界等待 → kill → reap。
- 每个 backend 绑定**一个** typed key；无插件→宿主调用；无自动重启；启动失败进入降级（登记 `plugin_error` 词表），不阻塞宿主。
- 进程边界**不是安全沙箱**；Windows 有执行位/`.exe` 解析差异。

### 5.4 UI / Console / API

- Core（`runtime/`）完全不 import UI；UI 边界由 `cmd/platform` 组合根装配。
- 贡献接口：`extensions/console/registry/composition.go`（`RegisterPage/Panel/ClientModule`，
  `ContributionOwner{PluginID,ComponentID,ActivationID}` 防陈旧清理）。
- 宿主能力：`extensions/console/host`（`UIHostKey`、`HubKey`、`PluginAliveKey`）。
- WebUI：`extensions/console/webui/server.go`（API/SSE/嵌入 bootstrap）、`listen.go`（默认 loopback，
  远程需 `GOCORDIS_ALLOW_REMOTE=1`）。**无内建认证**，`AccessPolicy` 只是应用侧接缝。
- 前端 client-module 激活是浏览器侧第二条 SSE 驱动的调和循环，不是 Kernel effect —— C# 需明确是否保留。

## 6. 测试资产（迁移的可执行规格）

- 总计 207 个 `*_test.go`、66,059 行。
- 论文定理证据：`thm64_preservation_test.go`、`thm68_recovery_test.go`、`thm70_ordering_test.go`、
  `thm73_thm80_test.go`、`thm80_det_interleaving_test.go`、`revision_thm80_test.go`、
  `proof_invariants_test.go`、`observation_equivalence_test.go`。
- 生命周期契约：`phase2_contract_test.go` … `phase8_contract_test.go`、`iterator_test.go`（divert）、
  `effect_phase_test.go`（严格 LIFO）、`declarations_containment_test.go`、`dependency_cycle` 相关。
- 扩展一致性：event 五派发模式（`dispatch_*_conformance_test.go`）、fs、loader、registry、
  scheduler、timer、watch、server、http、config/configwatch。
- 端到端：`integration/` 19 个文件（8,428 行），含 `platform_convergence_*`（PC01–PC30）、
  `wasm_e2e_test.go`、`wasm_hmr_e2e_test.go`、`e2e_close_full_test.go`。

**基线验证记录（2026-11-22，本工作区）**：

| 快照 | 命令 | 结果 |
| --- | --- | --- |
| 首次采集（12 文件，08:20） | `go build ./...` | `BUILD_OK` |
| 首次采集 | `go test -race -count=1 ./...` | `RACE_EXIT=0`；41 包 ok，2 包 `[no test files]` |
| 重冻结（13 文件，08:32） | `go build ./...` | 通过（无输出） |
| 重冻结 | `go test -race -count=1 ./...`（第一次） | **FAIL**：`extensions/broker` `TestBrokerRollingUpdate`，`broker_test.go:321: no response through broker` |
| 重冻结 | 同一测试定向复现 | `-race -count=1` 5 次中 1 次失败；无 race `-count=20` 20 次全过 ⇒ 基线既有 flake |
| 重冻结 | 全量 race 复跑（第二次） | `RACE2_EXIT=0`；全量通过，确认第一次失败为 flake 而非回归 |

**基线 flake（不修改冻结代码，迁移时修复测试写法）**：`extensions/broker/broker_test.go:315-321`
用 `select/default` 立即读 `out`，未等待消费者 Apply 的异步发射；race 下调度变化就会出现假失败。
C# 对应测试必须用有界等待/确定性信号，不得照抄这个 `default` 分支。

> 因此"基线全绿"不是无条件成立：无 race 全绿；race 全量曾通过一次、失败一次，失败可归因于上述已知 flake。
> 阶段 0 冻结的是**事实**，不是"无缺陷"承诺。

## 7. 语义资产（先验规范）

- `AGENTS.md`：十二条铁律、生命周期速查表、完成前检查清单。
- `docs/论文.pdf`：92 页，第一优先级；条目化见 `semantic-mapping.md` §2。
- `docs/ROADMAP.md` §1：论文条目 ↔ Go 代码对照；§1.2 记录已关闭的历史偏差。
- `docs/adr/0001-isolation-per-key-realms.md`–`0004-kernel-event-demotion.md`：逐 key 隔离、元数据拦截、效果迭代器、事件派发边界。
- `docs/JS-CORDIS-ALIGNMENT.md`、`docs/ACCEPTED-DIFFERENCES.md`、`docs/r1.md`：JS 能力对照与冻结差异。
- `docs/review/2026-09-08-paper-first-review-v0.1.md` §10：平台能力边界与 R4 分类。

## 8. 基线已知风险（迁移必须处理，不在此阶段修）

1. **叠加层未提交**：见 §2；fresh clone 不完整。
2. **多生命周期权威并存**：Loader Module、Kernel Fiber、Config 期望态、HMR binding/usage、
   procplugin `follows` 级联、浏览器 client-module 调和。C# 必须明确各自权威与 owner/generation 不变量。
3. **Manifest 声明不是运行边**：描述性 `Requires/Provides` 与 typed key 真实边并存；C# 需确定哪种声明权威。
4. **"合作式假设"不是论文前提**：论文 Thm 73 只假设 ≺ 无环、迭代器有限、名字集有限、每步是生命周期规则；
   `AGENTS.md` 铁律 12 把它写成论文假设。迁移中把它作为工程/测试约束，不作为定理前提。
5. **WASM 无 host API**：C# WASM 适配若不补能力桥，仍无法承载需要宿主服务的插件；需显式决策。
6. **测试缺口**：新 `transport/transport_test.go` 只有锁不变量，无握手/RPC/E2E；`listen.go` 只测地址校验。
   C# 侧应补握手失败、调用中崩溃、shutdown 送达、世代隔离、provider churn、远程监听拒绝。
7. **基线 flake**：`TestBrokerRollingUpdate` 在 race 下约 1/5 假失败（§6）；C# 测试不得复制该写法。
8. **并行写入者**：冻结期间工作区被外部进程修改（§2 漂移记录）；叠加层是时间点快照，需复核后才能宣称未漂移。
