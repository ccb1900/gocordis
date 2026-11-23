# C# 开发者手册（Gocordis / .NET 10）

本手册面向当前 `dotnet` 分支的 .NET 10 应用开发者（当前状态见 `../current-status.md`）。它不是 Go API 的逐行翻译：
语义裁决顺序为 `docs/论文.pdf` → [`../migration/semantic-mapping.md`](../migration/semantic-mapping.md)
→ Go 实现经验。仓库根的 仓库根 `AGENTS.md` 是强制工程宪章，十二条铁律在此逐条对应到 C# API；
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

### 2.1 打包插件的跨插件依赖：共享契约程序集

同进程内插件共享 `static readonly Key<T>` 即可（Key 是引用标识）。但两个独立
`.gocordis-plugin` 包各带一份 `Contracts.dll` 时，若各自加载进独立 ALC，Key 会变成两个对象，
`Inject` 永远等不到 `Provide` —— **打包形态下的跨插件依赖会静默失效**。

正确做法是把契约程序集声明为共享：打包时 `plugin pack ... --backend managed
--entry-assembly <dll> --entry-type <type> --shared-assembly <ContractsName>`（对应
`PluginPackageManifest.SharedAssemblies`）。宿主在声明解析阶段对**已安装（校验过摘要）的包**调用
宿主在装配阶段调用 `SharedAssemblyRegistry.Seal(candidates)`，收集所有已安装包携带的候选副本，
校验完整身份一致后把唯一规范副本提升进 default ALC 一次，`PluginLoadContext` 对共享名返回 `null`，回落到 default 的唯一实例。于是不同包的
`Provide(K.X)` 与 `Inject(K.X)` 解析到同一 Key，空间维组合在真实部署形态下成立。

**信任边界与生命周期（MUST 遵守）**：

- 共享程序集**只能通过宿主 `Seal()` 封存**（`Allow` 已是私有实现细节），插件不能在加载时把自己提升进 Default ALC。
  `SharedAssemblyRegistry` 校验**完整身份**：简单名、版本、culture、公钥令牌、模块 MVID 与 payload
  SHA-256；同名同版本但字节不同会被 `SharedAssemblyConflictException` 拒绝，不会静默换身份。
- **Managed 插件是受信任代码，不是安全沙箱**；ALC 可收集但不是隔离边界。需要隔离不可信代码用
  Process / WASM 后端。本机制防的是"意外的身份冲突"，不是安全边界。
- 共享程序集进入 `AssemblyLoadContext.Default` 后是**进程级生命周期**，不随插件 ALC 卸载。
  升级共享契约（新字节/MVID）需要**受控重启宿主**，没有进程内热替换；浏览器端前端模块的 `?v=`
  修订/HMR 是另一套机制，不适用于 CLR 契约程序集。

回归证据：`SharedAssemblyRegistryTests`（批准门、完整身份冲突、名称不符、进程级不换身份）、
`SharedContractPackagingTests.SharedContractAcrossTwoPackagesGatesTheConsumer`。

### 2.1.1 三层依赖（不可混为一谈）

| 层 | 表达 | 机制 | 失败形态 |
| --- | --- | --- | --- |
| 包依赖 | B 需要安装 A 的某版本 | `PluginDependency` + 版本范围，安装/升级/卸载反向检查 | 安装被拒 / 升级被阻 |
| 契约依赖 | B 编译期引用 A 的接口/类型/DTO/Key | 共享契约程序集（`--shared-assembly`），宿主统一封存进 Default ALC | 类型身份不一致 / 加载冲突 |
| 能力依赖 | B 需要 A 的运行时能力才允许激活 | `Inject()/Provide()` + 强 `Key<T>`，Core 调和 | B 保持 `Pending`，依赖恢复自动重激活 |

程序集能加载 ≠ 能力已提供；包依赖满足 ≠ 能力实现已 Active。B 可以只引用契约、声明所需能力，由 Runtime 决定实际提供者——这才是可替换的低耦合组合。

**装配由声明决定**：包依赖只约束 Store，不会自动把 A 纳入应用 manifest。应用声明是唯一装配集合；是否 Active 只由 `Inject()/Provide()` 的强类型 Key 决定。若声明的插件其包依赖 id 已安装但未出现在 manifest 中，运行时给出只读告警 `PACKAGE_DEPENDENCY_NOT_DECLARED`，不会假装"已满足"。升级/卸载的反向依赖判断统一走 `PluginDependencyPlanner`，不再各路径内联重复实现。

ALC 归属是产品规则：Core / Plugins / 共享契约在 **Default ALC**（单一类型身份、宿主所有）；每个插件实现程序集与私有依赖在**各自的独立可回收 ALC**（独立卸载、允许不同私有版本）。不把所有插件塞进 Default ALC，也不假设插件之间完全不能共享类型。共享契约由宿主在装配阶段校验所有副本的完整身份后封存一份；插件加载只能复用，不能抢先决定版本；契约升级需重启宿主。

### 2.2 打包插件的 UI 与依赖声明

