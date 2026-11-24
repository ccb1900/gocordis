#!/usr/bin/env bash
# Build the additive industrial solution and start its Web UI host.
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
OUT="${1:-/tmp/gocordis-industrial}"
mkdir -p "$OUT"
# Production deployments must export GOCORDIS_INDUSTRIAL_KEY and must NOT set
# GOCORDIS_INDUSTRIAL_ALLOW_DEV_KEY. This local script opts into the documented
# development fallback because it has no secret store.
dotnet build "$ROOT/industrial/Industrial.slnx" --nologo -m:1
export GOCORDIS_INDUSTRIAL_ALLOW_DEV_KEY=true
exec dotnet run --project "$ROOT/industrial/src/Industrial.Host/Industrial.Host.csproj" --no-build -- "$OUT" "${GOCORDIS_INDUSTRIAL_ADDR:-http://127.0.0.1:0}"
