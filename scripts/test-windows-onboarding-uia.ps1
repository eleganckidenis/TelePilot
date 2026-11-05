# Run using Windows PowerShell on the disposable desktop used to test the installer.
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Executable,
    [Parameter(Mandatory)][ValidateSet('en', 'pl')][string]$ExpectedLanguage,
    [ValidateSet('win-x64', 'win-arm64')][string]$Runtime,
    [switch]$FirstRun,
    [switch]$VerifySettingsLanguagePersistence
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted') {
    throw 'This test requires a disposable GitHub-hosted Windows runner.'
}
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
if ($Runtime) { . (Join-Path $PSScriptRoot 'windows-package-helpers.ps1') }
if ($VerifySettingsLanguagePersistence) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Text;

public static class NativeLanguagePicker
{
    [DllImport("user32.dll")]
    private static extern bool SetPhysicalCursorPos(int x, int y);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll")]
    private static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extraInfo);

    public static void ActivateWindow(int window)
    {
        SetForegroundWindow(new IntPtr(window));
    }

    public static void ClickMenuItem(int x, int y)
    {
        if (!SetPhysicalCursorPos(x, y))
            throw new InvalidOperationException("Could not move the pointer to the menu item.");
        mouse_event(0x0002, 0, 0, 0, UIntPtr.Zero); // MOUSEEVENTF_LEFTDOWN
        mouse_event(0x0004, 0, 0, 0, UIntPtr.Zero); // MOUSEEVENTF_LEFTUP
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(IntPtr window, uint message, IntPtr wParam,
        IntPtr lParam, uint flags, uint timeout, out IntPtr result);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(IntPtr window, uint message, IntPtr wParam,
        StringBuilder lParam, uint flags, uint timeout, out IntPtr result);

    private delegate bool EnumChildProc(IntPtr window, IntPtr state);

    [DllImport("user32.dll")]
    private static extern bool EnumChildWindows(IntPtr parent, EnumChildProc callback, IntPtr state);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr window, StringBuilder className, int maxCount);

    public static int FindLanguagePicker(int settingsWindow)
    {
        int picker = 0;
        EnumChildWindows(new IntPtr(settingsWindow), (window, state) =>
        {
            var className = new StringBuilder(64);
            GetClassName(window, className, className.Capacity);
            if (className.ToString() != "ComboBox" || Send(window.ToInt32(), 0x0146, 0, 0) != 3)
                return true; // CB_GETCOUNT

            if (ReadOption(window, 1) != "Polski" || ReadOption(window, 2) != "English")
                return true;

            picker = window.ToInt32();
            return false;
        }, IntPtr.Zero);
        return picker;
    }

    private static string ReadOption(IntPtr window, int index)
    {
        int length = Send(window.ToInt32(), 0x0149, index, 0); // CB_GETLBTEXTLEN
        if (length < 0 || length > 1000)
            return "";
        var text = new StringBuilder(length + 1);
        IntPtr result;
        return SendMessageTimeout(window, 0x0148, new IntPtr(index), text, 2, 2000, out result) != IntPtr.Zero
            ? text.ToString() : ""; // CB_GETLBTEXT
    }

    public static int ReadSelection(int window)
    {
        return Send(window, 0x0147, 0, 0); // CB_GETCURSEL
    }

    public static void MoveSelection(int window, bool down)
    {
        int key = down ? 0x28 : 0x26;
        Send(window, 0x0100, key, 1); // WM_KEYDOWN
        Send(window, 0x0101, key, unchecked((int)0xC0000001)); // WM_KEYUP
    }

    private static int Send(int window, uint message, int wParam, int lParam)
    {
        IntPtr result;
        if (SendMessageTimeout(new IntPtr(window), message, new IntPtr(wParam), new IntPtr(lParam),
                2, 2000, out result) == IntPtr.Zero)
            throw new InvalidOperationException("The native Settings combo box did not respond.");
        return result.ToInt32();
    }
}
'@
}

