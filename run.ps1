$ErrorActionPreference = "Stop"
$dotnet = Join-Path $PSScriptRoot "work\dotnet-sdk\dotnet.exe"
if (-not (Test-Path $dotnet)) { $dotnet = "dotnet" }
& $dotnet run --project (Join-Path $PSScriptRoot "src\SyncWallpaper.App\SyncWallpaper.App.csproj") -c Release -- $args
