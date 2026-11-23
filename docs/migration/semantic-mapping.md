# 语义对照表：论文 → JS Cordis → Go → C#

本文件是阶段 0 的核心交付物。它定义 C# 迁移要实现的**语义集合**与冲突裁决；
C# 实现不追求与 Go 类型/函数签名一致，只追求逐条满足这里的行为与不变量。

## 1. 权威用法

| 层级 | 来源 | 用法 |
| --- | --- | --- |
| 1 | `docs/论文.pdf`（92 页，PDF 页码 = 印刷页码） | 语义裁决；条目编号 `C01`–`C40` 在本文件内稳定 |
| 2 | JS Cordis 4.0.0-rc.13（基线 `5e2e6c17…`，见 `docs/JS-CORDIS-ALIGNMENT.md`） | 能力覆盖参照；等级：完整等价 / 语义等价·API 不同 / 部分等价 / 接受差异 / 缺失 |
| 3 | 当前 Go 实现与测试（`docs/ROADMAP.md` §1、`docs/adr/*`） | 实现经验与可执行规格参照 |

冲突时**自上而下**裁决。Go 中的实现强化（如严格 LIFO、声明校验）可以保留，但不能冒充论文要求。

## 2. 论文语义条目目录（稳定 ID）

### A. 基础（§§1–3）

| ID | 论文锚点 | 语义要点 |
| --- | --- | --- |
| C01 | §1.1 p.4 | 时间维 = 移除时完整/安全逆转所有环境修改；空间维 = 声明/发现/响应式解析依赖 |
| C02 | §3.1 p.9 | 可逆 effect `e : Γ → Γ × (Γ → Γ)`：逆是左逆 `g∘f`，不是 `f∘g` |
| C03 | Def 2 p.10；Thm 7 p.11 | effect 上下文 `∂Γ = Γ × (Γ→Γ)`；状态 `(γ,φ)`，不变量 `φ(γ)=γ0` |
| C04 | Def 1/3/6 pp.9–11；Thm 5 | 扭结组合 `(f1,g1)∘(f2,g2)=(f1∘f2,g2∘g1)`；`trackΓ` 是 monoid 同态 |
| C05 | Def 8/9 pp.12–13；Thm 10–11 | 见证 effect `𝔈*Γ`：`g(δ)=γ` 在应用态成立；组合 `⋄` 按 LIFO 合成逆 |
| C06 | Thm 16 p.15；Def 17/18 pp.15–16 | effect iterator `ℑΓ`：`yield (δ,g,Maybe(ℑ))`；加载 = 跑迭代器，卸载 = 逆按反向应用序（LIFO）执行 |
| C07 | Def 19/20 p.17 | coeffect 上下文 `Σ : K ⇀ V_k`；`get` 要求 k 存在，`set` 要求 k 不存在；提供本身是可逆 effect |
| C08 | Def 21/22 p.18 | 规格 `d⊆K`；`notify_d ∈ {activating,deactivating,neutral}`；组件永不读未绑定键 |
| C09 | Def 23 p.19 | 两种实现：就地（逆非平凡）与派生（新上下文、逆为 id、丢弃即恢复）；隔离/拦截属于派生 |
| C10 | Def 24/25 pp.19–20；§4.4 p.56 | Realm 隔离 `Σiso=(ρ,σ)`；`isolate(k,r)` 是重赋值；固定 key×realm 对；同 realm 内互斥 |
| C11 | Def 26/27 pp.20–21；§6.3 p.72 | 元数据拦截 `Σinter=(ι,σ)`，逐 key monoid `(M_k,⊕k,εk)`，`get(k,μ)=σ(k)(μ⊕k ι(k))`；**右偏**：组件声明 + 上下文覆盖；拦截改变用法不改变解析 ⇒ 不重载 |
| C12 | Def 28–30 pp.21–22 | 统一上下文 `Γ∞ = μΓ. Γ × (Γ→Γ) × Σ`；Σ 涵盖所有共享可变状态；key 带操作 `(V_k,A_k)`；一切交互经上下文 |
| C13 | Def 31–37 pp.23–25 | 恢复等式按观察等价 `≃` 理解；`≃_S` 遗忘未绑定 key；等式在 `≃` 下成立 |
| C14 | Def 40/42 pp.26–27；Thm 43/45/47 | 独立性：两 effect 的前向与逆交换且互不扰动；独立逆可按**任意排列**执行；LIFO 仍是组件内顺序 |

