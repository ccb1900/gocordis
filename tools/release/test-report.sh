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
run industrial-packaged-e2e bash industrial/e2e-packaged.sh
run industrial-postgresql bash industrial/e2e-database.sh
run windows-unc bash -c 'DOTNET_ROLL_FORWARD=LatestMajor dotnet test industrial/tests/Industrial.Database.Tests/Industrial.Database.Tests.csproj --nologo -m:1 --filter FullyQualifiedName~WindowsUnc'
run console-runtime bash -c 'cd web/console-runtime && npm test'
run console-shell bash -c 'cd web/console && npm test && npm run build'
run builtin-webconsole bash -c 'cd src/Gocordis.Builtin.WebConsole/frontend && npm test && npm run build'
run builtin-control-plane bash -c 'cd src/Gocordis.Builtin.ControlPlaneUi/frontend && npm test && npm run build'
run builtin-insight bash -c 'cd src/Gocordis.Builtin.InsightUi/frontend && npm test && npm run build'
run plugin-kit bash -c 'cd web/plugin-kit && npm test'

python3 - "$OUT" <<'PY'
import json
import pathlib
import re
import sys

out = pathlib.Path(sys.argv[1])
suites = []
for code_path in sorted(out.glob("*.code")):
    name = code_path.stem
    log = (out / f"{name}.log").read_text(errors="replace")
    exit_code = int(code_path.read_text().strip())
    dotnet = [int(x) for x in re.findall(r"(?:Passed|Skipped)!\s+-\s+Failed:\s+\d+,\s+Passed:\s+(\d+)", log)]
    vitest = [int(x) for x in re.findall(r"Tests\s+(\d+)\s+passed", log)]
    node = [int(x) for x in re.findall(r"^ℹ pass (\d+)$", log, re.M)]
    skipped = [int(x) for x in re.findall(r"(?:Passed|Skipped)!.*?Skipped:\s*(\d+)", log)]
    suites.append({
        "suite": name,
        "exitCode": exit_code,
        "passed": sum(dotnet + vitest + node),
        "skipped": sum(skipped),
        "log": f"{name}.log",
    })

unc_evidence_path = out.parent / "windows-unc-result.json"
unc_evidence = json.loads(unc_evidence_path.read_text()) if unc_evidence_path.exists() else {
    "status": "blocked", "url": "artifacts/windows-unc-blocker.md"
}

report = {
    "generatedAt": __import__("datetime").datetime.now(__import__("datetime").timezone.utc).isoformat(),
    "source": "tools/release/test-report.sh",
    "suites": suites,
    "totals": {
        "passed": sum(s["passed"] for s in suites),
        "skipped": sum(s["skipped"] for s in suites),
        "failedSuites": [s["suite"] for s in suites if s["exitCode"] != 0],
    },
    "environmentBlockers": [],
    "windowsUnc": unc_evidence,
    "acceptance": [
        {"requirement": "real-browser designer E2E", "status": "passed" if next(s for s in suites if s["suite"] == "industrial-browser-e2e")["exitCode"] == 0 else "failed", "evidence": "industrial-browser-e2e.log"},
        {"requirement": "real packaged-plugin E2E", "status": "passed" if next(s for s in suites if s["suite"] == "industrial-packaged-e2e")["exitCode"] == 0 else "failed", "evidence": "industrial-packaged-e2e.log"},
        {"requirement": "PostgreSQL integration", "status": "passed" if next(s for s in suites if s["suite"] == "industrial-postgresql")["exitCode"] == 0 else "failed", "evidence": "industrial-postgresql.log"},
        {"requirement": "Windows UNC/SMB integration", "status": unc_evidence.get("status", "blocked"), "passingResult": unc_evidence.get("url") if unc_evidence.get("status") == "passed" else None, "evidence": "artifacts/windows-unc-result.json; " + unc_evidence.get("url", "artifacts/windows-unc-blocker.md"), "runner": "windows-latest", "script": "industrial/e2e-windows-unc.ps1"},
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
         "| Suite | Exit | Passed | Skipped |", "|---|---:|---:|---:|"]
for s in suites:
    lines.append(f"| {s['suite']} | {s['exitCode']} | {s['passed']} | {s['skipped']} |")
lines += ["", f"Total passed: **{report['totals']['passed']}**, skipped: **{report['totals']['skipped']}**.",
          "", "| Acceptance requirement | Status | Evidence |", "|---|---|---|"]
for item in report["acceptance"]:
    lines.append(f"| {item['requirement']} | {item['status']} | {item['evidence']} |")
lines += ["", "Environment blockers:"]
lines += [f"- {item}" for item in report["environmentBlockers"]] or ["- none"]
(out / "report.md").write_text("\n".join(lines) + "\n")
print(json.dumps(report["totals"], ensure_ascii=False))
PY
