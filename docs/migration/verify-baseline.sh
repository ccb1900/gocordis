#!/usr/bin/env bash
# Verify that the frozen Go baseline has not drifted.
# Usage: bash docs/migration/verify-baseline.sh
set -euo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo="$(cd "$here/../.." && pwd)"
expected_commit="ed2ef724a24331ecc3fa21974539315fd90212bf"

if ! git -C "$repo" merge-base --is-ancestor "$expected_commit" HEAD; then
  echo "BASELINE DRIFT: baseline $expected_commit is not an ancestor of HEAD" >&2
  exit 1
fi
main_commit="$(git -C "$repo" rev-parse main)"
if [[ "$main_commit" != "$expected_commit" ]]; then
  echo "BASELINE DRIFT: main moved from $expected_commit to $main_commit" >&2
  exit 1
fi
echo "BASELINE OK: $expected_commit (ancestor of HEAD; main frozen)"

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
