param(
    [string]$PackagePath = $PSScriptRoot,
    [string]$InstallRoot = (Join-Path $env:LOCALAPPDATA "Programs\SyncWallpaper"),
    [switch]$CreateShortcuts,
    [switch]$StartWithWindows
)
$ErrorActionPreference = "Stop"
$package = [IO.Path]::GetFullPath($PackagePath)
$target = [IO.Path]::GetFullPath($InstallRoot)
$allowedRoot = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA "Programs"))
if (-not $target.StartsWith($allowedRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw "The install directory must be under the current user's LocalAppData\Programs folder." }
if (-not (Test-Path (Join-Path $package "App\SyncWallpaper.App.exe"))) { throw "App\SyncWallpaper.App.exe was not found in the release package." }
New-Item -ItemType Directory -Force -Path $target | Out-Null
# This is a user-invoked installer only. The product never invokes this script.
# Configuration and wallpapers live outside the program directory and are not touched.
Copy-Item -Path (Join-Path $package "*") -Destination $target -Recurse -Force
if ($StartWithWindows) {
    $runKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"
    New-Item -Path $runKey -Force | Out-Null
    Set-ItemProperty -Path $runKey -Name "SyncWallpaper" -Value ('"' + (Join-Path $target "App\SyncWallpaper.App.exe") + '" --background')
}
if ($CreateShortcuts) {
    $shell = New-Object -ComObject WScript.Shell
    $desktop = [Environment]::GetFolderPath("Desktop")
    $start = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs"
    New-Item -ItemType Directory -Force -Path $start | Out-Null
    foreach ($link in @(
        @{ Path = (Join-Path $desktop "SyncWallpaper.lnk"); Description = "SyncWallpaper" },
        @{ Path = (Join-Path $start "SyncWallpaper.lnk"); Description = "SyncWallpaper" }
    )) {
        $shortcut = $shell.CreateShortcut($link.Path)
        $shortcut.TargetPath = Join-Path $target "App\SyncWallpaper.App.exe"
        $shortcut.WorkingDirectory = Join-Path $target "App"
        $shortcut.Description = $link.Description
        $shortcut.IconLocation = (Join-Path $target "App\SyncWallpaper.App.exe") + ",0"
        $shortcut.Save()
    }
}
Write-Host "Installed to $target. Windows display and power settings were not changed. Startup is controlled by -StartWithWindows."
