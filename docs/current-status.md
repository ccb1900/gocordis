# Current Status — Gocordis .NET

This is the **only** place that records current branch/HEAD, entry commands, verified counts and known gaps. Everything under `docs/migration/` is a historical migration archive; do not read its branch names, SHAs or test counts as current state. `README.md` is the front door and points here.

- **Branch**: `dotnet` (tracks `origin/dotnet`).
- **Baseline for review**: the `dotnet` branch HEAD at the time of the review; always refer to the actual `git rev-parse HEAD`, not a remembered commit.
- **Engineering constitution**: `AGENTS.md` (root). Engineering manual: `docs/guide/csharp-developer-manual.md`. User manual: `docs/guide/user-manual.md`.

## Product entry

```bash
dotnet run --project src/Gocordis.Platform -- \
  --manifest <gocordis.app.json> --store <plugins-dir> [--address http://127.0.0.1:8170] [--host web|winforms|avalonia] [--validate-only]
```

Console: `http://127.0.0.1:8170/` by default when an explicit address is given. WinForms/Avalonia are render-plan profiles, **not** native window hosts.

## Runnable full loop

```bash
bash samples/SpatiotemporalShowcase/build-and-run.sh /tmp/gocordis-showcase
# add --run to start the platform; see samples/SpatiotemporalShowcase/README.md for verification steps
```

It builds 5 projects + a real TSX frontend (`@gocordis/plugin-kit`), packs 4 real `.gocordis-plugin` packages (`--shared-assembly`, `--provides`, `--dependency`), installs them, writes the declaration, and starts the host.

## Verification commands

```bash
dotnet build Gocordis.sln --nologo -m:1
dotnet test  Gocordis.sln --nologo --no-build -m:1
(cd web/console    && npm ci && npm test && npm run build)
(cd web/plugin-kit && npm test)
bash docs/migration/verify-baseline.sh   # Go baseline drift check
```

**Last locally verified** (2026-11-23, branch `dotnet`): C# **456** tests green, frontend **102** tests green, plugin-kit tests green, solution build 0 warning/0 error. These counts are a record of that run, not a claim about any earlier SHA.

**CI**: no confirmed public CI result for the current HEAD; do not reuse historical 443/443 or other round results as proof for this commit. The Go baseline verification is handled by `verify-baseline.sh`, with drift reported rather than silently accepted.

## Known boundaries (not "done" claims)

- Managed ALC is **not** a security sandbox; Process/WASM are the isolation boundaries; managed plugins are trusted code. Shared contract assemblies are host-approved, full-identity checked, and process-lifetime (contract upgrades require a controlled host restart).
- Package signatures are verifiable provenance, **not** an install-trust chain.
- WinForms/Avalonia are render-plan profiles, not full native hosts.
- Koishi plugin market / remote distribution / browser-side reconcile loop are product references, not ported runtime features.
- Paper C38 (`VersionedKey<T>` + NuGet ranges instead of nominal `K×P`) remains the single accepted difference; C34 is covered via `Runtime.RehomeAsync`.

## Doc layers

1. **Current engineering spec**: root `AGENTS.md`, `docs/guide/csharp-developer-manual.md`.
2. **Current status**: this file (and `README.md` pointer).
3. **Historical migration record**: `docs/migration/` — archive only, including `README.md`, round/review documents and their old branch names, SHAs and counts.
