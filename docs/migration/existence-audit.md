# Existence / Authority Audit (maintained 2026-11-24, dotnet @ 56214b7)

This is an audit of **whether each component should exist**, not a feature review.
Method: read the current source and the declared project graph, trace actual
production call sites (not test references), then ask what breaks if the item is
removed. Test counts are never used as proof of product necessity. The product
under audit is: a .NET 10 plugin application platform with one Runtime, one
declaration set, three execution backends and a Web Console. It is not a
translation of every Go extension.

## 1. Project inventory: necessity, consumer, deletion impact

| Project | Product job | Actual production consumer | If deleted | Verdict |
| --- | --- | --- | --- | --- |
| `Gocordis.Core` | Paper Runtime/Fiber/Activation/Effect/Capability/Realm | Every layer; one decision loop | No runtime semantics at all | Keep; never rewrite into a second authority |
| `Gocordis.Plugins` | Component/Manifest/Registry declaration reconciliation | Platform/control plane/CLI `app add` | No declaration-led assembly, no transactional Apply | Keep |
| `Gocordis.Extensions` | Diagnostics, host ACL, opt-in HTTP seam; experimental primitives | `DiagnosticsService` + `HostAcl`/`ServerRouteGuard` in Platform/WebUi; `HttpService` optional in `HttpPluginPackageSource` | Lose real diagnostics/host guard; most other types have no product consumer | **Split classification** (§2), not blanket Keep |
| `Gocordis.Runtime.Contracts` | Physical home of backend protocol + generation fence | Managed, Process and WASM backends | Backends would each own a private protocol again | Keep; Core-only dependency, no implementation |
| `Gocordis.Runtime.Managed` | Real .NET execution in collectible ALC | Platform factory for `backend=managed` | No Managed plugins | Keep; ALC is not a sandbox |
| `Gocordis.Runtime.Process` | Child-process execution + JSON-RPC | Platform factory for declared `backend=process` plugins | No Process plugins | Keep; address-space isolation, not OS sandbox |
| `Gocordis.Runtime.Wasm` | Wasmtime execution boundary | Platform factory for declared `backend=wasm` plugins | No WASM plugins | Keep |
| `Gocordis.Packaging` | Package format, store, version lock, trust, control plane | Platform composition root, CLI, Console upload/install | No install/upgrade/uninstall or trust enforcement | Keep |
| `Gocordis.Console.WebUi` | Only real UI host + control API | `PlatformApplication` Web branch | No management UI, no install UI | Keep |
| `Gocordis.UI.Hosts` | Render-plan profiles | `PlatformApplication.RenderPlan()` only; CLI no longer exposes them as hosts | Render-plan design is lost, no product path breaks | **Keep as profiles only**; native hosts not delivered |
| `Gocordis.Client` | Typed console HTTP client | No in-repo product consumer | Nothing in the current product breaks | **Candidate external SDK**, not core-required; keep but do not count as product capability |
| `Gocordis.Plugin.Sdk` | Thin re-export of public authoring surface | Generated plugin projects (`plugin create`, local feed) | Third-party authors must reference runtime sources again | Keep; deliberately does not re-export Extensions or Runtime |
| `Gocordis.Platform` | Single composition root | Product executable, `app run`, generated app template | No product host | Keep |
| `tools/Gocordis.Cli` | App/plugin author workflow + preflight | Developer workflow, empty-directory E2E | No offline authoring | Keep |
| `tests/*`, `tests/fixtures/*`, `tests/Gocordis.Benchmarks` | Conformance, E2E, deterministic measurement | CI/local gates | Evidence disappears; product binaries still run | Keep |

Rejected merge proposals: `ApplicationManifest` vs `PluginManifest`, `PluginPackageManifest`
vs `ApplicationPlugin`, `SharedAssemblyRegistry` vs `CapabilityCatalog`,
`PluginDependencyPlanner` vs capability resolution, `PluginRegistry` vs
`PluginControlService`, `RuntimeSnapshot` vs diagnostic projections. Each pair
describes a different authority or lifetime; merging them would create exactly
the conflation the platform boundaries forbid.

## 2. Extensions capability-by-capability classification

`Gocordis.Plugin.Sdk` references only Core and Plugins. Nothing in Extensions is
part of the standard SDK surface; absence of a product consumer is therefore a
real classification signal, not merely a missing wiring opportunity.

