<#
.SYNOPSIS
Tests the real release installer in a disposable GitHub Actions Windows runner.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Installer,
    [Parameter(Mandatory)][string]$Version,
    [ValidateSet('win-x64', 'win-arm64')][string]$Runtime = 'win-x64'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not $IsWindows -or $env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted' -or -not $env:RUNNER_TEMP) {
    throw 'This smoke test installs and uninstalls TelePilot on a disposable Windows runner only.'
}
. (Join-Path $PSScriptRoot 'windows-package-helpers.ps1')
Assert-WindowsProcessArchitecture -Process ([Diagnostics.Process]::GetCurrentProcess()) -Runtime $Runtime

$Installer = (Resolve-Path -LiteralPath $Installer).Path
$testDir = Join-Path $env:RUNNER_TEMP ('telepilot-install-' + [guid]::NewGuid())
$appDir = Join-Path $testDir 'app'
$exe = Join-Path $appDir 'TelePilot.exe'
$languageFile = Join-Path $appDir 'installer-language.txt'
$ffmpegPackage = Get-WindowsFfmpegDownload $Runtime
$ffmpegDirectory = Join-Path (Join-Path $appDir $ffmpegPackage.RelativeDirectory) ([IO.Path]::GetFileNameWithoutExtension($ffmpegPackage.Archive))
$ffmpeg = Join-Path $ffmpegDirectory 'bin\ffmpeg.exe'
$emptyPath = Join-Path $testDir 'empty-path'
function Get-NumericFileVersion([string]$File) {
    $info = (Get-Item -LiteralPath $File).VersionInfo
    "$($info.FileMajorPart).$($info.FileMinorPart).$($info.FileBuildPart)"
}
New-Item -ItemType Directory -Path $emptyPath -Force | Out-Null

function Invoke-SetupProcess([string]$File, [string[]]$ArgumentList) {
    $originalPath = $env:PATH
    try {
        $env:PATH = $emptyPath
        $process = Start-Process -FilePath $File -ArgumentList $ArgumentList -PassThru
    } finally {
        $env:PATH = $originalPath
    }
    if (-not $process.WaitForExit(900000)) {
        $process.Kill($true)
        throw "Timed out: $File"
    }
    if ($process.ExitCode -ne 0) { throw "$File returned exit code $($process.ExitCode)." }
}

if ((Get-NumericFileVersion $Installer) -ne $Version) {
    throw 'Setup file version does not match the release.'
}

