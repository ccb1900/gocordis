#!/usr/bin/env bash
# One executable fact source for local verification. Every suite writes its own
# log; the generated JSON/Markdown report is the only place counts are recorded.
set -uo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
OUT="${1:-$ROOT/artifacts/test-report}"
mkdir -p "$OUT"
cd "$ROOT"

run() {
  local name="$1"; shift
  echo "==> $name"
  set +e
  ( "$@" ) >"$OUT/$name.log" 2>&1
  echo $? >"$OUT/$name.code"
  set -e
}

run platform-dotnet bash -c 'dotnet build Gocordis.sln --nologo -m:1 -v q && DOTNET_ROLL_FORWARD=LatestMajor dotnet test Gocordis.sln --nologo --no-build -m:1'
run industrial-dotnet bash -c 'dotnet build industrial/Industrial.slnx --nologo -m:1 -v q && DOTNET_ROLL_FORWARD=LatestMajor dotnet test industrial/Industrial.slnx --nologo --no-build -m:1'
run industrial-frontend bash -c 'cd industrial/web && npm test -- --run && npx tsc --noEmit && npm run build'
run industrial-browser-e2e bash industrial/e2e-browser.sh
run industrial-user-flow bash industrial/e2e-user-flow.sh
run industrial-packaged-e2e bash industrial/e2e-packaged.sh
run industrial-postgresql bash industrial/e2e-database.sh
run industrial-sqlserver bash -c 'DOTNET_ROLL_FORWARD=LatestMajor dotnet test industrial/tests/Industrial.Database.Tests/Industrial.Database.Tests.csproj --nologo -m:1 --filter FullyQualifiedName~SqlServerSink'
run industrial-mysql bash -c 'DOTNET_ROLL_FORWARD=LatestMajor dotnet test industrial/tests/Industrial.Database.Tests/Industrial.Database.Tests.csproj --nologo -m:1 --filter FullyQualifiedName~MySqlSink' 
run windows-unc bash -c 'DOTNET_ROLL_FORWARD=LatestMajor dotnet test industrial/tests/Industrial.Database.Tests/Industrial.Database.Tests.csproj --nologo -m:1 --filter FullyQualifiedName~WindowsUnc'
run console-runtime bash -c 'cd web/console-runtime && npm test'
run console-shell bash -c 'cd web/console && npm test && npm run build'
run builtin-webconsole bash -c 'cd src/Gocordis.Builtin.WebConsole/frontend && npm test && npm run build'
run builtin-control-plane bash -c 'cd src/Gocordis.Builtin.ControlPlaneUi/frontend && npm test && npm run build'
run builtin-insight bash -c 'cd src/Gocordis.Builtin.InsightUi/frontend && npm test && npm run build'
run plugin-kit bash -c 'cd web/plugin-kit && npm test'

python3 - "$OUT" "$ROOT" <<'PY'
import json
import pathlib
import re
import sys

out = pathlib.Path(sys.argv[1])
root = pathlib.Path(sys.argv[2])
suites = []
for code_path in sorted(out.glob("*.code")):
    name = code_path.stem
    log = re.sub(r"\x1b\[[0-9;]*m", "", (out / f"{name}.log").read_text(errors="replace"))
    exit_code = int(code_path.read_text().strip())
    dotnet = [int(x) for x in re.findall(r"(?:Passed|Skipped)!\s+-\s+Failed:\s+\d+,\s+Passed:\s+(\d+)", log)]
    vitest = [int(x) for x in re.findall(r"Tests\s+(\d+)\s+passed", log)]
    node = [int(x) for x in re.findall(r"^ℹ pass (\d+)$", log, re.M)]
    playwright = [int(x) for x in re.findall(r"^\s*(\d+)\s+passed(?:\s*,|\s+\(|\s*$)", log, re.M)]
    skipped = [int(x) for x in re.findall(r"(?:Passed|Skipped)!.*?Skipped:\s*(\d+)", log)]
    skipped += [int(x) for x in re.findall(r"^\s*\d+\s+passed\b.*?\b(\d+)\s+skipped\b", log, re.M)]
    counts = dotnet + vitest + node + playwright
    suites.append({
        "suite": name,
        "exitCode": exit_code,
        # executionStatus is the suite gate; countStatus is independent so a
        # passing shell-driven suite is never mistaken for "0 tests passed".
        "executionStatus": "passed" if exit_code == 0 else "failed",
        "countStatus": "parsed" if counts else "unparsed",
        # null means the suite ran successfully but its log has no parseable
        # per-test count (shell-driven E2E). Exit code remains the gate.
        "passed": sum(counts) if counts else None,
        "skipped": sum(skipped),
        "log": f"{name}.log",
    })

