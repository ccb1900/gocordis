#!/usr/bin/env bash
# Incremental verification: run only the suites affected by the current change.
# Full acceptance stays a separate, explicit step (test-report.sh).
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
BASE="${BASE:-$(git -C "$ROOT" merge-base HEAD origin/main 2>/dev/null || git -C "$ROOT" rev-parse HEAD~1)}"
DRY_RUN="${DRY_RUN:-0}"
RUN_UNC_BROWSER="${RUN_UNC_BROWSER:-0}"

cd "$ROOT"

changed() {
  {
    git diff --name-only "$BASE"...HEAD 2>/dev/null || true
    git diff --name-only 2>/dev/null || true
    git ls-files --others --exclude-standard 2>/dev/null || true
  } | grep -v '^docs/.collector.md.swp$' | sort -u
}

FILES="$(changed)"
if [[ -z "$FILES" ]]; then
  printf 'No changed files since %s; nothing to verify.\n' "$BASE"
  exit 0
fi

printf 'Changed since %s:\n%s\n\n' "$BASE" "$FILES"

WEB_SRC=0; WEB_E2E=0; INDUSTRIAL_SRC=0; INDUSTRIAL_TESTS=0; CORE_SRC=0; ROOT_TESTS=0
DESIGNER=0; BINDING=0; RESOURCE_UI=0; CREDENTIAL=0
BACKEND_FILTER=''
SPEC_FILES=""

