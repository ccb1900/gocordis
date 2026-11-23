# Plugin Developer Workflow (Task 01)

This is the author-facing contract. A plugin developer needs only the SDK
package, the CLI and a product application manifest — not runtime internals.

## End-to-end flow

```bash
# 0) one-time: publish the SDK into a local feed
dotnet pack src/Gocordis.Core/Gocordis.Core.csproj -c Release -o <feed> -p:PackageVersion=1.0.0
dotnet pack src/Gocordis.Plugins/Gocordis.Plugins.csproj -c Release -o <feed> -p:PackageVersion=1.0.0
dotnet pack src/Gocordis.Plugin.Sdk/Gocordis.Plugin.Sdk.csproj -c Release -o <feed> -p:PackageVersion=1.0.0

# 1) create / develop / build
# Use a fresh --sdk-version for every local pack; NuGet caches by version and
# would otherwise reuse an older SDK Core from a previous run.
gocordis plugin create <dir> --id demo.plugin --name "Demo" --type demo.plugin \
  --feed <feed> --sdk-version <same-version-used-by-dotnet-pack>
cd <dir> && dotnet build

# 2) local preflight (no install, no package)
gocordis plugin validate .

# 3) pack / install / declare / run
gocordis plugin pack bin/Debug/net10.0 demo.gocordis-plugin \
  --id demo.plugin --version 1.0.0 --type demo.plugin \
  --backend managed --entry-assembly demo.plugin.dll --entry-type <entryType from create> \
  --capability 'provide|demo.plugin/log|endpoint|1.0.0|application/json|'
gocordis create <app-dir> --id demoapp --version 1.0.0 --host-version 1.0.0
gocordis plugin install demo.gocordis-plugin --store <store> --host-version 1.0.0
gocordis app add <app-dir>/gocordis.app.json demo.plugin --range 1.0.0 --store <store>
gocordis app run <app-dir>/gocordis.app.json --store <store> --validate-only
```

A runnable version of exactly this flow is
`samples/PluginDeveloperWorkflow/e2e.sh`.

## What the SDK gives you

`Gocordis.Plugin.Sdk` re-exports the public authoring surface via an MSBuild
`.props` file (`Component`, `Context`, `Key<T>`, `EventKey<T>`, declarations).
It deliberately does NOT export `Runtime`, `PluginRegistry`, backends, package
store or effect internals. Cross-plugin typed keys still belong to a shared
contracts assembly; cross-backend capabilities use the host catalog.

## Current gaps (explicitly deferred, not hidden in docs)

- **Hot reload / local debug without packing**: not implemented. A code change
  currently goes through build → pack → upgrade, which preserves paper
  Revision semantics. A file-watch debug mode must retarget as a revision, not
  an in-place fiber replacement.
- **Error-to-Activation attribution**: installed. `EffectFailureRecord` exposes
  fiber, activation, slot index and label in `runtime/snapshot`; history is
  bounded (256 runtime / 32 per fiber) to avoid pinning unloaded plugin types.
- **Native WinForms/Avalonia hosts**: see `ui-host-contract.md`; only Web is a
  real host today.
- **Trust policy / version lock**: CLI install/upgrade, `app run` and the product
  `PlatformRunner` all parse `--trust development|trusted-only` and `--public-key`
  through `PluginTrustPolicy.FromPemFiles`; Console installs inherit the host
  policy. `PlatformHostOptions.Trust` is the programmatic seam. Local development
  mode is the default; no remote marketplace. `--allow-remote` opens the listener
  but remote writes still need a host-supplied `ConsoleAccessPolicy`.
