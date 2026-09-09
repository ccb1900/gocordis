# AGENTS.md — 用 gocordis 开发应用(LLM 工作规范)

本文件是 **AI 编码代理在本仓库/基于本仓库开发应用时的强制规范**。
人类可读版教程见 `docs/guide/developing-applications.md`;语义争议以论文
`docs/论文.pdf`(*A Programming Paradigm for Spatiotemporal Composability*)
为唯一裁决来源。规范用语:MUST(必须)/ SHOULD(应当)/ MAY(可以)。

---

## 1. 设计思想 → 十二条铁律

以下每条都锚定论文语义,违反任何一条 = 实现错误,MUST 修正:

1. **声明权威** MUST:组件只允许 `Require`/`Provide` 自己 `Inject()`/`Provide()`
   集合里声明过的 key(运行时强制,undeclared 直接报错)。
2. **不做启动顺序编排** MUST:依赖满足是触发条件,不是你写的顺序。禁止手工
   "先加载 A 再加载 B + sleep 等待";全部声明成 Inject,让调和驱动。
3. **一切需撤销的后果必须经 ctx.Effect / Provide** MUST(论文:可逆 effect,
   逆是 LIFO):自己开的 goroutine、直接写的文件不在追踪范围内——要么纳管,
   要么明确接受它跨越组件生命周期存活。
4. **Active ⇔ 就绪** MUST:Apply 返回前资源必须真实可用(先就绪、后提交);
   参考模板 `cmd/httpd`。
5. **失败 = raise 语义** MUST:Apply/步骤返回 error → Runtime 撤销已装部分 →
   Failed,**不会**向父传播、**不会**自动重试。不要在组件里自建重试风暴;
   重试是"修正声明后重新启用"。
6. **依赖消失 = 撤回到 Pending** MUST:这不是失败,不要写"依赖丢了就 panic"
   的代码;依赖恢复后 Runtime 自动重新激活。
7. **单一生命周期权威** MUST:应用与插件都不得直接改 fiber 状态/绕过 Public API。
   谁该活、谁该死,由**声明 + 调和**决定(§4 开关)。
8. **entry 存活,fiber 是单次启停身份** MUST:插件开关(`Enabled`)翻动是
   Load/Dispose;disabled 的插件仍是被声明校验过的 entry。
9. **持久化三分** MUST:声明(开关/配置)→ 声明存储(TOML 等,持久);
   业务成果 → 组件层 journal + 幂等重放(模板 `cmd/backfill`);
   **runtime 派生态(fiber 表)永不持久化**——重启 = 全新调和。
10. **趁依赖还在时落盘** SHOULD:Cleanup 执行期间声明的依赖仍可 `Require`;
    检查点/ flush 写在 Cleanup 里,而不是依赖消失之后。
11. **一个 key 一个提供者**(命名空间内互斥)MUST:需要多实现共存时用
    `extensions/broker`,不要发明自己的注册表。
12. **合作式假设** MUST:Apply/Cleanup 有限步、可等待(`ctx.Done()`)、
    依赖图无环(装载边界会被拒绝并给环链诊断)。这是论文声明的宿主假设,
    违反它 theorem 不再覆盖你的代码。

---

## 2. 项目结构(应用本体 + 插件,两部分)

MUST 遵循以下布局与依赖方向;依赖方向用"允许的 import"定义:

```
myapp/
├── go.mod
├── manifest.toml                  # 声明存储:插件开关 + 配置(持久化的唯一对象之一)
├── cmd/myapp/main.go              # 组合根:runtime + 控制器 + 注册插件工厂
├── internal/
│   ├── contracts/                 # ★ 契约层:应用与插件之间唯一共享的依赖
│   │   ├── keys.go                #   typed Key[T] / EventKey[T] / MetaKey 定义
│   │   └── api.go                 #   能力背后的 Go 接口(如 Codec)
│   └── app/                       # 应用本体(第一方组件,宿主服务)
│       ├── server.go              #   每个文件/小组 = 一个 Component
│       └── store.go
└── plugins/                       # 插件(第二部分:可独立演进的组件包)
    ├── alpha/alpha.go             #   一个插件 = 一个包,导出组件构造器
    └── beta/beta.go
```

