# Windows UNC/SMB acceptance evidence

Status: **passed on a real Windows runner**.

- Workflow: `.github/workflows/windows-unc.yml`
- Runner: `windows-latest`
- Script: `industrial/e2e-windows-unc.ps1`
- Test: `Industrial.Database.Tests.WindowsUncIntegrationTests.UncSourceDiscoversFilesThroughARealSessionLease`
- Passing run: https://github.com/ccb1900/gocordis/actions/runs/45739397237 (job `139640415120`, commit `00b39e6d5959842b967f234b7367b9d72f5a63fc`, conclusion `success`)

When external `GOCORDIS_UNC_*` secrets are not configured, the script provisions an ephemeral local SMB share and account on the Windows runner, sets the four `GOCORDIS_UNC_*` variables, and runs `WindowsUncIntegrationTests` against the real Windows SMB client/server through `UncConnectionLease`. No mock, fake native seam, or Linux/macOS substitute is involved. The share and account are removed in a `finally` block after the test.

This macOS host still cannot execute the test locally, so the local `windows-unc` suite row remains explicitly skipped; the acceptance status is driven by the external Windows runner evidence in `artifacts/windows-unc-result.json`.
