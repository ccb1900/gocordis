# Runtime Integrity（批次 4）

Branch: `migration/dotnet-complete`

本文件冻结三种 .NET 后端与 Go Native 的迁移边界。Runtime 仍是唯一生命周期权威；
Managed / Process / WASM 只通过 `IPluginBackend` 接入，不建立自己的插件生命周期。

## 1. 共同协议

`src/Gocordis.Runtime.Managed/BackendProtocol.cs` 定义：

- `Prepare → Activate → Commit(Effect) → Rollback / Release`
- `BackendDegradationPolicy.FailFiber`：Managed / WASM
- `BackendDegradationPolicy.ErrorVocabulary`：Process（发布 `plugin_error`）
- 所有资源释放都作为 Core Effect 的逆执行，Owner 为当前 Activation。

三后端共享：Manifest、声明解析、依赖图、`PluginRegistry` 事务与 UI 贡献生命周期。
不存在第二套 Manifest 或第二套 Fiber 状态机。

## 2. Managed Runtime

- `PluginLoadContext` 使用 collectible `AssemblyLoadContext` + `AssemblyDependencyResolver`。
- 共享契约程序集交默认上下文，保持类型身份；插件私有依赖可多版本共存。
- `GenerationTracker` 做装载代次栅栏，陈旧 generation 不能卸载新 ALC。
- `ManagedPluginHost` 的固定拆卸顺序：Dispose Fiber → await Gone → 清引用 → `ALC.Unload`。
- `WeakReference` + 阻塞压缩 GC 证明 Pending 消费者不会钉住 provider ALC。

**边界**：ALC 不是安全沙箱。需要隔离不可信代码时使用 Process / WASM。

## 3. Process Runtime

- 握手：`GORIDIS-PROC-PLUGIN 1`，5s timeout；超时归一化并 reap 子进程。
- RPC：行分隔 JSON-RPC 2.0，单写锁 + 单在途调用；parse error `-32700`，未知方法 `-32601`。
- 关闭：graceful shutdown → 有界 kill → reap；调用超时只取消本次调用，不杀进程。
- 错误策略：启动失败不 Failed fiber，而是 Active + `plugin_error` + `ProcessDiagnostics.LastError`，无自动重启。
- 与 Go 一致：不支持插件→宿主调用。

## 4. WASM Runtime

- Wasmtime 34.0.2；ABI：`runtime_component_create` / `runtime_component_destroy`，装载期校验。
- 自定义段 `module_type` 读取逻辑类型；单一 `gocordis.report` host bridge，不暴露 FS/网络。
- **64 MiB 模块上限**已移植：`WasmPluginInstance.MaxModuleBytes`，`EnsureModuleSize`，
  `FromWat` / `FromBytes` 在编译前拒绝超限模块。
- HMR：`WasmHotSwapper` 仍为 warm-validate + 旧 Fiber Gone 后提交，不是零停机原子替换；
  通用 `GenericHmr` target/binding/usage 协议已在批次 3 迁移。

## 5. Go Native

.NET 不能直接加载 Go native 插件，也不为兼容名称引入自研 interop 框架。
替代路径是 Process backend：独立进程 + RPC + 故障隔离。账本标 Divergent，不标 Equivalent。

## 6. 验证证据

- `BackendLifecycleTests`：三后端 activate/undo/failure/core ownership 矩阵。
- `ManagedRuntimeTests`：ALC 回收、私有依赖多版本共存。
- `ProcessRuntimeTests`：握手、RPC、shutdown、崩溃/超时、降级。
- `WasmRuntimeTests`：ABI、自定义段、host bridge、HMR、64 MiB 上限。
- `dotnet build Gocordis.sln`：0 warning / 0 error。
- `dotnet test Gocordis.sln`：396/396（批次4新增 1 项）。
