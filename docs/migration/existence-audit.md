# Existence / Authority Audit (2026-11-23)

This is an audit of **whether each component and capability should exist**, not
another feature review. Source basis: current `dotnet` HEAD; no test results are
quoted as proof of existence.

## 1. Project inventory and necessity

| Project | Necessity | Decision |
| --- | --- | --- |
| `Gocordis.Core` | Paper semantics: Runtime/Fiber/Activation/Effect/Capability/Realm | Keep, no rewrite |
| `Gocordis.Plugins` | Component/Manifest/Registry, declaration reconciliation | Keep |
| `Gocordis.Extensions` | Broker/timers/fs/http/diagnostics; dispatch modes per ADR-0004 | Keep, no second lifecycle |
| `Gocordis.Runtime.Managed` | Real .NET execution in ALC | Keep |
| `Gocordis.Runtime.Process` | Process isolation + JSON-RPC transport | Keep |
| `Gocordis.Runtime.Wasm` | Wasmtime execution boundary | Keep |
| `Gocordis.Packaging` | Package format/store/control plane/trust | Keep |
| `Gocordis.Console.WebUi` | Only real UI host + control API | Keep |
| `Gocordis.UI.Hosts` | Render-plan profiles only | Keep as profiles; native hosts deferred |
| `Gocordis.Platform` | Single composition root | Keep |
| `Gocordis.Plugin.Sdk` | Thin re-export of public authoring surface; removes runtime-repo path from plugin projects | Keep (thin by design) |
| `Gocordis.Client` | Typed console client outside runtime graph | Keep |
| `tools/Gocordis.Cli` | Author workflow + offline declaration editing | Keep |
| `tests/*`, `tests/fixtures/*`, `tests/Gocordis.Benchmarks` | Conformance, product E2E, deterministic measurement | Keep |

## 2. Single-authority map

| Capability | Authority | Projections / adapters |
| --- | --- | --- |
| Fiber lifecycle state | `Core.Runtime` decision loop (`Channel<RuntimeCommand>`) | `PluginRegistry` submits declarations only; backends never write state |
| Capability identity | `Core.CapabilityCatalog` (Id → one `ICapabilityKey`) | Backend adapters translate declarations into those keys |
| Declaration set | `ApplicationManifest` + `PluginRegistry` | `ApplicationLoader` builds manifests; CLI `app add` edits the same serializer offline |
| Package reverse dependency | `PluginDependencyPlanner` | Store/control-plane call it; no inline copies |
| Package trust | `Packaging.PluginTrustPolicy` + `PluginStore` | CLI and `PlatformHostOptions` pass the policy; no separate trust engine |
| Event lifecycle | `Core` reversible registration (`Context.OnAsync`) | `Extensions.EventDispatcher` modes (no second registry); `RuntimeEventRing` is observation only |

## 3. Diagnostic data ownership

| Data | Authority | Retention |
| --- | --- | --- |
| `Fiber.Failure` | Authoritative install failure | Per fiber |
| `Fiber.CleanupFailures` | Authoritative inverse failures | Per fiber |
| `Fiber.EffectFailures` | Authoritative slot-level failures | 32 per fiber |
| `Runtime._cleanupFailures` | Projection of fiber cleanup failures for global query | 256 records |
| `Runtime._effectFailures` | Projection of slot failures for global query | 256 records |
| `RuntimeEventRing` | Lifecycle transition history (separate concern) | 256 events |

Conclusion: the failure collections are **projections**, not a second authority.
They were bounded in this batch; `RuntimeEventRing` was already bounded. No
further diagnostic store is needed. Do NOT move these into `Extensions` — that
would create a second observation authority.

## 4. Questionable capabilities — verdict

- **`LoadBatchAsync`**: exists because `PluginRegistry.CommitAsync` submits an
  entire declaration batch. Without it, registry Apply would issue N public
  replies and N snapshot publications. Keep, but it is a Core-batch API, not a
  general parallel execution facility.
- **Effect failure history**: justified by the need to attribute failures to
  exact slots (`runtime/snapshot` + console inspector). Keep as projection with
  bounded retention; original exceptions are retained for C33 semantics.
- **`PluginTrustPolicy`**: belongs in `Packaging` (store enforces it). The CLI
  and host pass it; no second trust engine. `TrustedOnly` now REQUIRES pinned
  keys — a signature field alone is not trust.
- **CLI vs control plane install**: both write declarations, but one is offline
  authoring and the other is a running host mutation. They share
  `ApplicationManifest` serialization and `PluginStore`; keep both.
- **`ObserveStream` vs `ObservationLog`**: single-consumer queue vs fan-out log;
  separate semantics, documented. Keep.

## 5. Known gaps (not implementation tasks here)

- Native WinForms/Avalonia hosts remain deferred render-plan profiles.
- Hot reload without packing remains deferred; a future implementation must go
  through declaration revision, not in-place replacement.
- Windows execution evidence requires Windows CI (`WindowsOnlyFact`).
- Cold 10k apply still has per-command `FindCycle`/`Reconcile` work.
