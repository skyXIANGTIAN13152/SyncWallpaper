[CmdletBinding()]
param([string]$ToolsRoot = (Join-Path (Split-Path -Parent $PSScriptRoot) 'work\tools'))
$ErrorActionPreference = 'Stop'

# Pin the official compiler and translation; never execute an unverified download.
$version = '6.7.3'
$compilerHash = '9c73c3bae7ed48d44112a0f48e66742c00090bdb5bef71d9d3c056c66e97b732'
$languageHash = '7d544b9bb1d142cfa11f2e5d3cc8abe2e55f8e066c5124e3772675aa236e1278'
$root = [IO.Path]::GetFullPath($ToolsRoot)
$cache = Join-Path $root 'downloads'
$compilerDirectory = Join-Path $root "inno-setup-$version"
$compiler = Join-Path $compilerDirectory 'ISCC.exe'
$language = Join-Path $compilerDirectory 'Languages\ChineseSimplified.isl'

function Assert-NoLinkedParent([string]$Path) {
    $current = [IO.Path]::GetFullPath($Path)
    while ($current) {
        if ((Test-Path -LiteralPath $current) -and ((Get-Item -LiteralPath $current -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw "Compiler tools path contains a link: $current"
        }
        $current = [IO.Path]::GetDirectoryName($current)
    }
}

function Get-PinnedFile([string]$Uri, [string]$Destination, [string]$Hash) {
    Assert-NoLinkedParent $Destination
    if (!(Test-Path -LiteralPath $Destination -PathType Leaf)) {
        Invoke-WebRequest -Uri $Uri -OutFile $Destination
    }
    if ((Get-FileHash -LiteralPath $Destination -Algorithm SHA256).Hash -ine $Hash) {
        throw "Downloaded dependency failed SHA-256 verification: $Destination"
    }
}

Assert-NoLinkedParent $compilerDirectory
New-Item -ItemType Directory -Force -Path $cache | Out-Null
if (!(Test-Path -LiteralPath $compiler -PathType Leaf)) {
    $download = Join-Path $cache "innosetup-$version.exe"
    Get-PinnedFile "https://github.com/jrsoftware/issrc/releases/download/is-6_7_3/innosetup-$version.exe" $download $compilerHash
    $signature = Get-AuthenticodeSignature -LiteralPath $download
    if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'Pyrsys B\.V\.') {
        throw 'The Inno Setup compiler download does not have the expected valid publisher signature.'
    }
    # Portable mode creates no compiler shortcuts, file associations or uninstall registration.
    $arguments = @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/SP-', '/CURRENTUSER', '/PORTABLE=1', '/NOICONS', "/DIR=`"$compilerDirectory`"")
    $process = Start-Process -FilePath $download -ArgumentList $arguments -WindowStyle Hidden -Wait -PassThru
    if ($process.ExitCode -ne 0) { throw "Compiler bootstrap failed with exit code $($process.ExitCode)." }
}
# Inno's compiler files report 0.0.0.0 in their PE metadata. Pin the actual
# components extracted from the verified 6.7.3 distribution instead.
$components = @{
    'ISCC.exe' = '0a8757031b33777e4c9cbffee40f11a5062b36d25cbe144c1db73b6102b80ad7'
    'ISCmplr.dll' = '85a1e3090d3a5b85319f001b7c8f9ecfad45f37eff030a67bbe29ef58b7aa2c3'
    'ISPP.dll' = 'bde04be7f4a55ca56afb6b06600d7cbe8326fac468ac3f2885b80eefb3ed2a7a'
}
foreach ($component in $components.GetEnumerator()) {
    $componentPath = Join-Path $compilerDirectory $component.Key
    Assert-NoLinkedParent $componentPath
    if (!(Test-Path -LiteralPath $componentPath -PathType Leaf) -or
        (Get-FileHash -LiteralPath $componentPath -Algorithm SHA256).Hash -ine $component.Value) {
        throw "Expected the pinned Inno Setup $version component at $componentPath"
    }
}
$languageDownload = Join-Path $cache "ChineseSimplified-$version.isl"
Get-PinnedFile 'https://raw.githubusercontent.com/jrsoftware/issrc/is-6_7_3/Files/Languages/Unofficial/ChineseSimplified.isl' $languageDownload $languageHash
Assert-NoLinkedParent $language
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $language) | Out-Null
Copy-Item -LiteralPath $languageDownload -Destination $language -Force
Write-Output $compiler
