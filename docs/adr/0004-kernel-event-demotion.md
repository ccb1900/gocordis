# ADR-0004: 内核瘦身 — 事件派发移出 Kernel

Status: **Proposed** → 实施 = P6
Date: 2026-09-08
Authority: 论文 §2.1(effect 的实例包括 event registration)、§4.2.2("no rule mentions
a scheduler; the steps are any sequence of rule applications");ROADMAP §1.3

## 背景

Kernel 现含事件运行时(`runtime/event_registry.go` + emit/serial/parallel/waterfall
派发,P1 一致性套件 PB/E 系列)。论文的 core calculus **没有事件总线或派发模式**:
事件注册只是"被追踪 effect"的一个实例(可逆注册),派发顺序语义(emit/serial/
parallel/bail/waterfall)是 JS Cordis 生态的 API 约定,不属于论文语义面。
论文 §4.2.2 明确演算是 "reactive only, in that no rule mentions a scheduler"。

## 问题

Kernel 公开面含非论文语义,违反 ROADMAP P6 的"kernel 公开 API 逐项可锚定论文条目"
目标;事件派发作为 Kernel 内机制还会与 P4 的迭代器调度产生不必要的耦合点。

## 决策(Proposed)

1. **派发模式整体迁往 `extensions/event`**(该扩展本已存在总线实现,合并即可):
   kernel 侧仅保留——事件处理器注册 = ctx 上的**可逆 effect**(注册句柄、撤销 = 注销),
   这正是论文支持的形态;派发(dispatch 及其模式)由 extension 在注册的能力之上实现。
2. **`runtime/event_registry.go` 降级**为 extension 可用的注册数据结构
   (owner + realm + 确定性注册序保留——注册序是可逆 effect 的撤销语义的一部分),
   派发引擎代码迁出。
3. **bail 模式**:随迁移一并补齐(Cordis 生态五模式之一;旧 spec 标注"可选",
   迁移是补齐的最低成本时机)。
4. **一致性证据迁移**:P1 emit/serial/parallel/waterfall conformance 套件随代码
   迁至 extension 包,断言不变;Kernel review 面缩到论文条目。

## 后果

- + Kernel 公开 API 全部可锚定;单一生命周期权威的边界审计更简单。
- − 一轮 import 路径迁移与测试包移动(机械性);依赖 kernel 事件分发 API 的
  扩展(若有)需改经 extension 事件总线。
