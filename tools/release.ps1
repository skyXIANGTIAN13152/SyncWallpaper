param([switch]$SelfContained)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$dotnet = Join-Path $root "work\dotnet-sdk\dotnet.exe"
if (-not (Test-Path -LiteralPath $dotnet)) { $dotnet = "dotnet" }

& $dotnet restore (Join-Path $root "SyncWallpaper.sln")
& $dotnet build (Join-Path $root "SyncWallpaper.sln") -c Debug --no-restore
if ($LASTEXITCODE -ne 0) { throw "Debug 构建失败。" }
& $dotnet test (Join-Path $root "SyncWallpaper.sln") -c Debug --no-build
if ($LASTEXITCODE -ne 0) { throw "Debug 测试失败。" }
& $dotnet build (Join-Path $root "SyncWallpaper.sln") -c Release --no-restore
if ($LASTEXITCODE -ne 0) { throw "Release 构建失败。" }
& $dotnet test (Join-Path $root "SyncWallpaper.sln") -c Release --no-build
if ($LASTEXITCODE -ne 0) { throw "Release 测试失败。" }
& (Join-Path $root "publish.ps1") -SelfContained:$SelfContained
if ($LASTEXITCODE -ne 0) { throw "发布包构建失败。" }
Write-Host "完成：发布包只包含用户主动下载/安装所需文件，不包含 Updater.exe。"
