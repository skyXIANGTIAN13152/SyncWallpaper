$ErrorActionPreference = "Stop"
$dotnet = Join-Path $PSScriptRoot "work\dotnet-sdk\dotnet.exe"
if (-not (Test-Path $dotnet)) { $dotnet = "dotnet" }
& $dotnet restore (Join-Path $PSScriptRoot "SyncWallpaper.sln")
& $dotnet build (Join-Path $PSScriptRoot "SyncWallpaper.sln") -c Release --no-restore
& $dotnet test (Join-Path $PSScriptRoot "SyncWallpaper.sln") -c Release --no-build
