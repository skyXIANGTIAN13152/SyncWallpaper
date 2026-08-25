param(
    [string]$InstallRoot = (Join-Path $env:LOCALAPPDATA "Programs\SyncWallpaper"),
    [switch]$KeepData
)
$ErrorActionPreference = "Stop"
$target = [IO.Path]::GetFullPath($InstallRoot)
$allowedRoot = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA "Programs"))
if (-not $target.StartsWith($allowedRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw "卸载目录必须位于当前用户 LocalAppData\Programs 下。" }
$targetPrefix = $target.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
foreach ($name in @("SyncWallpaper.App", "SyncWallpaper.Diagnostics")) {
    Get-Process -Name $name -ErrorAction SilentlyContinue | ForEach-Object {
        try {
            $processPath = $_.Path
            if (-not [string]::IsNullOrWhiteSpace($processPath) -and $processPath.StartsWith($targetPrefix, [StringComparison]::OrdinalIgnoreCase)) {
                Stop-Process -Id $_.Id -Force -ErrorAction Stop
            }
        } catch { Write-Warning ("无法停止进程 " + $_.Id + "；程序文件可能仍被占用。") }
    }
}
$runKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"
Remove-ItemProperty -Path $runKey -Name "SyncWallpaper" -ErrorAction SilentlyContinue
foreach ($path in @(
    (Join-Path ([Environment]::GetFolderPath("Desktop")) "屏序 SyncWallpaper.lnk"),
    (Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\屏序 SyncWallpaper.lnk")
)) { if (Test-Path $path) { Remove-Item -LiteralPath $path -Force } }
if (Test-Path $target) {
    $resolved = (Resolve-Path $target).Path
    if ($resolved -ne $target) { throw "无法验证卸载目录。" }
    Remove-Item -LiteralPath $target -Recurse -Force
}
if (-not $KeepData) {
    $data = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA "SyncWallpaper"))
    if (Test-Path $data) {
        Write-Warning "已保留数据目录 $data。若要清理，请在确认后手动删除；卸载脚本不会删除配置或壁纸资产。"
    }
}
Write-Host "程序文件、启动项和快捷方式已移除；配置与壁纸资产默认保留。"
