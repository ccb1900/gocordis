# gocordis — Spatiotemporal Composable Runtime (.NET 10)

以论文 *[A Programming Paradigm for Spatiotemporal Composability](docs/论文.pdf)* 为语义来源的插件运行时与应用平台：时间维 = 可逆 effect（LIFO 逆），空间维 = 声明式、反应式解析的 capability。当前仓库是 **.NET 10 产品代码**；Go 树是冻结基线，仅作参照与核验。

## 产品入口

```bash
dotnet run --project src/Gocordis.Platform -- \
  --manifest gocordis.app.json --store ~/.gocordis/plugins --address http://127.0.0.1:8170
```

Console 通过 UI 安装、配置、启停、升级、卸载插件，并查看依赖与运行状态；写操作默认仅 loopback 可用，远程需显式鉴权策略。`src/Gocordis.Platform` 才是产品宿主；`samples/PlatformHost` 只是示例。

## 从此处开始

- **当前状态 / 分支 / 验证计数 / 已知边界**：[`docs/current-status.md`](docs/current-status.md)
- **Agent 工程宪章（强制）**：[`AGENTS.md`](AGENTS.md)
- 用户手册：[`docs/guide/user-manual.md`](docs/guide/user-manual.md) · C# 开发手册：[`docs/guide/csharp-developer-manual.md`](docs/guide/csharp-developer-manual.md)
- 语义对照 / 覆盖账本：`docs/migration/semantic-mapping.md`、`docs/migration/paper-coverage-matrix.md`
- 语义裁决：论文 → JS Cordis → Go；Koishi 仅作产品组合参照。冻结差异见 `docs/ACCEPTED-DIFFERENCES.md`。

## 可运行全链路示例

```bash
bash samples/SpatiotemporalShowcase/build-and-run.sh /tmp/gocordis-showcase --run
```

真实编译 4 个 managed 插件包（共享契约 `--shared-assembly`、能力图 `--provides/--dependency`）、安装、声明、启动产品宿主，并演示严格 LIFO 撤销与依赖重激活。详见 `samples/SpatiotemporalShowcase/README.md`。

## 质量门

```bash
dotnet build Gocordis.sln --nologo -m:1
dotnet test  Gocordis.sln --nologo --no-build -m:1
(cd web/console && npm ci && npm test && npm run build)
(cd web/plugin-kit && npm test)
bash docs/migration/verify-baseline.sh   # Go 基线漂移检查（不是当前入口）
```

历史迁移过程与评审轮次都在 `docs/migration/`（归档），其中的分支名、SHA 与测试计数**不代表当前状态**。
