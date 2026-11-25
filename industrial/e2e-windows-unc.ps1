# Requires a Windows runner with a real SMB share. The test itself uses
# `net use` through UncConnectionLease; no Linux/macOS substitute is accepted.
$ErrorActionPreference = "Stop"

$required = @("GOCORDIS_UNC_PATH", "GOCORDIS_UNC_USER", "GOCORDIS_UNC_PASSWORD")
foreach ($name in $required) {
    if ([string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($name))) {
        throw "$name must be configured for the Windows UNC integration test"
    }
}

$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    dotnet test industrial/tests/Industrial.Database.Tests/Industrial.Database.Tests.csproj `
        --nologo -m:1 --filter FullyQualifiedName~WindowsUnc
    if ($LASTEXITCODE -ne 0) {
        throw "WindowsUncIntegrationTests failed with exit code $LASTEXITCODE"
    }
} finally {
    Pop-Location
}
