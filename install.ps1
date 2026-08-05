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
if (-not $target.StartsWith($allowedRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw "安装目录必须位于当前用户 LocalAppData\Programs 下。" }
if (-not (Test-Path (Join-Path $package "App\SyncWallpaper.App.exe"))) { throw "找不到发布包中的 App\SyncWallpaper.App.exe。" }
New-Item -ItemType Directory -Force -Path $target | Out-Null
$parent = Split-Path -Parent $target
$staging = Join-Path $parent ((Split-Path -Leaf $target) + ".new-" + ([Guid]::NewGuid().ToString("N")))
$previous = Join-Path $parent ((Split-Path -Leaf $target) + ".old-" + ([Guid]::NewGuid().ToString("N")))
New-Item -ItemType Directory -Force -Path $staging | Out-Null
$committed = $false
try {
    Copy-Item -Path (Join-Path $package "*") -Destination $staging -Recurse -Force
    if (Test-Path $target) { Move-Item -LiteralPath $target -Destination $previous -Force }
    Move-Item -LiteralPath $staging -Destination $target -Force
    if (Test-Path $previous) { Remove-Item -LiteralPath $previous -Recurse -Force }
    $committed = $true
} finally {
    if (Test-Path $staging) { Remove-Item -LiteralPath $staging -Recurse -Force }
    if (-not $committed -and (Test-Path $previous)) {
        if (Test-Path $target) { Remove-Item -LiteralPath $target -Recurse -Force }
        Move-Item -LiteralPath $previous -Destination $target -Force
    }
}
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
        @{ Path = (Join-Path $desktop "屏序 SyncWallpaper.lnk"); Description = "屏序 SyncWallpaper" },
        @{ Path = (Join-Path $start "屏序 SyncWallpaper.lnk"); Description = "屏序 SyncWallpaper" }
    )) {
        $shortcut = $shell.CreateShortcut($link.Path)
        $shortcut.TargetPath = Join-Path $target "App\SyncWallpaper.App.exe"
        $shortcut.WorkingDirectory = Join-Path $target "App"
        $shortcut.Description = $link.Description
        $shortcut.IconLocation = Join-Path $target "App\AppIcon.ico"
        $shortcut.Save()
    }
}
Write-Host "已安装到 $target；不会自动修改显示器、壁纸或电源。开机自启默认保持关闭。"
