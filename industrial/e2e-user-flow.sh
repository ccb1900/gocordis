#!/usr/bin/env bash
# Blank-environment browser E2E: install real packages, start a generic host
# with NO seeded resource, and drive the entire collection story in Chromium.
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
OUT="${1:-/tmp/gocordis-industrial-user-flow}"
PORT="${USER_FLOW_E2E_PORT:-8190}"
URL="http://127.0.0.1:${PORT}"

if lsof -nP -iTCP:"${PORT}" -sTCP:LISTEN >/dev/null 2>&1; then
  echo "port ${PORT} is already in use by a stale E2E host" >&2
  exit 1
fi

rm -rf "$OUT"
mkdir -p "$OUT/source"
printf 'record_id,reading\n1,42\n2,43\n3,44\n' > "$OUT/source/recent.csv"

(cd "$ROOT/industrial/web" && npm run build >/dev/null)
DOTNET_ROLL_FORWARD=LatestMajor bash "$ROOT/industrial/pack.sh" "$OUT" >/dev/null

DOTNET_ROLL_FORWARD=LatestMajor \
GOCORDIS_INDUSTRIAL_ROOT="$OUT/runtime" \
GOCORDIS_INDUSTRIAL_ALLOW_DEV_KEY=true \
GOCORDIS_INDUSTRIAL_SOURCE_ROOT="$OUT/source" \
  dotnet "$ROOT/industrial/src/Industrial.PackagedHost/bin/Debug/net10.0/Industrial.PackagedHost.dll" \
    "$OUT/gocordis.app.json" "$OUT/store" "$URL" >"$OUT/host.log" 2>&1 &
HOST_PID=$!
trap 'kill -9 "$HOST_PID" 2>/dev/null || true' EXIT

for _ in $(seq 1 30); do
  if curl -fsS "$URL/api/ui/pages" >/dev/null 2>&1; then break; fi
  sleep 1
done
if ! curl -fsS "$URL/api/ui/pages" >/dev/null 2>&1; then
  cat "$OUT/host.log"
  exit 1
fi

(cd "$ROOT/industrial/web" && E2E_BASE_URL="$URL" npx playwright test e2e/full-flow.spec.ts)
echo "==> blank-environment user flow passed: $URL"
