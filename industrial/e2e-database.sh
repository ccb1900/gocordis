#!/usr/bin/env bash
# Opt-in real PostgreSQL integration test against a locally running server.
# SQLite is never substituted: the sink under test is PostgreSqlRecordSink.
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DB="gocordis_industrial_$(date +%s)_$$"
PGUSER_NAME="${PGUSER:-$(whoami)}"

psql -h 127.0.0.1 -U "$PGUSER_NAME" -d postgres -v ON_ERROR_STOP=1 -c "DROP DATABASE IF EXISTS $DB" >/dev/null
psql -h 127.0.0.1 -U "$PGUSER_NAME" -d postgres -v ON_ERROR_STOP=1 -c "CREATE DATABASE $DB" >/dev/null
trap 'psql -h 127.0.0.1 -U "$PGUSER_NAME" -d postgres -c "DROP DATABASE IF EXISTS $DB" >/dev/null 2>&1 || true' EXIT

GOCORDIS_PG_CONNECTION="Host=127.0.0.1;Port=5432;Username=$PGUSER_NAME;Database=$DB" \
DOTNET_ROLL_FORWARD=LatestMajor \
  dotnet test "$ROOT/industrial/tests/Industrial.Database.Tests/Industrial.Database.Tests.csproj" \
    --nologo -m:1

echo "==> real PostgreSQL integration test passed: $DB"
