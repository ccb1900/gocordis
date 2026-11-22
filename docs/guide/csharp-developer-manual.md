# C# 开发者手册（Gocordis / .NET 10）

本手册面向基于 `migration/dotnet` 的 C# 应用开发者。它不是 Go API 的逐行翻译：
语义裁决顺序为 `docs/论文.pdf` → [`../migration/semantic-mapping.md`](../migration/semantic-mapping.md)
→ Go 实现经验。仓库根的 `AGENTS.md` 十二条铁律在此逐条对应到 C# API；
论文 §5.3（Koishi 案例）的完整复现见 [`../migration/koishi-case-study.md`](../migration/koishi-case-study.md)。

## 1. 三级权威与不可逆边界

- **论文**：`C01`–`C40` 是稳定条目编号，`Thm64/68/70/73/80` 的精确含义以论文为准。
- **契约层**：`Gocordis.Core` 的 `Key<T>` / `EventKey<T>` / `MetaKey<T,M>` / `VersionedKey<T>`、
  `Component`、`Context` 是应用与插件唯一共享面。插件之间不互相引用。
- **平台层**：`Gocordis.Plugins`（Manifest/Registry）、`Gocordis.Runtime.*`（Managed/WASM/Process）、
  `Gocordis.Extensions`（event/timer/fs/http/server/...）都只经 Core 能力协作。

只有经 `Context.EffectAsync`、`Provide`、`OnAsync`、`LoadChildAsync` 登记的后果才被 Runtime 逆撤销。
自己开的 `Task`、直接写的文件、已发出的 HTTP 响应不受恢复承诺（论文 §6.1 `idΓ`，对应 C35）。

## 2. 最小组件

```csharp
using Gocordis.Core;

public static class ChatContracts
{
    public static readonly Key<CommandRegistry> Commands = new("chat.commands");
    public static readonly EventKey<string> Message = new("chat.message");
}

public sealed class EchoFeature : Component
{
    private int _registered;

    public override string Name => "echo-feature";

    // 铁律 1：声明必须与下面实际 Require/Provide 完全一致。
    public override IReadOnlyList<ICapabilityKey> Inject() => [ChatContracts.Commands];

    public override async ValueTask<Cleanup?> ApplyAsync(Context context)
    {
        var commands = context.Require(ChatContracts.Commands); // 未声明会抛 UndeclaredCapabilityException
        _registered = commands.Register("echo", text => text);

        // 铁律 3：需撤销的后果走 Effect，逆按 LIFO 执行。
        await context.EffectAsync(
            () => ValueTask.CompletedTask,
            () =>
            {
                commands.Unregister(_registered);
                return ValueTask.CompletedTask;
            });
        return null;
    }
}
```

装载与组合根：

```csharp
await using var runtime = new Runtime();
var provider = new CommandHost();                 // Provide(ChatContracts.Commands)
var consumer = await runtime.LoadAsync(new EchoFeature());
// provider 到达前 consumer 保持 Pending；依赖满足后 Runtime 自动激活（不写启动顺序）。
```

## 3. 十二铁律 → C# 落地表