### B. 演算（§4.1–4.2）

| ID | 论文锚点 | 语义要点 |
| --- | --- | --- |
| C15 | Def 48 p.31 | Component `(d,p,e)`：`d` 需求、`p` 提供、`e ∈ ℑ^{d∪p}_Γ` |
| C16 | Def 49 p.32 | Fiber `⟨d,p,e,π,σ,τ,θ,ω⟩`；`θ ∈ {Inactive,Reloading,Active,Unloading}`；`ω` 为已提交 provider 视图 |
| C17 | Def 50/51 p.32–33；Eq.46 | Registry `F_γ : N ⇀ Fiber`；coeffect 上下文由 Active fiber 派生；互斥 ⇒ `provider_k` 唯一；多实现只能经 realm 或 broker |
| C18 | Def 52 p.35 | 实例化 = 以父为 owner 的一次迭代；逆是 **O-Retire**，不是 O-Remove |
| C19 | §4.2.1 p.34 | 外部输入只有 O-Insert / O-Retire；**O-Remove** 要求 Inactive；绝不直接写生命周期状态 |
| C20 | Def 53 p.35；Eq.48/49 | `target_n(γ)`：τ 或依赖不满足时为 ⊥，否则 `k↦provider_k`；`quiet` 要求全部 fiber 已定态 |
| C21 | §4.2.2 pp.36–37；Table 1 p.40 | 六条生命周期规则：**L-Begin / L-Iter / L-Finish / L-Divert / L-Leave / L-Unload**；只有 L-Unload 施加累积逆；规则反应式、**无调度器** |
| C22 | Def 54 p.36；§4.4 Config p.57 | `relied_n` 按绑定守卫：provider 撤回延迟到所有解析到它的消费者退出；消费者在整个拆除期仍读自己声明的 coeffect；依赖消失 ⇒ Pending 而非 Failed，恢复自动重激活 |
| C23 | Def 55/56 pp.38–39；Lemma 57 | 受限性：effect 只写 `σ_n` 与声明键的 provider 表；只读 `σ_n` 与 `σ_m|d_n`；实例化是唯一例外 |

### C. 元理论（§4.3）

| ID | 论文锚点 | 语义要点 |
| --- | --- | --- |
| C24 | Def 63 p.43 | 良构 registry：parent 存在、提供互斥、installed ⇒ `ω_n` 在 `d_n` 上全函数、provider installed |
| C25 | Thm 64 p.43 | 保持性：任一规则执行后仍良构；L-Unload 守卫承载条款 (3)/(4) |
| C26 | Thm 68 p.46；Cor 69 | 恢复精确性：`g^u_n(γ_u) ≃_K` 如同 n 从未开始；`σ_{u+1,n}=∅` |
| C27 | Thm 70 pp.47–48 | 顺序性：L-Begin 仅在 `γ⊨d_m`；一旦提交 `ω_m(k)=n`，整个 episode 不变；provider episode 严格包含 consumer episode |
| C28 | Thm 71 p.48 | 解析一致性：Reloading 占初始区间，每步用同一 `ω`；退出是 Finish 或 Divert（含恢复等式） |
| C29 | Def 72 p.49；Thm 73 pp.49–50 | 进展：假设 ≺ 无环、`len(e_n)≤K`、名字有限、每步为生命周期规则；`¬quiet ⇒ ∃rule`；`S(n) ≤ (K+3)(V(n)+1)`；**没有"合作式"前提** |
| C30 | Def 74–76 pp.51–52；Thm 80 pp.54–55 | 汇合：支持集良基；若 Active 时 `dom(σ_n)=p_n`（总性），任意两序列达 `≃` 相关的唯一正规形；失败 fiber 被排除 |
| C31 | Lemmas 59–62 pp.40–42 | 规则写分区、≃ 不变性、名字双射等变、vestigial 记录对所有规则不可见 |

### D. 扩展与边界（§§4.4–6）

