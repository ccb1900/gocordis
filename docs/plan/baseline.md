# Verification Baseline (Phase 1)

Reproducible toolchain and test baseline for the runtime + extension suite.

## Toolchain policy (pin & selection)

- Pinned Go version: **1.26.0** (see `.go-version`).
- Local development must invoke the pinned toolchain binary, NOT whatever `go`
  is first on `PATH` (this repo previously suffered a Go 1.25 `go` reading Go
  1.26 build-cache objects, which fails before tests run).
  Recommended (this machine): `/Users/ccb1900/.version-fox/sdks/golang/bin/go`
  (version-fox-managed 1.26.0).
- Cache isolation: never share a developer-global GOCACHE across toolchains.
  Use a run/workspace-local cache, e.g.:
  `GOCACHE=/tmp/gocache` (CI already gets a fresh per-job cache) or
  `<workspace>/.cache/go-build`.
- Alternative reproducible policy: set `GOTOOLCHAIN=go1.26.0`; Go >= 1.21 will
  then select/download the pinned toolchain automatically. Offline machines
  should install the pinned toolchain through their package manager instead.
- CI: `.github/workflows/ci.yml` installs the `.go-version` toolchain via
  `actions/setup-go`.

## Verification commands

```sh
GO="<pinned go 1.26.0 binary>"
GOCACHE="<isolated cache>"

gofmt -l $(find . -name '*.go' -not -path './vendor/*')
"$GO" vet ./...
"$GO" test ./...
"$GO" test -race ./...
# concurrency-sensitive repeat:
"$GO" test -race ./runtime/ ./integration/ ./extensions/hmr/ ./extensions/http/ -count=3
```

## Package baseline

Recorded: 2026-09-06, Go 1.26.0 (version-fox), isolated GOCACHE, macOS.

| Package | test ./... | test -race ./... | Notes |
|---|---|---|---|
| runtime | ok | ok | Kernel unit/contract/property |
| runtime/internal/wait | ok | ok | |
| extensions/config | ok | ok | |
| extensions/configwatch | ok | ok | full-Toml + real watch integration |
| extensions/event | ok | ok | |
| extensions/hmr | ok | ok | incl. concurrency |
| extensions/http | ok | ok | real HTTP bind/serve/shutdown |
| extensions/loader | ok | ok | incl. type-separation contract |
| extensions/loader/wasm | ok | ok | real wazero execution |
| extensions/registry | ok | ok | |
| extensions/scheduler | ok | ok | |
| extensions/watch | ok | ok | fsnotify transport (darwin verified) |
| integration | ok | ok | E2E incl. WASM/HMR/ConfigWatch |
| cmd/* (example/host/collector/backfill/wasmhmr) | ok (no tests) | ok | examples/apps |

Total test functions: 433.

## Environment limitations (documented, not skips)

- HTTP tests bind real loopback ports: they cannot run inside a sandbox that
  forbids socket binding (local run required escalation). CI (ubuntu) is fine.
- Windows: build + test-compile verified; runtime verification requires a
  Windows host/CI job (not present in this baseline).
- WASM runtime, TOML parser and file watcher are vendored third-party
  dependencies (wazero v1.11.0, pelletier/go-toml/v2 v2.4.3, fsnotify v1.10.1);
  all builds/tests run offline from the local module cache after `go mod
  download`.
