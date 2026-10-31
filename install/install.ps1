#!/usr/bin/env pwsh
# install.ps1 — Install or update .NET SDK Manager (dsm) on Windows
#
# Usage:
#   irm https://raw.githubusercontent.com/shahabganji/dotnet-sdk-tui/main/install/install.ps1 | iex
#
# Environment overrides (parameters don't work with `irm | iex`):
#   DSM_INSTALL_DIR  Installation directory (default: %LOCALAPPDATA%\dsm)
#   DSM_VERSION      Release tag to install, e.g. v1.2.3 (default: latest)
#   DSM_ARCHIVE      Path to a local release .zip to install instead of downloading

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$BinaryBaseName = 'dsm'
$ArchiveName = 'dotnet-sdk-tui'
$RepoOwner = 'shahabganji'
$RepoName = 'dotnet-sdk-tui'
$InstallDir = if ($env:DSM_INSTALL_DIR) { $env:DSM_INSTALL_DIR } else { Join-Path $env:LOCALAPPDATA $BinaryBaseName }
$Version = $env:DSM_VERSION
$LocalArchive = $env:DSM_ARCHIVE
$StagingDir = Join-Path $InstallDir '.install-staging'

Write-Host ''
Write-Host '  .NET SDK Manager (dsm)' -ForegroundColor Green
Write-Host '  Install or update' -ForegroundColor DarkGray
Write-Host ''

function Get-Rid {
    # Try .NET RuntimeInformation first (pwsh 7+), fall back to env var (Windows PowerShell 5.1)
    $arch = $null
    try { $arch = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString() } catch {}
    if (-not $arch) { $arch = $env:PROCESSOR_ARCHITECTURE }

    switch ($arch) {
        { $_ -in 'X64', 'AMD64' }  { return 'win-x64' }
        { $_ -in 'Arm64', 'ARM64' } { return 'win-arm64' }
        default { throw "Unsupported architecture: $arch. Expected X64 or Arm64." }
    }
}

function Test-PathEntryPresent {
    param([string]$PathValue = '', [Parameter(Mandatory)]$Entry)
    $norm = $Entry.TrimEnd('\\')
    foreach ($item in ($PathValue -split ';')) {
        if ($item -and $item.TrimEnd('\\') -ieq $norm) { return $true }
    }
    return $false
}

$rid = Get-Rid
$releasePath = 'latest/download'
if ($Version) {
    if (-not $Version.StartsWith('v')) { $Version = "v$Version" }
    $releasePath = "download/$Version"
}
$downloadUrl = "https://github.com/$RepoOwner/$RepoName/releases/$releasePath/$ArchiveName-$rid.zip"
$archivePath = Join-Path $StagingDir "$ArchiveName-$rid.zip"
$targetPath = Join-Path $InstallDir "$BinaryBaseName.exe"

$action = if (Test-Path $targetPath) { 'Updating' } else { 'Installing' }

Write-Host "==> $action dsm ($rid)"
Write-Host "==> Target: $InstallDir"

New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null
if (Test-Path -LiteralPath $StagingDir) {
    Remove-Item -LiteralPath $StagingDir -Recurse -Force
}
New-Item -ItemType Directory -Path $StagingDir -Force | Out-Null

try {
    if ($LocalArchive) {
        if (-not (Test-Path -LiteralPath $LocalArchive -PathType Leaf)) { throw "Archive not found: $LocalArchive" }
        Write-Host "==> Using local archive $LocalArchive"
        Copy-Item -LiteralPath $LocalArchive -Destination $archivePath -Force
    } else {
        $label = if ($Version) { $Version } else { 'latest' }
        Write-Host "==> Downloading $label release"
        # Windows PowerShell 5.1 defaults to TLS 1.0/1.1, which GitHub rejects.
        [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
        $ProgressPreference = 'SilentlyContinue'
        Invoke-WebRequest -Uri $downloadUrl -OutFile $archivePath -UseBasicParsing
    }

    Write-Host '==> Extracting'
    Expand-Archive -LiteralPath $archivePath -DestinationPath $StagingDir -Force

    # Find the binary (could be dotnet-sdk-tui.exe in the archive)
    $sourceBinary = Get-ChildItem -Path $StagingDir -Recurse -File -Filter "$ArchiveName.exe" | Select-Object -First 1
    if (-not $sourceBinary) {
        $sourceBinary = Get-ChildItem -Path $StagingDir -Recurse -File -Filter "$ArchiveName" | Select-Object -First 1
    }
    if (-not $sourceBinary) { throw "Binary not found in archive." }

    # Install as dsm.exe
    Copy-Item -LiteralPath $sourceBinary.FullName -Destination $targetPath -Force

    # Ensure on PATH
    $userPath = [Environment]::GetEnvironmentVariable('Path', 'User')
    if (-not (Test-PathEntryPresent -PathValue $userPath -Entry $InstallDir)) {
        $updatedPath = if ([string]::IsNullOrWhiteSpace($userPath)) { $InstallDir } else { "$userPath;$InstallDir" }
        [Environment]::SetEnvironmentVariable('Path', $updatedPath, 'User')
        if (-not (Test-PathEntryPresent -PathValue $env:Path -Entry $InstallDir)) {
            $env:Path = "$InstallDir;$env:Path"
        }
        Write-Host "==> Added $InstallDir to user PATH"
    } else {
        Write-Host "==> $InstallDir is already on PATH"
    }

    Write-Host ''
    Write-Host "$action complete. Run 'dsm' to get started." -ForegroundColor Green
    Write-Host 'Tip: open a new terminal if PATH changes are not visible yet.' -ForegroundColor DarkGray
} finally {
    if (Test-Path -LiteralPath $StagingDir) {
        Remove-Item -LiteralPath $StagingDir -Recurse -Force
    }
}
