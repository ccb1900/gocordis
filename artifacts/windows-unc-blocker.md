# Windows UNC/SMB acceptance blocker

The Windows UNC integration test cannot be executed from this session. Evidence:

- Host: Darwin 27.0.0 arm64 (`uname -a`), not Windows.
- `UncConnectionLease` requires the Windows SMB client (`net use`) and throws `PlatformNotSupportedException` off Windows.
- No local `pwsh`, `powershell.exe`, `wine`, `vmrun`, or Parallels tooling.
- No `GOCORDIS_UNC_PATH`, `GOCORDIS_UNC_USER`, `GOCORDIS_UNC_PASSWORD`, or `GOCORDIS_UNC_DOMAIN` in the environment.
- `gh auth status`: not logged into any GitHub hosts; the GitHub-hosted `windows-latest` workflow cannot be dispatched or inspected from here. A browser check of `https://github.com/` also showed **Sign in / Sign up** links and no logged-in session, so the workflow cannot be dispatched through the web UI either.
- Configured SSH hosts are not usable Windows runners: one timed out during banner exchange, the other failed strict host-key verification.

Acceptance path prepared but not executed:

- `.github/workflows/windows-unc.yml` runs on `windows-latest` with the four `GOCORDIS_UNC_*` secrets injected.
- `industrial/e2e-windows-unc.ps1` validates those variables and runs `WindowsUncIntegrationTests`.
- On this host the test records an explicit skip reason: "Windows UNC/SMB session lease requires Windows (net use)".

Status: blocked, incomplete. No passing result exists for this requirement.
