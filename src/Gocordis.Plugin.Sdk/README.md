# Gocordis Plugin SDK

The compile-time surface for writing Gocordis plugins. It re-exports only the
public authoring contracts from `Gocordis.Core` and `Gocordis.Plugins`:

- `Component`, `IIterativeComponent`, `Context`, `Cleanup`
- `Key<T>`, `EventKey<T>`, `VersionedKey<T>`, `MetaKey<T,M>`
- `PluginManifest` / declaration helpers

A plugin never references runtime internals, `PluginRegistry`, backends or the
package store. `Gocordis.Core` is always loaded into the host's default ALC;
keep cross-plugin keys in a shared contracts assembly (or use the host-owned
`ICapabilityCatalog` for cross-backend capabilities).
