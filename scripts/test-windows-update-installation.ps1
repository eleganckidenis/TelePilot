# Upgrade a real public TelePilot installation on a disposable GitHub-hosted runner.
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Installer,
    [Parameter(Mandatory)][string]$Version,
    [Parameter(Mandatory)][string]$PreviousInstaller,
    [Parameter(Mandatory)][string]$PreviousVersion,
    [Parameter(Mandatory)][ValidatePattern('^[a-fA-F0-9]{64}$')][string]$PreviousSha256,
    [Parameter(Mandatory)][ValidateSet('win-x64', 'win-arm64')][string]$Runtime,
    [Parameter(Mandatory)][string]$EvidenceDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not $IsWindows -or $env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted' -or
    -not $env:RUNNER_TEMP -or -not [Environment]::UserInteractive) {
    throw 'This test requires a disposable GitHub-hosted Windows runner with a native desktop.'
}
. (Join-Path $PSScriptRoot 'windows-package-helpers.ps1')
Assert-WindowsProcessArchitecture -Process ([Diagnostics.Process]::GetCurrentProcess()) -Runtime $Runtime

# Observe Inno's native progress bar without moving foreground focus. Inno's
# unpacked setup can own the wizard, so a launcher PID alone cannot identify it.
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Text;

public static class TelePilotInstallerProgress
{
    private delegate bool EnumWindowProc(IntPtr window, IntPtr state);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowProc callback, IntPtr state);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr window, EnumWindowProc callback, IntPtr state);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder name, int length);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, StringBuilder text, int length);
    [DllImport("user32.dll")] private static extern IntPtr SendMessageTimeout(IntPtr window, uint message,
        IntPtr wParam, IntPtr lParam, uint flags, uint timeout, out UIntPtr value);

    public static int ReadPosition()
    {
        const uint progressGetPosition = 0x0408; // PBM_GETPOS
        const uint abortIfHung = 0x0002; // SMTO_ABORTIFHUNG
        int position = -1;
        var text = new StringBuilder(256);
        EnumWindows((window, state) => {
            if (!IsWindowVisible(window) || IsIconic(window)) return true;
            GetClassName(window, text, text.Capacity);
            if (!text.ToString().Equals("TWizardForm", StringComparison.OrdinalIgnoreCase)) return true;
            GetWindowText(window, text, text.Capacity);
            if (!text.ToString().Contains("TelePilot", StringComparison.OrdinalIgnoreCase)) return true;

            EnumChildWindows(window, (child, unused) => {
                if (!IsWindowVisible(child)) return true;
                GetClassName(child, text, text.Capacity);
                string name = text.ToString();
                if (!name.Equals("TNewProgressBar", StringComparison.OrdinalIgnoreCase) &&
                    !name.Equals("msctls_progress32", StringComparison.OrdinalIgnoreCase)) return true;

                UIntPtr value;
                if (SendMessageTimeout(child, progressGetPosition, IntPtr.Zero, IntPtr.Zero, abortIfHung, 100, out value) != IntPtr.Zero)
                    position = Math.Max(position, (int)value.ToUInt64());
                return true;
            }, IntPtr.Zero);
            return true;
        }, IntPtr.Zero);
        return position;
    }
}
'@

function Get-NumericFileVersion([string]$File) {
    $info = (Get-Item -LiteralPath $File).VersionInfo
    "$($info.FileMajorPart).$($info.FileMinorPart).$($info.FileBuildPart)"
}

function Wait-ForWindow([Diagnostics.Process]$Process) {
    $timer = [Diagnostics.Stopwatch]::StartNew()
    while ($timer.Elapsed.TotalSeconds -lt 45 -and -not $Process.HasExited) {
        $Process.Refresh()
        if ($Process.MainWindowHandle -ne [IntPtr]::Zero) { return }
        Start-Sleep -Milliseconds 100
    }
    throw "TelePilot process $($Process.Id) did not open its window."
}

function Complete-Setup([Diagnostics.Process]$Process) {
    if (-not $Process.WaitForExit(600000)) { throw 'Setup timed out.' }
    if ($Process.ExitCode -ne 0) { throw "Setup returned exit code $($Process.ExitCode)." }
}

$Installer = (Resolve-Path -LiteralPath $Installer).Path
$PreviousInstaller = (Resolve-Path -LiteralPath $PreviousInstaller).Path
if ((Get-NumericFileVersion $Installer) -cne $Version -or
    (Get-NumericFileVersion $PreviousInstaller) -cne $PreviousVersion -or [version]$PreviousVersion -ge [version]$Version) {
    throw 'Expected correctly versioned preceding and candidate installers.'
}
if ((Get-FileHash -LiteralPath $PreviousInstaller -Algorithm SHA256).Hash -ine $PreviousSha256) {
    throw 'The preceding installer did not match the published SHA-256 digest.'
}

