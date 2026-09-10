[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'win-arm64')]
    [string]$Runtime = 'win-x64',

    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version = '1.0.0',

    [string]$InnoCompiler,

    [string]$DotnetPath,

    [string]$CertificateThumbprint,

    [string]$SignToolPath,

    [uri]$TimestampUrl = 'http://timestamp.digicert.com',

    [switch]$SkipTests,

    [switch]$NoRestore,

    [switch]$Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$releaseBase = [IO.Path]::GetFullPath((Join-Path $repositoryRoot 'artifacts\release'))
$releaseRoot = [IO.Path]::GetFullPath((Join-Path $releaseBase "$Version\$Runtime"))
$publishDirectory = Join-Path $releaseRoot 'publish'
$project = Join-Path $repositoryRoot 'src\TelePilot.App\TelePilot.App.csproj'
$solution = Join-Path $repositoryRoot 'TelePilot.sln'
$installerScript = Join-Path $repositoryRoot 'installer\windows\TelePilot.iss'
$localDotnet = Join-Path $repositoryRoot '.dotnet\dotnet.exe'
$dotnet = if ($DotnetPath) {
    $resolvedDotnet = [IO.Path]::GetFullPath($DotnetPath)
    if (-not (Test-Path -LiteralPath $resolvedDotnet -PathType Leaf)) {
        throw "Nie znaleziono wskazanego SDK .NET: $resolvedDotnet"
    }

    $resolvedDotnet
} elseif (Test-Path -LiteralPath $localDotnet) {
    $localDotnet
} else {
    (Get-Command dotnet -ErrorAction Stop).Source
}

function Assert-Success([string]$Operation) {
    if ($LASTEXITCODE -ne 0) {
        throw "$Operation nie powiodło się (kod $LASTEXITCODE)."
    }
}

function Assert-SafeReleasePath([string]$Path) {
    $fullPath = [IO.Path]::GetFullPath($Path)
    $prefix = $releaseBase.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if (-not $fullPath.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Odmowa operacji poza katalogiem wydania: $fullPath"
    }
}

$signingArguments = @{}
if ($CertificateThumbprint) {
    $signingArguments = @{
        CertificateThumbprint = $CertificateThumbprint
        SignToolPath = $SignToolPath
        TimestampUrl = $TimestampUrl
    }
    # Fail before replacing an existing release if signing is unavailable.
    & (Join-Path $PSScriptRoot 'sign-windows-files.ps1') @signingArguments -CheckOnly
} elseif ($SignToolPath -or $PSBoundParameters.ContainsKey('TimestampUrl')) {
    throw 'Parametry podpisywania wymagają -CertificateThumbprint.'
}

Assert-SafeReleasePath $releaseRoot
if (Test-Path -LiteralPath $releaseRoot) {
    if (-not $Force) {
        throw "Katalog wydania już istnieje: $releaseRoot. Użyj -Force, aby zbudować go ponownie."
    }

    Remove-Item -LiteralPath $releaseRoot -Recurse -Force
}

New-Item -ItemType Directory -Path $publishDirectory -Force | Out-Null

if (-not $SkipTests) {
    & $dotnet test $solution `
        --configuration Release `
        "-p:UsedAvaloniaProducts="
    Assert-Success 'Testy wydania'
}

$vlcArchitectureProperties = if ($Runtime -eq 'win-x64') {
    @(
        '-p:VlcWindowsX64Enabled=true',
        '-p:VlcWindowsX86Enabled=false',
        '-p:VlcWindowsArm64Enabled=false'
    )
} else {
    @(
        '-p:VlcWindowsX64Enabled=false',
        '-p:VlcWindowsX86Enabled=false',
        '-p:VlcWindowsArm64Enabled=true'
    )
}

$publishArguments = @(
    'publish',
    $project,
    '--configuration',
    'Release',
    '--runtime',
    $Runtime,
    '--self-contained',
    'true',
    '--output',
    $publishDirectory,
    '-p:PublishSingleFile=false',
    '-p:PublishTrimmed=false',
    '-p:DebugSymbols=false',
    '-p:DebugType=None',
    '-p:ContinuousIntegrationBuild=true',
    "-p:Version=$Version",
    '-p:UsedAvaloniaProducts='
) + $vlcArchitectureProperties

if ($NoRestore) {
    $publishArguments += '--no-restore'
}

& $dotnet @publishArguments
Assert-Success "Publikowanie $Runtime"

$symbolFiles = Get-ChildItem -LiteralPath $publishDirectory -Recurse -File -Filter '*.pdb'
foreach ($symbolFile in $symbolFiles) {
    Assert-SafeReleasePath $symbolFile.FullName
    Remove-Item -LiteralPath $symbolFile.FullName -Force
}
if (Get-ChildItem -LiteralPath $publishDirectory -Recurse -File -Filter '*.pdb' | Select-Object -First 1) {
    throw 'Publikacja nadal zawiera pliki symboli PDB.'
}