import subprocess
commit = subprocess.run(["git", "rev-parse", "HEAD"], cwd=root,
                        capture_output=True, text=True).stdout.strip()

def load_evidence(filename, blocker):
    path = out.parent / filename
    if not path.exists():
        return {"status": "blocked", "url": blocker, "bound": False}
    evidence = json.loads(path.read_text())
    # External evidence is only proof for the commit that actually ran it.
    evidence["bound"] = evidence.get("headSha") == commit
    if not evidence["bound"]:
        evidence["status"] = "not-executed"
    return evidence

unc_evidence = load_evidence("windows-unc-result.json", "artifacts/windows-unc-blocker.md")
sqlserver_evidence = load_evidence("sqlserver-result.json", "artifacts/sqlserver-blocker.md")
mysql_evidence = load_evidence("mysql-result.json", "artifacts/mysql-blocker.md")
expected_suites = re.findall(r"^run (\S+)", (root / "tools/release/test-report.sh").read_text(), re.M)
not_executed = [name for name in expected_suites if not (out / f"{name}.code").exists()]

report = {
    "generatedAt": __import__("datetime").datetime.now(__import__("datetime").timezone.utc).isoformat(),
    "source": "tools/release/test-report.sh",
    "commit": commit,
    "statusVocabulary": ["passed", "skipped", "not-executed", "unparsed", "failed"],
    "suites": suites,
    "totals": {
        "passed": sum(s["passed"] or 0 for s in suites),
        "skipped": sum(s["skipped"] for s in suites),
        "failedSuites": [s["suite"] for s in suites if s["exitCode"] != 0],
        "unparsedSuites": [s["suite"] for s in suites if s["passed"] is None],
        "notExecutedSuites": not_executed,
    },
    "environmentBlockers": [],
    "windowsUnc": unc_evidence,
    "sqlServer": sqlserver_evidence,
    "mySql": mysql_evidence,
    "acceptance": [
        {"requirement": "real-browser designer E2E", "status": "passed" if next(s for s in suites if s["suite"] == "industrial-browser-e2e")["exitCode"] == 0 else "failed", "evidence": "industrial-browser-e2e.log"},
        {"requirement": "blank-environment browser-only full flow (resources, graph, run, failure injection, repair, retry)", "status": "passed" if next(s for s in suites if s["suite"] == "industrial-user-flow")["exitCode"] == 0 else "failed", "evidence": "industrial-user-flow.log; industrial/web/e2e/full-flow.spec.ts"},
        {"requirement": "browser user stories: create workflow, configure/test resource, manage credential, read execution center and retry action", "status": "passed" if next(s for s in suites if s["suite"] == "industrial-browser-e2e")["exitCode"] == 0 else "failed", "evidence": "industrial-browser-e2e.log; industrial/web/e2e/user-stories.spec.ts"},
        {"requirement": "schedule trigger configuration and automatic cron run", "status": "passed" if next(s for s in suites if s["suite"] == "industrial-browser-e2e")["exitCode"] == 0 else "failed", "evidence": "industrial-browser-e2e.log; industrial/web/e2e/user-stories.spec.ts"},
        {"requirement": "backfill preview before execution", "status": "passed" if next(s for s in suites if s["suite"] == "industrial-browser-e2e")["exitCode"] == 0 else "failed", "evidence": "industrial-browser-e2e.log; industrial/web/e2e/user-stories.spec.ts"},
        {"requirement": "CSV imports for machines, file profiles and bindings with schedule/manual trigger policy persisted across restart", "status": "passed" if next(s for s in suites if s["suite"] == "industrial-dotnet")["exitCode"] == 0 and next(s for s in suites if s["suite"] == "industrial-browser-e2e")["exitCode"] == 0 else "failed", "evidence": "industrial-dotnet.log; ConfigurationImportTests.CsvImportsTriggerPolicyVersionUpgradeAndRestartPersist; industrial-browser-e2e.log; industrial/web/e2e/scale-e2e.spec.ts"},
        {"requirement": "80 machines x 10 file profiles generate 800 bindings, not 800 workflows, with pause/backfill/diagnostics in the browser", "status": "passed" if next(s for s in suites if s["suite"] == "industrial-browser-e2e")["exitCode"] == 0 and next(s for s in suites if s["suite"] == "industrial-dotnet")["exitCode"] == 0 else "failed", "evidence": "industrial-browser-e2e.log; industrial/web/e2e/scale-e2e.spec.ts; industrial-dotnet.log; ConfigurationImportTests.BindingBackfillRunsThePublishedTemplateWithBoundResources; CollectionBindingTests"},
        {"requirement": "800 binding cron occurrences dispatch with a bounded concurrency limit and isolated per-binding failures", "status": "passed" if next(s for s in suites if s["suite"] == "industrial-dotnet")["exitCode"] == 0 else "failed", "evidence": "industrial-dotnet.log; BindingSchedulerTests.EightHundredBindingsScheduleWithBoundedConcurrencyAndIsolatedFailure; industrial/binding-scheduler-status"},
        {"requirement": "template version impact control upgrades existing bindings only to published versions and survives restart", "status": "passed" if next(s for s in suites if s["suite"] == "industrial-dotnet")["exitCode"] == 0 and next(s for s in suites if s["suite"] == "industrial-browser-e2e")["exitCode"] == 0 else "failed", "evidence": "industrial-dotnet.log; ConfigurationImportTests.CsvImportsTriggerPolicyVersionUpgradeAndRestartPersist; industrial-browser-e2e.log; industrial/web/e2e/user-stories.spec.ts"},
        {"requirement": "file-level processing detail and classified failure diagnosis", "status": "passed" if next(s for s in suites if s["suite"] == "industrial-browser-e2e")["exitCode"] == 0 else "failed", "evidence": "industrial-browser-e2e.log; industrial/web/e2e/user-stories.spec.ts"},
        {"requirement": "node configuration reaches runtime (CSV delimiter/header/id/time; SQL write strategy)", "status": "passed" if next(s for s in suites if s["suite"] == "industrial-dotnet")["exitCode"] == 0 else "failed", "evidence": "industrial-dotnet.log"},
        {"requirement": "resource edit preserves connection config and credential binding", "status": "passed" if next(s for s in suites if s["suite"] == "industrial-browser-e2e")["exitCode"] == 0 else "failed", "evidence": "industrial-browser-e2e.log; IndustrialUiPluginTests"},
        {"requirement": "delivery retry reuses published mapping and validation", "status": "passed" if next(s for s in suites if s["suite"] == "industrial-dotnet")["exitCode"] == 0 else "failed", "evidence": "industrial-dotnet.log; CrashRecoveryTests.DeliveryRetryReusesPublishedMappingAndValidation"},
        {"requirement": "multi-run retry resolves delivery→run→Elsa instance and never reuses another run's stages", "status": "passed" if next(s for s in suites if s["suite"] == "industrial-dotnet")["exitCode"] == 0 else "failed", "evidence": "industrial-dotnet.log; CrashRecoveryTests.DeliveryRetryResumesTheRunThatOwnsTheFailedDelivery (two different files, first run's delivery and rows unchanged)"},
        {"requirement": "schedule fires separate claim from completion, compensate missed fires and recover failed fires", "status": "passed" if next(s for s in suites if s["suite"] == "industrial-dotnet")["exitCode"] == 0 else "failed", "evidence": "industrial-dotnet.log; SchedulerRecoveryTests.MissedScheduleFiresAreRetriedAfterFailureAndRecovery; DeliveryLeaseTests.ScheduleFireLifecycleSeparatesClaimCompletionAndRecovery"},
        {"requirement": "G6 native gestures: palette drag-drop, port drag connect, node move, canvas pan, save/reload", "status": "passed" if next(s for s in suites if s["suite"] == "industrial-browser-e2e")["exitCode"] == 0 else "failed", "evidence": "industrial-browser-e2e.log; industrial/web/e2e/designer-gestures.spec.ts"},
        {"requirement": "schedule fire claim is durable and at-most-once across restarts", "status": "passed" if next(s for s in suites if s["suite"] == "industrial-dotnet")["exitCode"] == 0 else "failed", "evidence": "industrial-dotnet.log; DeliveryLeaseTests.ScheduleFireIsClaimedAtMostOnceAcrossRestarts"},
        {"requirement": "backfill preview uses published sink target set", "status": "passed" if next(s for s in suites if s["suite"] == "industrial-dotnet")["exitCode"] == 0 else "failed", "evidence": "industrial-dotnet.log; ElsaPocTests.BackfillPreviewUsesPublishedSinkTargets"},
        {"requirement": "newly added sink backfills historical files without duplicating existing targets", "status": "passed" if next(s for s in suites if s["suite"] == "industrial-dotnet")["exitCode"] == 0 else "failed", "evidence": "industrial-dotnet.log; CrashRecoveryTests.PublishANewSinkBackfillsOnlyThatTargetForHistoricalFiles"},
        {"requirement": "shared sink write options (writeMode/businessKey) reach all database sinks", "status": "passed" if next(s for s in suites if s["suite"] == "industrial-dotnet")["exitCode"] == 0 else "failed", "evidence": "industrial-dotnet.log; SinkWriteOptionsTests"},
        {"requirement": "Windows UNC/SMB execution on this macOS host", "status": "not-executed", "evidence": "external windows-latest runner row below is the acceptance evidence; local suite records an explicit skip"},
        {"requirement": "real packaged-plugin E2E", "status": "passed" if next(s for s in suites if s["suite"] == "industrial-packaged-e2e")["exitCode"] == 0 else "failed", "evidence": "industrial-packaged-e2e.log"},
        {"requirement": "PostgreSQL integration", "status": "passed" if next(s for s in suites if s["suite"] == "industrial-postgresql")["exitCode"] == 0 else "failed", "evidence": "industrial-postgresql.log"},
        {"requirement": "SQL Server real-server integration", "status": sqlserver_evidence.get("status", "not-executed"), "passingResult": sqlserver_evidence.get("url") if sqlserver_evidence.get("status") == "passed" else None, "evidence": "artifacts/sqlserver-result.json; " + sqlserver_evidence.get("url", "artifacts/sqlserver-blocker.md"), "runner": "ubuntu-latest", "script": ".github/workflows/database-evidence.yml"},
        {"requirement": "MySQL real-server integration", "status": mysql_evidence.get("status", "not-executed"), "passingResult": mysql_evidence.get("url") if mysql_evidence.get("status") == "passed" else None, "evidence": "artifacts/mysql-result.json; " + mysql_evidence.get("url", "artifacts/mysql-blocker.md"), "runner": "ubuntu-latest", "script": ".github/workflows/database-evidence.yml"},
        {"requirement": "Windows UNC/SMB integration", "status": unc_evidence.get("status", "blocked"), "passingResult": unc_evidence.get("url") if unc_evidence.get("status") == "passed" else None, "evidence": "artifacts/windows-unc-result.json; " + unc_evidence.get("url", "artifacts/windows-unc-blocker.md"), "runner": "windows-latest", "script": "industrial/e2e-windows-unc.ps1"},
        {"requirement": "real OS process kill mid-write resumes original instance and retries only incomplete delivery", "status": "passed" if next(s for s in suites if s["suite"] == "industrial-dotnet")["exitCode"] == 0 else "failed", "evidence": "industrial-dotnet.log; industrial/tests/Industrial.ProcessCrashFixture"},
        {"requirement": "two-target mid-write crash resumes original instance and retries only incomplete delivery", "status": "passed" if next(s for s in suites if s["suite"] == "industrial-dotnet")["exitCode"] == 0 else "failed", "evidence": "industrial-dotnet.log"},
        {"requirement": "concurrent claims, lease renewal and staged-file identity guard", "status": "passed" if next(s for s in suites if s["suite"] == "industrial-dotnet")["exitCode"] == 0 else "failed", "evidence": "industrial-dotnet.log"},
        {"requirement": "ledger claim/lease/retry state machine", "status": "passed" if next(s for s in suites if s["suite"] == "industrial-dotnet")["exitCode"] == 0 else "failed", "evidence": "industrial-dotnet.log"},
        {"requirement": "stage output resume and EF migrations", "status": "passed" if next(s for s in suites if s["suite"] == "industrial-dotnet")["exitCode"] == 0 else "failed", "evidence": "industrial-dotnet.log"},
        {"requirement": "disconnect and large-file recovery", "status": "passed" if next(s for s in suites if s["suite"] == "industrial-dotnet")["exitCode"] == 0 else "failed", "evidence": "industrial-dotnet.log"},
        {"requirement": "unified test report", "status": "passed", "evidence": "tools/release/test-report.sh"},
    ],
}
(out / "report.json").write_text(json.dumps(report, indent=2, ensure_ascii=False) + "\n")
lines = ["# Local test report", "", f"Generated by `{report['source']}`.", "",
         f"Tested commit: `{report['commit']}`", "",
         "| Suite | Execution | Count | Passed | Skipped |", "|---|---|---|---:|---:|"]
for s in suites:
    passed = "unknown" if s["passed"] is None else str(s["passed"])
    lines.append(f"| {s['suite']} | {s['executionStatus']} | {s['countStatus']} | {passed} | {s['skipped']} |")
lines += ["", f"Total passed: **{report['totals']['passed']}**, skipped: **{report['totals']['skipped']}**.",
          f"Not executed: {', '.join(report['totals']['notExecutedSuites']) or 'none'}.",
          f"Unparsed suites (exit-code gated): {', '.join(report['totals']['unparsedSuites']) or 'none'}.",
          "", "| Acceptance requirement | Status | Evidence |", "|---|---|---|"]
for item in report["acceptance"]:
    lines.append(f"| {item['requirement']} | {item['status']} | {item['evidence']} |")
lines += ["", "Environment blockers:"]
lines += [f"- {item}" for item in report["environmentBlockers"]] or ["- none"]
(out / "report.md").write_text("\n".join(lines) + "\n")
print(json.dumps(report["totals"], ensure_ascii=False))
PY
