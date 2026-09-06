param(
    [string]$InstallRoot = (Join-Path $env:LOCALAPPDATA "Programs\SyncWallpaper"),
    [switch]$KeepData
)
$ErrorActionPreference = "Stop"
$target = [IO.Path]::GetFullPath($InstallRoot)
$allowedRoot = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA "Programs"))
if (-not $target.StartsWith($allowedRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw "The uninstall directory must be under the current user's LocalAppData\Programs folder." }
$targetPrefix = $target.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
foreach ($name in @("SyncWallpaper.App", "SyncWallpaper.Diagnostics")) {
    Get-Process -Name $name -ErrorAction SilentlyContinue | ForEach-Object {
        try {
            $processPath = $_.Path
            if (-not [string]::IsNullOrWhiteSpace($processPath) -and $processPath.StartsWith($targetPrefix, [StringComparison]::OrdinalIgnoreCase)) {
                Stop-Process -Id $_.Id -Force -ErrorAction Stop
            }
        } catch { Write-Warning ("Could not stop process " + $_.Id + "; program files may still be in use.") }
    }
}
$runKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"
Remove-ItemProperty -Path $runKey -Name "SyncWallpaper" -ErrorAction SilentlyContinue
foreach ($path in @(
    (Join-Path ([Environment]::GetFolderPath("Desktop")) "SyncWallpaper.lnk"),
    (Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\SyncWallpaper.lnk")
)) { if (Test-Path $path) { Remove-Item -LiteralPath $path -Force } }
if (Test-Path $target) {
    $resolved = (Resolve-Path $target).Path
    if ($resolved -ne $target) { throw "Could not verify the uninstall directory." }
    Remove-Item -LiteralPath $target -Recurse -Force
}
if (-not $KeepData) {
    $data = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA "SyncWallpaper"))
    if (Test-Path $data) {
        Write-Warning "Data directory $data was kept. Delete it manually after confirmation if you also want to remove configuration and wallpapers."
    }
}
Write-Host "Program files, startup entry and shortcuts were removed. Configuration and wallpaper assets are kept by default."
