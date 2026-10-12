[CmdletBinding()]
param(
    [string]$PackagePath = (Join-Path $PSScriptRoot 'artifacts\publish\win-x64-selfcontained'),
    [string]$OutputDirectory = (Join-Path $PSScriptRoot 'artifacts\publish'),
    [switch]$TestBuild
)
$ErrorActionPreference = 'Stop'
$package = [IO.Path]::GetFullPath($PackagePath).TrimEnd([IO.Path]::DirectorySeparatorChar)
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (!(Test-Path -LiteralPath $package -PathType Container)) { throw 'A self-contained release package is required.' }

function Assert-PlainPath([string]$Path) {
    $current = [IO.Path]::GetFullPath($Path)
    while ($current) {
        if ((Test-Path -LiteralPath $current) -and ((Get-Item -LiteralPath $current -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw "Installer build path contains a link: $current"
        }
        $current = [IO.Path]::GetDirectoryName($current)
    }
}
Assert-PlainPath $package
Assert-PlainPath $output
$manifest = Get-Content -LiteralPath (Join-Path $package 'package-manifest.json') -Raw | ConvertFrom-Json
if ($manifest.rid -ne 'win-x64' -or $manifest.selfContained -ne $true -or
    $manifest.language -ne 'zh-CN' -or $manifest.containsUpdater -ne $true) {
    throw 'The installer requires the Chinese win-x64 self-contained package with its updater.'
}
$version = [string]$manifest.version
if ($version -notmatch '^\d+\.\d+\.\d+(?:-[A-Za-z0-9.-]+)?$') { throw 'Invalid package version.' }
$allowedDirectories = @('App', 'Diagnostics', 'docs')
$allowedRootFiles = @('README.md','LICENSE','CHANGELOG.md','THIRD-PARTY-NOTICES.md','AppIcon.ico','syncwallpaper-icon.svg','package-manifest.json','install.ps1','upgrade.ps1','uninstall.ps1')
foreach ($entry in Get-ChildItem -LiteralPath $package -Force -Recurse) {
    $relative = [IO.Path]::GetRelativePath($package, $entry.FullName)
    $segments = $relative -split '[\\/]'
    if (($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) -or
        ($segments[0] -notin $allowedDirectories -and !($segments.Count -eq 1 -and !$entry.PSIsContainer -and $entry.Name -in $allowedRootFiles)) -or
        ($segments | Where-Object { $_ -match '^(?i:Config|Wallpapers|Logs|Cache|Thumbnails|Backups|Deleted|UserData|TestResults|work|\.git)$' }) -or
        $entry.Name -match '(?i)(\.pdb$|\.bak$|\.tmp$|\.log$|secret|credential)') {
        throw "Unexpected, private or linked file in the installer payload: $relative"
    }
}
foreach ($required in @('App\SyncWallpaper.App.exe','App\SyncWallpaper.App.dll','App\SyncWallpaper.Updater.exe','App\SyncWallpaper.Updater.dll','App\SyncWallpaper.Updater.deps.json','App\SyncWallpaper.Updater.runtimeconfig.json','App\coreclr.dll','AppIcon.ico','LICENSE')) {
    if (!(Test-Path -LiteralPath (Join-Path $package $required) -PathType Leaf)) { throw "Incomplete installer payload: $required" }
}
$appInfo = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $package 'App\SyncWallpaper.App.exe'))
$updaterInfo = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $package 'App\SyncWallpaper.Updater.exe'))
if ($appInfo.ProductVersion.Split('+')[0] -cne $version -or $updaterInfo.ProductVersion.Split('+')[0] -cne $version) {
    throw 'Application and updater versions must match the release manifest.'
}
$compiler = & (Join-Path $PSScriptRoot 'tools\Get-InnoSetup.ps1')
New-Item -ItemType Directory -Force -Path $output | Out-Null
$compilerArguments = @('/Qp', "/DPackageDir=$package", "/DOutputDir=$output", "/DAppVersion=$version", "/DAppVersionNumeric=$($appInfo.FileVersion)",
    "/DChineseLanguageFile=$(Join-Path (Split-Path -Parent $compiler) 'Languages\ChineseSimplified.isl')")
if ($TestBuild) { $compilerArguments += '/DTestBuild=1' }
& $compiler @compilerArguments (Join-Path $PSScriptRoot 'installer\SyncWallpaper.iss')
if ($LASTEXITCODE -ne 0) { throw "Installer compilation failed with exit code $LASTEXITCODE." }
$name = "SyncWallpaper-$version-win-x64-setup"
if ($TestBuild) { $name += '-test' }
$exe = Join-Path $output ($name + '.exe')
if (!(Test-Path -LiteralPath $exe -PathType Leaf)) { throw 'The installer executable was not generated.' }
$hash = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath (Join-Path $output ($name + '.sha256')) -Value "$hash  $name.exe" -Encoding ASCII
Write-Host "Installer: $exe"
Write-Host "SHA-256: $hash"
