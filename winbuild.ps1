#!/usr/bin/env pwsh
Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $repoRoot

$requiredDotnetMajor = 10

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
  Write-Error ".NET SDK $requiredDotnetMajor.0+ is required, but 'dotnet' was not found.`nInstall it from: https://dotnet.microsoft.com/download/dotnet/$requiredDotnetMajor.0"
  exit 1
}

$installedSdkLines = @(dotnet --list-sdks 2>$null)
$hasRequiredSdk = $false

foreach ($line in $installedSdkLines) {
  if ([string]::IsNullOrWhiteSpace($line)) { continue }
  $versionText = ($line -split '\s+')[0]
  $version = $null
  if ([Version]::TryParse($versionText, [ref]$version) -and $version.Major -ge $requiredDotnetMajor) {
    $hasRequiredSdk = $true
    break
  }
}

if (-not $hasRequiredSdk) {
  Write-Host ".NET SDK $requiredDotnetMajor.0+ is required to build this project."
  Write-Host "Installed SDKs:"
  if ($installedSdkLines.Count -gt 0) {
    $installedSdkLines | ForEach-Object { Write-Host "  $_" }
  } else {
    Write-Host "  (none found)"
  }
  Write-Host "Install .NET SDK $requiredDotnetMajor.0+ from: https://dotnet.microsoft.com/download/dotnet/$requiredDotnetMajor.0"
  exit 1
}

dotnet restore
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

dotnet build
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
