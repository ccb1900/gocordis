#!/usr/bin/env bash
# Real-browser designer E2E: build the dev host, start it in-process with a
# fresh store, then drive the console with Chromium through Playwright.
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
OUT="${1:-/tmp/gocordis-industrial-browser-e2e}"
PORT="${E2E_PORT:-8188}"
URL="http://127.0.0.1:${PORT}"

rm -rf "$OUT"
mkdir -p "$OUT/source"
printf 'record_id,reading\n1,42\n' > "$OUT/source/recent.csv"

dotnet build "$ROOT/industrial/Industrial.slnx" --nologo -m:1 -v q >/dev/null
(cd "$ROOT/industrial/web" && npm run build >/dev/null)
HOST_MODULES="$ROOT/industrial/src/Industrial.Host/bin/Debug/net10.0/client-modules/industrial-designer"
mkdir -p "$HOST_MODULES"
cp -r "$ROOT/industrial/web/dist/." "$HOST_MODULES/"

DOTNET_ROLL_FORWARD=LatestMajor \
GOCORDIS_INDUSTRIAL_ROOT="$OUT" \
GOCORDIS_INDUSTRIAL_ALLOW_DEV_KEY=true \
GOCORDIS_INDUSTRIAL_SEED_RESOURCE=true \
GOCORDIS_INDUSTRIAL_SOURCE_ROOT="$OUT/source" \
  dotnet run --project "$ROOT/industrial/src/Industrial.Host/Industrial.Host.csproj" --no-build -- \
    "$OUT" "$URL" >"$OUT/host.log" 2>&1 &
HOST_PID=$!
trap 'kill "$HOST_PID" 2>/dev/null || true' EXIT

for _ in $(seq 1 30); do
  if curl -fsS "$URL/api/ui/pages" >/dev/null 2>&1; then break; fi
  sleep 1
done
if ! curl -fsS "$URL/api/ui/pages" >/dev/null 2>&1; then
  cat "$OUT/host.log"
  exit 1
fi

(cd "$ROOT/industrial/web" && E2E_BASE_URL="$URL" npm run test:e2e)
echo "==> real-browser designer E2E passed: $URL"
