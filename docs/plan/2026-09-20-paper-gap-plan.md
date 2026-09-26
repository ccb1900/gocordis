# 论文缺口计划（Paper-First Gap Plan）— gocordis

Date: 2026-09-20。裁决源：[`docs/论文.pdf`](../论文.pdf)（唯一规范）。
本文件是**以论文原文为基准、逐节核查代码后**得出的缺口清单，取代任何"以仓库自述文档为准"的评估。
方法说明：本文每条缺口都附**代码/论文双向证据**（论文行号 ↔ 代码 file:line），不接受"文档说缺所以缺"。

门禁恒为 `make verify`（fmt-check + vet + test + race）。新增机制 MUST 配确定性种子 fuzz。

---

## 0. 一句话结论

可操作的演算内核（§3.1–3.3、§4.1–4.3、§5 loader/HMR、§6.2）已忠实实现，且**部分超过仓库自评的保守度**（见 §3"已订正的假偏差"）。
真正的缺口分两类：**元理论的方程/观测那一半没建**（≃ 商 + witness，可建）、**若干论文章节未实现或被 demo 顶替**（§5.3/§6.6/§6.3 策略层）。
**没有任何一条缺口是"Go 语言做不到"**——论文 §6.4 自证 language-independent。

---

## 1. 元理论层缺口（最高优先，纯语义正确性）

### G-A1 观测等价 `≃ₖ` 未成为一等公民

- **论文锚点**：Def 33/34（两 context 在键集 S 上 related；每键 operations 诱导 `≃ₖ`）；§3.3.2 末（"every equality of §3.1 is re-read up to that equivalence"，paper.txt:889）；Thm 68 结论式 (55) 右端为 `≃_K`。
- **代码证据**：`runtime/key.go` 全量搜 `equiv` → 0 处；`MetaKey`（key.go:57）已挂 `combine`（即 ⊕ₖ），证明"往 key 上挂用户函数"的机制已具备，只是 `Key[T]`（key.go:33）没挂 `equiv`。快照比较走 Go 原生相等。
- **为何不是语言限制**：`≃ₖ` 按定义"无操作序列可区分"，对任意用户操作**在所有图灵完备语言不可判定**（Rice 类）；故论文本身把 `≃ₖ` 当作**逐 key 自带、由实现方提供**的语义关系。Go 提供它 = 一个 `func(a,b T) bool`，与 `combine` 同构。
- **要做**：
  - [ ] `Key[T]` 增可选 `equiv func(T,T) bool`；`NewKey` 默认对 comparable T 用 `==`、否则 `reflect.DeepEqual`（并记 `defaulted bool` 供诊断）。
  - [ ] `runtime/observation.go` 的 snapshot 比较器接入逐 key `≃`（ProviderView/ScopeSnapshot 值比较改走 key.equiv）。
  - [ ] 定理性质测试（thm68/70/73/80）把"基线等价"断言从 Go `==` 换成"按声明 `≃` 比较"。
- **验收**：Thm 68 `TestThm68*` 至少一条在**非平凡 ≃**（例：单调分配器，状态不同但观测等价）下成立——当前这类只能靠 `==` 误判或跳过。
- **风险**：中。只加钩子 + 换比较器，不碰演算路径；`Key[T]` 结构体字段增加影响 identity 计算需保持 `(typeID,name)` 不变（equiv 不进 identity）。

### G-A2 witness 义务（`g∘f=id`）不在内核表达

- **论文锚点**：Def 8（`𝔈*Γ` = 见证 `g(δ)=γ` 的 effect function）、Def 17（witnessed iterator `ℑ*`）、Def 37（up to `≃ₛ` 的见证）；Thm 7/16/68 的**前提**即 witness。
- **代码证据**：`ctx.Effect`（`runtime/context.go`）接受任意 `(forward, inverse)` 闭包，类型与运行期均不要求 `g∘f=id`；全局搜 `witness`（非测试）→ 0 处。
- **要做**（取"运行期复核"而非"类型级证明"，语言无关）：
  - [ ] deterministic / **checked mode** 下：effect 提交后、或 fiber 进入 Active 前，复核该 effect 的 inverse 对 accumulator 的作用 `≡ id (up to ≃)`；违约记为 `ErrWitnessViolated` 事件，**不**默认 panic（保持单一生命周期权威，不改语义）。
  - [ ] 文档明确：Go 无依赖类型 → witness 是**可选运行期断言**，不是论文意义上的类型级约束；这是宿主实现选择，不是理论降级。
- **验收**：一个故意写错 inverse 的组件，在 checked mode 下被 `ErrWitnessViolated` 捕获；默认 mode 行为不变、`make verify` 全绿。
- **风险**：中低。仅在插桩 mode 生效；默认路径零开销。