| # | 铁律（AGENTS.md §1） | 论文锚点 / C 条目 | C# 落地 | 反模式（禁止） | 可执行证据 |
| --- | --- | --- | --- | --- | --- |
| 1 | 声明权威 | Def 19–22 / C07,C08 | `Component.Inject()/Provide()`；`Context.Require/Provide` 逐 key 校验，越声明即抛 | 在 Apply 里 Require 未声明 key，或靠运行时报错兜底 | `CoreSemanticsTests.UndeclaredRequireFailsTheFiber`、`UndeclaredProvideFailsTheFiber` |
| 2 | 不做启动顺序编排 | C19–C22 | `Runtime` 内部 Channel 决策域 + target/quiet 调和；`LoadAsync` 只提交声明 | 手写"先加载 A 再 sleep 等 B" | `TheoremMatrixTests.Thm73_Matrix`、`PaperConformanceTests.C22_DependencyLossIsWithdrawalNotFailure` |
| 3 | 一切需撤销的后果经 Effect / Provide | C02–C06,C26 | `Context.EffectAsync(install,inverse)`；`Provide`/`OnAsync`/`LoadChildAsync` 自带逆 | 在 Apply 里裸起后台 Task、直接改外部文件且不登记逆 | `PaperConformanceTests.C06_StrictLifoRecoveryLeavesNoResidue`、`CoreSemanticsTests.EffectsUnwindInStrictLifoOrder` |
| 4 | Active ⇔ 就绪 | C20,C21 | `LoadAsync` 在本次激活落地后返回；失败/未满足时不是 Active | Apply 返回后才开始监听端口/打开文件 | `TheoremMatrixTests.Thm70_Matrix["consumer-declared-first"]` |
| 5 | 失败 = raise，不向父传播 | C33 | Apply 异常 → 该 fiber `Failure` + `Failed`；不自动重试，重试=重新声明/revision | 组件内自建重试风暴；Cleanup 里再 Load 新组件"补偿" | `CoreSemanticsTests.ApplyFailureUnwindsInstalledEffectsAndNeverRetries` |
| 6 | 依赖消失 = 撤回到 Pending | C20–C22 | Runtime 撤销已装 effect → Pending，依赖恢复自动重激活 | "依赖丢了就 panic / Failed" | `PaperConformanceTests.C22_DependencyLossIsWithdrawalNotFailure`；`TheoremMatrixTests.Thm64_Matrix["dependency-withdrawal-recovery"]` |
| 7 | 单一生命周期权威 | C19,C21 | Fiber 状态 setter 为 internal，仅决策循环可写；插件/应用只提交声明 | 反射改 fiber 状态、跨组件持有 `Fiber` 直接操作 | `BoundaryTests.CoreDeclaresNoProjectReferences`；`PlatformConvergenceTests.PC12_PluginRegistryNeverCreatesASecondLifecycle` |
| 8 | entry 存活，fiber 是单次启停身份 | C40 | `PluginManifest`（id/type/enabled）+ `PluginRegistry`；enabled 翻动=Load/Dispose | 把 disabled 插件从声明中删除、把 fiber 表持久化 | `PluginSystemTests.EnabledSwitchLoadsAndDisposesThroughCoreOnly`、`EnabledDeclarationsLoadAndDisabledOnesStayDeclarations` |
| 9 | 持久化三分 | C40、§4.4 Config | 声明进 TOML manifest；业务成果走组件 journal；`Snapshot()` 只读、永不写盘 | 把 runtime snapshot 当恢复源 | `../migration/migration-coverage.md` §0/§3；`AuditRegressionTests.SnapshotIsSafeUnderConcurrentLoadDispose` |
| 10 | 趁依赖还在时落盘 | C22 | Cleanup 执行期 `Context.Require` 仍可读声明依赖（直到 Unwound） | 依赖消失后才 flush 检查点 | `CoreSemanticsTests.CleanupCanStillRequireItsDependency` |
| 11 | 一个 key 一个提供者 | C17 | 同 realm 同 key 重复 `Provide` 抛 `DuplicateProviderException`；多实现用 `Broker<T>` | 自建第二个注册表/路由层 | `CoreSemanticsTests.DuplicateProviderIsRejectedAtLoad`；`ExtensionTests.BrokerAndRegistryManageMembers` |
| 12 | 合作式假设（工程约束） | C29（论文**无**合作式前提） | Apply/Cleanup 有限步、观察 `Context.CancellationToken`；环在装载边界拒绝 | 无限阻塞、忽略取消、声明环 | `CoreSemanticsTests.DependencyCycleIsRejectedAtMount`；`DeterministicDriver.SameSeedReplaysTheSameInterleaving`；`../migration/semantic-mapping.md` §4.1 |

铁律 12 的表述要注意：`semantic-mapping.md` §4.1 明确论文 Thm73 只假设 ≺ 无环、迭代器有限、
名字有限、每步为生命周期规则；"合作式"是工程/测试约束，不是论文定理前提。

## 4. 迭代器、Realm、拦截与事件

- **迭代器形态**：论文默认形态是 effect iterator（C06）。C# 的 `IIterativeComponent.IterateAsync`
  产出 `EffectRegistration`；Runtime 每步前检查目标，目标改变即 L-Divert，已装逆序撤销。
  单次 `ApplyAsync` 是单步迭代器的语法糖（`semantic-mapping.md` §4.3）。
  - 证据：`AuditRegressionTests.SingleStepDivertStillRunsTheApplyCleanup`；
    `TheoremMatrixTests.Thm68_Matrix["three-stage-chain"]`。
- **Realm 隔离**：`context.Isolate(key, realm)` 是逐 key 重赋值（C10/C34），不是层级继承。
  证据：`CoreSemanticsTests.IsolateReassignsAKeyAndIsReversible`、
  `AuditRegressionTests.IsolateCannotHijackAKeyOwnedByAnotherRealm`。
