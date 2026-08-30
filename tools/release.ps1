param([switch]$SelfContained)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$dotnet = Join-Path $root "work\dotnet-sdk\dotnet.exe"
if (-not (Test-Path -LiteralPath $dotnet)) { $dotnet = "dotnet" }

& $dotnet restore (Join-Path $root "SyncWallpaper.sln")
& $dotnet build (Join-Path $root "SyncWallpaper.sln") -c Debug --no-restore
if ($LASTEXITCODE -ne 0) { throw "Debug build failed." }
& $dotnet test (Join-Path $root "SyncWallpaper.sln") -c Debug --no-build
if ($LASTEXITCODE -ne 0) { throw "Debug tests failed." }
& $dotnet build (Join-Path $root "SyncWallpaper.sln") -c Release --no-restore
if ($LASTEXITCODE -ne 0) { throw "Release build failed." }
& $dotnet test (Join-Path $root "SyncWallpaper.sln") -c Release --no-build
if ($LASTEXITCODE -ne 0) { throw "Release tests failed." }
& (Join-Path $root "publish.ps1") -SelfContained:$SelfContained
if ($LASTEXITCODE -ne 0) { throw "Release package build failed." }
Write-Host "Done: the package contains only files required for user-initiated download and installation; Updater.exe is not included."
