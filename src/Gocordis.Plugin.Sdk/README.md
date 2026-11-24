# Gocordis Plugin SDK

The compile-time surface for writing Gocordis plugins. It re-exports only the
public authoring contracts from `Gocordis.Core`, `Gocordis.Plugins` and the contracts-only
`Gocordis.Extensions.Contracts` (no ASP.NET Core framework reference):

- `Component`, `IIterativeComponent`, `Context`, `Cleanup`
- `Key<T>`, `EventKey<T>`, `VersionedKey<T>`, `MetaKey<T,M>`
- `PluginManifest` / declaration helpers
- `ExtensionKeys` + official provider components (Timer, HTTP, FS, Env, SOCKS,
  Devtools, Watch, RouteGuard). Consumers declare `Inject()` /
  `context.Require(ExtensionKeys.X)`; providers declare `Provide()` /
  `context.Provide(ExtensionKeys.X, service)`.

Packaged plugins that exchange official extension capabilities must declare
`--shared-assembly Gocordis.Extensions.Contracts` so every package resolves the same
Key<T> identity. A plugin never references runtime internals, `PluginRegistry`, backends or the
package store. `Gocordis.Core` is always loaded into the host's default ALC;
keep cross-plugin keys in a shared contracts assembly (or use the host-owned
`ICapabilityCatalog` for cross-backend capabilities).
