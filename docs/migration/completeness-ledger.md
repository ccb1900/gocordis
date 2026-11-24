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
| Timer service | `extensions/timer/timer.go:313,751-768` | extension plugin pattern | `src/Gocordis.Extensions/Timing.cs:9`, `ExtensionComponents.cs` | **partial** | Plugin provider + gating done (`ExtensionCompositionTests`); key binds concrete `TimerService`, not a public interface; no cross-backend provider replacement |
| HTTP service | `extensions/http/http.go:216,1609-1632` | `ctx.http` style service | `src/Gocordis.Extensions/Infrastructure.cs:411`, `ExtensionComponents.cs` | **partial** | Provider fiber + disposal done; identity-fenced proxy dialer registry + per-connection resolution done (`RegisterProxyDialer`, `SocketsHttpHandler.ConnectCallback`, ambient proxy disabled); no WebSocket dial; key binds concrete type |
| HTTP-SOCKS | `extensions/http-socks/component.go:15-49` (`Inject HTTP`, `RegisterProxy` in effect, inverse removes only own id) | extension contribution pattern | `src/Gocordis.Extensions/HttpSocks.cs:16`, `ExtensionComponents.cs:181-194` | **done (P0 closed)** | `SocksContributionRoutesAnHttpRequestAndUnwindsOnProviderWithdrawal` proves a real HTTP request traverses the fake SOCKS proxy and the registration is unwound on provider withdrawal; `ProxyRegistrationsAreIdentityFencedAndLastWins` proves last-wins and identity-fenced removal |
| FS service | `extensions/fs/fs.go:77,899-924` | capability service | `src/Gocordis.Extensions/Infrastructure.cs:16`, `ExtensionComponents.cs` | **partial** | mount/path policy done; no provider abstraction, no Memory provider; `MountScoped` handle is not identity-safe on re-mount |
| FS Local/Memory providers | `extensions/fs/contract/contract.go:72-161`, `fs/local/local.go`, `fs/memory/memory.go` | multiple implementations via broker/realm | not present | **missing (P1)** | Acceptance: `IFileSystemProvider` with Local and Memory implementations; env can consume Memory; mount/unmount reversible and identity-fenced |
| Env service | `extensions/env/env.go:29,51-158` (Inject FS, populate effect) | config via declarations | `src/Gocordis.Extensions/Infrastructure.cs:761`, `ExtensionComponents.cs` | **partial** | FS dependency and dotenv gating done; env is opt-in in the product; process-env layers unwind on withdrawal; key binds concrete type |
| Devtools diagnostics | `extensions/devtools/service.go:14,31-52` | diagnostics service | `src/Gocordis.Extensions/DevtoolsDiagnostics.cs:47`, `PlatformApplication.cs` | **done** | Runtime-owned provider fiber, `devtools/diagnostics` query, pre-start null guard |
| Watch service | `extensions/watch/watch.go:156-173`, `watch/file.go` (fsnotify only, no legacy polling watcher) | reactive watch | `src/Gocordis.Extensions/ReversibleWatch.cs:36` + legacy `Patterns.cs:574` | **partial (P1)** | Two semantics coexist; legacy lock mismatch and path-keyed handle overwrite leak watchers; new service has Error-after-dispose and callback-under-lock risks; unique formal service must be chosen |
| HTTP WebSocket | `extensions/http/http.go:205-215,1202-1302` (client dial) | websocket transport | none (`ServerGuards.cs:16` only shape) | **missing (P1)** | Acceptance: ws/wss dial through HTTP service, proxy-aware, consumer-bound and joined on provider withdrawal |
| Server service (route/WS registration) | `extensions/server/server.go:84,868-889` | console facade | `src/Gocordis.Console.WebUi/WebUiServer.cs:61`; only ACL contribution migrated via `RouteGuardExtensionComponent` | **not-porting (documented)** | C# uses declaration-led query/UI contributions, not plugin-owned raw HTTP routes; echo/static/proxy/WS server consumers not ported. Revisit only with an explicit product need |
| CLI service | `extensions/cli/cli.go:516` | CLI commands | `tools/Gocordis.Cli/Program.cs` | **not-porting (documented)** | C# CLI is a product tool, not a runtime capability; `CliDispatcher` stays library |
| Market / package-manager | `extensions/market/component.go`, `extensions/package-manager/package_manager.go` | console market | `src/Gocordis.Packaging/PluginControlService.cs` | **not-porting (documented)** | `PluginControlService` is the single package-operation authority; wiring these would create a second decision layer |
| Broker / registry / observe / event / scheduler / HMR | library-only in Go (no ServiceKey) | optional patterns | `src/Gocordis.Extensions/Patterns.cs`, `Observation.cs`, `GenericHmr.cs` | **not-porting (documented)** | No Go capability contract; keeping them as libraries preserves the reference semantics instead of inventing plugin semantics |
| Three execution backends | `loader/proc`, `loader/wasm` | JS module loader | `src/Gocordis.Runtime.{Managed,Process,Wasm}` + `Runtime.Contracts` | **done** | `CrossBackendCapabilityTests`, `BackendDispatchProductTests`; Process/WASM require capability declarations |
| Package trust / version lock | provenance in Go | manifest metadata | `src/Gocordis.Packaging/PluginTrustPolicy.cs`, `PluginStore.cs` | **done** | CLI/product host/Console share `FromPemFiles`; documented as provenance, not sandbox |
| Runtime batch load | channel orchestrator 256 + gate | async reconcile | `Runtime.LoadBatchAsync`, `PluginRegistry.CommitAsync` | **partial (P2)** | 100/1000/10000 measured historically; `FindCycle` + `Reconcile` + post-batch teardown snapshots remain O(N²); re-measure at this HEAD before optimizing |

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
2. Contracts-only assembly (`Gocordis.Extensions.Contracts`) with interfaces,
   keys and DTOs; SDK stops referencing the implementation assembly.
3. FS provider abstraction + Memory provider; HTTP WebSocket; single Watch
   implementation (P1).
4. Re-measure batch load at 100/1000/10000 and decide from scaling ratios (P2).
5. Keep the `not-porting` decisions above unless a product requirement changes.
