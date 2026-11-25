#!/usr/bin/env bash
# Rebuild the product's built-in Console plugin package.
#
# The package is REAL: a managed Component plus one TSX-built ESM and its UI
# declarations. It ships from src/, not samples/. Install it with
# `gocordis plugin install --builtin console` (offline store) or
# `gocordis console install --builtin console` (running HTTP control plane).
set -euo pipefail

# The checkout targets net10.0; development hosts may only carry a newer
# major runtime. Roll forward explicitly instead of changing target frameworks.
export DOTNET_ROLL_FORWARD="${DOTNET_ROLL_FORWARD:-LatestMajor}"

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
PLUGIN="$ROOT/src/Gocordis.Console.Plugin"
OUT="${1:-$PLUGIN/builtin}"
CLI_DLL="$ROOT/tools/Gocordis.Cli/bin/Debug/net10.0/Gocordis.Cli.dll"
PACKAGE="$OUT/gocordis-console-1.0.0.gocordis-plugin"
STAGE="$OUT/.stage-console"
mkdir -p "$OUT"

echo "==> building the managed console plugin"
dotnet build "$PLUGIN/Gocordis.Console.Plugin.csproj" --nologo -v q

echo "==> building the CLI used to pack the real package"
dotnet build "$ROOT/tools/Gocordis.Cli/Gocordis.Cli.csproj" --nologo -v q

echo "==> building the plugin's self-contained TSX frontend"
(
    cd "$PLUGIN/frontend"
    if [[ ! -d node_modules ]]; then npm install --no-audit --no-fund; fi
    npm run build
)

echo "==> staging + packing $PACKAGE"
rm -rf "$STAGE" && mkdir -p "$STAGE"
BIN="$PLUGIN/bin/Debug/net10.0"
cp "$BIN/Gocordis.Console.Plugin.dll" "$BIN/Gocordis.Console.Plugin.deps.json" "$STAGE/"
cp "$PLUGIN/ui.js" "$PLUGIN/gocordis-ui.json" "$STAGE/"
cp -r "$PLUGIN/ui" "$STAGE/"
dotnet "$CLI_DLL" plugin pack "$STAGE" "$PACKAGE" \
    --id gocordis-console --version 1.0.0 --type gocordis-console \
    --host-range "[1.0,)" --backend managed \
    --entry-assembly Gocordis.Console.Plugin.dll \
    --entry-type Gocordis.Console.Plugin.ConsolePluginComponent \
    --provides gocordis-console
rm -rf "$STAGE"

echo "==> built $PACKAGE"
echo "    install offline:  dotnet $CLI_DLL plugin install --builtin console --store <dir>"
echo "    install via host: dotnet $CLI_DLL console install --builtin console --address <url>"
