# Review Round 10 — Four-Dimensional Closure

审查基准：用户四维源码审查。代码提交：`ab9a3300`。分支：`migration/dotnet-complete`。

本轮不再做开放范围审查；只处理用户列出的 P1/P2/P3/P4，并用红测与本地执行关闭。

## 1. 第一维：论文语义

- **C34**：`Runtime.RehomeAsync` 在命令域执行 retire → deactivate → O-Remove → 同名重插，
  新 Fiber 具有新 Id、Generation+1、Revision+1；children 先于 parent 撤销。
  `Context.Isolate` 保持兼容，`IsolateAsync` 命令域化。C34 从“接受差异”改为“已覆盖”。
- **Managed 迭代器**：`ManagedPluginComponent` 实现 `IIterativeComponent`；内部插件实现
  迭代器时逐 step 交回 Core 调度，后端不再直接调用 `ApplyAsync`；divert 与 LIFO 逆有红测。
- **逆操作失败**：所有逆继续执行，但失败进入 `Fiber.CleanupFailures` /
  `RuntimeSnapshot.CleanupFailures`，槽位转 `Failed`，不再伪装为 `Undone` 成功。
- **C32**：新增可取消异步步骤的惯性迭代器测试，证明当前步落地后再逆。
- **账本**：`semantic-mapping` / `paper-coverage-matrix` / `theorem-matrix` 已同步；
  Thm43/C14 的过时“待补”表述已移除。

## 2. 第二维：JS Cordis

- rc.13 基线保持 `5e2e6c17...`；当前开发基线更新到 `f4c6b3ae...`
  （`feat(fs): add atomic and exclusive writes and copies`）。
- C# `FileSystemService` 新增 `FileWriteOptions` / `FileCopyOptions`，
  用 .NET 原生 `FileStream` + 同目录暂存 + `File.Move` 实现 atomic/exclusive，
  红测覆盖失败清理。该增量已闭合。
- 论文冲突项（Proxy 动态属性、跨 Effect 并发撤销、JS 保身份替换）继续按论文优先冻结。

## 3. 第三维：Koishi 式产品闭环

- Managed / Process / WASM 三种后端由唯一 `PlatformApplication.CreateComponent` 分发；
  CLI `app run` 委托 `PlatformRunner`，不再自建第二套 Runtime/Registry/WebUi。
- 插件包可声明 UI 资源，安装目录物化声明，激活时以插件 Fiber 为 owner 注册
  page / panel / client-module；禁用、升级、卸载随 Core effect 撤销。
- `KoishiProductE2ETests` 从产品程序集验证真实组合：feature 禁用/重启用、
  provider 切换、Final Quiescence。
- WinForms/Avalonia 仍是 RenderPlan profile，未伪称原生 Host。

## 4. 第四维：Go 能力与平台 gate

- Go 迁移基线裁决为冻结 `ed2ef724...`；后续 `main` 提交作为上游演进记录为 NOTE，
  `verify-baseline.sh` 继续验证冻结提交与 overlay 校验和。
- `pc-gate-disposition.md` 对 PC01–PC30 全部给出去向：6 项直接移植，其余等价覆盖，
  不再使用“其余 24 项未移植”的无状态表述。

## 5. 本地验证

```text
dotnet build Gocordis.sln → 0 warning / 0 error
dotnet test  Gocordis.sln → 442/442，连续 3 次全绿
plugin-kit npm test       → 2 passed
console npm test          → 88 passed
console npm run build     → success（仅既有 chunk-size 提示）
verify-baseline.sh        → BASELINE VERIFIED（Go main 差异为 NOTE）
git diff --check          → clean
```

CI 按用户约束未核验。本轮不声称 Windows runner 实跑通过。