| ID | 论文锚点 | 语义要点 |
| --- | --- | --- |
| C32 | §4.4 Asynchrony pp.55–56 | 异步宿主是**惯性的**：L-Divert 只能"落地"不能中止；在途迭代落地后才停用；不拒绝停用 |
| C33 | §4.4 Failure p.56 | 迭代可 raise Ξ；以中止式 L-Divert 退出，累积逆只覆盖到失败迭代；结果写在 fiber 上阻止重入；**不向父传播**；重试 = revision |
| C34 | §4.4 Isolation/Configuration pp.56–57 | Realm 重赋值 = 接口变更 ⇒ retire → deactivate → O-Remove → 同名重插（children 先于 parent）；重启端点与全新加载一致 |
| C35 | §6.1 pp.70–71 | 边界按**位置**判定：能独占修改且能恢复 ⇒ 在 Γ 内；否则操作为 `idΓ`；发射的恢复靠 output commit 或补偿 |
| C36 | §6.5 p.74 | 依赖环 ⇒ 相关组件永久不活跃；可从声明静态预测并在装载时报告 |
| C37 | §6.3 p.72 | 能力式访问控制：inject 声明 = 能力请求；上下文代理 = 仲裁者；能力集静态已知；元数据拦截可细粒度改策略而不重载 |
| C38 | §6.6 p.75 | 链接按 key 名义；`V_k` 只保证同一编译单元内类型一致；命名空间 `K×P`、peer dependencies、结构兼容是候选方案 |
| C39 | §6.4/§6.7 pp.73–77 | 语言无关性：时间维需闭包 + 运行时引入/撤回；空间维需 typed 依赖访问 + 动态 mediation；编译器协作可编译期查环 |
| C40 | §5.2.1 pp.65–69 | Loader entry `{id,url,isolate,intercept,config,disabled}`；entry 是跨 revision 的身份，fiber 是一次 enablement；字段变更分派；HMR = 模块级可逆 effect |

## 3. 三层能力对照与 C# 处置

状态：`迁移语义` = 按论文行为重写；`.NET 替代` = 用成熟机制；`继承` = 保留 Go 设计；`待定` = 需决策；`不迁移` = 明确排除。

