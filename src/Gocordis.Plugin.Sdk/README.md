# Gocordis Plugin SDK

The compile-time surface for writing Gocordis plugins. It re-exports only the
public authoring contracts from `Gocordis.Core` and `Gocordis.Plugins`. Each
official extension publishes its OWN contract package
`Gocordis.Extensions.Contracts.<Name>` (interface, DTOs, Key<T>; no ASP.NET Core
and no implementation). Add only the ones you use with `plugin create --contract <Name>`:

- `Component`, `IIterativeComponent`, `Context`, `Cleanup`
- `Key<T>`, `EventKey<T>`, `VersionedKey<T>`, `MetaKey<T,M>`
- `PluginManifest` / declaration helpers
- `ExtensionKeys` + official provider components (Timer, HTTP, FS, Env, SOCKS,
  Devtools, Watch, RouteGuard). Consumers declare `Inject()` /
  `context.Require(ExtensionKeys.X)`; providers declare `Provide()` /
  `context.Provide(ExtensionKeys.X, service)`.

Packaged plugins that exchange official extension capabilities must declare
`--shared-assembly Gocordis.Extensions.Contracts.<Name>` so every package resolves the same
Key<T> identity. A plugin never references runtime internals, `PluginRegistry`, backends or the
package store. `Gocordis.Core` is always loaded into the host's default ALC;
keep cross-plugin keys in a shared contracts assembly (or use the host-owned
`ICapabilityCatalog` for cross-backend capabilities).
