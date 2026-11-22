#!/usr/bin/env bash
# Verify that the frozen Go baseline has not drifted.
# Usage: bash docs/migration/verify-baseline.sh
set -euo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo="$(cd "$here/../.." && pwd)"
expected_commit="ed2ef724a24331ecc3fa21974539315fd90212bf"

actual_commit="$(git -C "$repo" rev-parse HEAD)"
if [[ "$actual_commit" != "$expected_commit" ]]; then
  echo "BASELINE DRIFT: HEAD=$actual_commit expected=$expected_commit" >&2
  exit 1
fi
echo "HEAD OK: $actual_commit"

for doc in migration-plan.md migration-baseline.md semantic-mapping.md migration-coverage.md execution-log.md baseline.sha256; do
  [[ -f "$here/$doc" ]] || { echo "MISSING: docs/migration/$doc" >&2; exit 1; }
done
echo "Documents OK"

if (cd "$repo" && sha256sum -c "$here/baseline.sha256"); then
  echo "Working-tree overlay OK"
else
  echo "BASELINE DRIFT: a frozen overlay file changed" >&2
  exit 1
fi

echo "BASELINE VERIFIED"