| 能力 | 论文 | JS Cordis | Go 实现锚点 | C# 目标 | 状态 |
| --- | --- | --- | --- | --- | --- |
| Component 定义 / 组合 | C15 | `Context` + 插件对象 | `runtime/component.go:17` | `Gocordis.Core` Component + Inject/Provide 声明 | 迁移语义 |
| Context / 统一上下文 | C12 | `ctx` 代理（`ctx.foo`） | `runtime/context.go` | 强类型 `Context`；**不实现 JS Proxy 魔法** | 迁移语义 |
| Fiber / 运行实例 | C16 | registry 中的 fiber | `runtime/fiber.go:19`、`state.go:8` | `Gocordis.Core` Fiber + 状态机 | 迁移语义 |
| Effect 与 LIFO 恢复 | C02–C06、C26 | effect context（逆按 LIFO） | `runtime/context.go`、`effect_phase_test.go` | `IAsyncDisposable` effect 栈；**严格 LIFO，不并发撤销** | 迁移语义 |
| Effect 迭代器 / divert | C06、C21、C28、C32 | 无直接对应 | `runtime/component.go:99`、`iterator_test.go:184` | 迭代器为主形态；`IIterativeComponent` + `CancellationToken`；每次 yield 由 Core 命令域做 Install/Divert 决策（含惯性异步步） | 迁移语义（已实现） |
| 依赖声明与解析 | C07、C08、C17 | `ctx.inject/provide`、typed key | `runtime/providers.go`、`dependency_graph.go` | typed `Key<T>` + provider 解析；一个 key 一个提供者；打包插件经 `SharedAssemblies` 把契约 DLL 提升进 default ALC，跨包共享同一 Key 身份（`SharedAssemblyRegistry`） | 迁移语义（打包形态已补齐） |
| 跨后端能力契约 | C07、C08、C17、C35、C38 | 字符串名 + Symbol | Go 仅有同进程 typed key（WASM v0.1 无 host API） | 宿主 `CapabilityCatalog` 按 `CapabilityId` 分配唯一 Key；Managed 注入 catalog（不需共享 DLL），WASM 自定义段 + 导入导出，Process 声明化 RPC；版本只作兼容元数据（NuGet 范围），端点注册/释放是 Core effect，外部调用受 C35 边界 | 迁移语义（新增，未改变 Core 匹配/绑定/撤回） |
| 依赖消失 → Pending → 自动恢复 | C20–C22 | registry 响应依赖变化 | `runtime/reconcile.go:59`、`phase5_contract_test.go:62` | 同一语义；**不得变成 Failed** | 迁移语义 |
| Realm 逐 key 隔离 | C10、C34 | 无等同机制 | `runtime/providers.go`、`rehome.go:70` | `Gocordis.Core` Realm；`Runtime.RehomeAsync` = retire→deactivate→O-Remove→同名重插（新 fiber id/generation/Revision），children 先于 parent | 迁移语义（已实现） |
| 元数据拦截（monoid、右偏） | C11、C37 | peer dependencies / 配置 | `runtime/context.go` `metaView`、`meta_intercept_test.go` | 派生式、每 Context 快照、不跨 root 泄漏 | 迁移语义 |
| Ownership / 级联清理 | C18、C19、C26 | 无等同机制 | `runtime/ownership.go:150` | 父子所有权 + 子先于父清理（工程强化） | 迁移语义 |
| 失败 = raise，不向父传播 | C33 | 插件错误隔离 | `runtime/orchestrator.go:501`、`phase3_contract_test.go:71` | Activation 失败 → Failed；不自动重试；重试 = revision | 迁移语义 |
| 调和 / target / quiet / 单一权威 | C19–C22 | registry 声明式装载 | `runtime/orchestrator.go:62`、`reconcile.go:19` | 单一 `Reconciler`；外部只提交声明，不写状态 | 迁移语义 |
| 环检测 | C29、C36 | 编译期/运行期检测 | `runtime/dependency_cycle.go:65` | 装载边界拒绝并给环链诊断 | 迁移语义 |
| 定理 64 / 68 / 70 / 73 / 80 | C25–C30 | 无 | `runtime/thm*_test.go`、`proof_invariants_test.go` | Thm64/68/70/73/80 均有 xUnit 规格（`tests/Gocordis.Conformance.Tests/TheoremTests.cs`） | 已验证 |
| 事件注册（可逆 effect） | §2.1 p.7、§3.4.2 p.29 | `ctx.on/emit` | `runtime/event_registry.go` | Core 提供 Effect 拥有的事件注册 | 迁移语义 |
| 事件派发（serial/parallel/bail/waterfall） | **论文无** | `emit/serial/...` | `extensions/event/dispatch.go` | 作为插件系统/扩展能力，不进 Core；R4 类 C | 继承 |
| Loader / entry（id,url,isolate,intercept,config,disabled） | C40 | loader/include/group | `extensions/loader/*`、`extensions/hmr/*` | `Gocordis.Plugins`；fiber 由 Core 唯一拥有 | 迁移语义 |
| Manifest / 声明存储 | C40 | `cordis.yml` | `extensions/pluginmeta/manifest.go`、`extensions/configwatch/toml.go` | 继承字段与语义；明确描述性 vs 权威性 | 继承 |
| Config 期望态与开关 | §4.4 Config p.57 | cordis 配置项 | `extensions/config/config.go`、`switch_test.go` | 声明 → 调和；enabled 翻动 = Load/Dispose | 迁移语义 |
| Broker / 多实现共存 | §6.2、C17 | 多实现路由 | `extensions/broker/broker.go` | 保留 broker；**不发明第二个注册表** | 继承 |
| 运行时快照 / 诊断 | 论文无（观察等价相关） | 无 | `runtime/observation.go:41`、`extensions/console/runtime` | 只读 Snapshot；**永不持久化** | 继承 |
| 确定性调度器 | 论文无 | 无 | `runtime/deterministic.go` | 仅作为测试/验证工具，不进生产 Core | .NET 替代 |
| WASM 插件 | C39 | 部分支持 | `extensions/loader/wasm/*`（wazero） | `Gocordis.Runtime.Wasm`，复用成熟 host，不造 VM | .NET 替代 |
| 进程插件 | 论文无（C35 边界外发射） | 无 | `extensions/loader/proc/*` | `Gocordis.Runtime.Process`，保留 IPC/RPC 与故障隔离 | 继承 |
| UI / Console 贡献 | 论文无 | 官方 UI 插件 | `extensions/console/registry/composition.go` | UI Contribution 契约先行；Core 零 UI 依赖 | 继承 |
| 程序集动态加载 / 卸载 | C39（语言无关） | 模块热替换 | Go 无等价（编译期注册） | `AssemblyLoadContext` + `AssemblyDependencyResolver` | .NET 替代 |

## 4. 冲突裁决（必须按此实现）

