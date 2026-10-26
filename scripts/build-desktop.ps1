# Builds the Windows desktop edition into artifacts\desktop (one folder, runs without installing .NET).
#   .\scripts\build-desktop.ps1                # build only
#   .\scripts\build-desktop.ps1 -Version 0.1.0 -Pack   # also make the installer with Velopack (dotnet tool install -g vpk)
param([string]$Version = "0.1.0", [switch]$Pack)
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$out  = Join-Path $root "artifacts\desktop"
if (Test-Path $out) { Remove-Item $out -Recurse -Force }

# 1) the web app, unchanged. ReadyToRun = faster start. No trimming: Razor and EF Core use reflection.
dotnet publish "$root\src\LeagueTeamBuilder\LeagueTeamBuilder.csproj" -c Release -r win-x64 --self-contained true `
  -p:PublishReadyToRun=true -p:Version=$Version -o $out
if ($LASTEXITCODE) { throw "web publish failed" }

# 2) the window, into the SAME folder, so both share one copy of the .NET runtime (about half the size).
dotnet publish "$root\src\LeagueTeamBuilder.Desktop\LeagueTeamBuilder.Desktop.csproj" -c Release -r win-x64 --self-contained true `
  -p:PublishReadyToRun=true -p:Version=$Version -o $out
if ($LASTEXITCODE) { throw "desktop publish failed" }

if (-not (Test-Path "$out\LeagueTeamBuilder.exe") -or -not (Test-Path "$out\LeagueTeamBuilderDesktop.exe")) { throw "an exe is missing from $out" }
Write-Host "Built: $out"

if ($Pack) {
  vpk pack --packId LeagueTeamBuilder --packVersion $Version --packDir $out --mainExe LeagueTeamBuilderDesktop.exe `
    --packTitle "League Team Builder" --outputDir "$root\artifacts\installer"
  if ($LASTEXITCODE) { throw "vpk pack failed" }
  Write-Host "Installer: $root\artifacts\installer"
}
