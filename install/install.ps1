#!/usr/bin/env pwsh
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# Installs the latest dotnet-sdk-tui release into the current user's local app folder.
$BinaryBaseName = 'dotnet-sdk-tui'
$RepoOwner = 'shahabganji'
$RepoName = 'dotnet-sdk-tui'
$InstallDir = Join-Path $env:LOCALAPPDATA $BinaryBaseName
$StagingDir = Join-Path $InstallDir '.install-staging'

function Write-Banner {
    Write-Host '✦────────────────────────────────────────────✦'
    Write-Host '★   dotnet-sdk-tui installer — Let''s-a go!   ★'
    Write-Host '✦   Silent steps. Sharp tools. Clean setup.  ✦'
    Write-Host '✦────────────────────────────────────────────✦'
}

# Maps the current Windows architecture to the release RID.
function Get-Rid {
    $architecture = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture

    switch ($architecture) {
        'X64' { return 'win-x64' }
        'Arm64' { return 'win-arm64' }
        default { throw "Unsupported Windows architecture: $architecture. Expected X64 or Arm64." }
    }
}

# Compares PATH entries case-insensitively while tolerating trailing backslashes.
function Test-PathEntryPresent {
    param(
        [AllowEmptyString()]
        [string]$PathValue = '',
        [Parameter(Mandatory = $true)]
        [string]$Entry
    )

    $normalizedEntry = $Entry.TrimEnd('\\')
    foreach ($item in ($PathValue -split ';')) {
        if ($item -and $item.TrimEnd('\\') -ieq $normalizedEntry) {
            return $true
        }
    }

    return $false
}

# Finds the published executable regardless of whether the zip contains a root folder.
function Get-BinaryPath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Root
    )

    foreach ($candidate in @("$BinaryBaseName.exe", $BinaryBaseName)) {
        $match = Get-ChildItem -Path $Root -Recurse -File -Filter $candidate | Select-Object -First 1
        if ($match) {
            return $match.FullName
        }
    }

    throw "Could not find $BinaryBaseName in the downloaded archive."
}

Write-Banner

$rid = Get-Rid
$downloadUrl = "https://github.com/$RepoOwner/$RepoName/releases/latest/download/$BinaryBaseName-$rid.zip"
$archivePath = Join-Path $StagingDir "$BinaryBaseName-$rid.zip"

Write-Host "==> Detected runtime: $rid"
Write-Host "==> Installing into $InstallDir"

New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null
if (Test-Path -LiteralPath $StagingDir) {
    Remove-Item -LiteralPath $StagingDir -Recurse -Force
}
New-Item -ItemType Directory -Path $StagingDir -Force | Out-Null

try {
    Write-Host '==> Downloading release archive'
    Invoke-WebRequest -Uri $downloadUrl -OutFile $archivePath

    Write-Host '==> Extracting archive'
    Expand-Archive -LiteralPath $archivePath -DestinationPath $StagingDir -Force

    $binaryPath = Get-BinaryPath -Root $StagingDir
    $targetPath = Join-Path $InstallDir ([System.IO.Path]::GetFileName($binaryPath))

    Write-Host "==> Installing $([System.IO.Path]::GetFileName($binaryPath))"
    Copy-Item -LiteralPath $binaryPath -Destination $targetPath -Force

    $userPath = [Environment]::GetEnvironmentVariable('Path', 'User')
    if (-not (Test-PathEntryPresent -PathValue $userPath -Entry $InstallDir)) {
        $updatedUserPath = if ([string]::IsNullOrWhiteSpace($userPath)) {
            $InstallDir
        }
        else {
            "$userPath;$InstallDir"
        }

        [Environment]::SetEnvironmentVariable('Path', $updatedUserPath, 'User')
        if (-not (Test-PathEntryPresent -PathValue $env:Path -Entry $InstallDir)) {
            $env:Path = "$InstallDir;$env:Path"
        }
        Write-Host "==> Added $InstallDir to your user PATH"
    }
    else {
        Write-Host "==> $InstallDir is already on your user PATH"
    }

    Write-Host ''
    Write-Host "✦ Installation complete. $BinaryBaseName is ready at $targetPath"
    Write-Host '★ Tip: open a new terminal if PATH changes are not visible yet.'
}
finally {
    if (Test-Path -LiteralPath $StagingDir) {
        Remove-Item -LiteralPath $StagingDir -Recurse -Force
    }
}
