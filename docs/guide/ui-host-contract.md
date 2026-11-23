# UI Host Contract and Product Scope (Task 02)

## Decision

- **Web is the only real UI host** today: `WebUiServer` hosts the SPA, serves
  client modules, exposes the control/query APIs and renders contributions.
- **WinForms / Avalonia are render-plan profiles only** (`UiHostProfiles`):
  no native windows, no native controls, no native renderer. This is an
  explicit product deferral, not a completed capability. Do not count
  `UiHostKind` values as completed hosts.

## Unified contribution contract (host-independent)

Plugins contribute data/contracts; hosts render them. The same contribution
set is consumed by any host:

| Contribution | Source contract | Web | WinForms | Avalonia |
| --- | --- | --- | --- | --- |
| Page | `UiPageDefinition` + `RegisterUiPageAsync` | rendered SPA page | deferred native host | deferred native host |
| Panel | `UiPanelDefinition` | rendered SPA panel | deferred | deferred |
| Settings | package `ConfigSchema` + `config/schema` query | typed form | deferred | deferred |
| Navigation | page `group`/`order` | menu tree | deferred | deferred |
| Command | `RegisterApiCommandAsync` | control API | shared control plane | shared control plane |
| Client Module | `gocordis-ui.json` client-module + `UiPackageBinder` | ESM served same-origin | not applicable | not applicable |
| Host Capability | API queries/commands, no UI code in C# | supported | supported | supported |
| Degradation | unsupported contribution reported by profile | explicit | explicit | explicit |

**Boundary**: UI hosts MUST NOT own plugin lifecycle, capability resolution or
effect inverses. They render/read contributions; `Runtime` + `PluginRegistry`
remain the only lifecycle and declaration authorities. `UiPackageBinder`
attaches package UI as fiber effects, so disable/uninstall reverses it.

## Acceptance for a future native host

A native host is "real" only when the same plugin, with the same contribution
declarations, can be enabled/disabled and its pages/settings/commands render
with the same owner, group/order and reversible teardown as Web. Until then,
the host is a profile and the product surface remains Web-only.
