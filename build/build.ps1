<#
.SYNOPSIS
    MUTEit build script: self-contained Release build, Inno Setup installer, portable ZIP, SHA-256.

.DESCRIPTION
    Full run (from the repository root or anywhere):
        powershell -NoProfile -ExecutionPolicy Bypass -File build\build.ps1
      1. checks that LICENSE.md (root) and licenses\LICENSE.md are identical
      2. runs the tests                                   (-SkipTests)
      3. Release build, self-contained win-x64            (dotnet build, the automatic installer hook is switched off)
      4. installer with Inno Setup 6 (ISCC)              (-SkipInstaller)
      5. portable ZIP with the MUTEit.portable marker  (-SkipPortable)
      6. SHA-256 file next to every package

    Installer only (called automatically by Visual Studio after every Release build, target "MuteitInstaller"
    in src\MUTEit.App\MUTEit.App.csproj):
        build.ps1 -InstallerOnly -SourceDir <bin\Release\net10.0-windows10.0.19041.0\win-x64>

    Messages in the MSBuild format ("error EVT…" / "warning EVT…") show up in the Visual Studio error list.
    A failing ISCC run fails the build; a missing Inno Setup installation is only a warning.

.PARAMETER Iscc
    Path to ISCC.exe if Inno Setup 6 is not installed in a standard location.

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File build\build.ps1
    powershell -NoProfile -ExecutionPolicy Bypass -File build\build.ps1 -SkipTests -SkipPortable
