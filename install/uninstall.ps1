#!/usr/bin/env pwsh
# uninstall.ps1 — Remove .NET SDK Manager (dsm) from Windows
#
# Usage:
#   irm https://raw.githubusercontent.com/shahabganji/dotnet-sdk-tui/main/install/uninstall.ps1 | iex

$ErrorActionPreference = 'Stop'

$BinaryName = 'dsm'
$InstallDir = Join-Path $env:LOCALAPPDATA $BinaryName
$TargetPath = Join-Path $InstallDir "$BinaryName.exe"

if (Test-Path $TargetPath) {
    Remove-Item -LiteralPath $TargetPath -Force
    Write-Host "Removed $TargetPath" -ForegroundColor Green

    # Remove from user PATH
    $userPath = [Environment]::GetEnvironmentVariable('Path', 'User')
    if ($userPath) {
        $norm = $InstallDir.TrimEnd('\\')
        $entries = ($userPath -split ';') | Where-Object { $_.TrimEnd('\\') -ine $norm -and $_ -ne '' }
        $newPath = $entries -join ';'
        [Environment]::SetEnvironmentVariable('Path', $newPath, 'User')
        Write-Host "Removed $InstallDir from user PATH" -ForegroundColor Green
    }

    # Remove install dir if empty
    if ((Get-ChildItem -Path $InstallDir -Force -ErrorAction SilentlyContinue | Measure-Object).Count -eq 0) {
        Remove-Item -LiteralPath $InstallDir -Force -ErrorAction SilentlyContinue
    }

    Write-Host 'dsm has been uninstalled.' -ForegroundColor Green
} else {
    Write-Host "dsm not found at $TargetPath" -ForegroundColor Red
    exit 1
}
