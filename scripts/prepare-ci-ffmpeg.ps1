[CmdletBinding()]
param([ValidateSet('win-x64', 'win-arm64')][string]$Runtime = 'win-x64')
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not $IsWindows -or -not $env:RUNNER_TEMP) { throw 'Run only on an isolated Windows CI runner.' }
. (Join-Path $PSScriptRoot 'windows-package-helpers.ps1')
$root = Join-Path $env:RUNNER_TEMP 'telepilot-test-ffmpeg'
New-Item -ItemType Directory -Path $root -Force | Out-Null
$package = Get-WindowsFfmpegDownload $Runtime
$archive = Join-Path $root $package.Archive
Invoke-WebRequest -Uri $package.Url -OutFile $archive
if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $package.Hash) { throw 'FFmpeg SHA-256 mismatch.' }
# Inno's ExternalSize represents the extracted files when using extractarchive.
$zip = [IO.Compression.ZipFile]::OpenRead($archive)
try {
    $uncompressedSize = ($zip.Entries | Measure-Object -Property Length -Sum).Sum
    if ($uncompressedSize -ne $package.UncompressedSize) { throw 'Incorrect uncompressed FFmpeg size in the installer.' }
} finally {
    $zip.Dispose()
}
Expand-Archive -LiteralPath $archive -DestinationPath $root
$ffmpeg = Join-Path $root (Join-Path ([IO.Path]::GetFileNameWithoutExtension($package.Archive)) 'bin/ffmpeg.exe')
Assert-WindowsBinaryArchitecture -LiteralPath $ffmpeg -Runtime $Runtime

$media = Join-Path $root 'synthetic.ts'
& $ffmpeg -hide_banner -loglevel error -y `
    -f lavfi -i 'testsrc2=size=320x240:rate=25' -f lavfi -i 'sine=frequency=440:sample_rate=48000' `
    -t 5 -c:v mpeg2video -c:a mp2 -f mpegts $media
if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $media -PathType Leaf)) {
    throw 'Could not generate the synthetic transport stream for recording tests.'
}
$env:TELEPILOT_TEST_FFMPEG = $ffmpeg
$env:TELEPILOT_TEST_MEDIA = $media
Write-Host "Verified $Runtime FFmpeg and generated a synthetic recording fixture in the runner temporary directory."
