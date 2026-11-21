# ADR-0002: Interception 回归论文语义 — 元数据 monoid

Status: **Accepted** (2026-09-08;实施与 conformance 见本文「实施记录」)
Date: 2026-09-08
Authority: 论文 §3.2.3 Definition 26/27、§4.4 Isolation 段末、§6.3 Access Control

## 背景

现有 `runtime.Intercept[T](ctx, key, fn)` 是**函数变换链**:per-realm 安装序链,Require
读取时依次变换值;Effect 可逆;panic 受控。论文原文的拦截是声明式元数据模型:

- **Def 26**:Σinter = (ι, σ);ι 是 context 携带的元数据表 (k ↦ ℳₖ),σ 把 key 映射到
  **provider 函数** ℳₖ → 𝒱ₖ;每个 key 配一个 monoid (ℳₖ, ⊕ₖ, εₖ)。
- **Def 27**:`intercept(k, ν)` 把 ν **合并**进 ι(k)(ι(k) ⊕ₖ ν,右偏——context 优先,
  可覆盖组件声明);`get(k, μ)` = σ(k)(d(k) ⊕ₖ ι(k)):组件声明元数据与 context 元数据
  合并后交给 provider 解释。"metadata adjusts how a binding is used rather than what a
  key resolves to … no premise reads it and no field of a fiber holds it" —— 拦截不进
  依赖图,增删不触发 reload。
- **§6.3**:该机制承担 capability 访问控制——provider 按合并元数据裁决(如文件系统
  key 的路径白名单),orchestrator 可在不改 provider 的前提下收紧任一组件的权限
  (如社区组件只读、核心组件全量)。

## 问题

函数链与元数据模型**同目的、异机制**:链直接改值,provider 无从知晓访问语境;
"provider 依元数据做策略裁决"(论文 §6.3 的核心能力)在函数链下无法表达。
且论文元数据模型下拦截是纯读取期机制(no field of a fiber holds it),与现有
per-realm 安装链的存放方式不同。

## 决策

1. **Key 类型扩展**:`Key[T]` 增加可选元数据参数——`Key[T, M]` 或伴随注册
   (M 提供 Monoid 三元组:zero、combine;右侧优先)。无元数据的 key 退化为
   ε 单位元平凡情形,现有 API 保持源兼容。
2. **provider 形态**:Provide 可选接收 `func(M) T` 形态(元数据解释器);读取 =
   `provider(declared ⊕ context-carried)`,合并右偏(context 覆盖声明)。
3. **`intercept(k, ν)` 语义** = 向 context 元数据合并 ν,返回可逆 effect(撤销 =
   恢复 ι 前值);**不进依赖图、不触发 reload**(与论文逐字一致;现有函数链已满足
   "不触发 reload",保留该性质)。
4. **旧函数链处置**:`Intercept[T](ctx, key, fn)` 保留为适配层(fn = 元数据内建
   函数 monoid 的特例,即"值为函数、合并为组合"的 key),文档标注为派生形态;
   视迁移情况在 P6 决定是否裁撤。
5. **Get 与 Require 统一**:组件声明侧元数据 d(k) 来自组件的 inject 声明
   (论文:component-declared),Require 读取时与 context 元数据合并后传给 provider。

## 后果

- + 解锁 §6.3 访问控制(策略在 provider,orchestrator 经 context 收紧)。
- + 拦截与 Theorem 70/80 的关系更干净:拦截不改变解析目标(no premise reads it),
  合流生成器无需为拦截建模交错。
- − Key 类型面扩展(泛型参数或伴随注册),扩展与测试需迁移;
  现有 `Intercept` 用户的语义需逐个核对(链序 vs monoid 合并的差别)。

---

## 实施记录 (2026-09-08, Status → Accepted)

- `MetaKey[T, M]`(`runtime/key.go`):携带 monoid(εₖ = zero,⊕ₖ = combine,右偏)的
  metadata key;capability 身份派生自 (func(M) T, name),与普通 Key 不碰撞。
