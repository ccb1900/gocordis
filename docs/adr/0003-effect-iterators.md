# ADR-0003: 效果迭代器 — range-over-func 激活与迭代边界 divert

Status: **Proposed** → 实施 = P4
Date: 2026-09-08
Authority: 论文 §3.1.3(effect iterators)、§4.2.2(L-Iter/L-Divert/L-Finish)、§4.4 Asynchrony

## 背景

论文的组件激活是**效果迭代器**:每次迭代 yield(变换, 逆, 续体),L-Iter 一步执行一次
迭代并累积逆(g ∘ h,LIFO),L-Finish 落入 Active;**L-Divert 可落在任意两次迭代之间**,
把还在 Loading 的 fiber 路由进 Unloading(带着已累积的逆)而不当场应用——这是
"consumer 卸载可抢占 provider 加载"的机制基础。现有实现(与 stc-go 同)把激活简化为
**Apply 一次到位 + ctx.Effect 槽位注册**:不可中断,仅完成后按 stale 检查决定去留。

论文 §4.4 Asynchrony 为此留了接口:异步宿主是 **inertial** 的——L-Divert 只取
landing 替代(让在飞迭代落地再进入 Unloading);"every result of §4.3 quantifies over
all sequences … covers the inertial ones, and Theorem 73 appeals to the aborting
alternative nowhere"。即:inertial 宿主下五定理逐字成立,无需 aborting 路径。

## 问题

Apply-once 丢失了迭代粒度:激活中途目标翻转(依赖消失/替换)只能等整个 Apply 跑完;
论文 Theorem 70(2) 的 episode 包夹关系、Theorem 80 的规范形都以迭代为基本步。
现有 stale-completion 检查只是 inertial 的最小形态,不是迭代步。

## 决策(Proposed)

1. **组件激活可选表达为 `iter.Seq`**(Go 1.23+ range-over-func,go.mod 已 1.24):
   ```go
   // 单迭代特例 = 现有 Apply(源兼容;ctx.Effect 槽位即逆的注册)
   func(ctx *Context) error                      // 现有形态,保留
   func(ctx *Context, yield func(Step) error) error // 迭代器形态(签名待实现期定稿)
   ```
   每次 yield = 一次 L-Iter:执行一段变换并注册其逆;yield 之间是 orchestrator 的
   天然检查点。
2. **L-Divert 落在 yield 边界**(inertial):目标翻转时,fiber 在当前迭代落地后进入
   Unloading,应用已累积的逆(现有 unwind 路径直接复用);**不实现 aborting 替代**。
3. **deterministic driver 扩展**:yield 边界成为可调度步骤(Step 增加 IterDone 类),
   divort 时刻成为驱动可控制的决定点;Theorem 70/80 生成器加入迭代粒度交错
   (激活中段依赖消失/替换)。
4. **失败语义不变**:迭代器任一步报错 = 论文 raise,走现有 Failed 路径(§4.4 Failure:
   经 Unloading 路由、已装部分照常撤销、outcome 阻止重入)。

## 后果

- + 激活可抢占粒度与论文对齐;Theorem 68/70 的证据从"完成时等价"细化到迭代步。
- + 组件作者可用顺序代码写长激活(如分批预热连接池),每批之间可被依赖变化打断。
- − orchestrator 的 fiber 状态机需要容纳"Loading 且在飞迭代"的补全路由
  (现有 det 模式的 parked-completion 机制是现成底座);
  双形态 API 增加文档与测试面。
