#!/usr/bin/env bash
# Real packaged-plugin E2E: build real .gocordis-plugin packages, install them
# into a fresh store, start Industrial.PackagedHost and exercise the product
# API over HTTP (no host-side raw factories).
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
OUT="${1:-/tmp/gocordis-industrial-packaged-e2e}"
PORT="${PACKAGED_E2E_PORT:-8189}"
URL="http://127.0.0.1:${PORT}"

if lsof -nP -iTCP:"${PORT}" -sTCP:LISTEN >/dev/null 2>&1; then
  echo "port ${PORT} is already in use by a stale E2E host" >&2
  exit 1
fi

rm -rf "$OUT"
mkdir -p "$OUT/source"
printf 'record_id,reading\npackaged-1,42\n' > "$OUT/source/recent.csv"
touch -t 202611250900 "$OUT/source/recent.csv"

DOTNET_ROLL_FORWARD=LatestMajor bash "$ROOT/industrial/pack.sh" "$OUT" >/dev/null

DOTNET_ROLL_FORWARD=LatestMajor \
GOCORDIS_INDUSTRIAL_ROOT="$OUT/runtime" \
GOCORDIS_INDUSTRIAL_ALLOW_DEV_KEY=true \
GOCORDIS_INDUSTRIAL_SEED_RESOURCE=true \
GOCORDIS_INDUSTRIAL_SOURCE_ROOT="$OUT/source" \
  DOTNET_ROLL_FORWARD=LatestMajor dotnet \
    "$ROOT/industrial/src/Industrial.PackagedHost/bin/Debug/net10.0/Industrial.PackagedHost.dll" \
    "$OUT/gocordis.app.json" "$OUT/store" "$URL" >"$OUT/host.log" 2>&1 &
HOST_PID=$!
trap 'kill -9 "$HOST_PID" 2>/dev/null || true' EXIT

# industrial/nodes becomes available before industrial.elsa finishes applying.
# industrial/recovery is Elsa-owned and always returns a payload, so it proves
# both the query and command registrations are live (unlike the execution query,
# which legitimately returns null/404 until the first run).
for _ in $(seq 1 30); do
  if curl -fsS "$URL/api/query/industrial/recovery" >/dev/null 2>&1; then break; fi
  sleep 1
done
if ! curl -fsS "$URL/api/query/industrial/recovery" >/dev/null 2>&1; then
  echo "==> packaged host diagnostics"
  curl -s "$URL/api/query/devtools/diagnostics" || true
  cat "$OUT/host.log"
  exit 1
fi

NODES=$(curl -fsS "$URL/api/query/industrial/nodes")
MACHINES=$(curl -fsS "$URL/api/query/industrial/machines")
python3 - "$NODES" "$MACHINES" <<'PY'
import json, sys
nodes = json.loads(sys.argv[1])["data"]
machines = json.loads(sys.argv[2])["data"]
assert len(nodes) >= 12, f"expected packaged node catalog, got {len(nodes)}"
assert isinstance(machines, list), "packaged machine query failed"
PY

curl -fsS -X POST "$URL/api/command/industrial/save-workflow" \
  -H 'content-type: application/json' \
  --data "$(python3 - <<'PY'
import json
node=lambda i,t,c:{"id":i,"type":t,"config":c,"x":0,"y":0}
print(json.dumps({
 "workflowId":"packaged-flow","name":"Packaged flow","version":1,
 "nodes":[node("src","industrial.filesystem.local",{"resourceId":"source-1"}),
          node("read","industrial.reader.csv",{}),
          node("store","industrial.sink.sqlite",{})],
 "edges":[{"source":"src","sourcePort":"files","target":"read","targetPort":"input"},
          {"source":"read","sourcePort":"records","target":"store","targetPort":"records"}],
 "trigger":{"kind":"manual","cron":None,"eventName":None,"config":{}},
 "resourceReferences":["source-1"],"layout":{},
}))
PY
)" >/dev/null
curl -fsS -X POST "$URL/api/command/industrial/publish-workflow" \
  -H 'content-type: application/json' --data '{"workflowId":"packaged-flow"}' >/dev/null
curl -fsS -X POST "$URL/api/command/industrial/run-workflow" \
  -H 'content-type: application/json' \
  --data '{"workflowId":"packaged-flow","resourceId":"source-1"}' >/dev/null

python3 - "$URL" <<'PY'
import json, sys, urllib.request
url = sys.argv[1]
execution = json.load(urllib.request.urlopen(f"{url}/api/query/industrial/execution"))["data"]
assert execution["status"] == "Completed", execution
assert execution["executed"] == ["src", "read", "store"], execution
center = json.load(urllib.request.urlopen(f"{url}/api/query/industrial/execution-center"))["data"]
assert center["runs"] and center["runs"][0]["status"] == "Succeeded", center
PY

# Published only after every assertion above succeeded; the unified report
# parses this as a real per-suite count instead of an unparsed zero.
echo "ℹ pass 7"
echo "==> real packaged-plugin E2E passed: $URL"
