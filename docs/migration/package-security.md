# Plugin Package Security & Store Transactions

Branch: `migration/dotnet-complete`

本文件记录审查指出的四个真实风险的关闭方式。全部先写攻击性测试，再实现最小修复。

## 1. Plugin ID 路径穿越（P0）

`PluginPackageWriter.ValidateIdentity` 现在强制 ID 格式：

```text
^[a-z0-9][a-z0-9._-]{0,127}$
```

拒绝：

- 路径分隔符 `/` 与 `\`
- 绝对路径
- `.` 与 `..`
- 大写字母与其他字符

校验在 writer 触碰文件系统之前执行；reader 也执行同一校验，绕过 writer 手工构造的恶意包会被拒绝。
`PluginStore` 对每个最终目录执行 `Path.GetFullPath` 与 store-root 包含性校验。

测试：`MaliciousPluginIdIsRejectedBeforeAnyStoreWrite`（6 组恶意 ID），验证 Store Root 外零写入。

## 2. ZIP 资源上限（P1）

`PluginPackageLimits` 定义共享上限：

| 限制 | 值 |
| --- | ---: |
| manifest | 1 MiB |
| package 文件 | 256 MiB |
| 单 payload 文件 | 16 MiB |
| payload 总量 | 64 MiB |
| payload 文件数 | 512 |

writer 与 reader 都执行限制；reader 在复制字节前检查 `ZipArchiveEntry.Length` 并累计总量，
同时拒绝重复 payload 路径，避免解压 bomb 与同包多义性。

测试：`ReaderEnforcesManifestPayloadAndCountLimits` 覆盖大文件、超大 manifest、超量文件和重复路径。

## 3. Package checksum 绑定 Manifest 元数据（P1）

完整性摘要现在覆盖（除 Checksum 自身外）：

- Id / Version（规范化）/ Type / DisplayName / HostVersionRange
- Dependencies（排序后）/ Provides（排序后）
- Files（排序后的 path + sha256 + length）

payload 文件哈希仍逐项校验。Checksum 是完整性校验，不是数字签名；来源真实性需要未来签名机制。

测试：`ManifestMetadataTamperingFailsIntegrityCheck` 验证只改 `Type` 也会失败。

## 4. Store 文件系统事务（P1）

Install / Upgrade / Uninstall 统一采用：

1. 新字节写入 `.staging/<tx>`
2. 受影响旧目录移动到 `.staging/<tx>-backup`
3. 新目录移动到最终位置
4. 原子提交 `installed.json`
5. 成功后清理 backup
6. 任一步失败：删除已落地新目录、按逆序恢复旧目录、恢复 lock、清理 staging

故障注入测试通过占用 `installed.json.tmp` 或 `installed.json` 触发真实文件系统提交失败：

- `FailedInstallLeavesNoFinalDirectoryAndNoLockChange`
- `FailedUpgradeRestoresOldVersionAndLock`
- `FailedUninstallRestoresDirectoryAndLock`

验证失败后目录、文件与 lock 状态一致。

## 5. 验证

- `dotnet test tests/Gocordis.Packaging.Tests`：24/24
- `dotnet test Gocordis.sln`：349/349
- Build：0 warning / 0 error
