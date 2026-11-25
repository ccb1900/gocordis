# Review Round 14 — Console, Control Plane and Root Group Architecture

Code commit: `1ee7e5f7` (`dbfd28af` architecture plus final test determinism). This round implements the final architecture
plan rather than discussing whether to introduce a Root Plugin or a second Runtime.

## Final boundaries

| Object | Identity | Ordinary uninstall |
| --- | --- | --- |
| Platform Host | process entry, startup and recovery owner | no |
| Core Runtime | sole lifecycle authority | no |
| Control Plane | protected system management component | no |
| HTTP / Timer / FS | built-in system components or plugins | by policy |
| Root Group | declarative application configuration group | not applicable |
| Console UI | ordinary UI plugin | yes |
| Workflow / Collector / other plugins | business plugins | yes |

## Implemented closures

### Stage 1 — startup and management boundary

- `ApplicationLoader.ResolveAsync` is the single effective declaration plan.
  Host pre-scan, capability wire, shared-contract sealing, schema registration,
  resolved list, diagnostics, and `PluginControlService` all consume it.
  `PackageId` follows the same resolution rule as `ApplicationLoader.ApplyAsync`.
- `PluginControlPlaneComponent.ControlPlaneComponentId` is the stable id. The
  fiber registers it in `PluginRegistry.ProtectedIds`; install, upgrade,
  uninstall, disable and config changes all return `forbidden`.
- Web UI and CLI both call the one `IPluginControlPlane`; CLI `console *` uses
  the HTTP control plane instead of direct Store/Manifest writes.
- `PlatformApplicationTests` and `PluginControlPlaneTests` cover no-Console
  startup, protected control plane, and effective Entry/Plugin parity.

### Stage 2 — Console UI plugin and host UI infrastructure

- `WebUiServer`, React/AntD assets, UI contribution registry, recovery page and
  API routes remain host infrastructure.
- `samples/ConsoleUiPlugin` is an ordinary plugin package contributing route,
  navigation, page, extension and resource through `gocordis-ui.json`.
- `UiComposition` now has first-class `UiRoute`, `UiNavigation`, `UiExtension`
  and `UiResource` contracts with owner-bound reversible effects.
- Uninstalling Console leaves host APIs, recovery page and other plugins' UI
  contributions intact (`UiHostSeparationTests`).

### Stage 3 — Root Group / ApplicationLoader

- Entries/Group/Include expansion, flat Plugins override, config write-back,
  dependency changes, provider withdrawal/reactivation and multi-field changes
  all use the same declaration plan.
- Plugin entries inside a group have no implicit Fiber parent/child relation.
- Entries-only plugins can be installed, enabled, configured, upgraded and
  uninstalled via the control plane.

### Stage 4 — recovery and product operations

- `ApplicationManifest.SaveAsync` writes `.last-known-good.json`; `LoadAsync`
  falls back on corrupt JSON and records `RecoveredFrom`; API recovery restores
  the last good manifest atomically.
- `GET /recover`, `POST /api/host/recover` and `GET /api/plugins/audit` are
  host-owned, not Console-owned.
- CLI `console restore/install/upgrade/uninstall/enable/disable/list/audit` uses
  the typed `ConsoleClient` and the HTTP control plane.
- Control Plane audit records source/action/id/outcome/revision in-process;
  it does not persist runtime state.

## Live verification

The showcase script now packs and installs a Console UI plugin plus four
business plugins. The running product on port `8171` reported:

```text
gocordis-console=Active
showcase-store=Active
showcase-worker=Active
showcase-dashboard=Active
showcase-client-module=Active
/recover = 200
/api/ui/contributions = route/navigation/extension/resource present
CLI console list/audit = OK
CLI console uninstall plugin-control-plane = forbidden
```

## Local verification

```text
dotnet build Gocordis.sln → 0 warning / 0 error
dotnet test  Gocordis.sln → 679 passed / 1 skipped / 0 failed, 3 consecutive runs
plugin-kit npm test       → 2 passed
console npm test          → 103 passed
console npm run build     → success
showcase build-and-run    → 5 real plugin packages packed and installed
```

No CI result was queried. Remaining boundaries: no complex microfrontend, no
cross-restart audit persistence, no automatic file-watch HMR, native desktop
hosts still deferred, and Host infrastructure is intentionally not an ordinary
uninstallable plugin.
