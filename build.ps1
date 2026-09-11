# ============================================================
#  AI2P - build.ps1  (Windows)
#  Builds the whole solution: C# projects + (later) native C/C++
#  CMake subprojects (TZ v1.8, ch. 4.2).
#  Usage:  .\build.ps1 [-Configuration Debug|Release]
# ============================================================
param(
    [string]$Configuration = "Debug",
    [string]$Lang = "",
    [switch]$Help
)

$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

# --help is printed from a separate file (T-65-S0): i18n/help/build.<lang>.txt.
# The output of this script itself is tier B and stays in English.
$locFile = Join-Path $PSScriptRoot "i18n\loc.ps1"
if ($Help) {
    if (Test-Path -LiteralPath $locFile) { . $locFile; Set-Ai2pLang $Lang; Show-Ai2pHelp "build" }
    else { Write-Host "Usage: build.ps1 [-Configuration Debug|Release]" }
    exit 0
}

Write-Host "=== AI2P build ($Configuration) ===" -ForegroundColor Cyan

# 1. C# solution (dotnet restore is part of build)
dotnet build AI2P.sln -c $Configuration
if ($LASTEXITCODE -ne 0) {
    Write-Host "[ERROR] dotnet build failed." -ForegroundColor Red
    exit 1
}

# 2. native C/C++ plugins (CMake) - nothing yet, placeholder for native/

Write-Host "=== Build OK ===" -ForegroundColor Green
Write-Host "Run:  dotnet run --project src/AI2P.Server"
exit 0
