$ErrorActionPreference = "Stop"
$dotnet = Join-Path $PSScriptRoot "work\dotnet-sdk\dotnet.exe"
if (-not (Test-Path $dotnet)) { $dotnet = "dotnet" }
& $dotnet build (Join-Path $PSScriptRoot "src\SyncWallpaper.Host\SyncWallpaper.Host.csproj") -c Release
& $dotnet run --project (Join-Path $PSScriptRoot "src\SyncWallpaper.App\SyncWallpaper.App.csproj") -c Release -- $args
