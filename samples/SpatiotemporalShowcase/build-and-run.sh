#!/usr/bin/env bash
# Build, pack, install and run the SpatiotemporalShowcase as REAL managed
# .gocordis-plugin packages. Demonstrates that cross-plugin dependency gating
# works in the packaged form because Showcase.Contracts is a shared assembly.
#
# Usage:
#   ./build-and-run.sh [targetDir]        # build + pack + install + write manifest
#   OUT=... GOCORDIS_ADDR=... ./build-and-run.sh ... --run
#   GOCORDIS_E2E_LOG=/tmp/showcase.log ./build-and-run.sh ...   # effect/undo proof
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
SHOWCASE="$ROOT/samples/SpatiotemporalShowcase"
OUT="${1:-/tmp/gocordis-showcase}"
ADDRESS="${GOCORDIS_ADDR:-http://127.0.0.1:8170}"
CLI_DLL="$ROOT/tools/Gocordis.Cli/bin/Debug/net10.0/Gocordis.Cli.dll"

PACKAGES="$OUT/packages"
STAGING="$OUT/staging"
STORE="$OUT/plugins"
MANIFEST="$OUT/gocordis.app.json"
mkdir -p "$PACKAGES" "$STAGING" "$STORE"

echo "==> building showcase projects"
for project in Contracts StoreProvider Worker Dashboard UiClientModule; do
    dotnet build "$SHOWCASE/$project" --nologo -v q
done

echo "==> building the plugin's REAL TSX frontend (src/ui.tsx -> ui.js + gocordis-ui.json)"
(
    cd "$SHOWCASE/UiClientModule/frontend"
    if [[ ! -d node_modules ]]; then npm install --no-audit --no-fund; fi
    npm run build
)

echo "==> building the CLI (needed for plugin pack/install)"
dotnet build "$ROOT/tools/Gocordis.Cli/Gocordis.Cli.csproj" --nologo -v q

pack_and_install() {
    local id="$1" project="$2" entry_type="$3"; shift 3
    local extra=("$@")   # --provides / --dependency declarations (the dependency graph)
    local asm="Showcase.$project"
    local bin="$SHOWCASE/$project/bin/Debug/net10.0"
    local stage="$STAGING/$id"
    rm -rf "$stage" && mkdir -p "$stage"
    cp "$bin/$asm.dll" "$bin/$asm.deps.json" "$stage/"
    # The shared contract assembly MUST ship in every package: the managed
    # loader promotes it into the default ALC once, giving all plugins the same
    # Key<T> identity. Gocordis.Core/Plugins are shared by the framework already.
    cp "$SHOWCASE/Contracts/bin/Debug/net10.0/Showcase.Contracts.dll" "$stage/"

    # UI lives WITH the plugin: gocordis-ui.json + page/panel definitions +
    # optional frontend module. The control plane binds these resources as
    # effects of the plugin's fiber — install/uninstall the package moves the UI.
    if [[ -d "$SHOWCASE/$project/ui" ]]; then
        cp -r "$SHOWCASE/$project/ui" "$stage/"
        # gocordis-ui.json must be at the PAYLOAD ROOT (the framework's
        # DeclarationEntryName); definitions stay under ui/.
        # Hand-written plugins keep it in ui/; the TSX kit emits it at the
        # plugin root. Accept either.
        if [[ -f "$SHOWCASE/$project/ui/gocordis-ui.json" ]]; then
            cp "$SHOWCASE/$project/ui/gocordis-ui.json" "$stage/gocordis-ui.json"
        elif [[ -f "$SHOWCASE/$project/gocordis-ui.json" ]]; then
            cp "$SHOWCASE/$project/gocordis-ui.json" "$stage/gocordis-ui.json"
        fi
    fi
    # The client module's ESM entry is declared at the payload root (ui.js).
    if [[ -f "$SHOWCASE/$project/ui.js" ]]; then
        cp "$SHOWCASE/$project/ui.js" "$stage/"
    fi

    local package="$PACKAGES/$id-1.0.0.gocordis-plugin"
    local schema_args=()
    # A package-declared config schema drives the console's typed config form;
    # it ships with the plugin, not with console code.
    if [[ -f "$SHOWCASE/$project/config/gocordis-config.schema.json" ]]; then
        schema_args=(--config-schema "$SHOWCASE/$project/config/gocordis-config.schema.json")
    fi
    dotnet "$CLI_DLL" plugin pack "$stage" "$package" \
        --id "$id" --version 1.0.0 --type "$id" --host-range "[1.0,)" \
        --backend managed \
        --entry-assembly "$asm.dll" \
        --entry-type "$entry_type" \
        --shared-assembly Showcase.Contracts \
        "${schema_args[@]+"${schema_args[@]}"}" "${extra[@]+"${extra[@]}"}"
    dotnet "$CLI_DLL" plugin install "$package" --store "$STORE" --host-version 1.0.0
}

echo "==> packing + installing managed plugin packages"
# The --dependency/--provides flags populate the package manifest that the
# control-plane dependency view reads. They describe the REAL capability graph:
# worker needs store; dashboard needs store + worker; client module needs dashboard.
pack_and_install showcase-store StoreProvider Showcase.StoreProvider.StoreProviderComponent \
    --provides showcase.store --provides showcase.store-changed
pack_and_install showcase-worker Worker Showcase.Worker.WorkerComponent \
    --dependency 'showcase-store@[1.0,2.0)' --provides showcase.metrics
pack_and_install showcase-dashboard Dashboard Showcase.Dashboard.DashboardComponent \
    --dependency 'showcase-store@[1.0,2.0)' --dependency 'showcase-worker@[1.0,2.0)' --provides showcase.dashboard
pack_and_install showcase-client-module UiClientModule Showcase.UiClientModule.ClientModuleComponent \
    --dependency 'showcase-dashboard@[1.0,2.0)' --provides showcase.client-module

echo "==> writing application declaration"
cat > "$MANIFEST" <<'JSON'
{
  "Id": "spatiotemporal-showcase",
  "Version": "1.0.0",
  "HostVersion": "1.0.0",
  "Plugins": [
    { "Id": "showcase-store",          "VersionRange": "[1.0,2.0)", "Enabled": true,
      "Config": { "intervalMs": 1500, "label": "showcase", "verbose": false, "mode": "steady" } },
    { "Id": "showcase-worker",         "VersionRange": "[1.0,2.0)", "Enabled": true, "Config": null },
    { "Id": "showcase-dashboard",      "VersionRange": "[1.0,2.0)", "Enabled": true, "Config": null },
    { "Id": "showcase-client-module",  "VersionRange": "[1.0,2.0)", "Enabled": true, "Config": null }
  ]
}
JSON

echo "==> ready"
echo "    store:    $STORE"
echo "    manifest: $MANIFEST"
echo "    packages: $PACKAGES"
echo ""
echo "Start the product host with:"
echo "  dotnet run --project $ROOT/src/Gocordis.Platform/Gocordis.Platform.csproj -- \\"
echo "    --manifest $MANIFEST --store $STORE --address $ADDRESS"
echo ""
echo "Or re-run with --run to start it now (Ctrl+C to stop)."

if [[ "${*: -1}" == "--run" ]]; then
    dotnet run --project "$ROOT/src/Gocordis.Platform/Gocordis.Platform.csproj" -- \
        --manifest "$MANIFEST" --store "$STORE" --address "$ADDRESS"
fi
