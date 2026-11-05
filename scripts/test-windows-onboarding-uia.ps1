# Run using Windows PowerShell on the disposable desktop used to test the installer.
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Executable,
    [Parameter(Mandatory)][ValidateSet('en', 'pl')][string]$ExpectedLanguage,
    [switch]$FirstRun
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted') {
    throw 'This test requires a disposable GitHub-hosted Windows runner.'
}
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes

function Find-Control($Window, [string]$Id) {
    $condition = [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::AutomationIdProperty, $Id)
    $Window.FindFirst([Windows.Automation.TreeScope]::Descendants, $condition)
}

function Find-AppControl([Diagnostics.Process]$Process, [string]$Id, [int]$TimeoutSeconds = 30) {
    $condition = [Windows.Automation.PropertyCondition]::new(
        [Windows.Automation.AutomationElement]::ProcessIdProperty, $Process.Id)
    $timer = [Diagnostics.Stopwatch]::StartNew()
    do {
        if ($Process.HasExited) { throw "TelePilot exited early with code $($Process.ExitCode)." }
        try {
            $windows = [Windows.Automation.AutomationElement]::RootElement.FindAll(
                [Windows.Automation.TreeScope]::Children, $condition)
            foreach ($window in $windows) {
                $found = Find-Control $window $Id
                if ($null -ne $found) {
                    return @{ Window = $window; Control = $found }
                }
            }
        } catch [Windows.Automation.ElementNotAvailableException] {
            # An onboarding window can disappear while we enumerate it.
        }
        Start-Sleep -Milliseconds 200
    } while ($timer.Elapsed.TotalSeconds -lt $TimeoutSeconds)
    return $null
}

function Invoke-Button($Control) {
    $invoke = $Control.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern)
    $invoke.Invoke()
}

$app = Start-Process -FilePath $Executable -WorkingDirectory (Split-Path -Parent $Executable) -PassThru
try {
    if ($FirstRun) {
        $onboarding = Find-AppControl $app 'first-run-language-continue'
        if ($null -eq $onboarding) { throw 'The first-run language chooser did not open.' }
        $continueLabel = if ($ExpectedLanguage -eq 'pl') { 'Dalej' } else { 'Continue' }
        if ($onboarding.Control.Current.Name -cne $continueLabel -or
            $null -eq (Find-Control $onboarding.Window 'first-run-language-picker')) {
            throw 'The first-run language controls are missing or in the wrong language.'
        }
        Invoke-Button $onboarding.Control

        $manager = Find-AppControl $app 'close-playlist-manager'
        if ($null -eq $manager) { throw 'The Playlist Manager did not open automatically.' }
        $add = Find-Control $manager.Window 'add-file-source'
        $addLabel = if ($ExpectedLanguage -eq 'pl') { 'Dodaj playlist? z pliku' } else { 'Add playlist from file' }
        if ($null -eq $add -or $add.Current.Name -cnotlike $addLabel) {
            throw 'The Playlist Manager add control is missing or in the wrong language.'
        }
        Invoke-Button $manager.Control
    }

    $main = Find-AppControl $app 'tab-live'
    if ($null -eq $main) { throw 'The main window did not open.' }
    $liveLabel = if ($ExpectedLanguage -eq 'pl') { 'TV na ?ywo' } else { 'Live TV' }
    if ($main.Control.Current.Name -cnotlike $liveLabel) { throw 'The main window is in the wrong language.' }
    if ($null -ne (Find-Control $main.Window 'playlist-source-selector')) {
        throw 'The main window still exposes the playlist selector.'
    }
    if (-not $FirstRun -and $null -ne (Find-AppControl $app 'close-playlist-manager' -TimeoutSeconds 2)) {
        throw 'The manager unexpectedly opened on an existing profile.'
    }

    $close = $main.Window.GetCurrentPattern([Windows.Automation.WindowPattern]::Pattern)
    $close.Close()
    if (-not $app.WaitForExit(10000) -or $app.ExitCode -ne 0) { throw 'TelePilot did not shut down cleanly.' }
    Write-Host "Verified native Windows UI Automation (first run: $FirstRun; language: $ExpectedLanguage)."
} finally {
    if (-not $app.HasExited) { $app.Kill(); $app.WaitForExit() }
}