$application = Join-Path $publishDirectory 'TelePilot.exe'
if (-not (Test-Path -LiteralPath $application)) {
    throw "Brak programu w publikacji: $application"
}

$applicationVersionInfo = [Diagnostics.FileVersionInfo]::GetVersionInfo($application)
$expectedFileVersion = "$Version.0"
$productVersionWithoutBuildMetadata = ($applicationVersionInfo.ProductVersion -split '\+', 2)[0]
if ($productVersionWithoutBuildMetadata -ne $Version -or $applicationVersionInfo.FileVersion -ne $expectedFileVersion) {
    throw "Metadane TelePilot.exe nie odpowiadają wydaniu $Version (ProductVersion=$($applicationVersionInfo.ProductVersion), FileVersion=$($applicationVersionInfo.FileVersion))."
}

$expectedLibVlc = Join-Path $publishDirectory "libvlc\$Runtime\libvlc.dll"
if (-not (Test-Path -LiteralPath $expectedLibVlc)) {
    throw "Publikacja nie zawiera właściwej biblioteki: $expectedLibVlc"
}

$unexpectedVlcRuntimes = @('win-x86', 'win-x64', 'win-arm64') | Where-Object {
    $_ -ne $Runtime -and (Test-Path -LiteralPath (Join-Path $publishDirectory "libvlc\$_"))
}
if ($unexpectedVlcRuntimes) {
    throw "Publikacja zawiera biblioteki VLC dla niewłaściwej architektury: $($unexpectedVlcRuntimes -join ', ')"
}

& (Join-Path $PSScriptRoot 'collect-windows-licenses.ps1') -PublishDirectory $publishDirectory -Runtime $Runtime
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'README.md') -Destination $publishDirectory
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'CHANGELOG.md') -Destination $publishDirectory

$privateFiles = Get-ChildItem -LiteralPath $publishDirectory -Recurse -File | Where-Object {
    $_.Name -ieq 'url.txt' -or $_.Extension -in @('.m3u', '.m3u8')
}
if ($privateFiles) {
    throw 'Publikacja zawiera niedozwolony plik playlisty lub url.txt.'
}

if (-not $InnoCompiler) {
    $candidates = @(
        (Join-Path $repositoryRoot '.tools\inno-setup-6\ISCC.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe')
    )
    $InnoCompiler = $candidates | Where-Object { $_ -and (Test-Path -LiteralPath $_) } | Select-Object -First 1
}

if (-not $InnoCompiler -or -not (Test-Path -LiteralPath $InnoCompiler)) {
    throw 'Nie znaleziono ISCC.exe. Podaj -InnoCompiler albo zainstaluj Inno Setup 6.'
}

if ($CertificateThumbprint) {
    $ownBinaries = @(Get-ChildItem -LiteralPath $publishDirectory -File | Where-Object {
        $_.Name -eq 'TelePilot.exe' -or $_.Name -eq 'TelePilot.dll' -or $_.Name -like 'TelePilot.*.dll'
    } | ForEach-Object { $_.FullName })
    & (Join-Path $PSScriptRoot 'sign-windows-files.ps1') @signingArguments -Path $ownBinaries
}

& $InnoCompiler `
    '/Qp' `
    "/DAppVersion=$Version" `
    "/DRuntime=$Runtime" `
    "/DPublishDir=$publishDirectory" `
    "/DOutputDir=$releaseRoot" `
    $installerScript
Assert-Success 'Budowanie instalatora'

$installer = Join-Path $releaseRoot "TelePilot-$Version-$Runtime-setup.exe"
if (-not (Test-Path -LiteralPath $installer)) {
    throw "Brak gotowego instalatora: $installer"
}

if ($CertificateThumbprint) {
    & (Join-Path $PSScriptRoot 'sign-windows-files.ps1') @signingArguments -Path $installer
}

$hash = Get-FileHash -LiteralPath $installer -Algorithm SHA256
$checksumPath = "$installer.sha256"
Set-Content -LiteralPath $checksumPath -Value "$($hash.Hash.ToLowerInvariant())  $([IO.Path]::GetFileName($installer))" -Encoding ascii

$installerInfo = Get-Item -LiteralPath $installer
[pscustomobject]@{
    Installer = $installerInfo.FullName
    SizeMiB = [Math]::Round($installerInfo.Length / 1MB, 2)
    Sha256 = $hash.Hash.ToLowerInvariant()
    Runtime = $Runtime
    Version = $Version
    SelfContained = $true
    InstallScope = 'per-machine'
    Signature = (Get-AuthenticodeSignature -LiteralPath $installer).Status.ToString()
}
