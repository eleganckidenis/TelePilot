function Read-WindowsPeInfo([string]$LiteralPath) {
    $reader = [IO.BinaryReader]::new([IO.File]::OpenRead($LiteralPath))
    try {
        if ($reader.ReadUInt16() -ne 0x5a4d) { throw "Missing MZ header: $LiteralPath" }
        $reader.BaseStream.Position = 0x3c
        $reader.BaseStream.Position = $reader.ReadUInt32()
        if ($reader.ReadUInt32() -ne 0x00004550) { throw "Missing PE header: $LiteralPath" }
        $machine = $reader.ReadUInt16()
        $optionalHeader = $reader.BaseStream.Position + 18
        $reader.BaseStream.Position = $optionalHeader
        $dataDirectoryOffset = switch ($reader.ReadUInt16()) {
            0x10b { 96 }
            0x20b { 112 }
            default { throw "Unsupported PE optional header: $LiteralPath" }
        }
        # The 15th PE data-directory entry points to the managed CLR header.
        $reader.BaseStream.Position = $optionalHeader + $dataDirectoryOffset + 14 * 8
        [pscustomobject]@{ Machine = $machine; IsManaged = $reader.ReadUInt32() -ne 0 }
    } finally {
        $reader.Dispose()
    }
}

function Get-WindowsPeMachine([string]$Runtime) {
    switch ($Runtime) {
        'win-x64' { [uint16]0x8664 }
        'win-arm64' { [uint16]0xaa64 }
        default { throw "Unsupported runtime: $Runtime" }
    }
}

function Assert-WindowsBinaryArchitecture([string]$LiteralPath, [string]$Runtime) {
    $pe = Read-WindowsPeInfo $LiteralPath
    if ($pe.IsManaged -or $pe.Machine -ne (Get-WindowsPeMachine $Runtime)) {
        throw ('Expected native {0} but found PE machine 0x{1:x4} in {2}.' -f $Runtime, $pe.Machine, $LiteralPath)
    }
}

function Assert-WindowsPayloadArchitecture([string]$Directory, [string]$Runtime) {
    $vlcDirectory = Join-Path $Directory "libvlc\$Runtime"
    foreach ($file in @('TelePilot.exe', 'hostfxr.dll', 'hostpolicy.dll', 'coreclr.dll', 'clrjit.dll',
        'libSkiaSharp.dll', 'libHarfBuzzSharp.dll', 'av_libglesv2.dll', "libvlc\$Runtime\libvlc.dll", "libvlc\$Runtime\libvlccore.dll")) {
        Assert-WindowsBinaryArchitecture -LiteralPath (Join-Path $Directory $file) -Runtime $Runtime
    }
    $vlcDirectories = @(Get-ChildItem -LiteralPath (Join-Path $Directory 'libvlc') -Directory)
    if ($vlcDirectories.Count -ne 1 -or $vlcDirectories[0].Name -cne $Runtime) {
        throw "Unexpected VLC runtime directories for $Runtime."
    }
    if (-not (Get-ChildItem -LiteralPath (Join-Path $vlcDirectory 'plugins') -Recurse -File -Filter '*.dll' | Select-Object -First 1)) {
        throw "Missing VLC plugins for $Runtime."
    }

    $machine = Get-WindowsPeMachine $Runtime
    $nativeCount = 0
    foreach ($file in Get-ChildItem -LiteralPath $Directory -Recurse -File) {
        $relativePath = $file.FullName.Substring($Directory.Length + 1)
        if ($file.Extension -notin @('.exe', '.dll') -or $relativePath -match '^tools\\ffmpeg(?:-arm64)?\\' -or
            $relativePath -match '^unins\d+\.exe$') {
            continue
        }
        $pe = Read-WindowsPeInfo $file.FullName
        if (-not $pe.IsManaged) {
            if ($pe.Machine -ne $machine) {
                throw ('Unexpected PE machine 0x{0:x4} for {1}: {2}.' -f $pe.Machine, $Runtime, $file.FullName)
            }
            $nativeCount++
        }
    }
    Write-Host "Verified $nativeCount native $Runtime executables and libraries including VLC plugins."
}

function Assert-WindowsProcessArchitecture([Diagnostics.Process]$Process, [string]$Runtime) {
    if (-not ('TelePilot.PackageTests' -as [type])) {
        Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

namespace TelePilot
{
    public static class PackageTests
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool IsWow64Process2(IntPtr process, out ushort processMachine, out ushort nativeMachine);
    }
}
'@
    }
    [uint16]$processMachine = 0
    [uint16]$nativeMachine = 0
    if (-not [TelePilot.PackageTests]::IsWow64Process2($Process.Handle, [ref]$processMachine, [ref]$nativeMachine)) {
        throw [ComponentModel.Win32Exception]::new([Runtime.InteropServices.Marshal]::GetLastWin32Error())
    }
    if ($processMachine -ne 0 -or $nativeMachine -ne (Get-WindowsPeMachine $Runtime)) {
        throw ('Process {0} did not run natively as {1} (process 0x{2:x4}, OS 0x{3:x4}).' -f
            $Process.Id, $Runtime, $processMachine, $nativeMachine)
    }
}

function Get-WindowsFfmpegDownload([string]$Runtime) {
    $scriptPath = Join-Path $PSScriptRoot '../installer/windows/TelePilot.iss'
    $script = Get-Content -LiteralPath $scriptPath -Raw
    $pattern = '(?m)^#(?:if|elif) Runtime == "' + [regex]::Escape($Runtime) +
        '"\r?\n(?:;[^\r\n]*\r?\n)*(?<entry>Source: "(?<url>https://[^"\r\n]+)"; DestName: "(?<archive>[^"\r\n]+\.zip)"; DestDir: "\{app\}\\(?<directory>tools\\[^"\r\n]+)"; Hash: "(?<hash>[0-9a-f]{64})"; ExternalSize: (?<size>\d+); [^\r\n]*Check: ShouldInstallFfmpeg)\r?$'
    $entries = [regex]::Matches($script, $pattern)
    if ($entries.Count -ne 1) { throw "Expected exactly one pinned FFmpeg download for $Runtime." }
    $groups = $entries[0].Groups
    [pscustomobject]@{
        Entry = $groups['entry'].Value
        Url = $groups['url'].Value
        Archive = $groups['archive'].Value
        RelativeDirectory = $groups['directory'].Value
        Hash = $groups['hash'].Value
        Size = [long]$groups['size'].Value
    }
}
