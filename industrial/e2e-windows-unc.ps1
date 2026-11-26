# Runs WindowsUncIntegrationTests against a real Windows SMB session.
# If GOCORDIS_UNC_* secrets are not configured, provision an ephemeral local
# SMB share on the Windows runner: it is still the real Windows SMB client and
# server, not a mock or a substituted fake native seam.
$ErrorActionPreference = "Stop"

$provisioned = $false
$shareName = $null
$userName = $null
$sharePath = $null

function New-LocalSmbFixture {
    if (-not [System.Environment]::IsWindows()) {
        throw "Windows UNC/SMB acceptance requires a Windows runner"
    }

    $suffix = [guid]::NewGuid().ToString("N").Substring(0, 12)
    $script:shareName = "gocordis-unc-$suffix"
    $script:userName = "gocordis_unc_$suffix"
    $password = "Gocordis!Unc$suffix!42"
    $script:sharePath = Join-Path $env:TEMP $script:shareName

    New-Item -ItemType Directory -Path $script:sharePath -Force | Out-Null
    Set-Content -Path (Join-Path $script:sharePath "recent.csv") `
        -Value "record_id,reading`n1,42`n"

    $secure = ConvertTo-SecureString $password -AsPlainText -Force
    New-LocalUser -Name $script:userName -Password $secure -PasswordNeverExpires | Out-Null
    icacls $script:sharePath /grant "$($script:userName):(OI)(CI)F" /T | Out-Null
    New-SmbShare -Name $script:shareName -Path $script:sharePath `
        -FullAccess $script:userName | Out-Null

    [Environment]::SetEnvironmentVariable("GOCORDIS_UNC_PATH", "\\localhost\$($script:shareName)", "Process")
    [Environment]::SetEnvironmentVariable("GOCORDIS_UNC_USER", $script:userName, "Process")
    [Environment]::SetEnvironmentVariable("GOCORDIS_UNC_PASSWORD", $password, "Process")
    [Environment]::SetEnvironmentVariable("GOCORDIS_UNC_DOMAIN", "", "Process")
    $script:provisioned = $true
    Write-Host "Provisioned real local SMB share \\localhost\$($script:shareName)"
}

try {
    $required = @("GOCORDIS_UNC_PATH", "GOCORDIS_UNC_USER", "GOCORDIS_UNC_PASSWORD")
    $missing = $required | Where-Object {
        [string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($_))
    }
    if ($missing) {
        New-LocalSmbFixture
    }

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
} finally {
    if ($provisioned) {
        Remove-SmbShare -Name $shareName -Force -ErrorAction SilentlyContinue
        Remove-LocalUser -Name $userName -ErrorAction SilentlyContinue
        Remove-Item -Path $sharePath -Recurse -Force -ErrorAction SilentlyContinue
    }
}