1. **"合作式假设"不是论文前提。** Thm 73 只假设 ≺ 无环、迭代器有限、名字有限、每步为生命周期规则；
   论文中不存在 cooperat* 字样。`AGENTS.md` 铁律 12、`ACCEPTED-DIFFERENCES.md`、用户手册仍如此表述。
   **裁决**：C# 把合作式（有限步、可取消、无环）作为**工程/测试约束**，不作为论文定理前提写入语义文档。
2. **拦截必须是派生的。** Go 早期把元数据 ι 写进共享 root realm，违反 Def 27 并跨 root 泄漏；已由 `metaView`/`Child()` 快照修复。
   **裁决**：C# 保持每 Context 派生的元数据视图，不跨 root 继承。
3. **激活形态反转。** 论文默认形态是 effect iterator（C06）；Go 默认是 Apply 一次，`IterComponent` 才开启迭代。
   **裁决**：C# 以迭代器语义为主形态，"单次 Apply"视为单步迭代器的语法糖；per-yield 调度已实现——
   `Runtime.StepsAsync` 逐 yield 经 `StepRequestCommand` / `StepInstalledCommand` 交回命令域裁决，
   在途步只能落地不能中止（C32），Managed 后端若包裹 `IIterativeComponent` 必须 yielding 给 Core 调度。
4. **汇合前提要显式编码。** Go 用规范 observable 近似 `≃_K`，Def 76 总性只隐式成立。
   **裁决**：C# 明确定义 `≃`（按 key 操作的可观察等价）与总性前提，不靠近似掩盖。
5. **拒绝 JS 的保身份替换与跨 effect 并发撤销。** 它们分别与 C34（revision = retire→重插）和 C06（LIFO）冲突。
   **裁决**：C# 采用 retire→reinsert 与严格顺序 LIFO；`RehomeAsync` 退役旧 fiber、撤销其 effect、
   O-Remove 旧记录并以新 id/Generation/Revision 同名重插，Snapshot 直接观察 revision 变化。
6. **子先于父清理是工程强化，不是论文要求。** Go 明确接受此强化。
   **裁决**：保留，但文档中不得写成论文条款。
7. **状态映射固定。** 论文 Inactive ↔ Go `Pending`/`Gone`；论文 Failed outcome ↔ Go `StateFailed`；
   依赖消失必须是 Pending；失败重入只经 revision。不引入"失败自动重启"策略。
8. **可逆边界按位置判定（C35）。** 未被 effect 捕获的外部发射不享有恢复承诺。
   C# 不得声称能撤销已发出的 HTTP 响应、日志、外部进程副作用。

## 5. 冻结的接受差异（不视为缺陷，但必须记录）

来源：`docs/ACCEPTED-DIFFERENCES.md`、`docs/JS-CORDIS-ALIGNMENT.md`、`docs/r1.md`。

| 差异 | 决策 | 对 C# 的含义 |
| --- | --- | --- |
| JS `ctx.foo` Proxy / Reflect 魔法 | Go 明确拒绝，采用 typed `NewKey<T>` + `Require/Provide` | C# 同样用强类型 key，不做动态 Proxy |
| JS 保身份 `registry.replace()` | 论文要求 revision 建新 fiber | C# 用 retire→重插 |
| JS `Promise.all` 并发撤销 effect | 违反 LIFO | C# 严格顺序逆序 |
| JS `Bail` 值短路 | 论文沉默；Go 保留 fail-fast + 独立 `BailValue` | C# 沿用，并记录为平台选择 |
| JS 专属 fs 选项 / HTTP file,data URL / SOCKS DNS / CLI 输入 / 远程脚手架 | 接受差异 | 迁移清单中逐项标注"缺失"，不假装覆盖 |
| Go 平台增强：事件派发五模式、registry、scheduler、observe、Snapshot、确定性驱动、proc/WASM 后端 | 非论文语义（R4 类 C） | 保留为平台能力，但不得写成论文语义 |

## 6. 论文有而 Go 不足 / Go 有而论文无

**论文有、Go 近似或缺失**（C# 应显式编码，不用近似掩盖）：

- 完整观察等价 `≃_K`：Go 用规范 observable 近似（review `:261`）。
- Def 76 总性：Go 生成器隐含满足，没有显式断言（review `:264`、`:300`）。
- Thm 43（独立逆可任意排列）：Go 只实现严格 LIFO；C# 把 LIFO 限定在单 fiber 内，
  并以两个独立 fiber 的两种撤销排列做可执行断言：`C14_IndependentInversesCommuteAcrossPermittedOrders`
  与 `Thm43_IndependentInversesCommuteAcrossPermittedOrders`（见 `TheoremMatrixTests` / `PaperGapClosureTests`）。
  不再表述为"无直接测试"。