### G-A3 Isolation 的 K×R 配对编码未做形式化落点

- **论文锚点**：§4.4 Isolation（K×R 配对；多 realm 并存时 `provisions disjoint within a realm`，元定理原样成立）。
- **代码证据**：`runtime/providers.go` provider 解析 `lookupOwn`（:159）确为"逐 key、无祖先回退"，与 Def 24 一致；但 K×R **配对编码**（把 realm 维度并入定理生成器）未见——定理测试的 realm 交错覆盖不完整（convergence-matrix GAP-02 同源）。
- **要做**：
  - [ ] Thm 80 合流生成器扩展到 **realm 维度交错**（多 realm 并存下逐 realm disjoint 断言）。
- **验收**：`TestThm80*` 含 realm 交错用例并绿。
- **风险**：低（测试侧扩展）。

---

## 2. 未实现 / 被顶替的论文章节

### G-B1 §5.3 Case Study: Koishi 未复现

- **论文锚点**：§5.3（paper.txt TOC:81，"Case Study: Koishi"，真实插件宿主）。
- **代码证据**：`cmd/` 下为 backfill/collector/example/host/httpd/procplugindemo/wasmhmr，无 Koishi 对应物。现有 demo 各自覆盖局部模式，但未复现论文那个端到端案例。
- **决策项（需与作者对齐）**：Koishi 是"论文示范"而非"规范"——可（a）新增 `cmd/koishi` 忠实复现，或（b）在 ROADMAP 显式声明"以自建 demo 承担 §5.3 角色"并逐条对照论文该节验证覆盖。**当前二者皆无，属空档。**
- **风险**：文档性/低，取决于选 (b)。

### G-B2 §6.6 Dependency Typing and Versioning 未实现

- **论文锚点**：§6.6（TOC:88）。ROADMAP P7 已列 `[ ]` 未打勾——此处**承认缺失**，非误判。
- **代码证据**：`loader.Module.Version` 字段存在（proc backend 透传），但无**版本选择/兼容性判定**逻辑；`Key` 无版本维度。
- **要做**：
  - [ ] 版本作为 `Key`/`Artifact` 元数据演进的落点（与 G-A1 的 key 元数据设计合并考虑，避免两套机制）。
  - [ ] broker/registry 的多实例选择策略纳入版本维度。
- **验收**：同键多版本共存 + 按声明版本路由的 conformance 测试。
- **风险**：高（触及 key identity 语义），排在 ≃ 层之后。

### G-B3 §6.3 Access Control 仅有机制、无策略层

- **论文锚点**：§6.3（TOC:85）；Def 26/27 的拦截 = capability 式访问控制的载体（paper.txt:880"让 enclosing context 约束组件如何用 coeffect"）。
- **代码证据**：`MetaKey`/`ProvideMeta`/`InterceptMeta`（context.go:460/470/515）已把"provider 依元数据裁决"的机制**建全**；但没有一个**策略 provider** 示例/默认实现把机制用起来（访问控制作为功能不存在）。
- **要做**：
  - [ ] 以 MetaKey 写一个访问控制参考 provider（读 `d(k)⊕ι(k)` 决定放行/限流），作为 case study 入 `cmd/` 或 docs。
- **验收**：论文 §6.3 场景可运行复现。
- **风险**：低（纯上层，用现成机制）。

### G-B4 §6.4 Language Independence 仅 WASM 一条路

- **论文锚点**：§6.4（TOC:86）。
- **代码证据**：`extensions/loader` 后端 = builtin(Go) + wasm + proc。proc 仍是 Go/本机可执行；跨语言实际只有 wasm guest。
- **要做**：低优先。若要坐实"language independence"，补一个非 Go、非当前 wasm ABI 的 guest 样例（扩展 §6.4 论证）。可长期挂起。
- **风险**：低。

---

## 3. 已订正的"假偏差"（ROADMAP 需回写，非代码缺口）

核查证明下列条目**代码已达成**，但 `docs/ROADMAP.md §1.2` 仍标"机制不同/缺失"——**文档落后于代码，须订正，勿据此重写代码**：

- **[已达成] Effect 迭代器 + 逐迭代 L-Divert**
  代码：`runtime/orchestrator.go:311 runApplyIter` + `probeDivert`（:355）+ `cmdDivertProbe`（:117），inertial landing（:310 注释）。
  论文：Def 17/18、Table 1 的 L-Iter/L-Finish/L-Divert（paper.txt:1824-1829）。
  ROADMAP §1.2"per-iteration divert 粒度缺失"——**作废**。

