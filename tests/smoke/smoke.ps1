# smoke.ps1 — Verify dsm installs, runs and uninstalls on Windows.
#
# Runs the real install/uninstall scripts the way users do (`irm ... | iex`) in a
# child shell, against a sandbox install directory (DSM_INSTALL_DIR).
# Note: the installer edits the *user* PATH; uninstall reverts it.
#
# Usage:
#   tests/smoke/smoke.ps1 -Phase install   [-Archive PATH | -Release [-Tag vX.Y.Z]] [-ExpectVersion X.Y.Z] [-Shell pwsh|powershell]
#   tests/smoke/smoke.ps1 -Phase uninstall [-Release] [-Shell ...]
#   tests/smoke/smoke.ps1 -Phase all ...
#
# Environment: SMOKE_HOME — sandbox dir (default: new temp dir; reuse across phases).
# On GitHub Actions the installed binary path is exported as DSM_E2E_BINARY.
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('install', 'uninstall', 'all')][string]$Phase,
    [string]$Archive,
    [switch]$Release,
    [string]$Tag,
    [string]$ExpectVersion,
    [ValidateSet('pwsh', 'powershell')][string]$Shell = 'pwsh',
    [string]$ScriptsUrl = $(if ($env:SMOKE_SCRIPTS_URL) { $env:SMOKE_SCRIPTS_URL } else { 'https://raw.githubusercontent.com/shahabganji/dotnet-sdk-tui/main/install' })
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Fail([string]$Message) { Write-Host "SMOKE FAIL: $Message" -ForegroundColor Red; exit 1 }
function Pass([string]$Message) { Write-Host "SMOKE OK:   $Message" -ForegroundColor Green }

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '../..')
if (-not $env:SMOKE_HOME) {
    $env:SMOKE_HOME = Join-Path ([IO.Path]::GetTempPath()) ("dsm-smoke-" + [Guid]::NewGuid().ToString('N'))
}
New-Item -ItemType Directory -Force -Path $env:SMOKE_HOME | Out-Null
$installDir = Join-Path $env:SMOKE_HOME 'dsm'
$binary = Join-Path $installDir 'dsm.exe'
$env:DSM_INSTALL_DIR = $installDir

function Test-UserPathHas([string]$Dir) {
    $userPath = [Environment]::GetEnvironmentVariable('Path', 'User')
    if (-not $userPath) { return $false }
    foreach ($item in ($userPath -split ';')) {
        if ($item -and $item.TrimEnd('\') -ieq $Dir.TrimEnd('\')) { return $true }
    }
    return $false
}

# Runs install.ps1/uninstall.ps1 through `iex` in a fresh child shell, like users do.
function Invoke-InstallScript([string]$Name) {
    if ($Release) {
        $command = "[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12; irm '$ScriptsUrl/$Name' | iex"
    } else {
        $path = Join-Path $repoRoot "install/$Name"
        $command = "Get-Content -Raw -LiteralPath '$path' | iex"
    }
    $output = & $Shell -NoProfile -ExecutionPolicy Bypass -Command $command 2>&1 | Out-String
    $code = $LASTEXITCODE
    Write-Host $output
    if ($code -ne 0) { Fail "$Name exited with code $code (shell: $Shell)" }
    return $output
}

function Invoke-Install {
    $env:DSM_ARCHIVE = $null
    $env:DSM_VERSION = $null
    if ($Release) {
        if ($Tag) { $env:DSM_VERSION = $Tag }
    } else {
        if (-not $Archive) { Fail '-Archive PATH or -Release is required' }
        if (-not (Test-Path -LiteralPath $Archive -PathType Leaf)) { Fail "archive not found: $Archive" }
        $env:DSM_ARCHIVE = (Resolve-Path -LiteralPath $Archive).Path
    }

    $out = Invoke-InstallScript 'install.ps1'
    if ($out -notmatch 'Installing dsm') { Fail "fresh install did not report 'Installing'" }
    if (-not (Test-Path -LiteralPath $binary -PathType Leaf)) { Fail "$binary missing" }
    Pass "installed $binary (via $Shell)"

    if (-not (Test-UserPathHas $installDir)) { Fail "user PATH does not contain $installDir" }
    Pass 'install dir added to user PATH'

    $out = Invoke-InstallScript 'install.ps1'
    if ($out -notmatch 'Updating dsm') { Fail "re-install did not report 'Updating'" }
    $count = @(([Environment]::GetEnvironmentVariable('Path', 'User') -split ';') | Where-Object { $_ -and $_.TrimEnd('\') -ieq $installDir }).Count
    if ($count -ne 1) { Fail "user PATH contains $installDir $count times" }
    Pass 're-install updates in place'

    $version = (& $binary --version 2>&1 | Out-String).Trim()
    if ($LASTEXITCODE -ne 0) { Write-Host $version; Fail "'dsm --version' exited with $LASTEXITCODE" }
    if ($version -notmatch '^dsm \d+\.\d+\.\d+') { Fail "unexpected --version output: $version" }
    if ($ExpectVersion -and $version -ne "dsm $($ExpectVersion.TrimStart('v'))") {
        Fail "expected 'dsm $($ExpectVersion.TrimStart('v'))', got '$version'"
    }
    Pass $version

    if ($env:GITHUB_ENV) {
        Add-Content -LiteralPath $env:GITHUB_ENV -Value "DSM_E2E_BINARY=$binary"
        Add-Content -LiteralPath $env:GITHUB_ENV -Value "SMOKE_HOME=$($env:SMOKE_HOME)"
    }
    Write-Host "SMOKE_HOME=$($env:SMOKE_HOME)"
}

function Stop-LeftoverDsm {
    # Terminals stopped by the E2E tests can leave dsm.exe exiting in the background; a running
    # exe is locked, so give it a moment, then force it — otherwise uninstall fails with access denied.
    for ($i = 0; $i -lt 20; $i++) {
        $running = @(Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.Path -and $_.Path -ieq $binary })
        if ($running.Count -eq 0) { return }
        if ($i -eq 10) {
            Write-Host "Stopping leftover dsm process(es): $($running.Id -join ', ')"
            $running | Stop-Process -Force -ErrorAction SilentlyContinue
        }
        Start-Sleep -Milliseconds 500
    }
    Fail "dsm.exe is still running from $binary"
}

function Invoke-Uninstall {
    Stop-LeftoverDsm
    Invoke-InstallScript 'uninstall.ps1' | Out-Null
    if (Test-Path -LiteralPath $binary) { Fail "$binary still exists" }
    if (Test-UserPathHas $installDir) { Fail "user PATH still contains $installDir" }
    Pass 'uninstalled'
}

switch ($Phase) {
    'install'   { Invoke-Install }
    'uninstall' { Invoke-Uninstall }
    'all'       { Invoke-Install; Invoke-Uninstall }
}
