<#
.SYNOPSIS
Exercises the installer FFmpeg download, SHA-256 check and extraction using Inno Setup.
.DESCRIPTION
Explicit opt-in network test. Downloads the real archive into an isolated, unprivileged
Inno harness under .test-output. It does not install TelePilot, launch the application,
create shortcuts or uninstall entries, change the global PATH, or access user settings.
The generated harness, logs and extracted files are retained for inspection.
.EXAMPLE
.\scripts\test-windows-ffmpeg-download.ps1 -RunDownloadTest -TestInvalidHash
#>
[CmdletBinding()]
param(
    [switch]$RunDownloadTest,
    [switch]$TestInvalidHash,
    [ValidateSet('win-x64', 'win-arm64')]
    [string]$Runtime = 'win-x64',
    [string]$InnoCompiler,
    [ValidateRange(30, 3600)]
    [int]$TimeoutSeconds = 900
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'windows-package-helpers.ps1')

if (-not $RunDownloadTest) {
    throw 'This network test requires explicit opt-in: -RunDownloadTest.'
}
if (-not $IsWindows) {
    throw 'This test requires Windows and PowerShell 7.'
}

$repositoryRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$testBase = [IO.Path]::GetFullPath((Join-Path $repositoryRoot '.test-output'))
$testRoot = [IO.Path]::GetFullPath((Join-Path $testBase ("ffmpeg-download-" + [Guid]::NewGuid().ToString('N'))))
$testPrefix = $testBase.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
if (-not $testRoot.StartsWith($testPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Refusing to write outside the repository test directory.'
}

if (-not $InnoCompiler) {
    $InnoCompiler = Join-Path $repositoryRoot '.tools\inno-setup-6\ISCC.exe'
}
$InnoCompiler = [IO.Path]::GetFullPath($InnoCompiler)
if (-not (Test-Path -LiteralPath $InnoCompiler -PathType Leaf)) {
    throw "Inno Setup compiler not found: $InnoCompiler"
}

$installerScript = Get-Content -LiteralPath (Join-Path $repositoryRoot 'installer\windows\TelePilot.iss') -Raw
$package = Get-WindowsFfmpegDownload $Runtime
$functions = [regex]::Match($installerScript, '(?ms)^function FfmpegExists\(\): Boolean;\r?\n.*?(?=^function LegacyInstallExistsInLoadedHives\()')
if (-not $functions.Success -or -not $functions.Value.Contains('function ShouldInstallFfmpeg(): Boolean;')) {
    throw 'Could not extract the unchanged production FFmpeg detection functions.'
}
if (-not [regex]::IsMatch($installerScript, '(?m)^ArchiveExtraction=full\s*$')) {
    throw 'The production installer must enable full ZIP extraction.'
}
New-Item -ItemType Directory -Path $testRoot | Out-Null
$emptyPath = Join-Path $testRoot 'empty-path'
$temporaryDirectory = Join-Path $testRoot 'temp'
New-Item -ItemType Directory -Path $emptyPath, $temporaryDirectory | Out-Null
if (Get-ChildItem -LiteralPath $emptyPath -Force | Select-Object -First 1) {
    throw 'The isolated child PATH directory is not empty.'
}

function Invoke-IsolatedProcess {
    param([string]$Executable, [string[]]$Arguments)

    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $Executable
    $startInfo.WorkingDirectory = $emptyPath
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    # Only these child processes receive the empty PATH and isolated temporary directory.
    $startInfo.Environment['PATH'] = $emptyPath
    $startInfo.Environment['TEMP'] = $temporaryDirectory
    $startInfo.Environment['TMP'] = $temporaryDirectory
    foreach ($argument in $Arguments) {
        $startInfo.ArgumentList.Add($argument)
    }
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    try {
        if (-not $process.Start()) { throw "Could not start $Executable" }
        $standardOutput = $process.StandardOutput.ReadToEndAsync()
        $standardError = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
            $process.Kill($true)
            $process.WaitForExit()
            throw "Process timed out after $TimeoutSeconds seconds: $Executable"
        }
        [pscustomobject]@{
            ExitCode = $process.ExitCode
            Output = $standardOutput.GetAwaiter().GetResult() + $standardError.GetAwaiter().GetResult()
        }
    } finally {
        $process.Dispose()
    }
}

function New-Harness {
    param([string]$Name, [string]$DownloadEntry)

    $scriptPath = Join-Path $testRoot "$Name.iss"
    $defaultDirectory = Join-Path $testRoot "$Name-install"
    $harnessId = 'TelePilotFfmpegSmoke-' + [Guid]::NewGuid().ToString('N')
    $script = @"
#define Runtime "$Runtime"
[Setup]
AppId=$harnessId
AppName=TelePilot FFmpeg isolated smoke test
AppVersion=1.0.0
DefaultDirName=$defaultDirectory
OutputDir=$testRoot
OutputBaseFilename=$Name
PrivilegesRequired=lowest
Uninstallable=no
CreateUninstallRegKey=no
UsePreviousAppDir=no
UsePreviousTasks=no
DisableProgramGroupPage=yes
DisableWelcomePage=yes
CloseApplications=no
RestartApplications=no
ArchiveExtraction=full
SetupLogging=yes

[Files]
$DownloadEntry

[Code]
$($functions.Value)
"@
    Set-Content -LiteralPath $scriptPath -Value $script -Encoding utf8
    $compiled = Invoke-IsolatedProcess -Executable $InnoCompiler -Arguments @('/Qp', $scriptPath)
    Set-Content -LiteralPath (Join-Path $testRoot "$Name-compile.log") -Value $compiled.Output -Encoding utf8
    if ($compiled.ExitCode -ne 0) {
        throw "Inno harness compilation failed ($($compiled.ExitCode)): $($compiled.Output)"
    }
    Join-Path $testRoot "$Name.exe"
}

