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
  # Adjudication: this migration targets the frozen commit above. Later Go main
  # work is upstream evolution, not a change to the migration baseline. It is
  # reported, not silently accepted, and does not invalidate this pin.
  echo "BASELINE NOTE: Go main advanced from $expected_commit to $main_commit; migration remains pinned to $expected_commit"
else
  echo "BASELINE NOTE: Go main is frozen at $expected_commit"
fi
echo "BASELINE OK: $expected_commit is an ancestor of HEAD"

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
