# gocordis 代码级审查 (R12, 2026-09-09)

范围:runtime/ 内核、extensions/ 全部、cmd/ 示例、client/ 前端 SDK。
方法:逐文件走读 + 可疑点实证(竞态面分析、阻塞链分析、协议窗口分析)。
**按用户要求:只评审、不修复**;每项给证据与修复建议。
严重度:**P1** = 挂起/丢数据路径;**P2** = 资源/健壮性;**P3** = 卫生/一致性。

结论先行:**未发现可被外部攻击者直接利用的安全漏洞**(无注入/越权/崩溃级
并发缺陷;race 全绿;无鉴权为已裁决的延期项)。发现 **4 项 P1**(均为特定
条件下的挂起或永久丢数据路径)、6 项 P2、7 项 P3。

---

## P1 — 高优先(挂起 / 永久丢数据)

### P1-1 事件流重放缺口 > 订阅缓冲 → 永久丢失
- **证据**:`extensions/observe/{observer,subscription}.go`;
  `subscriptionBuffer = 256`;Subscribe 的重放经同一 `deliver`(256 缓冲)。
- **影响**:控制台断线期间事件 > 256 条时,重连订阅必然溢出关闭 → 重 Snapshot
  换新锚点 → 新锚点之后无缺口但**断线窗口内的事件永久丢失**,且每次重试都
  复现(环形只有 1024,重放全集必然超缓冲)。事件风暴下控制台永远追不上。
- **建议**(三选一):① 重放改为**迭代式**——Subscribe 立即返回,重放由消费
  端拉取(专门的重放迭代器,不受订阅缓冲限制);② 提供
  `RingQuery(from, limit)` 分页 API 供控制台补页;③ 重放阶段临时使用无界
  队列(仅重放期,风险可控因为 ring ≤1024)。②+① 组合最稳。

### P1-2 proc 客户端写持锁 → 潜在永久挂起
- **证据**:`extensions/loader/proc/backend.go` `write()` 持 `p.mu` 调
  `p.stdin.Write`;`markDead` 同锁。
- **影响**:若插件进程存活但停止读取 stdin,且某请求参数超过管道缓冲
  (64KB),`Write` 永久阻塞并持有 `p.mu` → `markDead`(读循环侧)同锁阻塞
  → EOF 永远无法被处理 → 该插件全部调用挂死(含 Rehome 等待路径)。
- **建议**:stdin 写移出 `p.mu`(独立 `writeMu`);或投递到写 goroutine
  (channel + 单写者);`markDead` 等读侧清理不再需要与写互斥。另:为
  `Caller.Call` 增加**默认每调用超时**选项(当前示例用 `context.Background()`,
  插件不回包即永久挂起)。

### P1-3 EventSink 在序号锁内被调用(承载性契约)
- **证据**:`runtime/events.go` `emitEvent`:`r.evSeq.mu.Lock()` 下调用
  `r.eventSink.Emit(ev)`。
- **影响**:契约要求 sink "MUST NOT block",但该契约是**承载性的**——
  sink 一旦阻塞(如未来 observe 增加慢路径),全部事件发射与
  `Snapshot.EventSequence` 读取一起停摆。
- **建议**:锁内仅取号并收集事件,解锁后投递(取号顺序即投递顺序,顺序性
  不变)。一行注释把 "MUST NOT block" 升级为内核侧防御。

### P1-4 Rehome 无观测事件 + 记录跨表搬移非原子
- **证据**:`runtime/rehome.go` `executeRehomeStep2`:provision 记录从旧
  realm `removeOwn` 后 `fresh.registerOwn`(两把锁分立,非原子);全过程
  **不 emit 任何 RuntimeEvent**。
- **影响**:① UI-03 订阅者对"迁移"完全失明(事件流无 withdraw/publish 对),
  控制台展示陈旧;② Snapshot 恰在搬移窗口构建时,该 provider 行短暂消失
  (两 map 非原子),下游观测出现闪断假象。
- **建议**:迁移处成对 emit `EventProviderWithdrawn`/`EventProviderPublished`
  (观测词汇已在内核);快照异常窗口可在文档声明(单命令内完成,窗口微秒级)
  或把记录搬移改为"先注册 fresh、后移除旧"并在快照端容忍同 id 双行去重。

---

## P2 — 中优先(资源 / 健壮性)

### P2-1 wasm:制品无大小上限,实例无执行限额
- **证据**:`extensions/loader/wasm/backend.go` `readSource` 全量 `os.ReadFile`;
  实例化未配置 fuel/epoch/`WithCloseOnContextDone` 执行监听。
- **影响**:恶意/劣质 guest 可提交超大模块(OOM)或死循环独占激活 goroutine
  (合作式假设被 guest 破坏,而 wasm 正是"不受信代码"的形态,承诺打折)。
- **建议**:Load 前制品大小上限;实例化挂 `context.WithTimeout` +
  wazero `WithCloseOnContextDone`(已有 ctx 传递,补 done 传播);可选 fuel。