$setupArguments = @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/SP-', "/DIR=`"$appDir`"", '/TASKS=""')
# The GitHub-hosted runner's UI is English. Verify the automatic selection and
# both explicit choices on the same installation, including preservation of data.
$systemLanguage = if ((Get-UICulture).TwoLetterISOLanguageName -eq 'pl') { 'pl' } else { 'en' }
$cases = @(
    @{ Name = 'system'; Option = ''; Expected = $systemLanguage }
    @{ Name = 'polish'; Option = '/LANG=polish'; Expected = 'pl' }
    @{ Name = 'english'; Option = '/LANG=english'; Expected = 'en' }
)
$marker = Join-Path $appDir 'telepilot-smoke-marker.txt'
$obsoletePlugin = Join-Path $appDir "libvlc\$Runtime\plugins\access\libtelepilot_stale_plugin.dll"
$otherRuntime = if ($Runtime -eq 'win-arm64') { 'win-x64' } else { 'win-arm64' }
$otherVlcDirectory = Join-Path $appDir "libvlc\$otherRuntime"
$testNativeUi = [Environment]::UserInteractive
$profileDir = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)) 'TelePilot'
if ($testNativeUi -and (Test-Path -LiteralPath $profileDir)) {
    throw 'The native UI test requires an empty user profile.'
}
if (-not $testNativeUi) { Write-Warning 'Native UI Automation skipped: this runner has no interactive desktop.' }
$installationsPassed = $false
try {
    foreach ($case in $cases) {
        $log = Join-Path $testDir "$($case.Name).log"
        $arguments = $setupArguments + "/LOG=`"$log`""
        if ($case.Option) { $arguments += $case.Option }
        try {
            Invoke-SetupProcess $Installer $arguments
            if ((Get-Content -LiteralPath $languageFile -Raw).Trim() -cne $case.Expected) {
                throw "Incorrect installer language for $($case.Name)."
            }
            if ((Get-NumericFileVersion $exe) -ne $Version) {
                throw 'The installed executable does not match the release version.'
            }
            if (-not (Test-Path -LiteralPath (Join-Path $appDir 'licenses/nuget-inventory.json') -PathType Leaf) -or
                -not (Test-Path -LiteralPath (Join-Path $appDir 'licenses/vlc-binaries.json') -PathType Leaf)) {
                throw 'The installed application is missing its dependency notices.'
            }
            if ($case.Name -eq 'system') {
                Assert-WindowsPayloadArchitecture -Directory $appDir -Runtime $Runtime
                Assert-WindowsBinaryArchitecture -LiteralPath $ffmpeg -Runtime $Runtime
                $ffmpegVersion = & $ffmpeg -version
                if ($LASTEXITCODE -ne 0 -or ($ffmpegVersion | Select-Object -First 1) -notmatch '^ffmpeg version ') {
                    throw 'The installed FFmpeg did not report its version.'
                }
                Set-Content -LiteralPath $marker -Value 'keep'
            } else {
                if (-not (Test-Path -LiteralPath $marker -PathType Leaf)) {
                    throw 'Reinstall unexpectedly removed a file it does not own.'
                }
                if ($case.Name -eq 'polish') {
                    if ((Test-Path -LiteralPath $obsoletePlugin) -or (Test-Path -LiteralPath $otherVlcDirectory)) {
                        throw 'Upgrade left a stale VLC plugin or wrong-architecture VLC directory.'
                    }
                    Assert-WindowsPayloadArchitecture -Directory $appDir -Runtime $Runtime
                }
            }
            Write-Host "Verified Windows installation: $($case.Name)."
            if ($testNativeUi) {
                $firstRun = $case.Name -ne 'english'
                $uiLanguage = if ($firstRun) { $case.Expected } else { 'pl' }
                $uiArgs = @('-File', (Join-Path $PSScriptRoot 'test-windows-onboarding-uia.ps1'),
                    '-Executable', $exe, '-ExpectedLanguage', $uiLanguage, '-Runtime', $Runtime)
                if ($firstRun) { $uiArgs += '-FirstRun' } else { $uiArgs += '-VerifySettingsLanguagePersistence' }
                & powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass @uiArgs
                if ($LASTEXITCODE -ne 0) { throw "Native UI Automation failed for $($case.Name)." }
                if ($case.Name -eq 'system') {
                    Move-Item -LiteralPath $profileDir -Destination (Join-Path $testDir 'profile-system')
                }
            }
            if ($case.Name -eq 'system') {
                Set-Content -LiteralPath $obsoletePlugin -Value 'obsolete plugin fixture'
                New-Item -ItemType Directory -Path $otherVlcDirectory | Out-Null
                Set-Content -LiteralPath (Join-Path $otherVlcDirectory 'libvlc.dll') -Value 'old architecture fixture'
            }
        } catch {
            if (Test-Path -LiteralPath $log) { Get-Content -LiteralPath $log -Tail 50 }
            throw
        }
    }
    $installationsPassed = $true
} finally {
    $uninstall = @(Get-ChildItem -LiteralPath $appDir -Filter 'unins*.exe' -ErrorAction SilentlyContinue)
    if ($installationsPassed -and $uninstall.Count -ne 1) {
        throw 'Expected exactly one registered uninstaller.'
    }
    if ($uninstall.Count -eq 1) {
        Invoke-SetupProcess $uninstall[0].FullName @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART')
        if ((Test-Path -LiteralPath $exe) -or (Test-Path -LiteralPath $languageFile) -or (Test-Path -LiteralPath $ffmpeg)) {
            throw 'Uninstall left application files behind.'
        }
        if ($installationsPassed -and (
            -not (Test-Path -LiteralPath $marker -PathType Leaf) -or
            ($testNativeUi -and -not (Test-Path -LiteralPath (Join-Path $profileDir 'preferences.json') -PathType Leaf))
        )) {
            throw 'Uninstall removed files owned by the user.'
        }
        Write-Host 'Verified Windows uninstall and user data preservation.'
    }
}
