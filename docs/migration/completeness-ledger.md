# Migration Completeness Ledger (maintained; dotnet @ 25f9d37+)

This ledger answers one question: **did we migrate the capability, or only a
similarly named type?** Sources, in arbitration order: paper (`docs/论文.pdf`),
Go baseline (`extensions/`, `runtime/`), JS Cordis 4.0.0-rc.13 (capability
reference), current C# code. Status values: **done / partial / missing /
not-porting** (the last requires an explicit reason). Test evidence must call
the code as a consumer does; `new Service()` success is not evidence.

## 1. Capability matrix

| Capability | Go anchor | JS reference | C# anchor | Status | Evidence / acceptance gap |
| --- | --- | --- | --- | --- | --- |
| Core Runtime / Fiber / Effect / Pending / withdrawal | `runtime/context.go:542-549`, `extensions/config/config.go` | `ctx.plugin`, declaration reconciliation | `src/Gocordis.Core/Runtime.cs`, `Fiber.cs`, `Context.cs` | **done** | `CoreSemanticsTests`, `PaperConformanceTests`, `TheoremMatrixTests`; paper C01–C40 matrix machine-checked |
| Provider capability identity | `runtime/key.go:9-49` (type+name) | typed `Context` keys | `src/Gocordis.Core/Capabilities.cs:25-30`, `Runtime.cs:87-92` | **done** | reference identity; `Key<T>` never stringified; no second provider registry |
| Timer service | `extensions/timer/timer.go:313,751-768` | extension plugin pattern | `src/Gocordis.Extensions/Timing.cs:9`, `ExtensionComponents.cs` | **done** | Provider fiber, gating and effect teardown covered by `ExtensionCompositionTests`; key binds `ITimerService` from the contracts assembly |
| HTTP service | `extensions/http/http.go:216,1609-1632` | `ctx.http` style service | `src/Gocordis.Extensions/Infrastructure.cs:411`, `ExtensionComponents.cs` | **done** | Provider fiber + disposal, identity-fenced proxy dialer registry, per-connection resolution and WebSocket dial all implemented; key binds `IHttpService` from the contracts assembly |
| HTTP-SOCKS | `extensions/http-socks/component.go:15-49` (`Inject HTTP`, `RegisterProxy` in effect, inverse removes only own id) | extension contribution pattern | `src/Gocordis.Extensions/HttpSocks.cs:16`, `ExtensionComponents.cs:181-194` | **done (P0 closed)** | `SocksContributionRoutesAnHttpRequestAndUnwindsOnProviderWithdrawal` proves a real HTTP request traverses the fake SOCKS proxy and the registration is unwound on provider withdrawal; `ProxyRegistrationsAreIdentityFencedAndLastWins` proves last-wins and identity-fenced removal |
| FS service | `extensions/fs/fs.go:77,899-924` | capability service | `src/Gocordis.Extensions/Infrastructure.cs`, `FileSystemProviders.cs` | **done** | `IFileSystemProvider` contract with Local/Memory providers; service owns mount routing and identity-fenced `MountScoped`; `MemoryProviderSupportsReadWriteWatchAndUnmount`, `MountScopedHandlesAreIdentitySafeOnRemount` |
| FS Local/Memory providers | `extensions/fs/contract/contract.go:72-161`, `fs/local/local.go`, `fs/memory/memory.go` | multiple implementations via broker/realm | `src/Gocordis.Extensions/FileSystemProviders.cs` | **done** | Local provider keeps symlink-escape guard; Memory provider has in-memory tree + watch notifications; both implement the same contract, env reads through FS mount |
| Env service | `extensions/env/env.go:29,51-158` (Inject FS, populate effect) | config via declarations | `src/Gocordis.Extensions/Infrastructure.cs:761`, `ExtensionComponents.cs` | **done** | FS dependency and dotenv gating done; env is opt-in in the product; process-env layers unwind on withdrawal; key binds `IEnvService` |
| Devtools diagnostics | `extensions/devtools/service.go:14,31-52` | diagnostics service | `src/Gocordis.Extensions/DevtoolsDiagnostics.cs:47`, `PlatformApplication.cs` | **done** | Runtime-owned provider fiber, `devtools/diagnostics` query, pre-start null guard |
| Watch service | `extensions/watch/watch.go:156-173`, `watch/file.go` (fsnotify only, no legacy polling watcher) | reactive watch | `src/Gocordis.Extensions/ReversibleWatch.cs:36` + legacy `Patterns.cs:574` | **done (P1)** | `ReversibleWatchService` is the sole implementation; legacy `WatchService` is a delegating adapter; callbacks run outside locks with exception isolation, async owner-bound registration, Error gate after disposal; `WatchOwnedAsyncUnwindsWithItsOwnerAndStopsCallbacks` |
| HTTP WebSocket | `extensions/http/http.go:205-215,1202-1302` (client dial) | websocket transport | `IHttpService.ConnectWebSocketAsync`, `RegisterProxyUrlFactory`, `SocksExtensionComponent` | **done (P1)** | `HttpCapabilityDialsWebSocketAndEchoes` proves a real ws echo through the capability; SOCKS registers a URL proxy factory owned by its fiber. Server-side raw WS routes remain a deliberate `not-porting` decision |
| Server service (route/WS registration) | `extensions/server/server.go:84,868-889` | console facade | `src/Gocordis.Builtin.WebConsole/WebUiServer.cs:61`; only ACL contribution migrated via `RouteGuardExtensionComponent` | **not-porting (documented)** | C# uses declaration-led query/UI contributions, not plugin-owned raw HTTP routes; echo/static/proxy/WS server consumers not ported. Revisit only with an explicit product need |
| CLI service | `extensions/cli/cli.go:516` | CLI commands | `tools/Gocordis.Cli/Program.cs` | **not-porting (documented)** | C# CLI is a product tool, not a runtime capability; `CliDispatcher` stays library |
| Market / package-manager | `extensions/market/component.go`, `extensions/package-manager/package_manager.go` | console market | `src/Gocordis.Packaging/PluginControlService.cs` | **not-porting (documented)** | `PluginControlService` is the single package-operation authority; wiring these would create a second decision layer |
| Broker / registry / observe / event / scheduler / HMR | library-only in Go (no ServiceKey) | optional patterns | `src/Gocordis.Extensions/Patterns.cs`, `Observation.cs`, `GenericHmr.cs` | **not-porting (documented)** | No Go capability contract; keeping them as libraries preserves the reference semantics instead of inventing plugin semantics |
| Three execution backends | `loader/proc`, `loader/wasm` | JS module loader | `src/Gocordis.Runtime.{Managed,Process,Wasm}` + `Runtime.Contracts` | **done** | `CrossBackendCapabilityTests`, `BackendDispatchProductTests`; Process/WASM require capability declarations |
| Package trust / version lock | provenance in Go | manifest metadata | `src/Gocordis.Packaging/PluginTrustPolicy.cs`, `PluginStore.cs` | **done** | CLI/product host/Console share `FromPemFiles`; documented as provenance, not sandbox |
| Runtime batch load | channel orchestrator 256 + gate | async reconcile | `Runtime.LoadBatchAsync`, `PluginRegistry.CommitAsync` | **partial (P2, measured)** | Re-measured at this HEAD: 3.545 ms / 241.436 ms / 32.583 s and 5.35 / 461.14 / 45,910.5 MB. Scaling ~quadratic; `FindCycle` + `Reconcile` + post-batch teardown snapshots remain the targets. Optimization is a separate scheduled task, not this migration pass |

