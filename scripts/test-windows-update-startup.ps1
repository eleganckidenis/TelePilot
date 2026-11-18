# Exercise current source with a TEST-ONLY assembly version of 2.0.2 on a fresh CI runner.
# The real release binary and original public 2.0.2 are verified separately.
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Executable,
    [Parameter(Mandatory)][string]$ReferenceInstaller,
    [Parameter(Mandatory)][ValidateSet('win-x64', 'win-arm64')][string]$Runtime,
    [Parameter(Mandatory)][string]$EvidenceDirectory,
    [string]$ExpectedVersion = '',
    [int]$TimeoutSeconds = 120
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted') {
    throw 'This probe requires a fresh disposable GitHub-hosted Windows runner.'
}
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class NativeUpdaterForeground {
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
}
'@
$Executable = (Resolve-Path -LiteralPath $Executable).Path
$ReferenceInstaller = (Resolve-Path -LiteralPath $ReferenceInstaller).Path
$qaInfo = (Get-Item -LiteralPath $Executable).VersionInfo
$qaVersion = "$($qaInfo.FileMajorPart).$($qaInfo.FileMinorPart).$($qaInfo.FileBuildPart)"
if ($qaVersion -cne '2.0.2') { throw "Expected the current-source QA executable with test assembly version 2.0.2, found $qaVersion." }
$profile = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)) 'TelePilot'
if (Test-Path -LiteralPath $profile) { throw 'A TelePilot profile already exists; refusing to inspect or alter it.' }
New-Item -ItemType Directory -Path $EvidenceDirectory -Force | Out-Null
$release = Invoke-RestMethod -Uri 'https://api.github.com/repos/eleganckidenis/TelePilot/releases/latest' `
    -UserAgent 'TelePilot-LegacyStartup-Probe' -Headers @{Accept='application/vnd.github+json';'X-GitHub-Api-Version'='2022-11-28'}
if (-not $ExpectedVersion) { $ExpectedVersion = $release.tag_name.Substring(1) }
if ($release.draft -or $release.prerelease -or $release.tag_name -cne "v$ExpectedVersion") { throw 'Unexpected latest public release.' }
$name = "TelePilot-$ExpectedVersion-$Runtime-setup.exe"
$assets = @($release.assets | Where-Object { $_.name -ceq $name })
if ($assets.Count -ne 1) { throw 'Missing or duplicate expected public installer.' }
$asset = $assets[0]
$hash = (Get-FileHash -LiteralPath $ReferenceInstaller -Algorithm SHA256).Hash.ToLowerInvariant()
if ($asset.state -cne 'uploaded' -or $asset.digest -cne "sha256:$hash" -or (Get-Item -LiteralPath $ReferenceInstaller).Length -ne $asset.size) {
    throw 'The reference installer does not match the real published asset.'
}
$updates = Join-Path $profile 'Updates'
New-Item -ItemType Directory -Path $updates -Force | Out-Null
Set-Content -LiteralPath (Join-Path $profile 'preferences.json') -Value '{"language":"en"}' -Encoding UTF8
# Intentionally do not seed the cache: exercise the real transfer after consent.
function Find-Control($Window, [string]$Id) {
    $condition = [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::AutomationIdProperty, $Id)
    $Window.FindFirst([Windows.Automation.TreeScope]::Descendants, $condition)
}
function Invoke-Control($Control) {
    $invoke = $null
    if ($null -eq $Control -or -not $Control.TryGetCurrentPattern([Windows.Automation.InvokePattern]::Pattern, [ref]$invoke)) {
        throw 'Required native UI Automation Invoke control is unavailable.'
    }
    $invoke.Invoke()
}
function Find-ReleaseNotes($Window) {
    # The Win32 EDIT supplies the Text pattern and its platform automation ID.
    $name = [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::NameProperty, 'Release notes')
    $edit = [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ControlTypeProperty, [Windows.Automation.ControlType]::Edit)
    $condition = [Windows.Automation.AndCondition]::new([Windows.Automation.Condition[]]@($name, $edit))
    $Window.FindFirst([Windows.Automation.TreeScope]::Descendants, $condition)
}
$app = Start-Process -FilePath $Executable -WorkingDirectory (Split-Path -Parent $Executable) -PassThru
$timer = [Diagnostics.Stopwatch]::StartNew()
$offer = $null
$notice = $null
$main = $null
$windowNames = @()
$progressSeen = $false
$nonmodalVerified = $false
$progressMessages = [Collections.Generic.List[string]]::new()
try {
    $condition = [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ProcessIdProperty, $app.Id)
    while ($timer.Elapsed.TotalSeconds -lt $TimeoutSeconds) {
        if ($app.HasExited) { throw "QA TelePilot exited before showing an update: $($app.ExitCode)." }
        try {
            $windows = [Windows.Automation.AutomationElement]::RootElement.FindAll([Windows.Automation.TreeScope]::Children, $condition)
            $windowNames = @($windows | ForEach-Object { $_.Current.Name })
            $progressVisibleThisPass = $false
            foreach ($window in $windows) {
                $progressMessage = Find-Control $window 'update-progress-text'
                if ($null -ne $progressMessage -and -not $window.Current.IsOffscreen) {
                    if ($null -eq $offer -and $progressMessage.Current.Name -ceq "Version $ExpectedVersion is available. Download the update?") {
                        $notes = Find-ReleaseNotes $window
                        $download = Find-Control $window 'update-download'
                        $later = Find-Control $window 'update-later'
                        $textPattern = $null
                        if ($null -ne $notes -and $null -ne $download -and $null -ne $later -and
                            $notes.TryGetCurrentPattern([Windows.Automation.TextPattern]::Pattern, [ref]$textPattern)) {
                            $notesText = $textPattern.DocumentRange.GetText(-1)
                            if ([string]::IsNullOrWhiteSpace($notesText) -or $download.Current.Name -cne 'Download' -or $later.Current.Name -cne 'Later') {
                                throw 'The pre-download offer did not expose its accessible release notes and actions.'
                            }
                            $offer = @{ Message=$progressMessage.Current.Name; Download=$download; NotesLength=$notesText.Length; Consented=$false }
                        }
                    }
                    if ($null -ne (Find-Control $window 'update-progress-bar')) {
                        $progressVisibleThisPass = $true
                        $progressSeen = $true
                        if (-not $progressMessages.Contains($progressMessage.Current.Name)) { $progressMessages.Add($progressMessage.Current.Name) }
                    }
                }
                $message = Find-Control $window 'confirmation-dialog-content'
                if ($null -ne $message -and $message.Current.Name -ceq "Version $ExpectedVersion has been downloaded. Open the installer?") {
                    $notice = @{ Window=$window; Message=$message.Current.Name; Cancel=(Find-Control $window 'confirmation-dialog-cancel'); Confirm=(Find-Control $window 'confirmation-dialog-confirm') }
                    continue
                }
                if ($null -ne (Find-Control $window 'tab-live')) { $main = $window }
            }
            if ($null -ne $offer -and -not $offer.Consented -and $null -ne $main) {
                if (-not $main.Current.IsEnabled) { throw 'The pre-download offer disabled its owner.' }
                Start-Sleep -Milliseconds 500
                if (@(Get-ChildItem -LiteralPath $updates -Force).Count -ne 0) {
                    throw 'The updater wrote files before receiving consent.'
                }
                Invoke-Control $offer.Download
                $offer.Consented = $true
                $nonmodalVerified = $true
            }
            if ($progressVisibleThisPass -and $null -eq $notice -and $null -ne $main) {
                if (-not $main.Current.IsEnabled) { throw 'The download progress window disabled its owner.' }
                $nonmodalVerified = $true
            }
            if ($null -ne $notice) { break }
            if ($null -ne $main -and $main.Current.IsEnabled -and -not $main.Current.IsOffscreen) {
                [void][NativeUpdaterForeground]::SetForegroundWindow([IntPtr]$main.Current.NativeWindowHandle)
            }
        } catch [Windows.Automation.ElementNotAvailableException] { }
        Start-Sleep -Milliseconds 250
    }
    if ($null -eq $offer -or -not $offer.Consented -or $null -eq $notice) {
        throw "Current-source native updater did not complete the offer and consent flow; windows: $($windowNames -join ', ')."
    }
    if ($notice.Confirm.Current.Name -cne 'Open installer' -or $notice.Cancel.Current.Name -cne 'Later') {
        throw 'The updater did not expose its expected accessible installer controls.'
    }
    $downloaded = Join-Path $updates $name
    if ((Get-FileHash -LiteralPath $downloaded -Algorithm SHA256).Hash.ToLowerInvariant() -cne $hash) {
        throw 'The actual downloaded installer differs from the verified public reference.'
    }
    $evidence = [ordered]@{
        testExecutableVersion=$qaVersion; runtime=$Runtime; processId=$app.Id
        latestPublicVersion=$ExpectedVersion; referenceInstaller=$name; referenceSha256=$hash
        downloadedBytes=(Get-Item -LiteralPath (Join-Path $updates $name)).Length
        offerMessage=$offer.Message; releaseNotesLength=$offer.NotesLength; cacheEmptyBeforeConsent=$true
        progressObserved=$progressSeen; nonmodalOwnerVerified=$nonmodalVerified; progressMessages=$progressMessages.ToArray()
        sourceCommit=$env:GITHUB_SHA
        actualStartupNotice=$notice.Message; noticeTitle=$notice.Window.Current.Name
        elapsedSeconds=[Math]::Round($timer.Elapsed.TotalSeconds, 3)
        scenario='Current release source, TEST-ONLY assembly version 2.0.2, real uncached public update, native Windows UI Automation; this is not the original 2.0.2 binary'
    }
    $evidence | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $EvidenceDirectory "new-updater-native-$Runtime.json") -Encoding UTF8
    Invoke-Control $notice.Cancel
    Write-Host "PASS: current-source native $Runtime updater offered $ExpectedVersion with release notes and verified the installer after $($evidence.elapsedSeconds)s; progressObserved=$progressSeen, nonmodal=$nonmodalVerified."
} finally {
    if (-not $app.HasExited) {
        try {
            if ($null -ne $main) {
                $close = $null
                if ($main.TryGetCurrentPattern([Windows.Automation.WindowPattern]::Pattern, [ref]$close)) { $close.Close() }
            }
        } catch [Windows.Automation.ElementNotAvailableException] { }
        if (-not $app.WaitForExit(10000)) {
            $app.Kill(); $app.WaitForExit()
            throw 'The native updater application did not shut down within ten seconds.'
        }
    }
}
