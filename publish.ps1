param([switch]$SelfContained)
$ErrorActionPreference = "Stop"
$dotnet = Join-Path $PSScriptRoot "work\dotnet-sdk\dotnet.exe"
if (-not (Test-Path $dotnet)) { $dotnet = "dotnet" }
$outputName = if ($SelfContained) { "win-x64-selfcontained" } else { "win-x64" }
$output = Join-Path $PSScriptRoot ("artifacts\publish\" + $outputName)
if (Test-Path $output) {
    $resolvedOutput = [IO.Path]::GetFullPath($output)
    $resolvedRoot = [IO.Path]::GetFullPath($PSScriptRoot)
    if (-not $resolvedOutput.StartsWith($resolvedRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw "Refusing to clean an output path outside the workspace: $resolvedOutput" }
    Remove-Item -LiteralPath $resolvedOutput -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $output, (Join-Path $output "App"), (Join-Path $output "Host"), (Join-Path $output "Diagnostics"), (Join-Path $output "HardwareValidation") | Out-Null
$selfContainedValue = if ($SelfContained) { "true" } else { "false" }
& $dotnet publish (Join-Path $PSScriptRoot "src\SyncWallpaper.App\SyncWallpaper.App.csproj") -c Release -r win-x64 --self-contained $selfContainedValue -p:DebugType=None -p:DebugSymbols=false -o (Join-Path $output "App")
& $dotnet publish (Join-Path $PSScriptRoot "src\SyncWallpaper.Host\SyncWallpaper.Host.csproj") -c Release -r win-x64 --self-contained $selfContainedValue -p:DebugType=None -p:DebugSymbols=false -o (Join-Path $output "Host")
& $dotnet publish (Join-Path $PSScriptRoot "src\SyncWallpaper.Diagnostics\SyncWallpaper.Diagnostics.csproj") -c Release -r win-x64 --self-contained $selfContainedValue -p:DebugType=None -p:DebugSymbols=false -o (Join-Path $output "Diagnostics")
& $dotnet publish (Join-Path $PSScriptRoot "src\SyncWallpaper.HardwareValidation\SyncWallpaper.HardwareValidation.csproj") -c Release -r win-x64 --self-contained $selfContainedValue -p:DebugType=None -p:DebugSymbols=false -o (Join-Path $output "HardwareValidation")
Copy-Item (Join-Path $PSScriptRoot "README.md"), (Join-Path $PSScriptRoot "LICENSE"), (Join-Path $PSScriptRoot "CHANGELOG.md"), (Join-Path $PSScriptRoot "THIRD-PARTY-NOTICES.md") $output -Force
Copy-Item (Join-Path $PSScriptRoot "assets\AppIcon.ico") $output -Force
Copy-Item (Join-Path $PSScriptRoot "assets\syncwallpaper-icon.svg") $output -Force
Copy-Item (Join-Path $PSScriptRoot "install.ps1"), (Join-Path $PSScriptRoot "upgrade.ps1"), (Join-Path $PSScriptRoot "uninstall.ps1") $output -Force
Copy-Item (Join-Path $PSScriptRoot "docs") (Join-Path $output "docs") -Recurse -Force
$manifest = [ordered]@{ product = "屏序 SyncWallpaper"; version = "1.0.0-rc.1"; rid = "win-x64"; selfContained = [bool]$SelfContained; layout = "App/ Host/ Diagnostics/ HardwareValidation/"; telemetry = $false; startupDefault = $false; systemMutationDefault = $false; signed = $false; releaseLabel = "Unsigned Release Candidate" }
$manifest | ConvertTo-Json | Set-Content (Join-Path $output "package-manifest.json") -Encoding UTF8
$packageName = "SyncWallpaper-1.0.0-rc.1-win-x64" + $(if ($SelfContained) { "-selfcontained" } else { "" })
$zipPath = Join-Path $PSScriptRoot ("artifacts\publish\" + $packageName + ".zip")
$hashPath = Join-Path $PSScriptRoot ("artifacts\publish\" + $packageName + ".sha256")
if (Test-Path $zipPath) { Remove-Item -LiteralPath $zipPath -Force }
Compress-Archive -Path (Join-Path $output "*") -DestinationPath $zipPath -CompressionLevel Optimal
$hash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath $hashPath -Value ($hash + "  " + (Split-Path $zipPath -Leaf)) -Encoding ASCII
Write-Host "Release: $output (self-contained=$SelfContained)"
Write-Host "Portable ZIP: $zipPath"