| Capability | Evidence of consumer | Classification |
| --- | --- | --- |
| `DiagnosticsService`, diagnostic codes | `PlatformApplication` registers query; version/provider findings | **Product**: keep |
| `HostAcl`, `ServerRouteGuard` | Platform builds it from `--allowed-host`; WebUi installs middleware | **Product**: keep |
| `HttpService` | `HttpPluginPackageSource` accepts it, but shipped CLI passes no service | **Optional seam**: keep, do not claim product consumption |
| `GenericHmr` | Only `RuntimeGapTests`; no host call chain | **Experimental / deferred**: not product hot reload |
| `ObservationLog<T>`, `ObserveStream<T>` | Only tests; Console uses snapshot + HTTP/SSE | **Experimental**: keep, no product promise |
| `TimerService`, `Scheduler` | Only tests; no product periodic job | **Experimental** |
| `FileSystemService`, `EnvService` | Only tests; Web uses separate `WebAssetPaths` | **Experimental** |
| `ReversibleWatchService` | Only tests | **Experimental** |
| `SocksRegistry`, `SocksDialer` | Only tests; no product registration | **Experimental** |
| `Broker<T>`, `StableRegistry<T>`, `WatchService` | Public API + tests, no product composition | **Candidate developer API**: would need SDK export, example and published contract to become a promise |
| `MarketService`, `PackageManager`, `CliDispatcher`, `ServerHost` | No production use; control plane and CLI have their own authorities | **Experimental / duplicate-authority risk**: never wire into product |
| `EventDispatcher` | Type does not exist; ADR-0004 demoted dispatch and Core owns registration | **Removed**; stale migration mentions are archive only |

Consequence: the audit does not "Keep Extensions" as a block. Only the first two
rows are product capabilities; the rest are explicitly labelled so they cannot be
presented as completed migration work.

## 3. Single-authority map (current)

| Capability | Authority | Projections / adapters |
| --- | --- | --- |
| Fiber lifecycle state | `Core.Runtime` decision loop (`Channel<RuntimeCommand>`) | `PluginRegistry` submits declarations; backends never write state |
| Capability identity | `Core.CapabilityCatalog` (one Id → one `ICapabilityKey`) | Backend adapters translate declarations into those keys; no backend keeps a provider table |
| Declaration set | `ApplicationManifest` + `PluginRegistry` | `ApplicationLoader` builds manifests; CLI `app add` edits the same serializer offline |
| Package reverse dependency | `PluginDependencyPlanner` | Store/control plane call it; no inline copies |
| Package trust | `PluginTrustPolicy.FromPemFiles` + `PluginStore` | CLI install, `app run`, `PlatformRunner` and Console upload all converge here |
| Backend protocol | `Gocordis.Runtime.Contracts` | Managed/Process/WASM implement one `IPluginBackend`; no second protocol |
| Event lifecycle | `Core` reversible registration (`Context.OnAsync`) | Dispatch modes belong in Extensions per ADR-0004; `RuntimeEventRing` is observation only |
| Remote authorization | host-supplied `ConsoleAccessPolicy` | WebUiServer asks per scope; CLI never fabricates an identity |

## 4. Diagnostic data ownership

| Data | Authority | Retention |
| --- | --- | --- |
| `Fiber.Failure` | Authoritative install failure | Per fiber |
| `Fiber.CleanupFailures` | Authoritative inverse failures | Per fiber |
| `Fiber.EffectFailures` | Authoritative slot-level failures | 32 per fiber |
| `Runtime._cleanupFailures`, `_effectFailures` | Bounded projections for global queries | 256 records each |
| `RuntimeEventRing` | Lifecycle transition history (separate concern) | 256 events |

These are projections, not a second authority. Do not move them into Extensions;
that would create a second observation authority.

## 5. Backend legacy adapters and product boundary

`ProcessPluginComponent` / `WasmComponent` and their echo/error/value keys were
early demo adapters. They remain as backend-level classes for backend tests, but
`PlatformApplication.CreateComponent` now **rejects** any Process/WASM package
with no capability declaration instead of constructing them. `PlatformBackendKeys`
was removed. There is exactly one product path per backend: capability declarations
→ catalog keys → Core fiber. The boundary is enforced by product tests that assert
a no-declaration package fails at host start.

## 6. Product safety configuration

- CLI, `app run`, `PlatformRunner` all parse `--trust` / `--public-key` through
  `PluginTrustPolicy.FromPemFiles`; Web Console installs inherit the host policy.
  `PlatformHostOptions.Trust` is now actually assigned (the previous parameter
  shadowing left it unset).
- `--allow-remote` opens a listener only. Remote writes remain denied unless an
  embedding host supplies `PlatformHostOptions.AccessPolicy`; there is no CLI
  authentication shortcut. This is documented in `docs/guide/user-manual.md`.
- `plugin preflight` replaces `plugin sandbox` (alias retained) because it is a
  declaration check, not a security boundary.

## 7. Known gaps (not implementation tasks in this audit)

- Native WinForms/Avalonia hosts remain deferred render-plan profiles.
- Hot reload without packing remains deferred; a future implementation must go
  through declaration revision, not in-place replacement.
- Windows execution evidence requires Windows CI (`WindowsOnlyFact`).
- Cold 10k apply still has per-command `FindCycle`/`Reconcile` work.
- Most Extensions capabilities are experimental and absent from the SDK; making
  any of them a public promise requires an explicit scenario, API contract and example.
