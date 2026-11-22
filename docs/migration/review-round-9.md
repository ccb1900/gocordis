# Review Round 9 — Product Execution Closure

审查基准：`aa8e609`。代码提交：`61f15ae1`。分支：`migration/dotnet-complete`。

本轮按用户“产品能否真正交付和使用”的审查执行。范围限定为可证明的产品缺口，
不重审 Core / Runtime 决策域，不查 CI，不用 sleep，不改 Go/main。

## 1. 已关闭

| 审查项 | 关闭证据 |
| --- | --- |
| 默认宿主未执行插件真实代码 | `product-execution-chain.md`；`ManagedPluginProductE2ETests` 完整源码→编译→打包→安装→Activate→Disable→Enable→Upgrade→Uninstall |
| 原 R8 已修但审查基于旧 HEAD 的两项 | UI Upgrade 旧版本判断与 StopAsync 独立清理/重试已在 `8c4e7eb3` 关闭，本轮未回退 |
| CLI create 生成项目绑定本机绝对路径、绕过正式宿主 | 生成薄壳 `PlatformRunner` + PackageReference + 本地 feed；复制到新目录后 `dotnet build` 红→绿 |
| Store Upgrade 绕过反向依赖 | `PluginDependencyPlanner` 共享给 PluginStore；红测 `StoreUpgradeRejectsWhenRemovedVersionIsOnlySatisfyingDependent` |
| 远程读取面暴露配置/路径/SSE | `Read` / `Observe` scope、SSE 预授权、配置脱敏、store 路径 basename；WebUiPolicyTests 扩展 |
| 同 id 多版本 React key 冲突 | `storeRowKey(id,version)` + vitest |

## 2. 未关闭但已明确边界

- CLI `app run` 保持 declaration-only；主产品入口是 `PlatformApplication` / `PlatformRunner`。
- WASM / Process 未接入 Platform Backend 分发；不宣称全后端产品化。
- 插件前端模块安装链路未接通；Web Console 现有模块管理不等于插件包 UI 交付。
- 多版本语义仍有三个未统一点：按版本卸载保留声明、Dependencies 按版本查询、
  List 展示解析版本与全部已安装版本。
- 签名是可验证 provenance，不是安装信任策略；信任策略属于产品安全决策。
- WinForms / Avalonia 是 RenderPlan profile，不是原生 UI Host，继续标延期。
- Go 基线漂移与 CI 证据不是本轮可单方面关闭的外部项。

## 3. 本地验证

```text
dotnet build Gocordis.sln → 0 warning / 0 error
dotnet test  Gocordis.sln → 418/418，连续 3 次全绿
npm test                 → 86 passed
npm run build            → success（仅既有 chunk-size 提示）
git diff --check         → clean
```

`verify-baseline.sh` 之前已报告 `main` 漂移到 `44c1f271`，本轮仍未修改 Go/main，
不声称 Go 基线验证通过。CI 未查询。
