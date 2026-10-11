[CmdletBinding()]
param(
    [string]$InstallRoot = (Join-Path $env:LOCALAPPDATA "Programs\SyncWallpaper"),
    [switch]$KeepData
)
$ErrorActionPreference = "Stop"

function Remove-SyncWallpaperPackageFiles {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Target,
        [Parameter(Mandatory)][string]$AllowedRoot,
        [switch]$KeepData
    )

    $packageRoot = [IO.Path]::GetFullPath($Target).TrimEnd([IO.Path]::DirectorySeparatorChar)
    $allowedPrefix = [IO.Path]::GetFullPath($AllowedRoot).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if (-not $packageRoot.StartsWith($allowedPrefix, [StringComparison]::OrdinalIgnoreCase)) { throw "无法验证卸载目录。" }
    if (-not (Test-Path -LiteralPath $packageRoot)) { return }
    $rootItem = Get-Item -LiteralPath $packageRoot -Force
    if (-not $rootItem.PSIsContainer -or ($rootItem.Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw "卸载目录必须是普通目录。" }

    # Runtime data may live beside package-manifest.json. Never delete the root
    # recursively: only these directories and files belong to the release.
    $directories = @("App", "Diagnostics", "docs")
    $files = @("README.md", "LICENSE", "CHANGELOG.md", "THIRD-PARTY-NOTICES.md",
        "AppIcon.ico", "syncwallpaper-icon.svg", "package-manifest.json",
        "install.ps1", "upgrade.ps1", "uninstall.ps1")
    $packagePrefix = $packageRoot + [IO.Path]::DirectorySeparatorChar
    foreach ($name in @($directories) + @($files)) {
        $candidate = [IO.Path]::GetFullPath((Join-Path $packageRoot $name))
        if (-not $candidate.StartsWith($packagePrefix, [StringComparison]::OrdinalIgnoreCase)) { throw "发行文件路径超出卸载目录。" }
        if (-not (Test-Path -LiteralPath $candidate)) { continue }
        $item = Get-Item -LiteralPath $candidate -Force
        if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "发行文件路径包含链接，已停止卸载：$candidate" }
        if ($name -in $directories) {
            if (-not $item.PSIsContainer) { throw "发行目录类型不正确：$candidate" }
            if (Get-ChildItem -LiteralPath $candidate -Recurse -Force | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint } | Select-Object -First 1) {
                throw "发行目录中包含链接，已停止卸载：$candidate"
            }
            Remove-Item -LiteralPath $candidate -Recurse -Force
        } elseif (-not $item.PSIsContainer) {
            Remove-Item -LiteralPath $candidate -Force
        }
    }

    if (-not (Get-ChildItem -LiteralPath $packageRoot -Force | Select-Object -First 1)) {
        Remove-Item -LiteralPath $packageRoot -Force
    } elseif (-not $KeepData) {
        Write-Warning "已保留目录中的运行数据与其他非发行文件：$packageRoot。若要清理，请先确认内容后手动删除。"
    }
}

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
Remove-SyncWallpaperPackageFiles -Target $target -AllowedRoot $allowedRoot -KeepData:$KeepData
if (-not $KeepData) {
    $data = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA "SyncWallpaper"))
    if (Test-Path $data) {
        Write-Warning "已保留数据目录 $data。若要清理，请在确认后手动删除；卸载脚本不会删除配置或壁纸资产。"
    }
}
Write-Host "程序文件、启动项和快捷方式已移除；配置与壁纸资产默认保留。"
