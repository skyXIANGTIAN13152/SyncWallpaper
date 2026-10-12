[CmdletBinding()]
param([Parameter(Mandatory = $true)][string]$InstallerPath)
$ErrorActionPreference = 'Stop'

# This is an actual install/uninstall test. Never run it on a user's computer.
if ($env:GITHUB_ACTIONS -cne 'true' -or [string]::IsNullOrWhiteSpace($env:RUNNER_TEMP)) {
    throw 'Installer integration tests may run only on a GitHub Actions Windows runner.'
}
if (-not $IsWindows) { throw 'A Windows runner is required.' }

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$runnerRoot = [IO.Path]::GetFullPath($env:RUNNER_TEMP).TrimEnd('\')
$testRoot = Join-Path $runnerRoot ('SyncWallpaper-InstallerTests-' + [guid]::NewGuid().ToString('N'))
$logRoot = Join-Path $repositoryRoot 'artifacts\installer-tests'
$installer = [IO.Path]::GetFullPath($InstallerPath)
$testRegistryPath = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{81228564-7240-4574-A4BE-80BED83EDDA2}_is1'
$results = [Collections.Generic.List[object]]::new()
$fixtureProcess = $null
$installedRoot = Join-Path $testRoot 'installed'
$failure = $null

function Assert-True([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}
function Assert-PlainPath([string]$Path) {
    $current = [IO.Path]::GetFullPath($Path)
    while ($current) {
        if ((Test-Path -LiteralPath $current) -and
            ((Get-Item -LiteralPath $current -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw "Linked test paths are not allowed: $current"
        }
        $current = [IO.Path]::GetDirectoryName($current)
    }
}
function Assert-FixturePath([string]$Path, [switch]$AllowRoot) {
    $resolved = [IO.Path]::GetFullPath($Path).TrimEnd('\')
    Assert-True ([IO.Path]::GetDirectoryName($testRoot) -ieq $runnerRoot) 'Fixture root must be directly inside RUNNER_TEMP.'
    Assert-True ((Split-Path -Leaf $testRoot) -match '^SyncWallpaper-InstallerTests-[0-9a-f]{32}$') 'Invalid fixture root name.'
    Assert-True (($AllowRoot -and $resolved -ieq $testRoot) -or
        $resolved.StartsWith($testRoot + '\', [StringComparison]::OrdinalIgnoreCase)) "Path escapes the test fixture: $resolved"
    Assert-PlainPath $resolved
}
function Write-FixtureFile([string]$Path, [string]$Value) {
    Assert-FixturePath $Path
    New-Item -ItemType Directory -Path (Split-Path -Parent $Path) -Force | Out-Null
    Set-Content -LiteralPath $Path -Value $Value -Encoding utf8
}
function Get-FixtureHashes([string]$Path) {
    Assert-FixturePath $Path
    $hashes = @{}
    foreach ($file in @(Get-ChildItem -LiteralPath $Path -File -Force -Recurse)) {
        Assert-FixturePath $file.FullName
        $hashes[$file.FullName] = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
    }
    return $hashes
}
function Assert-HashesUnchanged([hashtable]$Before, [string]$Message) {
    foreach ($path in $Before.Keys) {
        Assert-FixturePath $path
        Assert-True (Test-Path -LiteralPath $path -PathType Leaf) "$Message Missing: $path"
        Assert-True ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ceq $Before[$path]) "$Message Changed: $path"
    }
}
function Invoke-InstallerProcess([string]$Executable, [string[]]$Arguments, [string]$LogName) {
    $log = Join-Path $logRoot ($LogName + '.log')
    Assert-PlainPath $Executable
    $argumentsWithLog = @($Arguments) + ('/LOG="' + $log + '"')
    $process = Start-Process -FilePath $Executable -ArgumentList $argumentsWithLog -PassThru -WindowStyle Hidden
    try {
        if (-not $process.WaitForExit(240000)) {
            $process.Kill($true)
            $process.WaitForExit()
            throw "Installer timed out: $LogName"
        }
        $process.Refresh()
        return [pscustomobject]@{ ExitCode = $process.ExitCode; Log = $log }
    } finally { $process.Dispose() }
}
function Invoke-TestInstall([string]$Target, [string]$LogName) {
    Assert-FixturePath $Target
    return Invoke-InstallerProcess $installer @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/SP-', '/CURRENTUSER', ('/DIR="' + $Target + '"')) $LogName
}
function Assert-Refused([string]$Target, [string]$LogName, [string]$Reason) {
    $before = Get-FixtureHashes $Target
    $outcome = Invoke-TestInstall $Target $LogName
    Assert-True ($outcome.ExitCode -ne 0) "$LogName unexpectedly installed."
    Assert-True ((Get-Content -LiteralPath $outcome.Log -Raw) -match $Reason) "$LogName did not report the expected refusal."
    Assert-HashesUnchanged $before "$LogName changed the rejected target."
    Assert-True ((Get-FixtureHashes $Target).Count -eq $before.Count) "$LogName wrote unexpected files into the rejected target."
    $results.Add([pscustomobject]@{ Test = $LogName; Passed = $true; ExitCode = $outcome.ExitCode })
    Write-Host "PASS: $LogName"
}
function Stop-FixtureProcess {
    if ($null -eq $script:fixtureProcess) { return }
    try {
        if (-not $script:fixtureProcess.HasExited) {
            Assert-FixturePath $script:fixtureProcess.MainModule.FileName
            $script:fixtureProcess.Kill()
            $script:fixtureProcess.WaitForExit()
        }
    } finally {
        $script:fixtureProcess.Dispose()
        $script:fixtureProcess = $null
    }
}
function Remove-TestFixture {
    Assert-FixturePath $testRoot -AllowRoot
    if (-not (Test-Path -LiteralPath $testRoot)) { return }
    $entries = @(Get-ChildItem -LiteralPath $testRoot -Force -Recurse)
    foreach ($entry in $entries) {
        Assert-FixturePath $entry.FullName
        Assert-True (-not ($entry.Attributes -band [IO.FileAttributes]::ReparsePoint)) "Refusing to clean a linked fixture: $($entry.FullName)"
    }
    # Delete verified files individually, then only directories that are empty.
    foreach ($entry in @($entries | Where-Object { -not $_.PSIsContainer })) {
        Assert-FixturePath $entry.FullName
        Remove-Item -LiteralPath $entry.FullName
    }
    foreach ($directory in @($entries | Where-Object PSIsContainer | Sort-Object { $_.FullName.Length } -Descending)) {
        Assert-FixturePath $directory.FullName
        Assert-True (@(Get-ChildItem -LiteralPath $directory.FullName -Force).Count -eq 0) 'Refusing to remove a nonempty fixture directory.'
        Remove-Item -LiteralPath $directory.FullName
    }
    Assert-True (@(Get-ChildItem -LiteralPath $testRoot -Force).Count -eq 0) 'Fixture root is not empty.'
    Remove-Item -LiteralPath $testRoot
}

Assert-True ([IO.Path]::IsPathFullyQualified($env:RUNNER_TEMP) -and $runnerRoot.Length -gt 3) 'RUNNER_TEMP must be a full dedicated directory path.'
Assert-PlainPath $runnerRoot
Assert-PlainPath $installer
Assert-PlainPath $logRoot
Assert-True ((Split-Path -Leaf $installer) -match '-win-x64-setup-test\.exe$') 'Only a separately compiled test installer may run.'
$installerInfo = [Diagnostics.FileVersionInfo]::GetVersionInfo($installer)
Assert-True ($installerInfo.ProductName -ceq 'SyncWallpaper 安装验证') 'The executable is not a TestBuild installer.'
Assert-True (-not (Test-Path -LiteralPath $testRegistryPath)) 'An earlier test installation exists; refusing to overwrite it.'
New-Item -ItemType Directory -Path $testRoot, $logRoot -Force | Out-Null

try {
    $first = Invoke-TestInstall $installedRoot 'fresh-install'
    Assert-True ($first.ExitCode -eq 0) 'Fresh installation failed.'
    foreach ($relative in @('App\SyncWallpaper.App.exe', 'App\SyncWallpaper.Updater.exe', 'Diagnostics\SyncWallpaper.Diagnostics.exe', 'package-manifest.json', '.uninstall\unins000.exe')) {
        Assert-True (Test-Path -LiteralPath (Join-Path $installedRoot $relative) -PathType Leaf) "Missing installed component: $relative"
    }
    Assert-True (Test-Path -LiteralPath $testRegistryPath) 'The isolated uninstall registration was not created.'
    $registration = Get-ItemProperty -LiteralPath $testRegistryPath
    Assert-True ([IO.Path]::GetFullPath($registration.InstallLocation).TrimEnd('\') -ieq $installedRoot) 'Uninstall registration points outside the test installation.'
    $installedFiles = Get-FixtureHashes $installedRoot
    $results.Add([pscustomobject]@{ Test = 'fresh-install'; Passed = $true })
    Write-Host 'PASS: fresh-install'

    Write-FixtureFile (Join-Path $installedRoot 'Config\profiles.json') '{"profiles":[{"name":"CI preserved combination"}]}'
    Write-FixtureFile (Join-Path $installedRoot 'Config\settings.json') '{"automaticUpdateCheckEnabled":false}'
    Write-FixtureFile (Join-Path $installedRoot 'Wallpapers\keep-original.bin') 'CI wallpaper bytes must remain unchanged.'
    Write-FixtureFile (Join-Path $installedRoot 'personal-notes.txt') 'CI unknown root file must remain unchanged.'
    $protected = @{}
    foreach ($relative in @('Config\profiles.json', 'Config\settings.json', 'Wallpapers\keep-original.bin', 'personal-notes.txt')) {
        $path = Join-Path $installedRoot $relative
        $protected[$path] = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
    }
    $second = Invoke-TestInstall $installedRoot 'reinstall-preserves-data'
    Assert-True ($second.ExitCode -eq 0) 'Reinstallation failed.'
    Assert-HashesUnchanged $protected 'Reinstallation must preserve original user files.'
    $results.Add([pscustomobject]@{ Test = 'reinstall-preserves-data'; Passed = $true })
    Write-Host 'PASS: reinstall-preserves-data'

    # Files added after installation are not owned by Inno's installation log.
    $unknownProgramFile = Join-Path $installedRoot 'App\unknown-after-install.txt'
    Write-FixtureFile $unknownProgramFile 'Unknown files must not be inferred to belong to the installer.'
    $protected[$unknownProgramFile] = (Get-FileHash -LiteralPath $unknownProgramFile -Algorithm SHA256).Hash
    $uninstall = Invoke-InstallerProcess (Join-Path $installedRoot '.uninstall\unins000.exe') @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART') 'uninstall-preserves-data'
    Assert-True ($uninstall.ExitCode -eq 0) 'Uninstallation failed.'
    Assert-HashesUnchanged $protected 'Uninstallation must preserve user and unknown files.'
    foreach ($path in $installedFiles.Keys) {
        if ($path.StartsWith((Join-Path $installedRoot '.uninstall') + '\', [StringComparison]::OrdinalIgnoreCase)) { continue }
        Assert-True (-not (Test-Path -LiteralPath $path -PathType Leaf)) "An installer-owned file remains: $path"
    }
    Assert-True (-not (Test-Path -LiteralPath $testRegistryPath)) 'The test uninstall registration remains.'
    $results.Add([pscustomobject]@{ Test = 'uninstall-preserves-data'; Passed = $true })
    Write-Host 'PASS: uninstall-preserves-data'

    $sourceTarget = Join-Path $testRoot 'rejected-source'
    Write-FixtureFile (Join-Path $sourceTarget 'SyncWallpaper.sln') 'CI source marker'
    Assert-Refused $sourceTarget 'reject-source-directory' '源码工程'
    $appTarget = Join-Path $testRoot 'rejected-subdirectory\App'
    Write-FixtureFile (Join-Path $appTarget 'preserve.txt') 'CI App subdirectory marker'
    Assert-Refused $appTarget 'reject-app-subdirectory' '上一级根目录'

    $newerTarget = Join-Path $testRoot 'rejected-newer'
    Write-FixtureFile (Join-Path $newerTarget 'App\preserve.txt') 'CI newer version marker'
    $higherVersionExe = Join-Path $env:WINDIR 'System32\cmd.exe'
    $higherInfo = [Diagnostics.FileVersionInfo]::GetVersionInfo($higherVersionExe)
    $higherVersion = [Version]::new($higherInfo.FileMajorPart, $higherInfo.FileMinorPart, $higherInfo.FileBuildPart, $higherInfo.FilePrivatePart)
    $setupVersion = [Version]::new($installerInfo.FileMajorPart, $installerInfo.FileMinorPart, $installerInfo.FileBuildPart, $installerInfo.FilePrivatePart)
    Assert-True ($higherVersion -gt $setupVersion) 'The runner fixture executable must have a higher version than the package.'
    Copy-Item -LiteralPath $higherVersionExe -Destination (Join-Path $newerTarget 'App\SyncWallpaper.App.exe')
    Assert-Refused $newerTarget 'reject-newer-version' '不会降级覆盖'

    # Compile a minimal sleeping stub, never launch the real wallpaper application.
    $stubSource = Join-Path $testRoot 'SleepingFixture.cs'
    Write-FixtureFile $stubSource 'using System.Reflection; [assembly: AssemblyFileVersion("0.0.0.1")] class Program { static void Main() { System.Threading.Thread.Sleep(300000); } }'
    $stubExe = Join-Path $testRoot 'SleepingFixture.exe'
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
    Assert-True (Test-Path -LiteralPath $compiler -PathType Leaf) 'The runner needs the .NET Framework C# compiler for an inert process fixture.'
    & $compiler /nologo /target:winexe ("/out:$stubExe") $stubSource
    Assert-True ($LASTEXITCODE -eq 0) 'Could not build the inert process fixture.'

    $runningTarget = Join-Path $testRoot 'rejected-running'
    Write-FixtureFile (Join-Path $runningTarget 'App\preserve.txt') 'CI running process marker'
    $runningExe = Join-Path $runningTarget 'App\SyncWallpaper.App.exe'
    Copy-Item -LiteralPath $stubExe -Destination $runningExe
    $fixtureProcess = Start-Process -FilePath $runningExe -PassThru -WindowStyle Hidden
    Assert-True (-not $fixtureProcess.WaitForExit(500)) 'The inert application fixture exited early.'
    Assert-Refused $runningTarget 'reject-running-target' '仍在运行'
    Stop-FixtureProcess

    $updaterTarget = Join-Path $testRoot 'rejected-external-updater'
    Write-FixtureFile (Join-Path $updaterTarget 'preserve.txt') 'CI separate updater marker'
    $externalUpdater = Join-Path $testRoot 'external-runner\SyncWallpaper.Updater.exe'
    New-Item -ItemType Directory -Path (Split-Path -Parent $externalUpdater) -Force | Out-Null
    Copy-Item -LiteralPath $stubExe -Destination $externalUpdater
    $fixtureProcess = Start-Process -FilePath $externalUpdater -PassThru -WindowStyle Hidden
    Assert-True (-not $fixtureProcess.WaitForExit(500)) 'The inert updater fixture exited early.'
    Assert-Refused $updaterTarget 'reject-external-updater' '更新'
    Stop-FixtureProcess
} catch {
    $failure = $_
    $results.Add([pscustomobject]@{ Test = 'failure'; Passed = $false; Message = $_.Exception.Message })
} finally {
    try {
        Stop-FixtureProcess
        if (Test-Path -LiteralPath $testRegistryPath) {
            $registration = Get-ItemProperty -LiteralPath $testRegistryPath
            $location = [IO.Path]::GetFullPath($registration.InstallLocation).TrimEnd('\')
            Assert-FixturePath $location
            Assert-True ($location -ieq $installedRoot) 'Refusing to clean another installation registration.'
            $uninstaller = Join-Path $location '.uninstall\unins000.exe'
            if (Test-Path -LiteralPath $uninstaller -PathType Leaf) {
                $cleanup = Invoke-InstallerProcess $uninstaller @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART') 'cleanup-uninstall'
                Assert-True ($cleanup.ExitCode -eq 0) 'Cleanup uninstallation failed; retaining fixture for diagnosis.'
            }
            Assert-True (-not (Test-Path -LiteralPath $testRegistryPath)) 'Cleanup did not remove the test uninstall registration.'
        }
        Remove-TestFixture
    } catch {
        $results.Add([pscustomobject]@{ Test = 'cleanup'; Passed = $false; Message = $_.Exception.Message; RetainedFixture = $testRoot })
        if ($null -eq $failure) { $failure = $_ }
        Write-Warning "Fixture cleanup failed: $($_.Exception.Message) Fixture: $testRoot"
    }
    $results | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $logRoot 'results.json') -Encoding utf8
}
if ($null -ne $failure) { throw $failure }
Write-Host "PASS: $($results.Count) isolated installer tests; fixture files and test registration removed."
