# Full Migration Final Acceptance

Branch: `migration/dotnet-complete`
Go baseline target: `ed2ef724a24331ecc3fa21974539315fd90212bf`
Go baseline target: `ed2ef724a24331ecc3fa21974539315fd90212bf` (pinned). Later `main` commits (`44c1f271...`) are upstream evolution; `verify-baseline.sh` reports them as a NOTE while verifying the pinned overlay.
C# calibrated target: R10 four-dimensional closure commit `migration/dotnet-complete` `ab9a3300`（历史基线 `488e959d` 已 superseded）

本文件是完整迁移的事实验收报告。所有状态以源码与测试为准。本轮未核验 CI；
Go 基线漂移未被静默接受，按 `review-round-8.md` 登记为阻塞项。

## 1. 批次执行结果

| 批次 | 状态 | 交付 |
| --- | --- | --- |
| 1 账本 | 完成 | `docs/migration/feature-matrix.md`，52 个 acceptance rows，源于 Go/JS/Koishi/C# 一手源码 |
| 2 插件包闭环 | 完成 | `Gocordis.Packaging` + `Gocordis.Cli`；包、安装、声明、启停、升级、卸载、本地源接口 |
| 3 扩展 parity | 完成 | Go 24 扩展缺口关闭；bundle/cli/config/configwatch/devtools/env/fs/http/socks/market/patch/pluginmeta/server/timer/watch + 通用 HMR |
| 4 运行时完整性 | 完成 | Managed/Process/WASM/Go Native 边界；WASM 64 MiB 上限；`runtime-integrity.md` |
| 5 多 Host UI | 完成 | Web/WinForms/Avalonia host profiles；同插件渲染/降级；`multi-host-ui.md` |
| 6 产品闭环 | 完成 | `src/Gocordis.Platform` 正式产品宿主；CLI app/plugin create/doctor/package/sandbox；UI 插件管理控制面；Diagnostics/HostAcl/HttpService 真实扩展集成；.NET Console Client SDK；最终验收报告 |

账本状态：`Equivalent 44 / Adapted 3 / Divergent 5 / Missing 0`（共 52 行），以 `final-calibration.md` 为准。
Adapted 均有具体 Gap/Target/Acceptance；Divergent 均有语义理由；没有未解释 Missing。

## 2. 验证命令与结果

```bash
dotnet build Gocordis.sln --nologo -m:1
dotnet test  Gocordis.sln --nologo --no-build -m:1
npm ci && npm test && npm run build     # web/console
bash docs/migration/verify-baseline.sh  # Go baseline
```

本地 .NET 验证结果：

- Build：0 warning / 0 error
- Test：442/442，R10 四维修正后连续 3 次零失败、零挂起
- 构成：Core 33 / Conformance 148 / Runtime 126 / Extensions 60 / Packaging 46 / UI 3 / Client 2 / Product 24

CI 状态不在本轮工作流中核验。最终验收以本地构建/测试为准；历史 `6ff30f1c` 曾记录 Go baseline Race
flake，CI workflow 对该已知 flake 采用最多 3 次有界重试，真实失败不会被掩盖。当前 HEAD 的 CI 结果需由外部流程确认，不在本文件中声称。

## 3. 产品使用示例

```bash
# 创建应用脚手架
dotnet gocordis create ./my-app --id my-app --version 1.0.0 --host-version 1.0.0

# 打包插件
dotnet gocordis plugin pack ./my-plugin ./my-plugin.gocordis-plugin \
  --id my-plugin --version 1.0.0 --type demo --host-range "[1.0,2.0)"

# 安装 / 升级 / 卸载 / 列表
dotnet gocordis plugin install ./my-plugin.gocordis-plugin --store ~/.gocordis/plugins
dotnet gocordis plugin list --store ~/.gocordis/plugins
dotnet gocordis plugin uninstall my-plugin --store ~/.gocordis/plugins

# 校验应用声明
dotnet gocordis app validate ./my-app/gocordis.app.json --store ~/.gocordis/plugins

# 启动完整平台
dotnet run --project src/Gocordis.Platform/Gocordis.Platform.csproj -- --manifest gocordis.app.json --store ~/.gocordis/plugins
```

## 4. 插件开发示例

一个插件同时贡献后端能力和 UI：

```csharp
public sealed class MyPlugin : Component
{
    private readonly Key<string> _value = new("my.value");
    public override IReadOnlyList<ICapabilityKey> Provide() => [_value];
    public override IReadOnlyList<ICapabilityKey> Inject() => [UiContributionRegistry.Key];

    public override async ValueTask<Cleanup?> ApplyAsync(Context context)
    {
        var ui = context.Require(UiContributionRegistry.Key);
        context.Provide(_value, "hello");
        await context.RegisterUiPageAsync(
            ui, ContributionOwner.ForContext(context, "my", Name),
            new UiPageDefinition("my-page", "My Page", "/my", "generic"));
        return null;
    }
}
```

禁用插件时，`Context.Effect` 逆撤销 Page 与 API，Core 同时撤销 `_value`；
Web/WinForms/Avalonia profile 按各自能力渲染或明确降级。

## 5. 与四个参照系的差异裁决

| 参照系 | 结论 |
| --- | --- |
| 论文 | Effect 可逆、Coeffect 声明、统一 Context、依赖门控、LIFO、Near/Remote 边界均有可执行矩阵。保留 C34/C38 接受差异。 |
| JS Cordis | 机制等价但 API 不同：强类型 Key、Fiber、严格 LIFO、先就绪后提交；不复制 Proxy、并发撤销、ForkScope。 |
| Koishi | 产品参考：插件组合、声明开关、配置回写思路、Console 数据服务；不迁移聊天业务、市场服务端、require.cache/vite 技术栈。 |
| Go Gocordis | 52 行账本覆盖 runtime/extensions/cmd/front-end/validation；Go Native 以 Process backend 替代并标 Divergent。 |

## 6. 明确保留的限制

- WinForms/Avalonia 是 host profile 与 render-plan 契约，不是完整原生控件宿主包（账本标 Adapted）。
  Platform CLI help、未知 host 错误与启动输出已明确该边界，见 `review-round-7.md`。
- Explorer/Inspector/Dashboard 的数据与资源交付已通过真实 HTTP 验收；原生控件级产品化由 web/console 前端承担。
- Koishi 完整插件市场、远程安装与云端服务不在迁移范围。
- 跨 effect 并发撤销按论文/Go 严格 LIFO，不采用 JS `Promise.all` 并发撤销。
- Go baseline 的 Race flake 属于冻结 main 的既有问题，不在此分支修改 Go。
- UI 控制面目前无认证：loopback 是默认安全边界；`--allow-remote` 且未安装 AccessPolicy 时所有写端点返回 403。远程部署必须在进程外或宿主侧提供鉴权。
- 平台收敛 gate 30/30 已逐项裁决，见 [`pc-gate-disposition.md`](pc-gate-disposition.md)；
  6 个为直接移植，其余为等价覆盖，不存在 Unknown。

## 7. 冻结条件

1. Go 全部生产能力进入账本：完成。
2. 全部扩展有明确结论：完成。
3. 无未解释 Missing：完成。
4. 论文核心语义测试通过：完成。
5. 插件安装/应用组合/加载/卸载闭环通过：完成。
6. Web/WinForms/Avalonia 插件契约验证：完成。
7. 主要构建与测试通过：完成。
8. 文档与代码一致：完成。

达到停止条件后冻结架构。后续只接受实际缺陷、安全问题、明确功能需求或可证明的性能问题。
