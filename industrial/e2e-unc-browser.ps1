# Windows-only product E2E: provision a real SMB share with two accounts,
# start the generic packaged platform host with a blank store, then drive the
# UNC resource story through a real Chromium browser.
$ErrorActionPreference = "Stop"

if (-not $IsWindows) { throw "Windows UNC browser acceptance requires a Windows runner" }

$suffix = [guid]::NewGuid().ToString("N").Substring(0, 6)
$shareName = "gocordis-unc-ui-$suffix"
$userA = "gui_a_$suffix"
$userB = "gui_b_$suffix"
$passwordA = "Gocordis!A$suffix!42"
$passwordB = "Gocordis!B$suffix!42"
$wrongPassword = "Definitely!Wrong$suffix"
$sharePath = Join-Path $env:TEMP $shareName
$out = Join-Path $env:TEMP "gocordis-unc-browser-$suffix"
# Git Bash needs an MSYS drive path, not a Windows C:\ path.
$outPosix = "/" + $out.Substring(0, 1).ToLower() + ($out.Substring(2) -replace '\\', '/')
$db = Join-Path $out "unc-browser.db"
$url = "http://127.0.0.1:8192"
$hostProcess = $null

function New-Fixture {
    New-Item -ItemType Directory -Path $sharePath -Force | Out-Null
    New-Item -ItemType Directory -Path $out -Force | Out-Null
    Set-Content -Path (Join-Path $sharePath "recent.csv") -Value "record_id,reading`n1,42`n2,43`n"
    foreach ($account in @(@($userA, $passwordA), @($userB, $passwordB))) {
        $secure = ConvertTo-SecureString $account[1] -AsPlainText -Force
        New-LocalUser -Name $account[0] -Password $secure -PasswordNeverExpires | Out-Null
        icacls $sharePath /grant "$($account[0]):(OI)(CI)F" /T | Out-Null
    }
    New-SmbShare -Name $shareName -Path $sharePath `
        -FullAccess $userA,$userB | Out-Null
    # No drive/session mapping may exist before the browser flow: the
    # product must establish the SMB session from the configured credential.
    $preexisting = Get-SmbMapping -ErrorAction SilentlyContinue |
        Where-Object { $_.RemotePath -eq "\\localhost\$shareName" }
    if ($preexisting) { throw "pre-existing SMB mapping would invalidate the credential-session story" }
}

try {
    New-Fixture

    $env:GOCORDIS_UNC_PATH = "\\localhost\$shareName"
    $env:GOCORDIS_UNC_USER = $userA
    $env:GOCORDIS_UNC_PASSWORD = $passwordA
    $env:GOCORDIS_UNC_DOMAIN = ""
    $env:GOCORDIS_UNC_ROTATED_USER = $userB
    $env:GOCORDIS_UNC_ROTATED_PASSWORD = $passwordB
    $env:GOCORDIS_UNC_WRONG_PASSWORD = $wrongPassword
    $env:GOCORDIS_UNC_BROWSER_DB = $db
    $env:DOTNET_ROLL_FORWARD = "LatestMajor"
    $env:GOCORDIS_INDUSTRIAL_ROOT = (Join-Path $out "runtime")
    $env:GOCORDIS_INDUSTRIAL_ALLOW_DEV_KEY = "true"
    $env:GOCORDIS_INDUSTRIAL_SEED_RESOURCE = "false"
    $env:E2E_BASE_URL = $url

    Push-Location (Split-Path -Parent $PSScriptRoot)
    try {
        Push-Location "industrial/web"
        try {
            npm ci
            if ($LASTEXITCODE -ne 0) { throw "npm ci failed" }
            npm run build
            if ($LASTEXITCODE -ne 0) { throw "web build failed" }
            npx playwright install chromium
            if ($LASTEXITCODE -ne 0) { throw "playwright install failed" }
        } finally { Pop-Location }

        bash ./industrial/pack.sh $outPosix
        if ($LASTEXITCODE -ne 0) { throw "plugin packaging failed" }

        $arguments = @(
            "./industrial/src/Industrial.PackagedHost/bin/Debug/net10.0/Industrial.PackagedHost.dll",
            (Join-Path $out "gocordis.app.json"), (Join-Path $out "store"), $url
        )
        $hostProcess = Start-Process -FilePath "dotnet" -ArgumentList $arguments `
            -PassThru -RedirectStandardOutput (Join-Path $out "host.log") `
            -RedirectStandardError (Join-Path $out "host.err.log")

        $ready = $false
        for ($attempt = 0; $attempt -lt 60; $attempt++) {
            Start-Sleep -Seconds 1
            try {
                $modules = (Invoke-WebRequest -UseBasicParsing "$url/api/ui/client-modules" -TimeoutSec 3).Content
                if ($modules -match "industrial-designer") { $ready = $true; break }
            } catch { }
        }
        if (-not $ready) {
            Get-Content (Join-Path $out "host.log") -Tail 40
            Get-Content (Join-Path $out "host.err.log") -Tail 40
            throw "packaged host did not become ready"
        }

        Push-Location "industrial/web"
        try {
            npx playwright test e2e/unc-browser-e2e.spec.ts
            if ($LASTEXITCODE -ne 0) { throw "UNC browser E2E failed with exit code $LASTEXITCODE" }
        } finally { Pop-Location }
    } finally { Pop-Location }
} finally {
    if ($hostProcess) { Stop-Process -Id $hostProcess.Id -Force -ErrorAction SilentlyContinue }
    Remove-SmbShare -Name $shareName -Force -ErrorAction SilentlyContinue
    foreach ($name in @($userA, $userB)) {
        Remove-LocalUser -Name $name -ErrorAction SilentlyContinue
    }
    Remove-Item -Path $sharePath -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item -Path $out -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host "==> Windows UNC browser E2E passed"
