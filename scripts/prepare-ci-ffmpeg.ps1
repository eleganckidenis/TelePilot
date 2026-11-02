[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not $IsWindows -or -not $env:RUNNER_TEMP) { throw 'Run only on an isolated Windows CI runner.' }
$root = Join-Path $env:RUNNER_TEMP 'telepilot-test-ffmpeg'
New-Item -ItemType Directory -Path $root -Force | Out-Null
$archive = Join-Path $root 'ffmpeg.zip'
$installer = Get-Content -LiteralPath (Join-Path $PSScriptRoot '../installer/windows/TelePilot.iss') -Raw
$entry = [regex]::Match($installer, '(?m)^Source: "(https://[^"\r\n]+)";[^\r\n]*Check: ShouldInstallFfmpeg[^\r\n]*').Value
$url = [regex]::Match($entry, '^Source: "([^"]+)"').Groups[1].Value
$expected = [regex]::Match($entry, 'Hash: "([a-fA-F0-9]{64})"').Groups[1].Value
if (-not $url -or -not $expected) { throw 'Pinned FFmpeg URL/checksum not found in installer.' }
Invoke-WebRequest -Uri $url -OutFile $archive
if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $expected) { throw 'FFmpeg SHA-256 mismatch.' }
Expand-Archive -LiteralPath $archive -DestinationPath (Join-Path $root 'expanded')
$ffmpeg = Get-ChildItem -LiteralPath (Join-Path $root 'expanded') -Recurse -File -Filter ffmpeg.exe | Select-Object -First 1
if (-not $ffmpeg) { throw 'Verified FFmpeg archive has no executable.' }
$env:TELEPILOT_TEST_FFMPEG = $ffmpeg.FullName
$env:PATH = $ffmpeg.DirectoryName + [IO.Path]::PathSeparator + $env:PATH
Write-Output 'Verified FFmpeg prepared only in runner temporary directory.'
