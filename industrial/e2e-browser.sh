#!/usr/bin/env bash
# Real-browser product E2E: install real .gocordis-plugin packages, start the
# generic platform host, then drive the console with Chromium through Playwright.
# The client module comes from the industrial.ui package UI resource, not from
# a host-side pre-registration.
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
OUT="${1:-/tmp/gocordis-industrial-browser-e2e}"
PORT="${E2E_PORT:-8188}"
URL="http://127.0.0.1:${PORT}"

if lsof -nP -iTCP:"${PORT}" -sTCP:LISTEN >/dev/null 2>&1; then
  echo "port ${PORT} is already in use by a stale E2E host" >&2
  exit 1
fi

rm -rf "$OUT"
mkdir -p "$OUT/source"
printf 'record_id,reading\n1,42\n' > "$OUT/source/recent.csv"

(cd "$ROOT/industrial/web" && npm run build >/dev/null)
DOTNET_ROLL_FORWARD=LatestMajor bash "$ROOT/industrial/pack.sh" "$OUT" >/dev/null

DOTNET_ROLL_FORWARD=LatestMajor \
GOCORDIS_INDUSTRIAL_ROOT="$OUT/runtime" \
GOCORDIS_INDUSTRIAL_ALLOW_DEV_KEY=true \
GOCORDIS_INDUSTRIAL_SEED_RESOURCE=true \
GOCORDIS_INDUSTRIAL_SOURCE_ROOT="$OUT/source" \
  dotnet "$ROOT/industrial/src/Industrial.PackagedHost/bin/Debug/net10.0/Industrial.PackagedHost.dll" \
    "$OUT/gocordis.app.json" "$OUT/store" "$URL" >"$OUT/host.log" 2>&1 &
HOST_PID=$!
trap 'kill -9 "$HOST_PID" 2>/dev/null || true' EXIT

for _ in $(seq 1 30); do
  if curl -fsS "$URL/api/query/industrial/recovery" >/dev/null 2>&1 &&
     curl -fsS "$URL/api/ui/client-modules" | grep -q 'industrial-designer'; then break; fi
  sleep 1
done
if ! curl -fsS "$URL/api/query/industrial/recovery" >/dev/null 2>&1 ||
   ! curl -fsS "$URL/api/ui/client-modules" | grep -q 'industrial-designer'; then
  cat "$OUT/host.log"
  exit 1
fi

(cd "$ROOT/industrial/web" && E2E_BASE_URL="$URL" npm run test:e2e -- e2e/designer.spec.ts e2e/user-stories.spec.ts e2e/designer-gestures.spec.ts e2e/designer-usability.spec.ts e2e/scale-e2e.spec.ts e2e/excel-e2e.spec.ts)
echo "==> real-browser designer E2E passed: $URL"
