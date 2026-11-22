# Multi-Host UI Contract（批次 5）

Branch: `migration/dotnet-complete`

## 1. 目标与边界

同一个插件包、同一个后端 Fiber、同一个 Activation，可以向不同 UI Host 贡献能力。
Host 只负责渲染或明确降级，不拥有插件生命周期，不建立第二套 Registry。

新增 `src/Gocordis.UI.Hosts`：

- `UiHostKind`：Web / WinForms / Avalonia
- `UiRenderTarget`：Kind / Id / Descriptor / Reason
- `UiHostProfile.Plan(UiContributionRegistry)`：只读映射当前贡献
- `WebUiHostProfile` / `WinFormsUiHostProfile` / `AvaloniaUiHostProfile`
- `UiHostProfiles.All`：不可变 profile 列表

## 2. 降级语义

| 贡献类型 | Web | WinForms | Avalonia |
| --- | --- | --- | --- |
| Page | web route renderer | winforms renderer descriptor | avalonia renderer descriptor |
| Panel | web position renderer | winforms renderer descriptor | avalonia renderer descriptor |
| ClientModule | ESM URL 动态加载 | 明确降级：web-only | 明确降级：web-only |

降级是**可观察契约**，不是静默丢失：`UiRenderPlan.Degraded` 给出 Kind、Id 与原因。
不支持的 Host 专属 UI 不会导致插件后端能力消失。

## 3. 生命周期验证

`MultiHostUiContractTests` 验证：

1. 三个 profile 均可用。
2. 一个插件同时贡献 typed key、Page、Panel、ClientModule；三个 profile 生成各自的 RenderPlan。
3. Web 渲染 client module；WinForms/Avalonia 明确降级 client module。
4. Profile 规划前后 Runtime Fiber 数不变，证明 Host 不是生命周期权威。
5. 插件停用后所有 profile 的贡献计划为空；重新启用后恢复。

## 4. 尚未完成

- WinForms/Avalonia profile 目前是契约与渲染计划层，不包含真实原生控件包与窗口宿主实现。
  这是有意的成熟度边界：不为了名称一致引入重型 UI 依赖或自研 UI 框架。
  Platform CLI help、未知 host 错误与非 Web 启动输出均已明确该边界，见 `review-round-7.md`。
- Koishi 完整插件市场与控制台生态不属于本批。
- Web React Console 的 Explorer/Inspector 数据与资产交付已验收；原生控件级产品化属于 Adapted 边界。