- **[已达成] Interception = 元数据 monoid**
  代码：`MetaKey.combine`（⊕ₖ 右偏，key.go:60）、`ProvideMeta(func(M)T)`（= σ(k):ℳₖ→𝒱ₖ，context.go:460）、`RequireMeta` 算 `d(k)⊕ι(k)`（:491/500）、`InterceptMeta` 做 `ι[k↦ι(k)⊕ν]`（:515）。
  ROADMAP §1.2"表达不了 provider 依元数据裁决"——**作废**（旧 `Intercept[T]` 值变换链并存，属历史 API，非当前机制）。

- **[非偏差，勿动] 元数据/拦截沿 realm 链累加**
  `providers.go:88 metaForKey` 沿 parent 链做 **累加折叠 `ι⊕ν`（祖先→派生，最右覆盖）**，是 Def 26/27 连续 `intercept` + Def 28 递归 `Γ∞` 的**忠实表示**，不是"祖先回退 shadow"式偏差。provider 侧 `lookupOwn` 才是逐 key 无回退（匹配 Def 24）。**此前一版误判为偏差，已收回。**

- **[已修] G-6 HMR×proc 集成测试**：`extensions/hmr/proc_replace_test.go`（env-gated 子进程 helper）存在且通过。评审清单里"G-6 未测"——**作废**。

- **[已修] CI 失效路径**：`.github/workflows/ci.yml:45` 原引用已删除的 `./extensions/http/`（该包 2026-09-09 commit `e33d9e54` 迁为 `cmd/httpd`，此步 2026-09-18 commit `0237011e` 引入即失效）。**已改为 `./cmd/httpd/`**，本地 `go test -race ./cmd/httpd/` 通过。

---

## 4. 工程 / 收敛缺口（非论文语义，但影响"可交付"）

- **E-1 无 benchmark**：`find -name '*_test.go' | grep 'func Benchmark'` → 0 处。无性能基线即无法声称"不随调和退化"。
  - [ ] 至少：激活/撤销吞吐、provider 解析、`metaForKey` 链折叠深度、snapshot 生成 各一 bench。
- **E-2 P5 未收敛：`Fiber.Revise` 未被 loader/HMR 复用**：`grep Revise extensions/loader extensions/hmr`（非测试）→ 0 处。HMR `doReplace`（hmr_impl.go:373）手写 `Load→Dispose→Gone` 序列，用公开 API 未违铁律，但与内核 revision 复合**重复实现**。ROADMAP P5 `[ ]` 已承认。
  - [ ] 评估 HMR 短路径改走 `Revise`（保 warm 语义前提下），或显式记"为何不用 Revise"的 ADR。
- **E-3 死代码**：`extensions/hmr/hmr.go:100 var _ = sync.Mutex{}`，仅为吊住 `sync` import，无实际引用。
  - [ ] 删该行 + 去掉多余 import。
- **E-4 CI 矩阵未实跑**（评审 #21）：E-1/E-2 之外，`.go-version` 已钉版本，但多 OS/多版本矩阵未确认在 Actions 上真跑（本次 CI bug 存活两轮编辑即为旁证）。
  - [ ] 确认 Actions 实际触发且绿；加 badge 或定期巡检。
- **E-5 插件开发指南缺**（评审 #20）：外部作者如何写 proc/wasm 插件无成文手册（`docs/guide/developing-applications.md` 覆盖组件层，未覆盖后端 ABI/协议）。

---

## 5. 建议里程碑顺序（低风险 → 高语义风险）

1. **M0（已完成，本文件同批）**：CI 路径修复（§3）。
2. **M1（纯清理，零语义）**：E-3 死代码、§3 ROADMAP §1.2 回写作废条目、G-B1 决策(选 b 则文档)、G-B3 访问控制参考 provider。
3. **M2（元理论·可建）**：**G-A1 `≃ₖ` 一等公民**（钩子 + 比较器 + Thm68 换 ≃）→ **G-A2 witness checked-mode** → **G-A3 Thm80 realm 交错**。这是"论文那一半还没走完"的核心。
4. **M3（性能护栏）**：E-1 benchmark + E-4 CI 真跑。
5. **M4（语义演进·高风险）**：G-B2 依赖类型/版本（与 G-A1 合并设计）+ E-2 Revise 收敛评估。

> 硬约束：M2 全程 MUST NOT 改动 `orchestrator.go` 决策域与演算规则——只加钩子、换比较、加插桩。任何触碰 `≡`/witness 的改动以"默认 mode 行为不变 + `make verify` 全绿"为准入门。

---

## 6. 本文件不含（明确排除）

- 已实现且绿的一切（演算内核、五定理行为级测试、config/loader/hmr/broker/proc/wasm/console/observe 全链）——**不在缺口清单**，勿回退。
- §7 Related Work / §8 Conclusion——散文，无实现项。