插件的 UI **属于插件包**，不写进控制面或框架代码：页面/面板/客户端模块声明在包根的
`gocordis-ui.json`（资源定义放在 `ui/`），控制面用 `UiPackageBinder` 把它作为插件 fiber 的
effect 注册——安装插件即安装 UI，禁用/卸载逆撤销。C# 组件只贡献运行时数据能力
（API 查询/命令）。打包时用 `--provides <name>` / `--dependency <id>@<range>` 声明能力图，
控制面的"依赖关系"视图据此显示包依赖、被依赖、能力依赖与提供能力。

`PlatformApplication` 不再预先注册原始组件工厂——否则会绕过 `UiPackageBinder` 导致包 UI 静默丢失；
工厂注册统一由控制面完成。回归证据：`PackageUiProductHostTests.ProductHostBindsPackageUiThroughThePluginFiber`。

可运行范例：`samples/SpatiotemporalShowcase/`（含 `build-and-run.sh` 与打包成真实包的四个插件）。
回归证据：`SharedContractPackagingTests.SharedContractAcrossTwoPackagesGatesTheConsumer`。

### 2.3 跨后端能力契约（Managed / WASM / Process）

共享契约程序集解决的是 **Managed ↔ Managed** 的编译期类型身份；跨执行后端没有 CLR 类型身份可共享，因此不能靠 `SharedAssemblyRegistry` 打通。统一做法是：宿主持有唯一的 `CapabilityCatalog`，为每一个 `CapabilityId = (Namespace, Name)` 分配**一个权威 `ICapabilityKey` 实例**，三个后端在 Core 装载组件之前都把声明翻译成这些 Key；之后匹配、绑定、撤回、重激活全部仍由 Core 完成。

- **Managed**：需要跨后端能力的插件在构造函数接收 `ICapabilityCatalog`，用 `catalog.KeyFor<ICapabilityEndpoint>(id, kind)` 取 Key 并在 `Inject()/Provide()` 中声明。它只与宿主共享 `Gocordis.Core`，不需要任何共享契约 DLL。未改造的静态 `Key<T>` 插件完全不受影响。
- **WASM**：在 `gocordis.capabilities` 自定义段写 JSON 声明；提供方导出 `capability_invoke(ptr,len)→(ptr,len)`，消费方导入 `gocordis.capability_buffer/require/invoke`。宿主桥接双向调用，句柄只代表能力，不代表 Fiber。
- **Process**：在包清单用 `--capability` 声明；v1 握手不变，只额外放行 `capability/require` 与 `capability/invoke`，未声明的插件→主机调用仍是 `-32601`。
- **版本**：`CapabilityContract` 携带 SemVer 版本与媒体类型/Schema；版本是兼容性元数据，不进身份；范围匹配复用 Core 已有的 NuGet.Versioning（C38）。破坏性变更请换 `CapabilityId`。
- **边界**：跨后端值统一为 `CapabilityEnvelope`（wire 字节 + 媒体类型），插件 ALC 内部再由自有类型做序列化；CLR 类型身份从不跨后端边界。端点注册/释放是 Core effect，严格 LIFO；**调用本身的外部副作用不受可逆性保证**（C35）。

打包示例：

```bash
gocordis plugin pack ./Provider out.gocordis-plugin \
  --backend managed --entry-assembly Provider.dll --entry-type Provider.Component \
  --capability 'provide|org.example.calc/add|endpoint|1.2.0|application/json|calc'
```

`PluginPackageWriter` 对三种执行后端一视同仁：managed 要求 `.dll` + entry type，wasm 要求 `.wat`/`.wasm`，process 要求安全相对路径的可执行文件；三者都要求声明的入口真实存在于包内，能力声明纳入校验和。安装后的 manifest 由 `PlatformApplication.CreateComponent` 分派，不需要写 `installed.json` 打补丁。

回归证据：`CrossBackendCapabilityTests`（独立 ALC 的 Managed 插件不靠共享 DLL 拿到同一 Key、Managed↔WASM↔Process 双向真实调用、Trap 失败与撤回 Pending 的区分）、`CapabilityDeclarationTests`/`BackendPackageValidationTests`（声明随包往返、后端入口校验）、`BackendDispatchProductTests.Declared*CapabilityPackage*`（真实包 pack→install→load→invoke→stop）。

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
- **事件**：`EventKey<T>` + `Context.OnAsync/EmitAsync` 是唯一注册与派发权威；JS 五模式扩展派发无产品消费者，不保留第二套注册字典
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

- 论文条目：`../migration/paper-coverage-matrix.md`（C01–C40；当前 39 已覆盖 / 0 待补 / 1 接受差异 C38），
  由 `PaperItemMatrixTests` 逐行校验。
- 定理：`../migration/theorem-matrix.md`（Thm64/68/70/73/80 各 3 场景）。
- JS / 接受差异：`../migration/js-cordis-csharp-differences.md`。
- 验证以实际命令结果为准（见 `../current-status.md`）；Go 基线用 `docs/migration/verify-baseline.sh` 核验漂移，历史 396/396 等计数只属归档，不代表当前 HEAD。

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
- [ ] `dotnet build` / `dotnet test` 与前端 `npm test && npm run build` 全绿；当前状态写入 `docs/current-status.md`（不是历史 execution-log）。