function Invoke-Harness {
    param([string]$Executable, [string]$InstallDirectory, [string]$LogName)

    $logPath = Join-Path $testRoot $LogName
    $result = Invoke-IsolatedProcess -Executable $Executable -Arguments @(
        '/VERYSILENT', '/SUPPRESSMSGBOXES', '/SP-', '/NORESTART',
        "/DIR=$InstallDirectory", "/LOG=$logPath"
    )
    if (-not (Test-Path -LiteralPath $logPath -PathType Leaf)) {
        throw "Inno did not produce its log (exit $($result.ExitCode)): $($result.Output)"
    }
    [pscustomobject]@{ ExitCode = $result.ExitCode; Log = (Get-Content -LiteralPath $logPath -Raw) }
}

function Get-InstalledManifest {
    param([string]$Directory)

    @(Get-ChildItem -LiteralPath $Directory -Recurse -File | Sort-Object FullName | ForEach-Object {
        $relativePath = [IO.Path]::GetRelativePath($Directory, $_.FullName)
        "$relativePath|$($_.Length)|$((Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash)"
    }) -join "`n"
}

Write-Host "Testing production FFmpeg download in $testRoot"
$harness = New-Harness -Name 'download' -DownloadEntry $package.Entry
$installDirectory = Join-Path $testRoot 'download-install'
$first = Invoke-Harness -Executable $harness -InstallDirectory $installDirectory -LogName 'download.log'
if ($first.ExitCode -ne 0) { throw "FFmpeg download/extraction failed ($($first.ExitCode)); see $testRoot\download.log" }
if (-not $first.Log.Contains($package.Url)) {
    throw 'The first run did not prove the missing-FFmpeg production download branch.'
}
$archiveRoot = Join-Path (Join-Path $installDirectory $package.RelativeDirectory) ([IO.Path]::GetFileNameWithoutExtension($package.Archive))
$ffmpeg = Join-Path $archiveRoot 'bin\ffmpeg.exe'
Assert-WindowsBinaryArchitecture -LiteralPath $ffmpeg -Runtime $Runtime
$notices = if ($Runtime -eq 'win-arm64') { @('LICENSE.txt') } else { @('LICENSE', 'README.txt') }
foreach ($notice in $notices) {
    $required = Join-Path $archiveRoot $notice
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) { throw "Missing extracted file: $required" }
}
$documentation = Join-Path $archiveRoot 'doc'
if (-not (Test-Path -LiteralPath $documentation -PathType Container) -or
    -not (Get-ChildItem -LiteralPath $documentation -Recurse -File | Select-Object -First 1)) {
    throw 'The extracted archive does not contain documentation.'
}
$version = Invoke-IsolatedProcess -Executable $ffmpeg -Arguments @('-version')
Set-Content -LiteralPath (Join-Path $testRoot 'ffmpeg-version.txt') -Value $version.Output -Encoding utf8
if ($version.ExitCode -ne 0 -or $version.Output -notmatch '(?m)^ffmpeg version ') {
    throw 'The extracted FFmpeg executable did not report its version.'
}
$before = Get-InstalledManifest -Directory $installDirectory
$second = Invoke-Harness -Executable $harness -InstallDirectory $installDirectory -LogName 'already-installed.log'
if ($second.ExitCode -ne 0) {
    throw 'The second run did not skip the existing bundled FFmpeg.'
}
if ($second.Log.Contains($package.Url) -or $before -cne (Get-InstalledManifest -Directory $installDirectory)) {
    throw 'The second run downloaded FFmpeg or changed installed file contents.'
}

$invalidHashRejected = $null
if ($TestInvalidHash) {
    $invalidHash = if ($package.Hash[0] -eq '0') { '1' + $package.Hash.Substring(1) } else { '0' + $package.Hash.Substring(1) }
    $invalidEntry = $package.Entry.Replace($package.Hash, $invalidHash)
    $invalidHarness = New-Harness -Name 'invalid-hash' -DownloadEntry $invalidEntry
    $invalidDirectory = Join-Path $testRoot 'invalid-hash-install'
    $negative = Invoke-Harness -Executable $invalidHarness -InstallDirectory $invalidDirectory -LogName 'invalid-hash.log'
    $unexpectedFile = if (Test-Path -LiteralPath $invalidDirectory) {
        Get-ChildItem -LiteralPath $invalidDirectory -Recurse -File | Select-Object -First 1
    } else { $null }
    if ($negative.ExitCode -eq 0 -or
        -not $negative.Log.Contains($package.Url) -or
        -not $negative.Log.Contains('Verification error: File hash is incorrect.') -or $unexpectedFile) {
        throw 'The incorrect SHA-256 was not rejected before extraction.'
    }
    $invalidHashRejected = $true
}

[pscustomobject]@{
    TestDirectory = $testRoot
    DownloadAndExtraction = 'Passed'
    ExistingFfmpegSkipped = 'Passed'
    InstalledFilesUnchangedOnSecondRun = $true
    InvalidHashRejected = $invalidHashRejected
    ArchiveSha256 = $package.Hash
    Version = ($version.Output -split '\r?\n')[0]
    ChildPath = $emptyPath
}
