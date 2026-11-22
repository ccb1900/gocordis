# Review-Fix Round 3

本轮关闭 Round 2 遗留的 P1/P2 候选，全部先写红测再修根因。

| 问题 | 根因修复 | 红测 |
| --- | --- | --- |
| GenericHmr Bind 不 retire 旧值，Replace 在 usage 仍有效时提前 retire | generation-keyed pending retirement ledger；usage 归零时按 fence retire | `GenericHmrBindRetiresPreviousValue`、`GenericHmrReplaceDefersRetireWhileUsageHeld` |
| WasmHotSwapper ReleaseGeneration 可竞态清掉/写回 Current | Release/Replace/Dispose 共用 `_replaceGate`，generation fence 在 gate 内判定 | `ReleaseGenerationCannotRaceReplace` |
| SOCKS5 IPv6/IDN CONNECT 语义错误 | ATYP=4 for IPv6，IdnMapping，剥离方括号 | `SocksConnectSendsIpv6AtypAndPunycode`、`SocksRegistryParsesBracketedIpv6Proxy` |
| ConfigSchema 嵌套 enum/default 丢失，Default 不生效 | 递归序列化 schema；Validate 填充默认值；数值 enum 归一化比较 | `ConfigSchemaEmitsNestedEnumAndDefault` |
| 包写入临时文件残留、超限检查在 Move 后 | try/catch 清理 temp；Move 前检查大小 | `CancelledPackageWriteLeavesNoTempFile` |
| ReversibleWatch Ready 可能晚于 Change | buffered observations，Ready 后按序 flush | `WatchDeliversReadyBeforeAnyChange` |
| PluginRegistry.Entries 暴露活字典 | gate 内返回快照字典 | `EntriesSnapshotIsStableDuringConcurrentApply` |

本地验证：

```text
dotnet build Gocordis.sln → 0 warning / 0 error
dotnet test  Gocordis.sln → 378/378
npm test                 → 85 passed
npm run build            → success
```
