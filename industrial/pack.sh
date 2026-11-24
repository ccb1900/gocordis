#!/usr/bin/env bash
# Build real .gocordis-plugin packages for the industrial product. No raw
# factories are needed: Industrial.PackagedHost loads only these packages.
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
OUT="${1:-/tmp/gocordis-industrial-packages}"
STORE="$OUT/store"
MANIFEST="$OUT/gocordis.app.json"
mkdir -p "$OUT/packages" "$STORE"

dotnet build "$ROOT/industrial/Industrial.slnx" --nologo -m:1 >/dev/null
dotnet build "$ROOT/tools/Gocordis.Cli/Gocordis.Cli.csproj" --nologo -v q
CLI=(dotnet "$ROOT/tools/Gocordis.Cli/bin/Debug/net10.0/Gocordis.Cli.dll")

pack() {
  local id="$1" assembly="$2" type="$3"
  local stage="$OUT/packages/$id"
  rm -rf "$stage" && mkdir -p "$stage"
  # The tests output contains the complete dependency closure (EF Core,
  # CsvHelper, SqlClient, Elsa...). Package it so a collectible ALC can resolve
  # every dependency without relying on the host's default context.
  cp "$ROOT/industrial/tests/Industrial.Tests/bin/Debug/net10.0/"*.dll "$stage/"
  local package="$OUT/packages/$id-1.0.0.gocordis-plugin"
  "${CLI[@]}" plugin pack "$stage" "$package" \
    --id "$id" --version 1.0.0 --type "$id" --host-range '[1.0,)' \
    --backend managed --entry-assembly "$assembly" --entry-type "$type" \
    --shared-assembly Industrial.Contracts
  "${CLI[@]}" plugin install "$package" --store "$STORE" --host-version 1.0.0
}

pack industrial.domain Industrial.Plugins.dll Industrial.Plugins.IndustrialDomainComponent
pack industrial.nodes Industrial.Plugins.dll Industrial.Plugins.IndustrialNodesComponent
pack industrial.elsa Industrial.Elsa.dll Industrial.Elsa.IndustrialElsaComponent
pack industrial.ui Industrial.Plugins.dll Industrial.Plugins.IndustrialUiComponent

python3 - "$MANIFEST" <<'PY'
import json, sys
manifest = {
    "Id": "industrial-app", "Version": "1.0.0", "HostVersion": "1.0.0",
    "Plugins": [
        {"Id": "industrial.domain", "VersionRange": "[1.0,2.0)", "Enabled": True,
         "Config": {"allowDevelopmentKey": "true"}},
        {"Id": "industrial.nodes", "VersionRange": "[1.0,2.0)", "Enabled": True},
        {"Id": "industrial.elsa", "VersionRange": "[1.0,2.0)", "Enabled": True},
        {"Id": "industrial.ui", "VersionRange": "[1.0,2.0)", "Enabled": True},
    ],
}
with open(sys.argv[1], "w", encoding="utf-8") as handle:
    json.dump(manifest, handle, indent=2, ensure_ascii=False)
PY

echo "==> packaged industrial app ready: $MANIFEST"
if [[ "${2:-}" == "--run" ]]; then
  export GOCORDIS_INDUSTRIAL_ROOT="$OUT/runtime"
  export GOCORDIS_INDUSTRIAL_ALLOW_DEV_KEY=true
  exec dotnet run --project "$ROOT/industrial/src/Industrial.PackagedHost/Industrial.PackagedHost.csproj" --no-build -- \
    "$MANIFEST" "$STORE" "${GOCORDIS_INDUSTRIAL_ADDR:-http://127.0.0.1:0}"
fi