- Thm 45/47/71：作为证明要素使用，无专门命名测试（Thm 71 由每步 target 检查操作化保证）。
- 按 yield 调度：C# Core 已实现每步命令域决策（`Runtime.StepsAsync`）；确定性重放驱动（G-05/G-8）仍未移植，
  属于平台测试工具，不是论文语义缺口。证据：`C32_InertialIteratorLandsCancellableStepBeforeItsInverse`、
  `ManagedInnerIteratorIsScheduledStepByStepByCore`。
- 逆失败诚实性：失败逆继续剩余 LIFO 栈，但槽位转 `EffectState.Failed`，`Fiber.CleanupFailures` /
  `RuntimeSnapshot.CleanupFailures` 可观察"清理不完整"，不伪装为 Undone 成功。
  证据：`FailingInverseContinuesTheStackAndIsObservableAsIncomplete`、
  `FailingInverseOnDependencyLossBlocksReentryInsteadOfPretendingPending`。
- §6.5 分解策略、§6.7 语言/OS 协同：仅讨论；§6.6 在 C# 迁移中由 `VersionedKey<T>` + peer range 实现（见 `VersioningTests`）。

**Go 有、论文无或论文中性**：

- 事件派发五模式、`BailValue`/`OnValue`/`OnOnce`（ADR-0004）。
- `Runtime.Snapshot`/EventSink/Subscribe、确定性驱动；`Fiber.Rehome`（Go 的**保身份短路由**，
  不是论文 C34 的 revision 复合；C# 只实现论文的 retire→reinsert 形态 `Runtime.RehomeAsync`）。
- 声明校验（undeclared Require/Provide 拒绝）、`ErrDuplicateProvider`、装载期自依赖拒绝。
- 平台能力：registry、scheduler、timer、watch、fs、http、server、market、package-manager、console。

### 6.1 P1 语义修复的可执行证据锚点

| 论文条目 / 修复 | 实现锚点（生产代码） | 红→绿证据（测试） |
| --- | --- | --- |
| C34 revision 复合 | `src/Gocordis.Core/Runtime.cs` `HandleRehome` / `CompleteRehome`；`Context.IsolateAsync` 走 `IsolateCommand` | `tests/Gocordis.Core.Tests/RevisionSemanticsTests.cs`：`RehomeRetiresOldFiberAndReinsertsWithNewIdentityAndRealm`、`RehomeRetiresChildrenBeforeTheirOwnerAndReinsertsAtTheSamePosition`；`tests/Gocordis.Conformance.Tests/PaperGapClosureTests.cs`：`C34_RealmReassignmentIsARetireReinsertComposite` |
| 迭代器不被后端绕过 | `src/Gocordis.Runtime.Managed/ManagedBackend.cs` `ManagedPluginComponent.IterateAsync`；后端不再对 `IIterativeComponent` 调 `ApplyAsync` | `tests/Gocordis.Runtime.Tests/ManagedRuntimeTests.cs`：`ManagedInnerIteratorIsScheduledStepByStepByCore`、`ManagedInnerIteratorDivertSkipsStepsThatWereNeverRequested` |
| 逆失败诚实性 | `src/Gocordis.Core/Runtime.cs` `RunUnwindAsync` / `RecordCleanupFailures`；`EffectState.Failed`、`CleanupIncompleteException` | `tests/Gocordis.Core.Tests/InverseFailureTests.cs`：`FailingInverseContinuesTheStackAndIsObservableAsIncomplete`、`FailingInverseOnDependencyLossBlocksReentryInsteadOfPretendingPending` |
| C32 惯性异步 | `src/Gocordis.Core/Runtime.cs` `StepsAsync` / `HandleStepInstalled` | `tests/Gocordis.Conformance.Tests/PaperGapClosureTests.cs`：`C32_InertialIteratorLandsCancellableStepBeforeItsInverse` |
| Thm43 / C14 独立逆 | Core 严格 LIFO；跨 fiber 可交换 | `tests/Gocordis.Conformance.Tests/TheoremMatrixTests.cs`：`Thm43_IndependentInversesCommuteAcrossPermittedOrders`、`C14_IndependentInversesCommuteAcrossPermittedOrders` |

