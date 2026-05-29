#!/usr/bin/env pwsh
Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $repoRoot

dotnet run --project src/Mhs.Editor/Mhs.Editor.csproj
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