### P2-2 proc:stdin 行读取无长度上限
- **证据**:`readLineSync` 用 `ReadString('\n')`,无上限。
- **影响**:故障插件输出一行超长"响应"即无界占用内存。
- **建议**:改 `bufio.Scanner` + `Buffer(make([]byte,64K), 1<<20)` 上限;
  超限按协议违规关闭。

### P2-3 webui:请求体无大小上限
- **证据**:`serveAPI` 直接 `json.Decode(r.Body)`,无 `http.MaxBytesReader`。
- **影响**:超大 POST(Uninstall/Install/SetConfig body)可被用于内存 DoS。
- **建议**:ServeHTTP 入口统一 `http.MaxBytesReader(w, r.Body, 1<<20)`(含
  静态与流路由)。

### P2-4 rehome 在确定性模式未支持
- **证据**:rehome 阻塞等待消费者 detach,而确定性驱动的完成全部停靠;
  rehome 测试全部跑普通 runtime。
- **影响**:det 模式下调用 Rehome 会死等(无驱动步骤放行)。
- **建议**:文档明确"det 模式不支持 Rehome",或为 det 补
  `StepRehomePhase2` 停靠类型。

### P2-5 observe:每事件 O(ring) 拷贝
- **证据**:`Observer.Emit` 满环时 `copy(ring, ring[1:])`(1024 指针)且持
  `o.mu`;事件风暴下发射路径成本线性于环深。
- **建议**:换真环形数组(头尾游标 + 定长槽),或仅当存在订阅者时保留环。

### P2-6 控制台开关的双路径语义(F-5 残留面)
- **证据**:`explorer.Service.Control` 现为**声明优先 + 直操 fiber 回退**;
  未接 `DeclarationSwitch` 的应用得到的是"瞬态覆写"(下次 Reconcile 翻回)。
- **影响**:行为因应用是否接线而不同,UI 无法区分"拒绝"与"暂时生效"。
- **建议**:未接开关时控制端点显式返回 `unsupported` 而非静默瞬态;或在
  ControlResult 增加 `Transient bool` 字段。

---

## P3 — 低优先(卫生 / 一致性)

| # | 项 | 建议 |
|---|---|---|
| P3-1 | `runtime/fiber.go` 注释"keyRealms 发布后只读"已因 Rehome 过时(唯一写者=orchestrator step2) | 更新注释;写明写者=orchestrator、读者=orchestrator+本 fiber Apply |
| P3-2 | `explorer.currentState` 读 `s.owned` 无锁(其余路径有锁) | 统一走 `s.mu` |
| P3-3 | `client/src/api.ts` `hubQuery` 参数 **key** 未编码(值已编码) | `encodeURIComponent(k)` |
| P3-4 | `extensions/console/webui/server.go` 尾部 `var _ = errors.New` 占位残留 | 删除 |
| P3-5 | explorer 的 `scalarConfig` 把非标量折叠为 "(N entries)" 摘要——只读展示可;若前端以该 DTO 回传 SetConfig 会毁掉嵌套配置 | 编辑回环仅允许标量字段,或走 `PluginLifecycle.Config`(全量) |
| P3-6 | client/types.ts 与 Go DTO 仍为手工镜像(无代码生成/一致性测试) | host DTO JSON 形状守护测试(F-2 已部分做)扩展到 ExplorerPlugin/ControlResult |
| P3-7 | rehome 后旧命名空间消费者恢复依赖"新提供者恰好出现在旧命名空间"——若声明层把 provider 永久迁走,旧消费者将永 Pending(符合论文,但控制台应展示原因) | explorer 的 DependencyView 增加 Reason 标注(已具备 Waiting 原因字段,前端透出即可) |
| P3-8 | `proc` 握手超时期间插件 stderr 默认丢弃(无诊断) | `WithStderr` 已有;demo/文档提示接线 |

---

## 已核查且判定**无问题**的点(避免重复怀疑)

- **webui.Publish 链**:hub → server → SSE 全程非阻塞丢弃,慢客户端不阻塞
  内核与其它订阅者;SSE 挂在 `r.Context()` 上,客户端断连即回收;
- **static 服务**:go:embed fs + FileServerFS,无路径遍历面(embed 根约束);
- **scheduler**:`Every <= 0` 显式防护(interval.go:88);
- **explorer scalarConfig**:仅展示折叠,`PluginLifecycle.Config` 走全量,
  无数据破坏路径;
- **configwatch**:绝对路径校验在位;缺失文件走"空声明"删除语义(文档化);
- **rehome keyRealms 写**:唯一写者在 orchestrator step2;其他读者的表均为
  各自 fiber 的表——race 全绿 + 分析无并发窗口;
- **kernel 命令循环 / 陈旧完成隔离 / Failed 语义 / 环检测 / Enabled 调和**:
  本轮复核无新发现(与 R8 结论一致)。

## 处置建议优先级

**P1-1/P1-2(丢数据与挂起)先行**;P1-3/P1-4 随下一个内核触点顺带修;
P2-1/P2-2/P2-3 属"上线前硬门槛"(与 F-3 鉴权同列生产清单);其余按顺手修。
