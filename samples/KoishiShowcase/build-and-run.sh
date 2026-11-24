#!/usr/bin/env bash
# Koishi-style packaged case: build → pack real .gocordis-plugin packages →
# install → write application declaration → start the product host.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
SHOWCASE="$ROOT/samples/KoishiShowcase"
OUT="${1:-/tmp/gocordis-koishi-showcase}"
RUN_HOST=false
for argument in "$@"; do
  if [[ "$argument" == "--run" ]]; then
    RUN_HOST=true
  fi
done
STORE="$OUT/plugins"
MANIFEST="$OUT/gocordis.app.json"
mkdir -p "$OUT/packages" "$STORE"

echo "==> building Koishi contracts and plugins"
for project in Contracts Vocabulary Database Adapter Feature; do
  dotnet build "$SHOWCASE/$project" --nologo -v q
done

echo "==> building the CLI"
dotnet build "$ROOT/tools/Gocordis.Cli/Gocordis.Cli.csproj" --nologo -v q
CLI=(dotnet "$ROOT/tools/Gocordis.Cli/bin/Debug/net10.0/Gocordis.Cli.dll")

pack_and_install() {
  local id="$1" project="$2" type="$3"; shift 3
  local stage="$OUT/packages/$id"
  local bin="$SHOWCASE/$project/bin/Debug/net10.0"
  rm -rf "$stage" && mkdir -p "$stage"
  cp "$bin/Koishi.$project.dll" "$bin/Koishi.$project.deps.json" "$stage/"
  cp "$SHOWCASE/Contracts/bin/Debug/net10.0/Koishi.Contracts.dll" "$stage/"
  local package="$OUT/packages/$id-1.0.0.gocordis-plugin"
  "${CLI[@]}" plugin pack "$stage" "$package" \
    --id "$id" --version 1.0.0 --type "$id" --host-range '[1.0,)' \
    --backend managed --entry-assembly "Koishi.$project.dll" --entry-type "$type" \
    --shared-assembly Koishi.Contracts "$@"
  "${CLI[@]}" plugin install "$package" --store "$STORE" --host-version 1.0.0
}

pack_and_install koishi.vocabulary Vocabulary Koishi.VocabularyPlugin.Vocabulary \
  --capability 'provide|koishi/commands|endpoint|1.0.0|application/json|'
pack_and_install koishi.database Database Koishi.DatabasePlugin.Database \
  --capability 'provide|koishi/database|endpoint|1.0.0|application/json|'
pack_and_install koishi.adapter Adapter Koishi.AdapterPlugin.Adapter \
  --dependency "koishi.database@[1.0,2.0)" \
  --capability 'provide|koishi/adapter|endpoint|1.0.0|application/json|'
pack_and_install koishi.feature Feature Koishi.FeaturePlugin.Feature \
  --dependency "koishi.vocabulary@[1.0,2.0)" \
  --dependency "koishi.database@[1.0,2.0)" \
  --dependency "koishi.adapter@[1.0,2.0)"

python3 - "$MANIFEST" <<'PY'
import json, sys
path = sys.argv[1]
manifest = {
    "Id": "koishi-showcase", "Version": "1.0.0", "HostVersion": "1.0.0",
    "Plugins": [
        {"Id": "koishi.vocabulary", "VersionRange": "[1.0,2.0)", "Enabled": True},
        {"Id": "koishi.database", "VersionRange": "[1.0,2.0)", "Enabled": True},
        {"Id": "koishi.adapter", "VersionRange": "[1.0,2.0)", "Enabled": True},
        {"Id": "koishi.feature", "VersionRange": "[1.0,2.0)", "Enabled": True},
    ],
}
with open(path, "w", encoding="utf-8") as handle:
    json.dump(manifest, handle, indent=2, ensure_ascii=False)
PY

echo "==> ready"
echo "    store:    $STORE"
echo "    manifest: $MANIFEST"

if [[ "$RUN_HOST" == true ]]; then
  exec dotnet run --project "$ROOT/src/Gocordis.Platform/Gocordis.Platform.csproj" -- \
    --manifest "$MANIFEST" --store "$STORE" --address "${GOCORDIS_ADDR:-http://127.0.0.1:8170}"
fi
