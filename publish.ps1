param([switch]$SelfContained)
$ErrorActionPreference = "Stop"

$dotnet = Join-Path $PSScriptRoot "work\dotnet-sdk\dotnet.exe"
if (-not (Test-Path -LiteralPath $dotnet)) { $dotnet = "dotnet" }
$props = [xml](Get-Content -LiteralPath (Join-Path $PSScriptRoot "Directory.Build.props") -Raw)
$version = [string]$props.Project.PropertyGroup.Version
if ([string]::IsNullOrWhiteSpace($version)) { throw "Directory.Build.props 中缺少 Version。" }
$outputName = if ($SelfContained) { "win-x64-selfcontained" } else { "win-x64" }
$output = Join-Path $PSScriptRoot ("artifacts\publish\" + $outputName)
$publishRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "artifacts\publish"))
if (Test-Path -LiteralPath $output) {
    $resolvedOutput = [IO.Path]::GetFullPath($output)
    if (-not $resolvedOutput.StartsWith($publishRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw "Refusing to clean an output path outside artifacts/publish: $resolvedOutput" }
    Remove-Item -LiteralPath $resolvedOutput -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $output | Out-Null

$selfContainedValue = if ($SelfContained) { "true" } else { "false" }
$projects = [ordered]@{
    App = "src\SyncWallpaper.App\SyncWallpaper.App.csproj"
    Host = "src\SyncWallpaper.Host\SyncWallpaper.Host.csproj"
    Diagnostics = "src\SyncWallpaper.Diagnostics\SyncWallpaper.Diagnostics.csproj"
    HardwareValidation = "src\SyncWallpaper.HardwareValidation\SyncWallpaper.HardwareValidation.csproj"
}
foreach ($entry in $projects.GetEnumerator()) {
    $destination = Join-Path $output $entry.Key
    New-Item -ItemType Directory -Force -Path $destination | Out-Null
    & $dotnet publish (Join-Path $PSScriptRoot $entry.Value) -c Release -r win-x64 --self-contained $selfContainedValue -p:DebugType=None -p:DebugSymbols=false -o $destination
}

Copy-Item (Join-Path $PSScriptRoot "README.md"), (Join-Path $PSScriptRoot "LICENSE"), (Join-Path $PSScriptRoot "CHANGELOG.md"), (Join-Path $PSScriptRoot "THIRD-PARTY-NOTICES.md") $output -Force
Copy-Item (Join-Path $PSScriptRoot "assets\AppIcon.ico") $output -Force
Copy-Item (Join-Path $PSScriptRoot "assets\syncwallpaper-icon.svg") $output -Force
Copy-Item (Join-Path $PSScriptRoot "install.ps1"), (Join-Path $PSScriptRoot "upgrade.ps1"), (Join-Path $PSScriptRoot "uninstall.ps1") $output -Force
Copy-Item (Join-Path $PSScriptRoot "docs") (Join-Path $output "docs") -Recurse -Force
# The local acceptance report contains workspace-specific hashes and is not a product manual.
# Keep it in the repository, but do not put it into the distributable package.
$packagedReport = Join-Path $output "docs\RELEASE-REPORT.md"
if (Test-Path -LiteralPath $packagedReport) { Remove-Item -LiteralPath $packagedReport -Force }

$manifest = [ordered]@{
    product = "屏序 SyncWallpaper"
    version = $version
    releaseChannel = "Stable"
    rid = "win-x64"
    selfContained = [bool]$SelfContained
    layout = "App/ Host/ Diagnostics/ HardwareValidation/"
    updateMode = "GitHub Releases page; user initiated download and install"
    telemetry = $false
    automaticDownload = $false
    automaticInstall = $false
    containsUpdater = $false
    startupDefault = $false
    systemMutationDefault = $false
    signed = $false
    releaseLabel = "Unsigned Release Candidate"
}
$manifest | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $output "package-manifest.json") -Encoding UTF8

$packageName = "SyncWallpaper-$version-win-x64" + $(if ($SelfContained) { "-selfcontained" } else { "" })
$zipPath = Join-Path $PSScriptRoot ("artifacts\publish\" + $packageName + ".zip")
$hashPath = Join-Path $PSScriptRoot ("artifacts\publish\" + $packageName + ".sha256")
if (Test-Path -LiteralPath $zipPath) { Remove-Item -LiteralPath $zipPath -Force }
Compress-Archive -Path (Join-Path $output "*") -DestinationPath $zipPath -CompressionLevel Optimal
$hash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath $hashPath -Value ($hash + "  " + (Split-Path $zipPath -Leaf)) -Encoding ASCII

if (Get-ChildItem -Path $output -Recurse -File -Filter "*Updater*.exe" -ErrorAction SilentlyContinue) { throw "发布目录意外包含 Updater.exe。" }
$sumLines = Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot "artifacts\publish") -File -Filter "*.zip" |
    Sort-Object Name |
    ForEach-Object { $fileHash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(); "$fileHash  $($_.Name)" }
Set-Content -LiteralPath (Join-Path $PSScriptRoot "artifacts\publish\SHA256SUMS.txt") -Value $sumLines -Encoding ASCII
Write-Host "Release package: $output (self-contained=$SelfContained)"
Write-Host "Portable ZIP: $zipPath"
Write-Host "SHA256: $hash"
