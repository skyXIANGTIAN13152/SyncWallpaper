param([string]$WorkRoot = (Join-Path $PSScriptRoot "..\..\work\script-tests"))
$ErrorActionPreference = "Stop"

# Load only the filesystem helper's AST. Never execute the uninstall entrypoint:
# process termination, registry edits and shortcut removal are outside this test.
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\.."))
$tokens = $null
$parseErrors = $null
$ast = [Management.Automation.Language.Parser]::ParseFile((Join-Path $repositoryRoot "uninstall.ps1"), [ref]$tokens, [ref]$parseErrors)
if ($parseErrors.Count -ne 0) { throw "uninstall.ps1 has syntax errors." }
$helper = $ast.Find({ param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq "Remove-SyncWallpaperPackageFiles" }, $true)
if ($null -eq $helper) { throw "Filesystem cleanup helper is missing." }
. ([scriptblock]::Create($helper.Extent.Text))

$workPath = [IO.Path]::GetFullPath($WorkRoot).TrimEnd([IO.Path]::DirectorySeparatorChar)
$testRoot = [IO.Path]::GetFullPath((Join-Path $workPath ("uninstall-" + [guid]::NewGuid().ToString("N"))))
if (-not $testRoot.StartsWith($workPath + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw "Unsafe fixture path." }
New-Item -ItemType Directory -Path $testRoot -Force | Out-Null

function Assert-True([bool]$Condition, [string]$Message) { if (-not $Condition) { throw $Message } }
function Write-FixtureFile([string]$Path, [string]$Value = "fixture") {
    New-Item -ItemType Directory -Path (Split-Path -Parent $Path) -Force | Out-Null
    Set-Content -LiteralPath $Path -Value $Value -Encoding UTF8
}
function Get-FixtureHashes([string]$Path) {
    $hashes = @{}
    Get-ChildItem -LiteralPath $Path -File -Recurse -Force | ForEach-Object {
        $hashes[$_.FullName] = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
    }
    return $hashes
}

try {
    foreach ($mode in @("default", "KeepData", "KeepDataFalse")) {
        $installation = Join-Path $testRoot $mode
        foreach ($directory in @("Config", "Wallpapers", "Cache", "Thumbnails", "Logs", "Backups", "Deleted", "Personal")) {
            Write-FixtureFile (Join-Path $installation "$directory\preserve.txt") "preserve $directory"
        }
        Write-FixtureFile (Join-Path $installation "my-notes.txt") "keep unknown root files"
        $before = Get-FixtureHashes $installation
        foreach ($directory in @("App", "Diagnostics", "docs")) { Write-FixtureFile (Join-Path $installation "$directory\package.txt") }
        foreach ($file in @("README.md", "LICENSE", "CHANGELOG.md", "THIRD-PARTY-NOTICES.md", "AppIcon.ico", "syncwallpaper-icon.svg", "package-manifest.json", "install.ps1", "upgrade.ps1", "uninstall.ps1")) {
            Write-FixtureFile (Join-Path $installation $file)
        }
        $arguments = @{ Target = $installation; AllowedRoot = $testRoot; WarningAction = "SilentlyContinue" }
        if ($mode -eq "KeepData") { $arguments.KeepData = $true }
        if ($mode -eq "KeepDataFalse") { $arguments.KeepData = $false }
        Remove-SyncWallpaperPackageFiles @arguments
        $after = Get-FixtureHashes $installation
        Assert-True ($after.Count -eq $before.Count) "${mode}: only user files should remain."
        foreach ($path in $before.Keys) { Assert-True ($after[$path] -eq $before[$path]) "${mode}: changed or removed user file $path" }
        Write-Host "PASS: $mode preserves all user files byte-for-byte and removes package files."
    }

    $emptyInstallation = Join-Path $testRoot "package-only"
    Write-FixtureFile (Join-Path $emptyInstallation "App\package.txt")
    Remove-SyncWallpaperPackageFiles -Target $emptyInstallation -AllowedRoot $testRoot
    Assert-True (-not (Test-Path -LiteralPath $emptyInstallation)) "An empty installation directory should be removed."
    Write-Host "PASS: empty package root is removed without recursive root deletion."

    $refused = $false
    try { Remove-SyncWallpaperPackageFiles -Target $workPath -AllowedRoot $testRoot } catch { $refused = $true }
    Assert-True $refused "Cleanup must reject a target outside the allowed root."
    Write-Host "PASS: outside target is refused."

    $deleteFixture = Join-Path $testRoot "no-delete-data"
    Write-FixtureFile (Join-Path $deleteFixture "Config\preserve.txt")
    $refused = $false
    try { Remove-SyncWallpaperPackageFiles -Target $deleteFixture -AllowedRoot $testRoot -RemoveData } catch [Management.Automation.ParameterBindingException] { $refused = $true }
    Assert-True $refused "There must not be an implicit data-deletion option."
    Assert-True (Test-Path -LiteralPath (Join-Path $deleteFixture "Config\preserve.txt")) "Explicit unsupported deletion request must not remove data."
    Write-Host "PASS: unsupported data-deletion switch is refused before cleanup."
} finally {
    $resolvedTestRoot = [IO.Path]::GetFullPath($testRoot)
    if (-not $resolvedTestRoot.StartsWith($workPath + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw "Unsafe fixture cleanup path." }
    if (Test-Path -LiteralPath $resolvedTestRoot) { Remove-Item -LiteralPath $resolvedTestRoot -Recurse -Force }
}
