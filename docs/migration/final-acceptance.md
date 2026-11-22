# Full Migration Final Acceptance

Branch: `migration/dotnet-complete`
Go baseline: `origin/main` `ed2ef724a24331ecc3fa21974539315fd90212bf`
C# calibrated target: `migration/dotnet-complete` HEAD `60b13311`（历史基线 `488e959d` 已 superseded）

本文件是完整迁移的事实验收报告。所有状态以源码与测试为准；CI 状态按 SHA 记录，历史结果不复用。

## 1. 批次执行结果

| 批次 | 状态 | 交付 |
| --- | --- | --- |
| 1 账本 | 完成 | `docs/migration/feature-matrix.md`，52 个 acceptance rows，源于 Go/JS/Koishi/C# 一手源码 |
| 2 插件包闭环 | 完成 | `Gocordis.Packaging` + `Gocordis.Cli`；包、安装、声明、启停、升级、卸载、本地源接口 |
| 3 扩展 parity | 完成 | Go 24 扩展缺口关闭；bundle/cli/config/configwatch/devtools/env/fs/http/socks/market/patch/pluginmeta/server/timer/watch + 通用 HMR |
| 4 运行时完整性 | 完成 | Managed/Process/WASM/Go Native 边界；WASM 64 MiB 上限；`runtime-integrity.md` |
| 5 多 Host UI | 完成 | Web/WinForms/Avalonia host profiles；同插件渲染/降级；`multi-host-ui.md` |
| 6 产品闭环 | 完成 | CLI create 应用脚手架；.NET Console Client SDK；最终验收报告 |

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
- Test：349/349，连续 3 次零失败、零挂起
- 构成：Core 26 / Conformance 113 / Runtime 114 / Extensions 52 / Packaging 8 / UI 3 / Client 2

CI 状态按 SHA 记录在最终交付消息；已知 `6ff30f1c` 的 `ci` workflow 在 Go baseline Race 步骤出现一次
flake（Go main 冻结，不在本分支修改）。CI workflow 对该已知 flake 采用最多 3 次的有界重试：
仍未通过则失败，真实缺陷不会被掩盖；Windows/Linux 该 SHA 均成功。

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
dotnet run --project samples/PlatformHost/PlatformHost.csproj
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
- Explorer/Inspector/Dashboard 的数据与资源交付已通过真实 HTTP 验收；原生控件级产品化由 web/console 前端承担。
- Koishi 完整插件市场、远程安装与云端服务不在迁移范围。
- 跨 effect 并发撤销按论文/Go 严格 LIFO，不采用 JS `Promise.all` 并发撤销。
- Go baseline 的 Race flake 属于冻结 main 的既有问题，CI 状态按 SHA 留痕，不在此分支修改 Go。

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