**依赖方向规则**:

| 方向 | 允许 | 说明 |
|---|---|---|
| `plugins/*` → `internal/contracts` | ✅ | 插件只认契约(key + 接口),不认应用 |
| `internal/app/*` → `internal/contracts` | ✅ | 应用本体同样只通过能力说话 |
| `plugins/*` → `internal/app/*`、plugin → plugin | ❌ 禁止 | 跨组件通信只有两条路:能力(Require)与事件(On/Emit) |
| `cmd/myapp` → 全部 | ✅ | 组合根:import 所有插件包并注册工厂 |

**为什么这样切**:契约层使插件对应用是**依赖倒置**的;插件之间永远不知道彼此,
一切协作经能力解析(空间可组合);应用增删一个插件 = main 里一行注册 +
manifest 一行开关,其余零改动。

---

## 3. 组件模板(签名精确,可直接抄)

### 3.1 插件/组件(extensions 无关的最小形态)

```go
package alpha

import (
    "context"

    "myapp/internal/contracts"
    "dynamic-runtime/runtime"
)

type Component struct{ /* 配置字段 */ }

func (c *Component) Name() string                 { return "alpha" }
func (c *Component) Inject() []runtime.Dependency {
    return []runtime.Dependency{runtime.Requires(contracts.CodecKey)}
}
func (c *Component) Provide() []runtime.Capability {
    return []runtime.Capability{contracts.AlphaKey.Capability()}
}
func (c *Component) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
    codec, err := runtime.Require(ctx, contracts.CodecKey)
    if err != nil {
        return nil, err
    }
    // …启动工作;每个需撤销的后果一个 ctx.Effect…
    if err := ctx.Effect(func() (func() error, error) {
        return func() error { /* 逆:LIFO 撤销 */ ; return nil }, nil
    }); err != nil {
        return nil, err
    }
    return nil, nil
}
```

### 3.2 组合根(main.go)

```go
func main() {
    rt, err := runtime.New()
    if err != nil { panic(err) }

    reg := config.NewFactoryRegistry()
    // 每个插件一个 Type;工厂即"插件装载器"
    must(reg.Register("alpha", config.FactoryFunc(func(cc config.ComponentConfig) (runtime.Component, error) {
        return alpha.New(cc.Config) // 从声明读配置
    })))
    must(reg.Register("beta", config.FactoryFunc(func(cc config.ComponentConfig) (runtime.Component, error) {
        return beta.New(cc.Config)
    })))

    ctrl := config.NewController(rt, reg)
    // 声明存储 → 调和;之后文件变更经 configwatch 实时调和(见 §5)
    if err := ctrl.Reconcile(ctx, loadManifest("manifest.toml")); err != nil {
        panic(err)
    }
    // …进程信号处理:ctrl.CloseContext → rt.Close…
}
```

### 3.3 声明存储(manifest.toml)

```toml
[[components]]
id = "alpha"
type = "alpha"
enabled = true                  # 插件开关:持久化的声明;翻动即启停
[components.config]
batch = 8

[[components]]
id = "beta"
type = "beta"
enabled = false                 # 关闭的插件:entry 仍被校验,但无运行对象
```

---

## 4. 生命周期语义速查(LLM 预测行为用)

| 你做的 / 发生的 | Runtime 行为 |
|---|---|
| 声明满足 | Pending → Loading → **Active**(Apply 成功) |
| 依赖消失 | 撤回(LIFO 撤销已装 effect)→ **Pending**,依赖恢复自动重激活 |
| Apply error / panic | 撤回 → **Failed**(outcome 阻止重入;不向父传播) |
| `Enabled: true→false` | **Dispose**(Gone);entry 保留、定义仍被校验 |
| `Enabled: false→true` | **Load**(新一次 enablement) |
| 定义变更(同 id) | replace 复合:旧卸新装(disabled 时仅换声明,不创建 fiber) |
| `Runtime.Close` | 全部 owned fiber 撤销 → Gone,优雅有序 |
| 长激活中目标翻转 | 迭代器 `yield` 返回 `ErrDiverted`:停止安装,已装部分照常撤销,非失败 |

---

## 5. 应用级设施的 MUST/SHOULD

