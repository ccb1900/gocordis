# Extension Integration Audit and Closure

用户指出：扩展可能只是“有类有测试”，没有进入产品组合。源码审计确认这个判断基本正确：

- `src/Gocordis.Extensions/**` 在 Platform / WebUi / 三后端 / KoishiCase 中此前引用数为 0。
- 产品中的 HTTP、文件、SSE、ACL、诊断、包操作都是另一套自研实现。
- 但 Go `cmd/platform` 也没有组合 timer/scheduler/broker/observe 等全部扩展；不能为了覆盖率强行接线。

## 本轮真实集成

| 能力 | 之前状态 | 现在 | 证据 |
| --- | --- | --- | --- |
| DiagnosticsService | 仅测试；产品自拼诊断 | PlatformApplication 注册 `devtools/diagnostics` 只读查询，复用 DiagnosticsService | `PlatformApplicationTests.DiagnosticsQueryReportsVersionAndProviderFindings` |
| HostAcl / ServerRouteGuard | 仅测试；WebUi 自研 ListenGuard / AccessPolicy | WebUiServer 支持可选 routeGuard；Platform 通过 `--allowed-host` 透传 | `WebUiPolicyTests.HostAclDeniesNonAllowlistedHostAs404AndKeepsLoopback`、`PlatformApplicationTests.ProductHostHonorsAllowedHostPolicyWhenConfigured` |
| HttpService | 仅测试；远程包源用静态 HttpClient | HttpPluginPackageSource 可注入 HttpService，index/包下载走扩展 HTTP，interceptor/SOCKS 有落点 | `HttpPluginSourceInjectionTests.InjectedHttpServiceInterceptorsObservePackageRequests` |
| CLI | 自研 switch | 保持自研；Go cmd/platform 也只是直接注册命令，不强接 CliDispatcher | 理由见下 |

## 明确不集成的能力

| 能力 | 理由 |
| --- | --- |
| ServerHost | 与 WebUiServer 重复；替换等于重写宿主 |
| TimerService / Scheduler | 产品无周期作业；SSE 的 PeriodicTimer 足够 |
| EventDispatcher | Core `OnAsync` 是唯一事件注册权威；多模式派发无产品消费者 |
| Broker / StableRegistry | 会形成第二注册表/第二生命周期权威 |
| MarketService / PackageManager | `plugin-control-plane` Fiber 提供的 `PluginControlService` 已是包操作唯一权威，接入会形成两套决策层 |
| GenericHmr / SocksRegistry / EnvService | 产品无对应 HMR/代理/dotenv 场景；通过 HttpService 已给 SOCKS/interceptor 留落点 |
| ObservationLog SSE | 已记录为 Snapshot+HTTP polling 的接受差异；无序号重放需求，不为状态好看改协议 |
| FileSystemService 静态资源 | WebAssetPaths 已有安全边界；无挂载/热更新需求，不增加双套路径语义 |

## 结论

现在产品宿主真实复用了三类扩展：诊断、Host ACL、出向 HTTP。  
其余“仅测试”能力与 Go 产品宿主的使用面一致，或是明确接受差异，不伪造集成。

本地验证：

```text
dotnet build Gocordis.sln → 0 warning / 0 error
dotnet test  Gocordis.sln → 443/443（R11 当时快照；当前计数见 docs/current-status.md）
console npm test         → 88 passed
npm run build            → success
```