function Find-Control($Window, [string]$Id, [switch]$OnScreen) {
    $condition = [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::AutomationIdProperty, $Id)
    if ($OnScreen) {
        $visible = [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::IsOffscreenProperty, $false)
        $condition = [Windows.Automation.AndCondition]::new([Windows.Automation.Condition[]]@($condition, $visible))
    }
    $Window.FindFirst([Windows.Automation.TreeScope]::Descendants, $condition)
}

function Find-AppControl([Diagnostics.Process]$Process, [string]$Id, [int]$TimeoutSeconds = 30, [switch]$OnScreen) {
    $condition = [Windows.Automation.PropertyCondition]::new(
        [Windows.Automation.AutomationElement]::ProcessIdProperty, $Process.Id)
    $timer = [Diagnostics.Stopwatch]::StartNew()
    do {
        if ($Process.HasExited) { throw "TelePilot exited early with code $($Process.ExitCode)." }
        try {
            $windows = [Windows.Automation.AutomationElement]::RootElement.FindAll(
                [Windows.Automation.TreeScope]::Children, $condition)
            foreach ($window in $windows) {
                $found = Find-Control $window $Id -OnScreen:$OnScreen
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

function Invoke-Button($Control, [string]$Stage) {
    $invoke = $null
    if ($null -eq $Control -or -not $Control.TryGetCurrentPattern([Windows.Automation.InvokePattern]::Pattern, [ref]$invoke)) {
        throw "The button for '$Stage' is missing or does not support UI Automation Invoke."
    }
    $invoke.Invoke()
}

function Click-MenuItem($Control, [string]$Stage) {
    # Avalonia menu items do not expose Invoke or ExpandCollapse.
    $bounds = $Control.Current.BoundingRectangle
    if ($bounds.IsEmpty -or $bounds.Width -le 0 -or $bounds.Height -le 0 -or $Control.Current.IsOffscreen) {
        throw "The menu item for '$Stage' is not visible."
    }
    try {
        [NativeLanguagePicker]::ClickMenuItem(
            [int]($bounds.Left + $bounds.Width / 2), [int]($bounds.Top + $bounds.Height / 2))
    } catch {
        throw "Could not click the menu item for '$Stage': $_"
    }
}

function Assert-MainLanguage([Diagnostics.Process]$Process, [string]$Language) {
    $main = Find-AppControl $Process 'tab-live'
    if ($null -eq $main) { throw 'The main window did not open.' }
    $liveLabel = if ($Language -eq 'pl') { 'TV na ?ywo' } else { 'Live TV' }
    if ($main.Control.Current.Name -cnotlike $liveLabel) { throw 'The main window is in the wrong language.' }
    return $main
}

function Close-App([Diagnostics.Process]$Process, $Window) {
    $close = $null
    if (-not $Window.TryGetCurrentPattern([Windows.Automation.WindowPattern]::Pattern, [ref]$close)) {
        throw 'The main window does not support UI Automation Window.Close.'
    }
    $close.Close()
    if (-not $Process.WaitForExit(10000) -or $Process.ExitCode -ne 0) { throw 'TelePilot did not shut down cleanly.' }
}

function Open-AppSettings([Diagnostics.Process]$Process, $MainWindow) {
    $menu = Find-Control $MainWindow 'menu-file'
    if ($null -eq $menu) { throw 'The File menu was not found.' }
    [NativeLanguagePicker]::ActivateWindow($MainWindow.Current.NativeWindowHandle)
    Click-MenuItem $menu 'open File menu'
    $menuItem = Find-AppControl $Process 'menu-settings' -TimeoutSeconds 5 -OnScreen
    if ($null -eq $menuItem) { throw 'Settings did not become visible after clicking File.' }
    Click-MenuItem $menuItem.Control 'open Settings'
    $settings = Find-AppControl $Process 'settings-save' -OnScreen
    if ($null -eq $settings) { throw 'The Settings window did not open after clicking its menu item.' }

    $settingsHandle = $settings.Window.Current.NativeWindowHandle
    $timer = [Diagnostics.Stopwatch]::StartNew()
    do {
        $picker = [NativeLanguagePicker]::FindLanguagePicker($settingsHandle)
        if ($picker -eq 0) { Start-Sleep -Milliseconds 100 }
    } while ($picker -eq 0 -and $timer.Elapsed.TotalSeconds -lt 5)
    if ($picker -eq 0) { throw 'The native Settings language picker was not found.' }
    return @{ Settings = $settings; PickerHandle = $picker }
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
        Invoke-Button $onboarding.Control 'confirm first-run language'

        $manager = Find-AppControl $app 'close-playlist-manager'
        if ($null -eq $manager) { throw 'The Playlist Manager did not open automatically.' }
        $add = Find-Control $manager.Window 'add-file-source'
        $addLabel = if ($ExpectedLanguage -eq 'pl') { 'Dodaj playlist? z pliku' } else { 'Add playlist from file' }
        if ($null -eq $add -or $add.Current.Name -cnotlike $addLabel) {
            throw 'The Playlist Manager add control is missing or in the wrong language.'
        }
        Invoke-Button $manager.Control 'close first-run Playlist Manager'
    }

    $main = Assert-MainLanguage $app $ExpectedLanguage
    if ($Runtime) { Assert-WindowsProcessArchitecture -Process $app -Runtime $Runtime }
    if ($null -ne (Find-Control $main.Window 'playlist-source-selector')) {
        throw 'The main window still exposes the playlist selector.'
    }
    if (-not $FirstRun -and $null -ne (Find-AppControl $app 'close-playlist-manager' -TimeoutSeconds 2)) {
        throw 'The manager unexpectedly opened on an existing profile.'
    }

    if ($VerifySettingsLanguagePersistence) {
        $settings = Open-AppSettings $app $main.Window
        $newLanguage = if ($ExpectedLanguage -eq 'pl') { 'en' } else { 'pl' }
        $previousIndex = if ($ExpectedLanguage -eq 'pl') { 1 } else { 2 }
        $newIndex = if ($newLanguage -eq 'pl') { 1 } else { 2 }
        $pickerHandle = $settings.PickerHandle
        if ([NativeLanguagePicker]::ReadSelection($pickerHandle) -ne $previousIndex) {
            throw 'The settings picker does not display the explicit language of the existing profile.'
        }
        [NativeLanguagePicker]::MoveSelection($pickerHandle, ($newIndex -gt $previousIndex))
        if ([NativeLanguagePicker]::ReadSelection($pickerHandle) -ne $newIndex) {
            throw 'The native language picker did not select the requested language.'
        }
        Invoke-Button $settings.Settings.Control 'save Settings language'
        $timer = [Diagnostics.Stopwatch]::StartNew()
        while ($null -ne (Find-AppControl $app 'settings-save' -TimeoutSeconds 1)) {
            if ($timer.Elapsed.TotalSeconds -ge 15) { throw 'Settings did not close after saving the language.' }
        }
        Close-App $app $main.Window
        $app = Start-Process -FilePath $Executable -WorkingDirectory (Split-Path -Parent $Executable) -PassThru
        $main = Assert-MainLanguage $app $newLanguage
        if ($Runtime) { Assert-WindowsProcessArchitecture -Process $app -Runtime $Runtime }
        $settings = Open-AppSettings $app $main.Window
        if ([NativeLanguagePicker]::ReadSelection($settings.PickerHandle) -ne $newIndex) {
            throw 'The explicit language was not selected in Settings after restart.'
        }
        Invoke-Button (Find-Control $settings.Settings.Window 'settings-cancel') 'close Settings after restart'
        Write-Host "Verified the explicit Settings language after restarting TelePilot: $newLanguage."
    }

    Close-App $app $main.Window
    Write-Host "Verified native Windows UI Automation (first run: $FirstRun; language: $ExpectedLanguage)."
} finally {
    if (-not $app.HasExited) { $app.Kill(); $app.WaitForExit() }
}
