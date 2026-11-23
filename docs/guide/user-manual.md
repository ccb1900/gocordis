# Gocordis 用户手册（.NET 10 产品）

> 读者：使用 Gocordis 开发、打包、部署插件化应用的工程师与运维人员。
> 本手册以当前仓库的 **.NET 10** 产品代码为准；Go 树是冻结基线，仅作参照。
> 语义权威是论文 *A Programming Paradigm for Spatiotemporal Composability*
> （`docs/论文.pdf`）。工程约束见根目录 `AGENTS.md`，当前状态见 `docs/current-status.md`。

## 1. 产品是什么

Gocordis 是一个基于时空可组合运行时的 .NET 插件应用平台：

- **时间维**：所有可撤销后果都通过 `EffectAsync` / `Provide` / `OnAsync` / `LoadChildAsync`
  登记，逆操作严格 LIFO 执行。
- **空间维**：插件用 `Inject()` / `Provide()` 声明强类型能力；依赖满足时自动激活，
  依赖撤回时退回 `Pending`，再次满足时自动重激活。没有手工启动顺序。

产品由三部分构成：**Runtime**（唯一生命周期权威）、**Application Manifest + Package Store**
（声明与包）、**三种执行后端**（Managed / Process / WASM）。Web Console 是目前唯一
可运行的 UI Host；WinForms/Avalonia 只有 render-plan profile，**不是**可运行的宿主选项。

## 2. 环境准备

需要 .NET 10 SDK。构建与验证：

```bash
dotnet build Gocordis.sln --nologo -m:1
dotnet test  Gocordis.sln --nologo --no-build -m:1
```

CLI 名为 `gocordis`（源码在 `tools/Gocordis.Cli`）。查看全部命令：

```bash
gocordis --help
```

## 3. 五分钟启动一个应用

应用声明文件是 `gocordis.app.json`，由应用根目录的 `gocordis create` 生成：

```bash
gocordis create ./my-app --id demo.app --version 1.0.0 --host-version 1.0.0
```

启动产品宿主：

```bash
dotnet run --project src/Gocordis.Platform -- \
  --manifest ./my-app/gocordis.app.json \
  --store ~/.gocordis/plugins \
  --address http://127.0.0.1:8170
```

打开 `http://127.0.0.1:8170/`。Console 支持插件安装、启停、配置、升级、卸载，
以及依赖图、运行状态和诊断查询。常用只读接口：

```bash
curl http://127.0.0.1:8170/api/meta
curl http://127.0.0.1:8170/api/plugins
curl http://127.0.0.1:8170/api/query/framework/dashboard
curl http://127.0.0.1:8170/api/ui/pages
```

`--host` 只接受 `web`。`winforms` / `avalonia` 会被拒绝——它们是未来原生宿主的
render-plan 设计，当前没有窗口、消息循环或 Console。

`--validate-only` 会完整执行声明解析、后端装载与 effect 安装，但不监听端口，适合 CI。

## 4. 插件开发闭环

### 4.1 创建与本地开发

SDK 是一个很薄的 MSBuild 包，只向插件项目暴露 `Gocordis.Core` 与 `Gocordis.Plugins`
的公开创作面（不含 Runtime、Registry、后端实现或 Store）。从空目录开始：

```bash
# 一次性：把 SDK 与 Core/Plugins 打包到本地 NuGet feed（详细命令见
# docs/guide/plugin-developer-workflow.md）
gocordis plugin create ./my-plugin --id demo.plugin --name Demo \
  --type demo.plugin --feed <local-feed>
cd my-plugin && dotnet build
gocordis plugin validate .          # 仅做本地声明预检，不安装
```

一个最小组件：

```csharp
using Gocordis.Core;

public static class DemoKeys
{
    public static readonly Key<string> Greeting = new("demo.plugin/greeting");
}

public sealed class DemoComponent : Component
{
    public override string Name => "demo-plugin";
    public override IReadOnlyList<ICapabilityKey> Provide() => [DemoKeys.Greeting];

    public override async ValueTask<Cleanup?> ApplyAsync(Context context)
    {
        await context.EffectAsync(
            () => { context.Provide(DemoKeys.Greeting, "hello"); return ValueTask.CompletedTask; },
            () => ValueTask.CompletedTask);
        return null;
    }
}
```

要点：

- `Inject()` / `Provide()` 必须与实际 `Require()` / `Provide()` 完全一致；未声明即抛异常。
- 需要撤销的外部资源放在 `EffectAsync` 的 install 回调里，逆回调撤销它；不要在
  `Cleanup` 里新建组件或写死启动顺序。
- `Context.CancellationToken` 必须被长步骤观察。
- 插件之间不要互相引用。编译期共享类型（如跨插件 Key）放在一个共享契约程序集，
  打包时用 `--shared-assembly <name>` 声明，宿主将其封入 Default ALC。

### 4.2 打包、安装、声明

```bash
gocordis plugin pack ./my-plugin/bin/Debug/net10.0 demo-plugin.gocordis-plugin \
  --id demo.plugin --version 1.0.0 --type demo.plugin \
  --backend managed --entry-assembly demo.plugin.dll --entry-type DemoComponent \
  --capability 'provide|demo.plugin/greeting|endpoint|1.0.0|application/json|'

gocordis plugin install demo-plugin.gocordis-plugin --store ~/.gocordis/plugins \
  --host-version 1.0.0
gocordis app add ./my-app/gocordis.app.json demo.plugin --range 1.0.0 \
  --store ~/.gocordis/plugins
gocordis app validate ./my-app/gocordis.app.json --store ~/.gocordis/plugins
gocordis app run ./my-app/gocordis.app.json --store ~/.gocordis/plugins --validate-only
```

