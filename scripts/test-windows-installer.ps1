<#
.SYNOPSIS
Tests the real release installer in a disposable GitHub Actions Windows runner.
#>
[CmdletBinding()]
param([Parameter(Mandatory)][string]$Installer, [Parameter(Mandatory)][string]$Version)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not $IsWindows -or $env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted' -or -not $env:RUNNER_TEMP) {
    throw 'This smoke test installs and uninstalls TelePilot on a disposable Windows runner only.'
}

$Installer = (Resolve-Path -LiteralPath $Installer).Path
$testDir = Join-Path $env:RUNNER_TEMP ('telepilot-install-' + [guid]::NewGuid())
$appDir = Join-Path $testDir 'app'
$exe = Join-Path $appDir 'TelePilot.exe'
$languageFile = Join-Path $appDir 'installer-language.txt'
function Get-NumericFileVersion([string]$File) {
    $info = (Get-Item -LiteralPath $File).VersionInfo
    "$($info.FileMajorPart).$($info.FileMinorPart).$($info.FileBuildPart)"
}
New-Item -ItemType Directory -Path $testDir | Out-Null

function Invoke-SetupProcess([string]$File, [string[]]$ArgumentList) {
    $process = Start-Process -FilePath $File -ArgumentList $ArgumentList -PassThru
    if (-not $process.WaitForExit(300000)) {
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
                Set-Content -LiteralPath $marker -Value 'keep'
            } elseif (-not (Test-Path -LiteralPath $marker -PathType Leaf)) {
                throw 'Reinstall unexpectedly removed a file it does not own.'
            }
            Write-Host "Verified Windows installation: $($case.Name)."
            if ($testNativeUi) {
                $firstRun = $case.Name -ne 'english'
                $uiLanguage = if ($firstRun) { $case.Expected } else { 'pl' }
                $uiArgs = @('-File', (Join-Path $PSScriptRoot 'test-windows-onboarding-uia.ps1'),
                    '-Executable', $exe, '-ExpectedLanguage', $uiLanguage)
                if ($firstRun) { $uiArgs += '-FirstRun' }
                & powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass @uiArgs
                if ($LASTEXITCODE -ne 0) { throw "Native UI Automation failed for $($case.Name)." }
                if ($case.Name -eq 'system') {
                    Move-Item -LiteralPath $profileDir -Destination (Join-Path $testDir 'profile-system')
                }
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
        if ((Test-Path -LiteralPath $exe) -or (Test-Path -LiteralPath $languageFile)) {
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
