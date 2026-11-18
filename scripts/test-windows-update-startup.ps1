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
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, Accessibility
Add-Type -ReferencedAssemblies ([Accessibility.IAccessible].Assembly.Location) -TypeDefinition @'
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
    [DllImport("oleacc.dll")]
    private static extern int AccessibleObjectFromWindow(IntPtr window, uint objectId, ref Guid iid,
        [MarshalAs(UnmanagedType.Interface)] out Accessibility.IAccessible accessible);

    public sealed class AccessibleEdit {
        public string Name { get; set; }
        public int Role { get; set; }
        public bool ReadOnly { get; set; }
        public string Value { get; set; }
    }

    public static AccessibleEdit ReadMsaaEdit(IntPtr editor) {
        var iid = typeof(Accessibility.IAccessible).GUID;
        Accessibility.IAccessible accessible;
        if (AccessibleObjectFromWindow(editor, unchecked((uint)-4), ref iid, out accessible) != 0 || accessible == null)
            return null; // OBJID_CLIENT
        try {
            const int self = 0; // CHILDID_SELF
            var role = Convert.ToInt32(accessible.get_accRole(self));
            var readOnly = (Convert.ToInt32(accessible.get_accState(self)) & 0x40) != 0; // STATE_SYSTEM_READONLY
            return new AccessibleEdit {
                Name = accessible.get_accName(self), Role = role, ReadOnly = readOnly,
                Value = role == 42 && readOnly ? accessible.get_accValue(self) : null, // ROLE_SYSTEM_TEXT
            };
        }
        catch (COMException) { return null; }
        finally { Marshal.ReleaseComObject(accessible); }
    }

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
function Get-RawSubtree($Root) {
    if ($null -eq $Root) { return }
    $walker = [Windows.Automation.TreeWalker]::RawViewWalker
    $pending = [Collections.Generic.Queue[Windows.Automation.AutomationElement]]::new()
    $pending.Enqueue($Root)
    for ($visited = 0; $visited -lt 24 -and $pending.Count -gt 0; $visited++) {
        $element = $pending.Dequeue()
        $element
        for ($child = $walker.GetFirstChild($element); $null -ne $child; $child = $walker.GetNextSibling($child)) {
            $pending.Enqueue($child)
        }
    }
}
function Test-ReleaseNotesText([string]$Notes, $Entry) {
    $Entry.length = $Notes.Length
    $Entry.isPlaceholder = $Notes.Trim() -ceq 'Release notes for this version are unavailable.'
    -not $Entry.isPlaceholder -and -not [string]::IsNullOrWhiteSpace($Notes)
}
function Read-ReleaseNotes($Window, [IntPtr]$WindowHandle, [int]$ProcessId, $Inspection) {
    # The Avalonia peer, an interop pane and the actual EDIT can be distinct nodes.
    $namedNotes = Find-Control $Window 'update-release-notes'
    $editors = [NativeUpdaterWindows]::VisibleEditors($WindowHandle, $ProcessId)
    $roots = @($namedNotes) + @($editors | ForEach-Object { Get-WindowElement $_ })
    $valueResult = $null
    foreach ($root in $roots) {
        $underNamedNotes = $null -ne $namedNotes -and $null -ne $root -and
            [Windows.Automation.Automation]::Compare($root, $namedNotes) -and $namedNotes.Current.Name -ceq 'Release notes'
        foreach ($notes in (Get-RawSubtree $root)) {
            $properties = $notes.Current
            if ($properties.ProcessId -ne $ProcessId) { continue }
            $text = $null
            $value = $null
            $entry = [ordered]@{
                handle=$properties.NativeWindowHandle; id=$properties.AutomationId; name=$properties.Name
                controlType=$properties.ControlType.ProgrammaticName; offscreen=$properties.IsOffscreen
                textPattern=$notes.TryGetCurrentPattern([Windows.Automation.TextPattern]::Pattern, [ref]$text)
                valuePattern=$notes.TryGetCurrentPattern([Windows.Automation.ValuePattern]::Pattern, [ref]$value)
            }
            $Inspection.Add($entry)
            $nativeHandle = [IntPtr]$properties.NativeWindowHandle
            $isNotes = $properties.Name -ceq 'Release notes' -or
                ($underNamedNotes -and ($properties.ControlType -eq [Windows.Automation.ControlType]::Edit -or
                    $editors -contains $nativeHandle))
            if (-not $isNotes -or $properties.IsOffscreen) { continue }
            if ($entry.textPattern) {
                $notesText = $text.DocumentRange.GetText(-1)
                if (Test-ReleaseNotesText $notesText $entry) { return @{ Text=$notesText; Provider='UIA TextPattern' } }
            }
            if ($entry.valuePattern -and $value.Current.IsReadOnly) {
                $notesText = $value.Current.Value
                if (Test-ReleaseNotesText $notesText $entry) { $valueResult = @{ Text=$notesText; Provider='UIA ValuePattern' } }
            }
        }
    }
    if ($null -ne $valueResult) { return $valueResult }

    # Read the native edit through MSAA when UIA supplies only a wrapper pane.
    foreach ($editor in $editors) {
        $notes = [NativeUpdaterWindows]::ReadMsaaEdit($editor)
        if ($null -eq $notes) { continue }
        $entry = [ordered]@{ handle=$editor.ToInt64(); msaaName=$notes.Name; role=$notes.Role; readOnly=$notes.ReadOnly }
        $Inspection.Add($entry)
        if ($notes.Role -eq 42 -and $notes.ReadOnly -and $notes.Name -ceq 'Release notes' -and
            (Test-ReleaseNotesText $notes.Value $entry)) {
            return @{ Text=$notes.Value; Provider='MSAA accValue' }
        }
    }
}
$app = Start-Process -FilePath $Executable -WorkingDirectory (Split-Path -Parent $Executable) -PassThru
$timer = [Diagnostics.Stopwatch]::StartNew()
$offer = $null
$main = $null
$mainHandle = [IntPtr]::Zero
$observedWindows = [ordered]@{}
$controlSamples = [ordered]@{}
$lastOfferInspection = $null
$lastActivation = $null
try {
    while ($timer.Elapsed.TotalSeconds -lt $TimeoutSeconds) {
        if ($app.HasExited) { throw "QA TelePilot exited before showing an update: $($app.ExitCode)." }
        $handles = [NativeUpdaterWindows]::TopLevelWindows($app.Id)
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
                # Avalonia exposes owned windows in the main UIA subtree as well.
                # Match the top-level element's own ID before querying descendants.
                if ($properties.AutomationId -ceq 'main-window') {
                    $main = $window
                    $mainHandle = $handle
                    $liveTab = Find-Control $window 'tab-live'
                    if ($null -ne $liveTab) { $sample.liveTabName = $liveTab.Current.Name }
                    continue
                }
                $progressMessage = if ($properties.AutomationId -ceq 'update-progress-window') { Find-Control $window 'update-progress-text' }
                if ($null -ne $progressMessage) {
                    $sample.message = $progressMessage.Current.Name
                    if ($null -eq $offer -and $sample.message.StartsWith("Version $ExpectedVersion is available. After downloading, TelePilot will close,")) {
                        $editorInspection = [Collections.Generic.List[object]]::new()
                        $notes = Read-ReleaseNotes $window $handle $app.Id $editorInspection
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
                        if ($null -ne $notes -and -not $properties.IsOffscreen -and
                            $lastOfferInspection.downloadName -ceq 'Download' -and $lastOfferInspection.laterName -ceq 'Later' -and
                            $lastOfferInspection.downloadInvoke -and $lastOfferInspection.laterInvoke) {
                            $offer = @{ Message=$sample.message; Later=$later; Handle=$handle; NotesLength=$notes.Text.Length; NotesProvider=$notes.Provider }
                        }
                    }
                }
            } catch [Windows.Automation.ElementNotAvailableException] { $sample.staleUiaElement = $true }
        }
        try {
            if ($null -ne $offer -and $null -ne $main) {
                if (-not $main.Current.IsEnabled -or -not [NativeUpdaterWindows]::IsWindowEnabled($mainHandle)) { throw 'The pre-download offer disabled its owner.' }
                Start-Sleep -Milliseconds 500
                if (@(Get-ChildItem -LiteralPath $updates -Force).Count -ne 0) {
                    throw 'The updater wrote files before receiving consent.'
                }
                break
            }
            $foreground = [NativeUpdaterWindows]::GetForegroundWindow()
            if ($null -ne $main -and -not $ownedWindowVisible -and $foreground -ne $mainHandle -and
                $main.Current.IsEnabled -and -not $main.Current.IsOffscreen) {
                # Startup waits for an active owner.
                # Never take focus away from an owned window that's already open.
                $lastActivation = [ordered]@{
                    from=$foreground.ToInt64(); previousProcessId=[NativeUpdaterWindows]::ProcessId($foreground)
                    succeeded=[NativeUpdaterWindows]::SetForegroundWindow($mainHandle)
                }
            }
        } catch [Windows.Automation.ElementNotAvailableException] { }
        Start-Sleep -Milliseconds 250
    }
    if ($null -eq $offer -or $null -eq $main) {
        throw 'Current-source native updater did not present the startup offer.'
    }
    Invoke-Control $offer.Later
    $dismissal = [Diagnostics.Stopwatch]::StartNew()
    while ([NativeUpdaterWindows]::IsWindowVisible($offer.Handle) -and $dismissal.Elapsed.TotalSeconds -lt 5) {
        Start-Sleep -Milliseconds 100
    }
    if ([NativeUpdaterWindows]::IsWindowVisible($offer.Handle) -or $app.HasExited -or
        -not [NativeUpdaterWindows]::IsWindowEnabled($mainHandle) -or @(Get-ChildItem -LiteralPath $updates -Force).Count -ne 0) {
        throw 'Later failed to dismiss the offer, preserve the open main window, or skip the download.'
    }
    $evidence = [ordered]@{
        testExecutableVersion=$qaVersion; runtime=$Runtime; processId=$app.Id
        latestPublicVersion=$ExpectedVersion; referenceInstaller=$name; referenceSha256=$hash
        offerMessage=$offer.Message; releaseNotesLength=$offer.NotesLength; releaseNotesProvider=$offer.NotesProvider
        laterDismissedWithoutDownload=$true; nonmodalOwnerVerified=$true
        sourceCommit=$env:GITHUB_SHA
        elapsedSeconds=[Math]::Round($timer.Elapsed.TotalSeconds, 3)
        scenario='Current-source QA app at version 2.0.2, public GitHub metadata, native Windows UI Automation, and deferral via Later'
    }
    $evidence | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $EvidenceDirectory "new-updater-native-$Runtime.json") -Encoding UTF8
    Write-Host "PASS: current-source native $Runtime updater offered $ExpectedVersion with release notes and deferred the update after $($evidence.elapsedSeconds)s."
} catch {
    $foreground = [NativeUpdaterWindows]::GetForegroundWindow()
    $diagnostics = [ordered]@{
        failure=$_.Exception.Message; runtime=$Runtime; processId=$app.Id; elapsedSeconds=[Math]::Round($timer.Elapsed.TotalSeconds, 3)
        foregroundHandle=$foreground.ToInt64(); foregroundProcessId=[NativeUpdaterWindows]::ProcessId($foreground)
        mainHandle=$mainHandle.ToInt64(); lastActivation=$lastActivation; windows=@($observedWindows.Values); controlSamples=$controlSamples
        offerInspection=$lastOfferInspection; updateCacheEntries=@(Get-ChildItem -LiteralPath $updates -Force).Count
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
