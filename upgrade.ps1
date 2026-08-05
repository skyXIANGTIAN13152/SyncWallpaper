param(
    [string]$PackagePath = $PSScriptRoot,
    [string]$InstallRoot = (Join-Path $env:LOCALAPPDATA "Programs\SyncWallpaper")
)
$ErrorActionPreference = "Stop"
& (Join-Path $PSScriptRoot "install.ps1") -PackagePath $PackagePath -InstallRoot $InstallRoot
Write-Host "升级完成；用户配置、壁纸资产和现有开机自启选择均保留。"