- **元数据拦截**：`MetaKey<T,M>` 带 monoid 的 `Identity`/`Combine`；`DeclareMeta` + `OverrideMeta`
  右偏组合，且每 Context 派生快照，不跨 root 泄漏（C11/C37）。
  证据：`CoreSemanticsTests.MetaInterceptionIsRightBiasedAndDoesNotReload`。
- **事件**：`EventKey<T>` + `Context.OnAsync/EmitAsync`；派发五模式在 `Gocordis.Extensions.EventDispatcher`
  （serial/bail/parallel/waterfall/BailValue），属平台能力（R4-C），不冒充论文语义。
  证据：`ExtensionTests.EventModesCoverSerialBailParallelAndWaterfall`、
  `EventDispatchGapTests.BailValueStopsAtFirstPresentResult`。
- **版本路由**：`VersionedKey<T>` 用 NuGet 版本范围选最高兼容 provider；不实现论文的 `K×P` 名义命名空间，
  这是显式接受差异（C38）。证据：`VersioningTests.PeerRangeSelectsTheHighestCompatibleProvider`。

## 5. 声明存储与插件生命周期

```csharp
await using var runtime = new Runtime();
await using var registry = new PluginRegistry(runtime);
registry.RegisterFactory("adapter", config => new AdapterPlugin(config["tag"]?.ToString()));

// 声明是唯一持久化对象；Requires/Provides 仅描述意图，真实边来自组件的 Inject/Provide。
var declarations = PluginManifest.ParseToml(File.ReadAllText("manifest.toml"));
await registry.ApplyAsync(declarations);   // 重复 Apply 幂等；enabled=false 只保留 entry，不建 fiber

await registry.ApplyAsync(declarations.Select(d => d with { Enabled = false }));
// 等价于 Dispose（Gone）；再次 enabled=true 是一次新的 Load。
```

运行时禁止把 `Snapshot()` 写盘；配置热更新用 `ConfigWatcher`（先 `SyncAsync()` 再 `Start()`）。
证据：`PluginSystemTests.EnabledDeclarationsLoadAndDisabledOnesStayDeclarations`、
`Phase6Tests.ConfigWatcherCoalescesFileChangesIntoOneRevision`。

## 6. 论文 §5.3：Koishi 组合案例

论文 §5.3 讨论"宿主只提供领域词汇、功能由插件组合"的真实应用形态。C# 复现为
`samples/KoishiCase`：服务端 `KoishiHost` 只 `Provide` 命令与消息词汇，db/adapter/feature
三个插件通过 typed key 组合；关闭 feature 只撤销它自己的 effect，不动 provider；
切换 adapter 只让依赖者重激活；浏览器 `BrowserConsoleApp` 是**第二个独立 Runtime**，
不共享 fiber 表。断言见 `KoishiCaseTests`（7 项）与 [`../migration/koishi-case-study.md`](../migration/koishi-case-study.md)。

要点：双应用不是"第二个生命周期权威"——每个应用各有一个 `Runtime`，它们之间只经能力/事件协作，
符合铁律 7/11。

## 7. 测试与验收

本手册关联的批次 C 覆盖账本：

- 论文条目：`../migration/paper-coverage-matrix.md`（C01–C40；38 已覆盖 / 0 待补 / 2 接受差异），
  由 `PaperItemMatrixTests` 逐行校验。
- 定理：`../migration/theorem-matrix.md`（Thm64/68/70/73/80 各 3 场景）。
- JS / 接受差异：`../migration/js-cordis-csharp-differences.md`。
- 并发用例必须跑 `go test -race`（Go 基线）与主线程统一 `dotnet test`；本子任务未运行构建/测试，
  最终计数见 `../migration/execution-log.md`「批次 B/C 最终验证」：287/287。

```bash
dotnet build Gocordis.sln --nologo -m:1
dotnet test  Gocordis.sln --nologo -m:1
```

## 8. 收尾检查清单（提交前）

- [ ] `Inject/Provide` 与实际 `Require/Provide` 完全一致；
- [ ] 外部资源（端口、文件、订阅、子进程）都经 `EffectAsync` / `Provide` 登记逆；
- [ ] 没有 sleep 等依赖、没有跨组件持有 `Fiber`、没有第二套生命周期；
- [ ] Cleanup 内不 Load 新组件、不忽略 `CancellationToken`；
- [ ] 新 key / 事件 key 定义在契约层，插件包之间无互相引用；
- [ ] 接受差异写进 `js-cordis-csharp-differences.md`，未覆盖项写进 `paper-coverage-matrix.md` 的 `待补`；
- [ ] 主线程 `dotnet build` / `dotnet test` 与 Go `go test -race` 结果回填 `execution-log.md`。
