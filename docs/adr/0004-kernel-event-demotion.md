# ADR-0004: 内核瘦身 — 事件派发移出 Kernel

Status: **Accepted**(2026-09-08)
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

---

## 实施记录 (2026-09-08, Status → Accepted)

- **Kernel 保留**(论文可锚定面):EventKey/EventHandler/WaterfallHandler/Next 类型、
  On/OnWaterfall 注册(= 可逆 effect,EffectKindEvent)、registry 数据结构
  (owner + context 链 + 确定性注册序)、公开读模型 `Context.EventBindings(id)` +
  `EventBinding{Owner, Order, Handler, Chain}`、统一 panic 政策
  `GuardEventHandler`、`Context.Cancel()`(协作式自取消,非生命周期决策)。
- **迁往 `extensions/event/dispatch.go`**:Emit / Serial / Parallel / **Bail(新增,
  Cordis 五模式补齐)** / Waterfall——全部经 EventBindings 读模型实现;派发快照、
  scope 可见性、注册序、owner 有效性、取消检查、panic 受控语义逐条保持。
- **一致性证据迁移**:P1.1–P1.4 conformance 套件(emit/serial/parallel/waterfall)
  与 review gap 套件整体迁至 `extensions/event/*_test.go`(package event_test),
  断言不变;原经由内核内部的访问点(f.activation.ctx、eventReg.count、effect 槽位
  直读)改写为公开面等价物(ctx 自捕获 + Snapshot EffectView + EventBindings 读模型)。
  registry 残留/规模断言改经 EventBindings 探针。
- **消费方更新**:p2.1 边界套件与 integration 套件的 runtime.Emit/Serial/Parallel/
  Waterfall 全部改为 event.*;kernel 内不再有任何派发模式代码。
- 门禁:`go test -count=1 ./...`、`go test -race -count=1 ./...`、vet、gofmt 全绿。

### R3 附录 (2026-09-09):Bus 移除

评审 R3 发现 `extensions/event` 存在两套事件机制并存:kernel-registry 派发
(dispatch.go)与旧 P1-10 独立总线(Bus,Publish/Subscribe)。Bus 无任何生产消费方
(仅 4 处测试引用),属"仅靠测试续命的生产 API 面",已整体移除:

- 删除 `event.go`/`event_test.go`/`event_internal_test.go`/`runtime_event_test.go`
  (E12–E14 的真实意图是"稳定性能力语义",已由 registry/scheduler 套件覆盖,Bus
  只是载体);
- 三个集成消费点迁移:`E2E15` 改经 `event.Emit`(语义不变);`E2E24`/`E2E30` 中
  Bus 仅作"并发扩展活动/关停参与者",直接摘除(注册表变更 + scheduler 仍是压力源)。
- 同批内部化:确定性驱动面(Step/StepKind/StepApplyDone/StepUnwindDone/
  RuntimeMode/WithRuntimeMode 等)原为导出但消费方全部是 `package runtime` 内部
  测试——全部改为非导出(detStep/runtimeMode/...),kernel 公开面进一步收缩,
  定理驱动器(定理基础设施)功能不变。
