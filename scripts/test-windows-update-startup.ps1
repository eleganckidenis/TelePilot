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
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
public static class NativeUpdaterWindows {
    private delegate bool EnumWindowProc(IntPtr window, IntPtr state);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowProc callback, IntPtr state);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr parent, EnumWindowProc callback, IntPtr state);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr window, uint command);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder value, int capacity);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, StringBuilder value, int capacity);
    [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr window);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] public static extern bool IsWindowEnabled(IntPtr window);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();

    public static IntPtr Owner(IntPtr window) { return GetWindow(window, 4); } // GW_OWNER
    public static uint ProcessId(IntPtr window) {
        uint id;
        GetWindowThreadProcessId(window, out id);
        return id;
    }
    public static string ClassName(IntPtr window) {
        var value = new StringBuilder(64);
        GetClassName(window, value, value.Capacity);
        return value.ToString();
    }
    public static string Caption(IntPtr window) {
        var value = new StringBuilder(120);
        GetWindowText(window, value, value.Capacity);
        return value.ToString();
    }
    public static IntPtr[] TopLevelWindows(int processId) {
        var windows = new List<IntPtr>();
        EnumWindows((window, state) => {
            if (ProcessId(window) == processId) windows.Add(window);
            return true;
        }, IntPtr.Zero);
        return windows.ToArray();
    }
    public static IntPtr[] VisibleEditors(IntPtr parent, int processId) {
        var windows = new List<IntPtr>();
        EnumChildWindows(parent, (window, state) => {
            if (ProcessId(window) == processId && IsWindowVisible(window)
                && string.Equals(ClassName(window), "Edit", StringComparison.OrdinalIgnoreCase)) windows.Add(window);
            return true;
        }, IntPtr.Zero);
        return windows.ToArray();
    }
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
function Get-WindowElement([IntPtr]$Handle) {
    try { [Windows.Automation.AutomationElement]::FromHandle($Handle) }
    catch [ArgumentException] {
        # A short-lived progress window can close between enumeration and lookup.
        if ([NativeUpdaterWindows]::IsWindow($Handle)) { throw }
    }
}
function Invoke-Control($Control) {
    $invoke = $null
    if ($null -eq $Control -or -not $Control.TryGetCurrentPattern([Windows.Automation.InvokePattern]::Pattern, [ref]$invoke)) {
        throw 'Required native UI Automation Invoke control is unavailable.'
    }
    $invoke.Invoke()
}
function Read-ReleaseNotes([IntPtr]$WindowHandle, [int]$ProcessId, $Inspection) {
    # The hosted Win32 EDIT may not be reachable through the Avalonia provider's
    # descendant tree. Query that child HWND's own UIA provider directly.
    foreach ($editor in [NativeUpdaterWindows]::VisibleEditors($WindowHandle, $ProcessId)) {
        $notes = Get-WindowElement $editor
        if ($null -eq $notes) { continue }
        $properties = $notes.Current
        $textPattern = $null
        $hasTextPattern = $notes.TryGetCurrentPattern([Windows.Automation.TextPattern]::Pattern, [ref]$textPattern)
        $entry = [ordered]@{
            handle=$editor.ToInt64(); name=$properties.Name; controlType=$properties.ControlType.ProgrammaticName
            offscreen=$properties.IsOffscreen; textPattern=$hasTextPattern; length=0
        }
        $Inspection.Add($entry)
        if ($properties.ControlType -ne [Windows.Automation.ControlType]::Edit -or
            $properties.Name -cne 'Release notes' -or $properties.IsOffscreen -or -not $hasTextPattern) { continue }
        $notesText = $textPattern.DocumentRange.GetText(-1)
        if (-not [string]::IsNullOrWhiteSpace($notesText)) {
            $entry.length = $notesText.Length
            $entry.isPlaceholder = $notesText.Trim() -ceq 'Release notes for this version are unavailable.'
            if (-not $entry.isPlaceholder) { return $notesText }
        }
    }
}
$app = Start-Process -FilePath $Executable -WorkingDirectory (Split-Path -Parent $Executable) -PassThru
$timer = [Diagnostics.Stopwatch]::StartNew()
$offer = $null
$notice = $null
$main = $null
$mainHandle = [IntPtr]::Zero
$observedWindows = [ordered]@{}
$controlSamples = [ordered]@{}
$lastOfferInspection = $null
$lastActivation = $null
$progressSeen = $false
$nonmodalVerified = $false
$progressMessages = [Collections.Generic.List[string]]::new()
try {
    while ($timer.Elapsed.TotalSeconds -lt $TimeoutSeconds) {
        if ($app.HasExited) { throw "QA TelePilot exited before showing an update: $($app.ExitCode)." }
        $handles = [NativeUpdaterWindows]::TopLevelWindows($app.Id)
        $progressVisibleThisPass = $false
        $ownedWindowVisible = $false
        foreach ($handle in $handles) {
            $owner = [NativeUpdaterWindows]::Owner($handle)
            $visible = [NativeUpdaterWindows]::IsWindowVisible($handle)
            $sample = [ordered]@{
                handle=$handle.ToInt64(); owner=$owner.ToInt64(); visible=$visible
                caption=[NativeUpdaterWindows]::Caption($handle); class=[NativeUpdaterWindows]::ClassName($handle)
                nativeEnabled=[NativeUpdaterWindows]::IsWindowEnabled($handle)
            }
            $key = [string]$handle.ToInt64()
            $observedWindows[$key] = $sample
            if (-not $visible) { continue }
            if ($owner -ne [IntPtr]::Zero) { $ownedWindowVisible = $true }
            try {
                $window = Get-WindowElement $handle
                if ($null -eq $window) { continue }
                $properties = $window.Current
                $sample.uiaName = $properties.Name
                $sample.uiaId = $properties.AutomationId
                $sample.uiaOffscreen = $properties.IsOffscreen
                $sample.uiaEnabled = $properties.IsEnabled
                if (-not $controlSamples.Contains($key)) {
                    $controls = $window.FindAll([Windows.Automation.TreeScope]::Descendants, [Windows.Automation.Condition]::TrueCondition)
                    $controlSamples[$key] = @($controls | ForEach-Object { $_.Current.AutomationId } |
                        Where-Object { $_ } | Select-Object -First 12)
                }
                $liveTab = Find-Control $window 'tab-live'
                if ($null -ne $liveTab) {
                    $main = $window
                    $mainHandle = $handle
                    $sample.liveTabName = $liveTab.Current.Name
                }
                $progressMessage = Find-Control $window 'update-progress-text'
                if ($null -ne $progressMessage) {
                    $sample.message = $progressMessage.Current.Name
                    if ($null -eq $offer -and $sample.message -ceq "Version $ExpectedVersion is available. Download the update?") {
                        $editorInspection = [Collections.Generic.List[object]]::new()
                        $notesText = Read-ReleaseNotes $handle $app.Id $editorInspection
                        $download = Find-Control $window 'update-download'
                        $later = Find-Control $window 'update-later'
                        $downloadInvoke = $null
                        $laterInvoke = $null
                        $lastOfferInspection = [ordered]@{
                            message=$sample.message; handle=$handle.ToInt64(); offscreen=$properties.IsOffscreen
                            editors=$editorInspection.ToArray()
                            downloadName=$(if ($null -ne $download) { $download.Current.Name })
                            laterName=$(if ($null -ne $later) { $later.Current.Name })
                            downloadInvoke=($null -ne $download -and $download.TryGetCurrentPattern([Windows.Automation.InvokePattern]::Pattern, [ref]$downloadInvoke))
                            laterInvoke=($null -ne $later -and $later.TryGetCurrentPattern([Windows.Automation.InvokePattern]::Pattern, [ref]$laterInvoke))
                        }
                        if ($null -ne $notesText -and -not $properties.IsOffscreen -and
                            $lastOfferInspection.downloadName -ceq 'Download' -and $lastOfferInspection.laterName -ceq 'Later' -and
                            $lastOfferInspection.downloadInvoke -and $lastOfferInspection.laterInvoke) {
                            $offer = @{ Message=$sample.message; Download=$download; NotesLength=$notesText.Length; Consented=$false }
                        }
                    }
                    $bar = Find-Control $window 'update-progress-bar'
                    if ($null -ne $bar -and -not $bar.Current.IsOffscreen -and -not $properties.IsOffscreen) {
                        $progressVisibleThisPass = $true
                        $progressSeen = $true
                        if (-not $progressMessages.Contains($sample.message)) { $progressMessages.Add($sample.message) }
                    }
                }
                $message = Find-Control $window 'confirmation-dialog-content'
                if ($null -ne $message) {
                    $sample.message = $message.Current.Name
                    if (-not $properties.IsOffscreen -and $sample.message -ceq "Version $ExpectedVersion has been downloaded. Open the installer?") {
                        $notice = @{ Window=$window; Message=$sample.message; Cancel=(Find-Control $window 'confirmation-dialog-cancel'); Confirm=(Find-Control $window 'confirmation-dialog-confirm') }
                    }
                }
            } catch [Windows.Automation.ElementNotAvailableException] { $sample.staleUiaElement = $true }
        }
        try {
            if ($null -ne $offer -and -not $offer.Consented -and $null -ne $main) {
                if (-not $main.Current.IsEnabled -or -not [NativeUpdaterWindows]::IsWindowEnabled($mainHandle)) { throw 'The pre-download offer disabled its owner.' }
                Start-Sleep -Milliseconds 500
                if (@(Get-ChildItem -LiteralPath $updates -Force).Count -ne 0) {
                    throw 'The updater wrote files before receiving consent.'
                }
                Invoke-Control $offer.Download
                $offer.Consented = $true
                $nonmodalVerified = $true
            }
            if ($progressVisibleThisPass -and $null -eq $notice -and $null -ne $main) {
                if (-not $main.Current.IsEnabled -or -not [NativeUpdaterWindows]::IsWindowEnabled($mainHandle)) { throw 'The download progress window disabled its owner.' }
                $nonmodalVerified = $true
            }
            if ($null -ne $notice) { break }
            $foreground = [NativeUpdaterWindows]::GetForegroundWindow()
            if ($null -ne $main -and -not $ownedWindowVisible -and $foreground -ne $mainHandle -and
                $main.Current.IsEnabled -and -not $main.Current.IsOffscreen) {
                # Startup and the ready-to-install prompt wait for an active owner.
                # Never take focus away from an owned window that's already open.
                $lastActivation = [ordered]@{
                    from=$foreground.ToInt64(); previousProcessId=[NativeUpdaterWindows]::ProcessId($foreground)
                    succeeded=[NativeUpdaterWindows]::SetForegroundWindow($mainHandle)
                }
            }
        } catch [Windows.Automation.ElementNotAvailableException] { }
        Start-Sleep -Milliseconds 250
    }
    if ($null -eq $offer -or -not $offer.Consented -or $null -eq $notice) {
        throw 'Current-source native updater did not complete the offer and consent flow.'
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
} catch {
    $foreground = [NativeUpdaterWindows]::GetForegroundWindow()
    $diagnostics = [ordered]@{
        failure=$_.Exception.Message; runtime=$Runtime; processId=$app.Id; elapsedSeconds=[Math]::Round($timer.Elapsed.TotalSeconds, 3)
        foregroundHandle=$foreground.ToInt64(); foregroundProcessId=[NativeUpdaterWindows]::ProcessId($foreground)
        mainHandle=$mainHandle.ToInt64(); lastActivation=$lastActivation; windows=@($observedWindows.Values); controlSamples=$controlSamples
        offerInspection=$lastOfferInspection; consented=($null -ne $offer -and $offer.Consented)
        progressMessages=$progressMessages.ToArray(); updateCacheEntries=@(Get-ChildItem -LiteralPath $updates -Force).Count
    }
    $diagnostics | ConvertTo-Json -Depth 7 | Set-Content -LiteralPath (Join-Path $EvidenceDirectory "new-updater-native-$Runtime-failure.json") -Encoding UTF8
    Write-Host (ConvertTo-Json -InputObject $diagnostics -Depth 7 -Compress)
    throw
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