while IFS= read -r file; do
  [[ -z "$file" ]] && continue
  case "$file" in
    industrial/web/src/*|industrial/web/package.json|industrial/web/vite.config.ts|industrial/web/tsconfig.json)
      WEB_SRC=1
      case "$file" in
        *CordisDesigner*|*GraphCanvas*|*graph-model*|*PropertyEditor*|*config-editor*|*styles*) DESIGNER=1 ;;
        *BindingConsole*|*FileProfileConsole*|*CollectionWizard*|*workflow-model*) BINDING=1 ;;
        *ResourceConsole*|*MachineConsole*) RESOURCE_UI=1 ;;
        *CredentialConsole*) CREDENTIAL=1 ;;
      esac ;;
    industrial/web/e2e/*|industrial/e2e-*.sh)
      WEB_E2E=1
      case "$file" in
        *designer*) DESIGNER=1 ;;
        *scale*|*parameters*|*import-preview*|*excel*|*user-stories*) BINDING=1 ;;
        *full-flow*) RESOURCE_UI=1 ;;
        *unc-browser*) CREDENTIAL=1 ;;
      esac
      case "$file" in
        industrial/web/e2e/*.spec.ts) SPEC_FILES="$SPEC_FILES e2e/$(basename "$file")" ;;
      esac ;;
    industrial/src/*) INDUSTRIAL_SRC=1 ;;
    industrial/tests/*) INDUSTRIAL_TESTS=1 ;;
    src/*|tools/Gocordis.Cli/*) CORE_SRC=1 ;;
    tools/release/*) ;;
    tests/*) ROOT_TESTS=1 ;;
  esac
  case "$file" in
    *BindingConsole*|*ConfigurationRegistries*|*IndustrialUiComponent*|*Vocabulary.cs|*IndustrialDbContext*|*Industrial.Elsa*|*BindingParameterTests*|*ImportPreviewTests*|*CollectionBindingTests*|*ConfigurationImportTests*|*ConfigPackageTests*|*ExcelImportTests*|*FileProfileRuntimeTests*|*CsvImportTests*)
      BACKEND_FILTER='FullyQualifiedName~BindingParameterTests|FullyQualifiedName~ImportPreviewTests|FullyQualifiedName~CollectionBindingTests|FullyQualifiedName~ConfigurationImportTests|FullyQualifiedName~ConfigPackageTests|FullyQualifiedName~ExcelImportTests|FullyQualifiedName~FileProfileRuntimeTests|FullyQualifiedName~CsvImportTests|FullyQualifiedName~ResourceReferenceGuardTests' ;;
    industrial/src/*Credential*|industrial/tests/*Credential*|*ResourceConnectionTester*|*SinkFactories*)
      BACKEND_FILTER='FullyQualifiedName~CredentialBackendTests|FullyQualifiedName~ResourceReferenceGuardTests' ;;
    *MachineConsole*|*ResourceReferenceGuardTests*)
      BACKEND_FILTER='FullyQualifiedName~ResourceReferenceGuardTests' ;;
    *IndustrialNodesComponent*|*PropertyEditor*|*workflow-model*)
      BACKEND_FILTER='FullyQualifiedName~WorkflowValidatorTests|FullyQualifiedName~FeatureMatrixTests' ;;
  esac
done <<< "$FILES"

SPECS=""
add_spec() {
  for spec in "$@"; do
    [[ " $SPECS " == *" $spec "* ]] || SPECS="$SPECS $spec"
  done
}
if [[ -n "$SPEC_FILES" ]]; then
  for spec in $SPEC_FILES; do add_spec "$spec"; done
elif (( DESIGNER )); then
  add_spec e2e/designer.spec.ts e2e/designer-gestures.spec.ts e2e/designer-usability.spec.ts e2e/designer-advanced.spec.ts
fi
if [[ -z "$SPEC_FILES" ]]; then
  if (( BINDING )); then
    add_spec e2e/scale-e2e.spec.ts e2e/parameters-e2e.spec.ts e2e/import-preview-e2e.spec.ts e2e/excel-e2e.spec.ts e2e/user-stories.spec.ts
  fi
  if (( RESOURCE_UI || CREDENTIAL )); then
    add_spec e2e/user-stories.spec.ts
  fi
fi
if (( WEB_E2E )) && [[ -z "$SPECS" ]]; then
  # A new/changed spec with no detected domain: run the changed spec file only.
  while IFS= read -r file; do
    case "$file" in
      industrial/web/e2e/*.spec.ts) add_spec "e2e/$(basename "$file")" ;;
    esac
  done <<< "$FILES"
fi
SPECS="${SPECS# }"

run() {
  if [[ "$DRY_RUN" == "1" ]]; then
    printf '[dry-run] %s\n' "$*"
  else
    printf '\n==> %s\n' "$*"
    eval "$*"
  fi
}

if (( CORE_SRC )); then
  run "dotnet build Gocordis.sln --nologo -m:1 -v q"
  run "DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/Gocordis.Runtime.Tests/Gocordis.Runtime.Tests.csproj --nologo --no-build -m:1 --filter 'FullyQualifiedName!~CrossBackendCapabilityTests'"
fi

if (( INDUSTRIAL_SRC || INDUSTRIAL_TESTS )); then
  run "dotnet build industrial/Industrial.slnx --nologo -m:1 -v q"
  if [[ -n "$BACKEND_FILTER" ]]; then
    run "DOTNET_ROLL_FORWARD=LatestMajor dotnet test industrial/Industrial.slnx --nologo --no-build -m:1 --filter '$BACKEND_FILTER'"
  else
    # Skip the 800-fire scheduler test in incremental loops; final report runs it.
    run "DOTNET_ROLL_FORWARD=LatestMajor dotnet test industrial/Industrial.slnx --nologo --no-build -m:1 --filter 'FullyQualifiedName!~BindingSchedulerTests'"
  fi
fi

if (( WEB_SRC )); then
  run "(cd industrial/web && npx tsc --noEmit && npm test -- --run)"
fi

if [[ -n "$SPECS" ]]; then
  run "E2E_SPECS='$SPECS' bash industrial/e2e-browser.sh"
fi

if (( RESOURCE_UI || CREDENTIAL )); then
  run "bash industrial/e2e-user-flow.sh"
fi

if [[ "$RUN_UNC_BROWSER" == "1" && "$CREDENTIAL" == "1" ]]; then
  printf '\nUNC browser E2E requires a Windows runner; dispatch .github/workflows/windows-unc-browser.yml only when needed.\n'
fi

printf '\nSelected suites above are intentionally minimal. Full acceptance: bash tools/release/test-report.sh artifacts/test-report\n'