- **配置文件监听** SHOULD:让"改 manifest 即生效"成立——
  `configwatch.New(Source{ID, Path, Format: configwatch.FormatTOML}, ctrl, watch.NewFileWatcher())`,
  先 `adapter.Sync(ctx)` 再 `go adapter.Run(ctx)`(参考 `cmd/collector`)。
- **事件** SHOULD:跨组件通知用 `runtime.On`(注册即可逆)+ `event.Emit/Serial/
  Parallel/Bail/Waterfall`(extensions/event);事件 key 也定义在 contracts。
- **多实现** MAY:`extensions/broker`;后端组件注册即入路由集,卸载自动移出,
  消费者依赖稳定的 broker 能力,替换零 reload。
- **测试** MUST:
  - 每个 Component 至少:Active 行为断言、依赖消失→Pending 恢复、Dispose 后
    资源释放(端口/文件句柄确实消失);
  - 并发相关 MUST 跑 `go test -race`;
  - 实现了 `IterComponent` 的 MUST 覆盖"激活中途依赖消失"的 divert 路径
    (`yield` 返回 `runtime.ErrDiverted`);
  - 检查点语义 MUST 测:Cleanup 内 `Require` 仍成功。
- **观测** SHOULD:排障用 `rt.Snapshot(ctx)`(线性化只读全量),不要在组件里
  print 状态机。

---

## 6. 禁止清单(反模式,出现即错)

1. ❌ 手写启动顺序 / `time.Sleep` 等依赖 / 全局 `sync.Once` 单例资源
   (→ 声明依赖,Runtime 调和);
2. ❌ 在 Apply/Cleanup 之外触碰 fiber 状态,或用反射/类型断言进 runtime 内部
   (→ 单一生命周期权威);
3. ❌ 组件保存另一个组件的 `*Fiber` 直接操作(→ 通过能力与事件协作);
4. ❌ 持久化 fiber 表 / 把 runtime snapshot 写盘当恢复源(→ 持久化声明与业务
   journal,重启 = 重放声明);
5. ❌ 未声明就 Require/Provide(运行时会拒绝,但不要依赖报错兜底);
6. ❌ plugin 包之间互相 import、插件 import 应用本体(→ 只经 contracts);
7. ❌ 在 Apply 里无限阻塞、忽略 `ctx.Done()`(→ 合作式假设);
8. ❌ 循环依赖声明(装载边界 `ErrDependencyCycle` 会拒绝,设计期就避免);
9. ❌ 捕获 `ErrDependencyMissing` 后自旋重试(→ 门控是 Runtime 的职责);
10. ❌ 组件 Cleanup 里再去 Load 新组件"补偿"(→ 撤销期只撤销)。

---

## 7. 完成前检查清单(任务收尾 MUST 逐项过)

- [ ] `go build ./... && go vet ./... && gofmt -l .` 干净;
- [ ] `go test -race -count=1 ./...` 全绿;
- [ ] 每个组件:Inject/Provide 与实际 Require/Provide 一致;
- [ ] 外部资源走 ctx.Effect 模式;Active 前有就绪确认;
- [ ] 开关/配置只存在于 manifest;runtime 状态零持久化;
- [ ] 插件间无 import(仅经 contracts 的能力与事件);
- [ ] 新能力 key / 事件 key 都定义在 `internal/contracts`;
- [ ] 长激活有 divert 路径测试;Cleanup 期间依赖可读有测试;
- [ ] 新增语义能在 `docs/ROADMAP.md` §1 对照表中锚定论文条目。

---

## 8. 参考与延伸

- 人类教程:`docs/guide/developing-applications.md`
- 可运行样例:`cmd/collector`(配置驱动替换)、`cmd/backfill`(journal 持久化)、
  `cmd/httpd`(外部资源绑定)、`cmd/wasmhmr`(WASM 插件热替换)
- 语义对照表(论文条目 ↔ 代码):`docs/ROADMAP.md` §1
- 设计裁决:`docs/adr/0001`(逐 key 隔离)、`0002`(元数据拦截)、
  `0003`(效果迭代器)、`0004`(事件派发边界)
- 平台能力边界(哪些是平台、哪些不是):`docs/review/2026-09-08-paper-first-review-v0.1.md` §10