## 2. Decisions that must not be reopened silently

- `Key<T>` matching is reference identity. Changing `Key<HttpService>` to
  `Key<IHttpService>` is safe **only** if the assembly holding the static key
  is shared and all DTO types in signatures come from that same assembly.
- The SDK must not drag ASP.NET Core into third-party plugin projects just to
  expose keys. The contracts-only assembly is the chosen boundary.
- External extension side effects remain subject to C35: only reversible
  registrations are guaranteed, not the HTTP call itself.
- No new plugin coordination mechanism outside Core Runtime. The SOCKS proxy
  registry, FS mount table and watch subscriptions are service-internal
  resource routing, not plugin lifecycle systems.

## 3. Closure order

1. HTTP-SOCKS real proxy loop (P0).
2. **done** One contract assembly per official extension
   (`Gocordis.Extensions.Contracts.<Name>`) with its own `Key<I…>`, interfaces
   and DTOs; SDK stays contract-free and `plugin create --contract <Name>`
   adds only the consumer's contract package. Generated plugins carry no
   `Microsoft.AspNetCore.App` reference.
3. **done** FS provider abstraction + Memory provider; HTTP WebSocket client;
   single Watch implementation with a compatibility adapter (P1).
4. **done (measurement only)** Re-measured 100/1000/10000 at this HEAD;
   scaling remains quadratic. Optimization is a separately scheduled P2 task,
   not part of this migration convergence.
5. Keep the `not-porting` decisions above unless a product requirement changes.
