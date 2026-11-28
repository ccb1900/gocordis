# Industrial Collection Platform (new, additive)

This tree implements the industrial data-collection product from `docs/collector.md`
**without modifying any existing Cordis file**. It adds its own solution that
references the existing Cordis projects as infrastructure.

Two launch paths:
- `bash industrial/build-and-run.sh <out>` — development host with pre-registered factories and opt-in dev key.
- `bash industrial/pack.sh <out> --run` — **product path**: real `.gocordis-plugin` packages, shared contract sealing, `PlatformApplication`, no raw factories.

```bash
dotnet build industrial/Industrial.slnx --nologo -m:1
dotnet test  industrial/Industrial.slnx --no-build --nologo -m:1
bash industrial/build-and-run.sh /tmp/gocordis-industrial
```

Default UI pages are contributed by `industrial.ui` (a Cordis plugin), not
hardcoded into the UI host. Disabling the plugin withdraws pages, queries and the
run command through the owning Fiber's effect stack.

## Layers

- `src/Industrial.Contracts` — public contracts and `Key<T>` vocabulary only.
- `src/Industrial.Persistence` — EF Core / SQLite business store, separate from Cordis runtime state.
- `src/Industrial.Core` — resource, credential, workflow, ledger, file identity, CSV, idempotent sinks.
- `src/Industrial.Plugins` — Cordis provider components and UI contributions.
- `src/Industrial.Elsa` — Elsa 3 PoC adapter (activity contributed by a plugin, Definition → Elsa publish).
- `src/Industrial.Host` — ASP.NET Core host composing the existing Cordis Runtime/Registry/WebUiServer.

See `docs/architecture/current-state.md` and `docs/architecture/gap-analysis.md`.

Execution/maintenance leases are renewed for the entire workflow, recovery and retry lifetime.