- `Dependency.Meta` + `RequiresMeta(key, d)`:组件**声明侧**元数据 d(k)(Def 26)。
  声明不参与满足性判定——拦截只影响"绑定如何被使用",不影响"解析到什么"(§6.3)。
- `ProvideMeta(ctx, key, func(M) T)`:provider 即元数据解释器(σ(k): ℳₖ → 𝒱ₖ)。
- `RequireMeta(ctx, key)`:读取 = `provider(d(k) ⊕ₖ ι(k))`;ι 为 context 携带元数据。
- `InterceptMeta(ctx, key, ν)`:向 context 合并 ν(ι ⊕ₖ ν,右偏、context 优先),经
  ctx.Effect 可逆;安装/调整/卸除不触发 reload;combine panic 受控返回错误。
- ~~ι 存储于 realm 的 per-key meta 表,沿 context 链继承~~ **已于 2026-11-21 修订**:
  初版把 ι 存入共享 realm,导致同 Runtime 的独立根 Fiber 互相读到对方的元数据
  (审计 P-CO-01,论文 Def 27 要求 Derived realization)。现 ι 存储于 **Context 的
  派生 metaView**(`Context.metaView`),`Child()` 在派生时快照父视图,`newContext`
  注入;独立根无继承。见本文件「修订记录 (2026-11-21)」。
- 一致性测试:`runtime/meta_intercept_test.go`(声明单独生效、context 右偏覆盖
  声明、monoid 折叠序、不触发 reload、可逆恢复、未声明拒绝,以及
  `TestMetaInterceptDoesNotLeakAcrossIndependentRoots` 防跨根泄漏)。
- 旧 `Intercept[T]` 函数链保留为适配层(内建"值为函数"monoid 的特例),P6 复审去留。
- **P6 复审决定 (2026-09-08):保留。** 无生产扩展消费它,但它是"值变换"这一合法
  用例的最短表达,且 scoped_realm 套件持续约束其可逆/panic 受控语义;裁撤的收益
  (API 面 -1)低于迁移成本。与 MetaKey 的边界:改值用 Intercept,策略/访问控制
  用 InterceptMeta(§6.3)。
- 门禁:`go test -count=1 ./...`、`go test -race -count=1 ./runtime/`、vet、gofmt 全绿。

---

## 修订记录 (2026-11-21, Derived realization 作用域)

审计 P-CO-01 确认初版实现违反论文 §3.2.3 Def 27 的 **Derived realization**:
拦截应派生新 context 而非原地修改共享上下文。具体缺陷:初版 `InterceptMeta`
经 `realm.addMeta` 写入 Runtime 的 `rootRealm`,而所有未显式隔离的根 Fiber 共享
该 realm —— 一个根组件安装的元数据会泄漏给完全独立的另一个根组件及其消费者。
反例测试 `TestMetaInterceptDoesNotLeakAcrossIndependentRoots` 修复前失败
(`host-only:[/public,/secret]`)。

**修订(不改变 ADR-0002 已接受的语义决策,仅改正实现作用域)**:
- `Context` 持有 `metaView []*metaEntry`(Coeffect 派生视图,metaMu 保护);
- `InterceptMeta` 安装/撤销只改当前 Context 的派生视图,不再触碰 realm;
- `RequireMeta` 从当前 Context 读取 ι,combine 在锁外执行(RT-12 保持);
- `Context.Child` 在派生时快照父 Context 的视图,经 `cmdSpawnChild.inheritedMeta`
  → `Fiber.initialMetaView` → `newContext` 注入子 Context;
- 独立根 Fiber 无 initialMetaView,不继承任何其他根的元数据。

这使拦截的作用域等于论文的 derived context:安装可逆、不触发 reload、不进依赖图
的语义不变,同时消除了跨根泄漏。变异验证:恢复 realm 写入后防泄漏测试立即变红。