#>
[CmdletBinding()]
param(
    [string] $Configuration = 'Release',
    [string] $Runtime = 'win-x64',
    [switch] $InstallerOnly,
    [string] $SourceDir,
    [string] $Iscc,
    [switch] $SkipTests,
    [switch] $SkipInstaller,
    [switch] $SkipPortable
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = Split-Path -Parent $PSScriptRoot
$artifacts = Join-Path $root 'artifacts'
$project = Join-Path $root 'src\MUTEit.App\MUTEit.App.csproj'
$iss = Join-Path $root 'build\installer\muteit.iss'

function Fail([string] $code, [string] $message) {
    # MSBuild picks this format up as an error in Visual Studio.
    Write-Host "build.ps1 : error $($code): $message"
    exit 1
}

function Warn([string] $code, [string] $message) {
    Write-Host "build.ps1 : warning $($code): $message"
}

function Get-Version {
    [xml] $props = Get-Content -Raw -Path (Join-Path $root 'Directory.Build.props')
    $node = $props.SelectSingleNode('//Version')
    if ($null -eq $node) { Fail 'MUT010' 'Version not found in Directory.Build.props.' }
    return $node.InnerText.Trim()
}

function Find-Iscc {
    if ($Iscc) {
        if (Test-Path $Iscc) { return $Iscc }
        Fail 'MUT011' "ISCC not found at '$Iscc'."
    }

    # Environment variables can be missing (e.g. 32-bit or service context) – skip empty bases.
    $bases = @(
        @{ Base = ${env:ProgramFiles(x86)}; Sub = 'Inno Setup 6\ISCC.exe' },
        @{ Base = $env:ProgramFiles; Sub = 'Inno Setup 6\ISCC.exe' },
        @{ Base = $env:LOCALAPPDATA; Sub = 'Programs\Inno Setup 6\ISCC.exe' }
    )
    foreach ($entry in $bases) {
        if ([string]::IsNullOrEmpty($entry.Base)) { continue }
        $candidate = Join-Path $entry.Base $entry.Sub
        if (Test-Path $candidate) { return $candidate }
    }

    $onPath = Get-Command 'ISCC.exe' -ErrorAction SilentlyContinue
    if ($onPath) { return $onPath.Source }

    return $null
}

function Write-Sha256([string] $file) {
    $hash = (Get-FileHash -Algorithm SHA256 -Path $file).Hash.ToLowerInvariant()
    "$hash  $(Split-Path -Leaf $file)" | Set-Content -Encoding ascii -Path "$file.sha256"
}

function Test-LicenseCopies {
    $rootLicense = Join-Path $root 'LICENSE.md'
    $copy = Join-Path $root 'licenses\LICENSE.md'
    if (-not (Test-Path $rootLicense) -or -not (Test-Path $copy)) {
        Fail 'MUT020' 'LICENSE.md must exist in the repository root and in licenses\.'
    }

    if ((Get-FileHash $rootLicense).Hash -ne (Get-FileHash $copy).Hash) {
        Fail 'MUT021' 'LICENSE.md (root) and licenses\LICENSE.md differ – keep both copies identical.'
    }
}

function Build-Installer([string] $source, [string] $version) {
    if (-not (Test-Path (Join-Path $source 'MUTEit.exe'))) {
        Fail 'MUT030' "MUTEit.exe not found in '$source'. Build the Release configuration first."
    }

    $isccPath = Find-Iscc
    if ($null -eq $isccPath) {
        Warn 'MUT031' 'Inno Setup 6 (ISCC.exe) not found – installer skipped. Install it from https://jrsoftware.org/isinfo.php or pass -Iscc <path>.'
        return
    }

    New-Item -ItemType Directory -Force -Path $artifacts | Out-Null
    $setup = Join-Path $artifacts "muteit_v$($version)_setup.exe"
    foreach ($old in $setup, "$setup.sha256") {
        if (Test-Path $old) { Remove-Item -Force $old }
    }

    & $isccPath /Q "/DAppVersion=$version" "/DSourceDir=$source" "/DOutputDir=$artifacts" $iss
    if ($LASTEXITCODE -ne 0) { Fail 'MUT032' "Inno Setup failed (exit code $LASTEXITCODE)." }
    if (-not (Test-Path $setup)) { Fail 'MUT033' "Inno Setup ran, but $setup was not created." }

    Write-Sha256 $setup
    Write-Host "MUTEit installer: $setup"
}

function Build-Portable([string] $source, [string] $version) {
    $name = "muteit_v$($version)_portable"
    $staging = Join-Path $artifacts $name
    $zip = Join-Path $artifacts "$name.zip"
    foreach ($old in $staging, $zip, "$zip.sha256") {
        if (Test-Path $old) { Remove-Item -Recurse -Force $old }
    }

    New-Item -ItemType Directory -Force -Path $staging | Out-Null
    Copy-Item -Recurse -Path (Join-Path $source '*') -Destination $staging
    Get-ChildItem -Path $staging -Recurse -Filter '*.pdb' | Remove-Item -Force
    if (Test-Path (Join-Path $staging 'data')) { Remove-Item -Recurse -Force (Join-Path $staging 'data') }
    Set-Content -Path (Join-Path $staging 'MUTEit.portable') -Value 'Settings are stored in the data folder next to MUTEit.exe.' -Encoding utf8

    Compress-Archive -Path (Join-Path $staging '*') -DestinationPath $zip
    Remove-Item -Recurse -Force $staging
    Write-Sha256 $zip
    Write-Host "MUTEit portable: $zip"
}

$version = Get-Version
Test-LicenseCopies

if ($InstallerOnly) {
    if (-not $SourceDir) { Fail 'MUT001' '-InstallerOnly needs -SourceDir.' }
    Build-Installer ($SourceDir.TrimEnd('\')) $version
    exit 0
}

Write-Host "MUTEit $version · $Configuration · $Runtime"

if (-not $SkipTests) {
    dotnet test (Join-Path $root 'MUTEit.slnx') -c $Configuration -p:MuteitSkipInstaller=true
    if ($LASTEXITCODE -ne 0) { Fail 'MUT040' 'Tests failed.' }
}

dotnet build $project -c $Configuration -p:MuteitSkipInstaller=true
if ($LASTEXITCODE -ne 0) { Fail 'MUT041' 'Release build failed.' }

$buildDir = Join-Path $root "src\MUTEit.App\bin\$Configuration\net10.0-windows10.0.19041.0\$Runtime"
if (-not (Test-Path (Join-Path $buildDir 'MUTEit.exe'))) { Fail 'MUT042' "Build output not found: $buildDir" }

foreach ($required in 'README.md', 'licenses\LICENSE.md', 'licenses\third-party\AVALONIA.md') {
    if (-not (Test-Path (Join-Path $buildDir $required))) { Fail 'MUT043' "Missing in build output: $required" }
}

if (-not $SkipInstaller) { Build-Installer $buildDir $version }
if (-not $SkipPortable) { Build-Portable $buildDir $version }

Write-Host 'Done.'