`plugin pack` 把入口、能力声明、共享契约和 UI 资源写进真实 `.gocordis-plugin` 包；
`app add` 同时写入版本范围、锁定版本与包哈希。包依赖（另一插件的包必须先安装）、
程序集依赖（共享契约）和能力依赖（运行时 `Inject()/Provide()`）是三层不同的东西，
不能互相替代。安装了但没有写进应用清单的依赖只产生只读告警
（`PACKAGE_DEPENDENCY_NOT_DECLARED`），不会被静默当成已满足。

UI 属于包：页面、面板与前端模块写在包的 `gocordis-ui.json`，由宿主作为插件 fiber 的
effect 绑定；禁用/卸载插件时 UI 自动撤销。C# 端只贡献 API 查询/命令等数据能力。

### 4.3 三种执行后端

| 后端 | 入口 | 能力声明 | 说明 |
| --- | --- | --- | --- |
| managed | `.dll` + entry type | `--capability` / 构造函数注入 catalog | .NET 插件，独立 collectible ALC；**不是安全沙箱** |
| process | 可执行文件 | 包清单必须声明 `--capability` | JSON-RPC 子进程；无能力声明的旧 Echo Demo 路径已从产品工厂移除 |
| wasm | `.wat` / `.wasm` | 包清单 + `gocordis.capabilities` 自定义段双向一致 | Wasmtime 执行；无能力声明的旧 Value Demo 路径已从产品工厂移除 |

跨后端能力统一为 `CapabilityEnvelope` + `ICapabilityEndpoint`，由宿主唯一的
`CapabilityCatalog` 分配 Key；版本是兼容性元数据，不参与身份。当前只支持
`endpoint` 这一种跨后端 ValueKind，其他 Kind 的 Wire 编解码尚未实现。

### 4.4 声明预检（不执行插件）

```bash
gocordis plugin preflight demo-plugin.gocordis-plugin --host-version 1.0.0
```

它在临时 Store 与临时 Runtime 中检查声明、依赖和快照，不运行包内代码、不写用户目录，
输出 `securityBoundary: false`。这是**声明预检**，不是安全沙箱，也不是插件宿主；
`sandbox` 只是兼容别名。

## 5. 运行时观察与故障定位

- Fiber = 一次启用；Activation = 一次激活 episode。依赖缺失时状态为 `Pending`，不是 `Failed`。
- Apply 失败会让本 fiber 失败并撤销已安装的 effect，不向父组件传播、不自动重试；
  修正声明或升级版本后再装载。
- `runtime/snapshot` 查询包含 Fiber 状态、效果失败槽位（`EffectFailures`，含槽位序号与标签）、
  清理失败与诊断。历史记录有界（每个 fiber 32 条、全局 256 条）。
- `cortex/diagnostics`（`devtools/diagnostics`）提供版本与提供方发现结果。
- `Snapshot()` 是只读投影，不能作为恢复源。只有声明（开关/配置）和组件 journal 会持久化；
  Fiber 表与运行时派生状态从不落盘。

## 6. 安全与远程管理边界

签名是**来源证明**，不是安装信任链。本地开发默认策略 `development` 接受未签名包；
正式部署可以锁定发布者：

```bash
gocordis plugin keygen --out ./keys --name publisher
gocordis plugin sign demo-plugin.gocordis-plugin --key ./keys/publisher.private.pem \
  --output demo-plugin.signed.gocordis-plugin
gocordis plugin install demo-plugin.signed.gocordis-plugin --store <store> \
  --trust trusted-only --public-key ./keys/publisher.public.pem

gocordis app run ./my-app/gocordis.app.json --store <store> \
  --trust trusted-only --public-key ./keys/publisher.public.pem
```

产品宿主（`PlatformRunner` 与 `app run`）和 CLI 安装使用**同一个**信任解析函数
（`PluginTrustPolicy.FromPemFiles`），Web Console 上传安装也继承宿主的策略。
`trusted-only` 必须提供至少一个 pinned PEM 公钥；空公钥集会直接拒绝。

远程管理要区分监听与授权：`--allow-remote` 只放宽监听地址检查，默认远程**写操作仍被拒绝**。
需要远程写入时，嵌入宿主必须通过 `PlatformHostOptions.AccessPolicy` 提供明确的
`ConsoleAccessPolicy`（按 scope 授权），并放在认证代理之后。CLI 不会凭空构造身份策略。

## 7. 已知边界

- 无打包热更新 / 无打包调试：未实现。代码变更走 build → pack → upgrade，保持论文
  Revision 语义；`GenericHmr` / `WasmHotSwapper` 只是实验性代际原语，不是产品能力。
- WinForms / Avalonia 原生窗口宿主：未实现；目前只提供 render-plan profile。
- 第三方不可信插件安全：Managed 插件是受信任代码；Process 只提供地址空间隔离，不是
  OS 权限沙箱；当前没有远程市场、撤销列表或自动信任升级。
- 跨平台证据：Windows 的真实 Process/WASM E2E 需要 Windows CI；本地验证以 Linux 为主。
- `Gocordis.Extensions` 中多数能力（HMR、Timer、Scheduler、Observe、Watch、Socks 等）
  目前只有测试消费者，属于实验性/可选原语，不在标准 SDK 中，也不算产品承诺。

## 8. 可运行范例与延伸阅读

```bash
bash samples/SpatiotemporalShowcase/build-and-run.sh /tmp/gocordis-showcase --run
```

该脚本真实编译并安装 4 个 managed 插件包，演示共享契约、能力门控、严格 LIFO 撤销与
依赖重激活。更多细节：`samples/SpatiotemporalShowcase/README.md`、
`docs/guide/plugin-developer-workflow.md`、`docs/guide/csharp-developer-manual.md`、
`docs/guide/ui-host-contract.md`、`docs/current-status.md`。
