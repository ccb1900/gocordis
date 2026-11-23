# cmd/platform

A runnable composition root that closes the Phase-2/Phase-3 wiring gaps: one
Runtime, one Config Controller, the console hub/UI host and the console Web UI
in one process.

## Run

From the repository root:

```sh
go run ./cmd/platform                       # http://127.0.0.1:8170
GOCORDIS_ADDR=127.0.0.1:9000 go run ./cmd/platform
GOCORDIS_CATALOG_DIR=/path/to/catalog go run ./cmd/platform
# remote bind requires an explicit opt-in (see security note below)
GOCORDIS_ALLOW_REMOTE=1 GOCORDIS_ADDR=0.0.0.0:8170 go run ./cmd/platform
```

**Security:** the Web UI has NO built-in authentication. Binding a non-loopback
address (including `:8170` / empty host) is refused by
`webui.ValidateListenAddr` unless `GOCORDIS_ALLOW_REMOTE=1` (also accepts
`true`/`yes`). The deployer MUST supply authentication and network isolation
(firewall / VPN / reverse proxy); binding remote emits a loud warning.

`Ctrl-C` / `SIGTERM` triggers graceful shutdown: the HTTP server drains, then
the Config Controller withdraws every owned declaration, then the Runtime
closes. No fixed sleeps — lifecycle progress is awaited through fiber signals.

Environment: `GOCORDIS_ADDR` (listen address, default `127.0.0.1:8170`),
`GOCORDIS_ALLOW_REMOTE` (remote-listen opt-in, default off), `GOCORDIS_MANIFEST`
(manifest path), `GOCORDIS_CATALOG_DIR` (market catalog path), `GOCORDIS_WEB_DIST`
(built console SPA directory; API works without it).

## Manifest shape

`manifest.toml` is a declaration store only (`[[components]]` +
`[components.config]`); no runtime state is persisted. It declares:

- `host` → `console.host` — provides `HubKey` and `UIHostKey`;
- `insight` → `console.insight` — registers `insight/graph` and `config/schema`;
- `market` → `market` — registers `market/catalog` plus install/upgrade/uninstall
  commands, configured with `catalog_dirs`, `runtime_version`, `api_version`;
- `devtools` → `devtools` — provides the diagnostics capability;
- `demo` → `demo.settings` — a real `config.SchemaFactory` (string enum, bool,
  array of string), so `config/schema` is non-empty and bad configs are refused
  at reconcile time.

Relative catalog paths resolve against the manifest directory. The checked-in
`catalog/plugin.toml` is a native, build-time-only example row; replace it with
WASM/proc manifests (plus artifacts and checksums) to make install/upgrade live.

## What is demonstrable

- `GET /api/ui/pages` — host + insight + market pages, the `/market` page now
  declares `rowActions` for the three mutation commands;
- `GET /api/query/config/schema` — the demo settings JSON Schema;
- `GET /api/query/insight/graph` — declared graph (from the Controller) joined
  with the runtime graph (from `Runtime.Snapshot`);
- `GET /api/query/market/catalog` — local catalog rows with compatibility and
  install state;
- market mutations via the hub preserve unrelated declarations (they only
  submit a new desired set to the controller).

Integration tests use the real runtime, controller, hub and webui transport
with no network listener:

```sh
go test -race -count=1 ./cmd/platform/... ./extensions/market/...
```