$profile = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)) 'TelePilot'
$uninstallKey = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{873edad3-c012-470d-a2e9-f3b66d236796}_is1'
if ((Test-Path -LiteralPath $profile) -or (Test-Path -LiteralPath $uninstallKey) -or
    (Get-Process -Name TelePilot -ErrorAction SilentlyContinue)) {
    throw 'Expected a clean TelePilot install and user profile on the runner.'
}

$testDir = Join-Path $env:RUNNER_TEMP ('telepilot-upgrade-' + [guid]::NewGuid().ToString('N'))
$appDir = Join-Path $testDir 'existing installation'
$exe = Join-Path $appDir 'TelePilot.exe'
$oldLog = Join-Path $EvidenceDirectory 'previous-setup.log'
$updateLog = Join-Path $EvidenceDirectory 'update-setup.log'
$waitingMessage = 'Waiting for the previous TelePilot process to exit.'
$exitedMessage = 'The previous TelePilot process has exited.'
$installMessage = 'Starting the installation process.'
New-Item -ItemType Directory -Path $EvidenceDirectory -Force | Out-Null

$previousSetup = $candidateSetup = $previousApp = $newApp = $null
try {
    $previousSetup = Start-Process -FilePath $PreviousInstaller -PassThru -WindowStyle Hidden -ArgumentList @(
        '/VERYSILENT', '/SUPPRESSMSGBOXES', '/SP-', '/NORESTART', '/LANG=english', "/DIR=`"$appDir`"", "/LOG=`"$oldLog`"")
    Complete-Setup $previousSetup
    if ((Get-NumericFileVersion $exe) -cne $PreviousVersion) { throw 'The preceding installation has the wrong version.' }

    $playlists = Join-Path $profile 'playlists'
    New-Item -ItemType Directory -Path $playlists -Force | Out-Null
    $preferences = Join-Path $profile 'preferences.json'
    $settings = Join-Path $profile 'settings.json'
    $sourceId = [guid]::NewGuid()
    $playlist = Join-Path $playlists "$($sourceId.ToString('N')).m3u"
    $original = Join-Path $testDir 'synthetic-original.m3u'
    Set-Content -LiteralPath $preferences -Value '{"language":"pl","recording":{"audioBitrateKbps":128}}' -Encoding utf8
    @{ sources = @(@{
        id = $sourceId.ToString('D'); name = 'Synthetic upgrade source'; kind = 0; location = $playlist
        refreshOnStartup = $false; downloadTimeoutSeconds = 30; relativeReferenceBaseLocation = $original
    }) } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $settings -Encoding utf8
    Set-Content -LiteralPath $playlist -Encoding utf8 -Value @('#EXTM3U', '#EXTINF:-1,Synthetic channel', 'http://127.0.0.1:9/unused')
    $marker = Join-Path $appDir 'user-owned.txt'
    Set-Content -LiteralPath $marker -Value 'Keep this file' -Encoding utf8

    $previousApp = Start-Process -FilePath $exe -WorkingDirectory $appDir -PassThru
    Wait-ForWindow $previousApp
    Assert-WindowsProcessArchitecture -Process $previousApp -Runtime $Runtime
    $playlistHash = (Get-FileHash -LiteralPath $playlist -Algorithm SHA256).Hash

    # The production switch is exercised with a deliberately live process to
    # verify that its exit is a prerequisite for the install phase. Omit /DIR
    # so Inno must find the registered custom install location.
    $installerReadLock = [IO.File]::Open($Installer, 'Open', 'Read', 'Read')
    try {
        $candidateSetup = Start-Process -FilePath $Installer -WorkingDirectory ([Environment]::SystemDirectory) -PassThru -ArgumentList @(
            '/SILENT', '/SP-', '/NORESTART', '/NOCLOSEAPPLICATIONS', '/LANG=polish',
            "/TELEPILOTUPDATE=$($previousApp.Id)", "/LOG=`"$updateLog`"")
    } finally {
        $installerReadLock.Dispose()
    }
    $timer = [Diagnostics.Stopwatch]::StartNew()
    do {
        if ($candidateSetup.HasExited -or $previousApp.HasExited -or $timer.Elapsed.TotalSeconds -ge 60) {
            throw 'Setup did not wait for the running TelePilot instance.'
        }
        $trace = if (Test-Path -LiteralPath $updateLog) { Get-Content -LiteralPath $updateLog -Raw } else { '' }
        if ($trace -like "*$waitingMessage*") { break }
        Start-Sleep -Milliseconds 50
    } while ($true)
    if ($trace -like "*$installMessage*" -or (Get-NumericFileVersion $exe) -cne $PreviousVersion) {
        throw 'Setup began applying the update while the old TelePilot process was running.'
    }
    if (-not $previousApp.CloseMainWindow()) { throw 'Could not close the previous TelePilot main window normally.' }

    $timer.Restart()
    $maxProgress = -1
    while (-not $candidateSetup.HasExited -and $timer.Elapsed.TotalSeconds -lt 300) {
        $maxProgress = [Math]::Max($maxProgress, [TelePilotInstallerProgress]::ReadPosition())
        if ($maxProgress -gt 0 -and $previousApp.HasExited) { break }
        Start-Sleep -Milliseconds 25
    }
    if ($maxProgress -le 0) { throw 'The visible native installation progress bar never reported progress.' }
    if (-not $previousApp.HasExited -or $previousApp.ExitCode -ne 0) { throw 'The preceding TelePilot did not exit cleanly.' }
    Complete-Setup $candidateSetup

    $trace = Get-Content -LiteralPath $updateLog -Raw
    $waitIndex = $trace.IndexOf($waitingMessage, [StringComparison]::Ordinal)
    $exitIndex = $trace.IndexOf($exitedMessage, [StringComparison]::Ordinal)
    $installIndex = $trace.IndexOf($installMessage, [StringComparison]::Ordinal)
    if ($waitIndex -lt 0 -or $exitIndex -le $waitIndex -or $installIndex -le $exitIndex) {
        throw 'The installer log did not confirm waiting, process exit, and installation in order.'
    }
    $registered = Get-ItemProperty -LiteralPath $uninstallKey
    $uninstallers = @(Get-ChildItem -LiteralPath $appDir -File -Filter 'unins*.exe')
    if ((Get-NumericFileVersion $exe) -cne $Version -or $registered.DisplayVersion -cne $Version -or
        $registered.InstallLocation.TrimEnd('\') -ine $appDir -or $uninstallers.Count -ne 1) {
        throw 'The update did not replace the registered installation in place.'
    }

    $timer.Restart()
    do {
        $newApp = Get-Process -Name TelePilot -ErrorAction SilentlyContinue | Where-Object { $_.Path -ieq $exe } | Select-Object -First 1
        if ($newApp) { break }
        Start-Sleep -Milliseconds 100
    } while ($timer.Elapsed.TotalSeconds -lt 30)
    if (-not $newApp -or $newApp.Id -eq $previousApp.Id) { throw 'Setup did not start the upgraded TelePilot process.' }
    Wait-ForWindow $newApp
    Assert-WindowsProcessArchitecture -Process $newApp -Runtime $Runtime

    $saved = Get-Content -LiteralPath $preferences -Raw | ConvertFrom-Json
    $sources = (Get-Content -LiteralPath $settings -Raw | ConvertFrom-Json).sources
    if ($saved.language -cne 'pl' -or $saved.recording.audioBitrateKbps -ne 128 -or
        $sources.Count -ne 1 -or $sources[0].id -cne $sourceId.ToString('D') -or
        $sources[0].location -cne $playlist -or (Get-FileHash -LiteralPath $playlist -Algorithm SHA256).Hash -cne $playlistHash -or
        (Get-Content -LiteralPath $marker -Raw).Trim() -cne 'Keep this file') {
        throw 'The upgrade did not preserve preferences, a managed playlist and an unowned file.'
    }
    @{
        runtime = $Runtime; previous = $PreviousVersion; installed = $Version
        previousProcessId = $previousApp.Id; relaunchedProcessId = $newApp.Id
        nativeProgressPosition = $maxProgress
    } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $EvidenceDirectory 'update-installation.json') -Encoding utf8
    Write-Host "PASS: upgraded $Runtime from $PreviousVersion to $Version in place, with visible installation progress, preserved profile and native relaunch."
} finally {
    foreach ($setup in @($previousSetup, $candidateSetup)) {
        if ($null -ne $setup -and -not $setup.HasExited) { $setup.Kill($true); $setup.WaitForExit(10000) | Out-Null }
    }
    foreach ($app in Get-Process -Name TelePilot -ErrorAction SilentlyContinue | Where-Object { $_.Path -ieq $exe }) {
        if (-not $app.CloseMainWindow() -or -not $app.WaitForExit(10000)) { $app.Kill(); $app.WaitForExit(10000) | Out-Null }
    }
}
