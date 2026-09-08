# ADR-0001: Isolation 回归论文语义 — 逐 key realm 表

Status: **Proposed** → 实施 = P2
Date: 2026-09-08
Authority: 论文 §3.2.3 Definition 24/25、§4.4 Isolation 段、§4.4 Configuration 段

## 背景

现有实现用**层级 realm 树**承载隔离:`WithScope()` 派生子 realm、继承祖先、shadow、
祖先回退,以及 GAP-01 冻结的"retiring 记录阻断祖先回退"规则。论文原文的隔离是另一套:

- **Def 24**:coeffect context with isolation = `(ρ, σ)`,ρ: K ⇀ R 是**逐 key** 的
  realm 指派表(key 不在 dom(ρ) 时解析到自身 realm,即对角线 (k,k));σ 是 (realm → 值) 表。
- **Def 25**:`isolate(k, r)` 派生新 context(ρ[k↦r],σ 不变);已隔离的 key 被**重指派**
  而非拒绝;"a key already isolated is reassigned rather than refused"。
- **§4.4 Isolation**:realm 在 fiber **插入时固定**("For realms fixed at a fiber's
  insertion");把 key 集取为 K×R 配对后,"the rules and the results of §4.3 apply at
  K×R as they stand" —— 即 provision 互斥改为 **realm 内互斥**,五个元定理原样成立。
- **§4.4 Configuration**:运行时重指派 ρ 是**接口变更**,只能走 revision 复合
  (retire → deactivate → remove → 同名重插),因为 dₙ/pₙ 随 entry 一次写入(Lemma 59(5))。
- 论文**没有** realm 层级、祖先回退、shadow 这三个概念;每个声明 key 解析到唯一 (k, ρ(k))。

## 问题

层级 scope 树是论文之上的超结构:其祖先回退/shadow 语义无定理背书(五定理证明依赖
Def 63 的良构性 + K×R 编码,不含回退查找),且 GAP-01(retiring-shadow)这类边角规则
在论文模型下不存在对应物,持续产生"论文是否允许 X"的裁决负担。

## 决策(Proposed)

1. **原语改为论文形态**:Key 级隔离——fiber 插入时其声明 key 经 ρ 映射为 (k, ρ(k));
   provision 互斥在 realm 内成立;`Isolate(k, r)` 作为宿主 API 提供,语义 = 论文 Def 25。
2. **运行时重指派 = revision**:不在 resolve 路径上做动态 ρ 查找变化;isolate 变更走
   P5 的 revision 复合(卸下→重插)。
3. **层级 scope 降级**:保留 `WithScope()` 作为**派生语法糖**——"给一组 key 建隔离 realm",
   编译到逐 key 表(展开发生在组件声明期,不在 resolve 期);**删除祖先回退/shadow**,
   resolve 只读 (k, ρ(k)) 唯一绑定。GAP-01 规则随之失效(retiring 期间 (k,r) 绑定本身就
   不满足,无回退问题可言),相应 conformance 测试改写为论文语义。
4. **定理背书**:实现以 K×R 配对为不变量的编码,使 Theorem 64/68/70/73/80 在多 realm
   下逐字适用(论文已给出该论证);生成器扩展 realm 维度交错。

## 后果

- + resolve 路径简化为唯一绑定查找,无回退分支;定理背书完整。
- − "子树隔离即得"的便利降级为声明期展开;依赖祖先回退的用例(如有)需显式建模
  (父提供 → 子 revision 换绑)。
- 迁移:`extensions/*` 全部经 public API,预计零改动;`scoped_realm_test.go` 中依赖
  shadow/回退的断言需改写。

---

## 实施记录 (2026-09-08, Status → Accepted)

按本 ADR 落地(与"决策"节的差异见 §偏差):

- `realm.lookup` → `lookupOwn`(单命名空间,无祖先回退);新增 `effectiveRealm(f, key)`:
  fiber 的逐 key 隔离表(`Fiber.keyRealms`,论文 ρ)优先,否则 fiber 的 scope realm。
  resolve/Require/观测快照全部经 `effectiveRealm` 单命名空间解析。
- `WithScope()` = 派生语法糖:child 获得全新 provider 命名空间;声明期把 child 的全部
  声明 key 归巢到该命名空间(显式逐 key 覆盖存活)。per-key 隔离表在 Load/Child 时
  固定,运行时变更留待 P5 revision。
- **context 链保留**:realm.parent 链继续服务拦截元数据继承(论文 Def 26 的 ι)与
  扩展事件作用域;解析路径绝不走该链(代码注释与 ROADMAP §1.2 均已注明)。
- 环检测边条件改为 `effectiveRealm(x,key) == effectiveRealm(y,key)`(命名空间相等,
  无路径可达性)。
- 测试改写为论文语义:`gap01_retiring_shadow_test.go`(GAP-01 祖先回退规则废止;恢复
  = 同命名空间 reload 后重绑)、`f01_scope_test.go`(NoFallback)、`ui02_snapshot_test.go`
  (空 scope 消费者 Pending)、PC-08/PC-18(scope 自持 provider;root 撤离不扰动
  scope 绑定)。
- 门禁:`go test -count=1 ./...`、`go test -race -count=1 ./...`、`go vet`、gofmt 全绿。
