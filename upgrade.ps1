param(
    [string]$PackagePath = $PSScriptRoot,
    [string]$InstallRoot = (Join-Path $env:LOCALAPPDATA "Programs\SyncWallpaper")
)
$ErrorActionPreference = "Stop"
& (Join-Path $PSScriptRoot "install.ps1") -PackagePath $PackagePath -InstallRoot $InstallRoot
Write-Host "Upgrade complete. User configuration, wallpaper assets and the current startup choice were preserved."