## 7. 平台边界（review §10 + R4 分类）

- **属于平台且在范围内**（§10.2 #1–9、11、13、17、18）：声明式装载/卸载、持久化开关、替换升级、
  依赖门控、失败隔离、级联清理、服务发布/注入、多实现（broker）、插件间事件、配置热更新、
  内置/WASM 代码供给、优雅关闭、并发安全。
- **有意排除**：业务状态只能走组件 journal（#15，`cmd/backfill`）；runtime 派生状态（fiber 表/registry）
  永不持久化（#16）；插件市场服务端、云端管理平台不在范围内。
- **已记录越界后落地**：跨进程插件（#10 → `extensions/loader/proc`）；沙箱能力形式化（#14）仍未做；
  CI/Windows 矩阵（#21）需保留；性能基线（#22）未做。
- **未清偿债务（不是范围外）**：HMR/loader 短路径终点对齐（G-1）、流式观察（G-3）、统一插件开发文档。
- **R4 扩展分类**：A = 论文 §5.2 实现（loader/config/configwatch/watch/hmr/console 模块调和）；
  B = §6 讨论项（broker、wasm）；C = 明确的非论文平台能力（event/registry/scheduler/observe）；
  D = 示例/演示（http → `cmd/httpd`）。

## 8. `docs/ROADMAP.md` §1 锚点（论文条目 ↔ Go 代码）

| 论文条目 | Go 代码锚点 | 测试证据 |
| --- | --- | --- |
| Def 48/49 Component/Fiber | `runtime/component.go:17`、`fiber.go:19`、`state.go:8` | `declarations_containment_test.go` |
| Def 50/51 Registry（单一决策域） | `runtime/runtime.go:24`、`orchestrator.go:62`、`observation.go:41` | `proof_invariants_test.go` |
| Def 52 实例化 = 父的受控 effect | `runtime/context.go:397`、`ownership.go:150` | `phase6_contract_test.go`、`deterministic_close_test.go` |
| Def 53 target / quiet | `runtime/reconcile.go:19`、`dependency_graph.go:95`、`orchestrator.go:159` | `thm73_thm80_test.go` |
| Def 54 relied 守卫 | `runtime/dependency_graph.go:12`、`orchestrator.go:455` | `thm73_thm80_test.go:543` |
| Thm 64 保持性 | `runtime/proof_invariants_test.go` | `thm64_preservation_test.go:225` |
| Thm 68 恢复精确性 | `runtime/context.go:783`、`:803`；`orchestrator.go:455` | `thm68_recovery_test.go:85`、`:125`；`iterator_test.go:127` |
| Thm 70 顺序性 | `runtime/dependency_graph.go:68`、`:95`；`orchestrator.go:455` | `thm70_ordering_test.go:87`、`iterator_test.go:184` |
| Thm 73 进展性 | `runtime/deterministic.go`、`dependency_cycle.go:65`、`:151` | `thm73_thm80_test.go:190`、`obs_test.go:361` |
| Thm 80 汇合（排除 failed） | `runtime/observation_equivalence.go`、`fiber.go:401` | `thm73_thm80_test.go:474`、`:914`、`thm80_det_interleaving_test.go` |
| Def 27 派生实现（无元数据泄漏） | `runtime/context.go:139`、`:146`、`:149`、`:397`；`fiber.go:86` | `meta_intercept_test.go:398` |
| §4.4 异步惯性 | `runtime/orchestrator.go:358`、`:399`、`:455`；`component.go:99/79` | `iterator_test.go:184`、`det_admission_test.go` |
| §4.4 失败 raise | `runtime/orchestrator.go:501`、`:519`；`state.go:24`；`reconcile.go:45` | `phase3_contract_test.go:71`、`:201` |
| §4.4 配置 revision | `runtime/fiber.go:401`、`rehome.go:70`；`extensions/config/config.go:252`、`:269` | `revision_thm80_test.go:201`、`revise_isolation_test.go:77` |

**已关闭的历史偏差（不得重新引入）**：层级 realm 树 + 祖先回退/遮蔽（ADR-0001）、
值变换式 Intercept（ADR-0002）、无迭代器的 Apply-once（ADR-0003）。
子先于父清理是明确接受的强化（`ROADMAP.md:51`），不是历史偏差。
