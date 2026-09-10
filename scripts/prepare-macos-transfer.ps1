[CmdletBinding()]
param(
    [string]$Destination,

    [switch]$IncludePrivateFixtures,

    [switch]$IncludeUrlFixture,

    [string]$UrlFixturePath,

    [switch]$Force
)

$ErrorActionPreference = 'Stop'

function Get-RelativePathCompat {
    param(
        [Parameter(Mandatory = $true)]
        [string]$BaseDirectory,

        [Parameter(Mandatory = $true)]
        [string]$FullPath
    )

    $separator = [System.IO.Path]::DirectorySeparatorChar
    $baseWithSeparator = $BaseDirectory.TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar) + $separator
    $baseUri = [Uri]::new($baseWithSeparator)
    $pathUri = [Uri]::new($FullPath)
    [Uri]::UnescapeDataString($baseUri.MakeRelativeUri($pathUri).ToString()).Replace(
        '/',
        $separator)
}

$repositoryRoot = [System.IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$projectFile = Join-Path $repositoryRoot 'src\TelePilot.App\TelePilot.App.csproj'
[xml]$project = Get-Content -LiteralPath $projectFile -Raw
$version = [string]($project.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1)
if ([string]::IsNullOrWhiteSpace($version)) {
    throw 'Could not read the TelePilot version from the application project.'
}

$includeUrl = $IncludePrivateFixtures.IsPresent -or $IncludeUrlFixture.IsPresent -or
    -not [string]::IsNullOrWhiteSpace($UrlFixturePath)
$includePrivatePlaylists = $IncludePrivateFixtures.IsPresent
$privacySuffix = if ($includeUrl -or $includePrivatePlaylists) { '-PRIVATE-fixtures' } else { '' }
if ([string]::IsNullOrWhiteSpace($Destination)) {
    $Destination = Join-Path $repositoryRoot "artifacts\transfer\TelePilot-$version-macos-source$privacySuffix.zip"
}

$urlFixtureSource = $null
if ($includeUrl) {
    if (-not [string]::IsNullOrWhiteSpace($UrlFixturePath)) {
        $urlFixtureSource = [System.IO.Path]::GetFullPath($UrlFixturePath)
    }
    else {
        $urlFixtureSource = Join-Path $repositoryRoot 'url.txt'
    }

    if (-not (Test-Path -LiteralPath $urlFixtureSource -PathType Leaf)) {
        throw "The requested private URL fixture was not found: $urlFixtureSource"
    }
}

$destinationPath = [System.IO.Path]::GetFullPath($Destination)
$hashPath = "$destinationPath.sha256"
$destinationDirectory = Split-Path -Parent $destinationPath
if ([string]::IsNullOrWhiteSpace($destinationDirectory)) {
    throw 'The destination archive path is invalid.'
}

if ((Test-Path -LiteralPath $destinationPath) -and -not $Force) {
    throw "The archive already exists: $destinationPath. Use -Force to replace it."
}

[System.IO.Directory]::CreateDirectory($destinationDirectory) | Out-Null
if ($Force) {
    Remove-Item -LiteralPath $destinationPath -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $hashPath -Force -ErrorAction SilentlyContinue
}

$excludedDirectories = [System.Collections.Generic.HashSet[string]]::new(
    [System.StringComparer]::OrdinalIgnoreCase)
foreach ($name in @(
        '.git', '.dotnet', '.tools', '.artifacts', '.test-output',
        '.vs', '.vscode', '.idea', 'artifacts', 'bin', 'obj',
        'TestResults', 'logs', 'app-data', 'recordings', 'macos-handoff')) {
    $excludedDirectories.Add($name) | Out-Null
}

$temporaryBase = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
$temporaryRoot = Join-Path $temporaryBase ("TelePilot-transfer-" + [Guid]::NewGuid().ToString('N'))
$stagedProject = Join-Path $temporaryRoot 'TelePilot'
[System.IO.Directory]::CreateDirectory($stagedProject) | Out-Null

try {
    $copiedFiles = 0
    $copiedBytes = [long]0
    $sourceFiles = Get-ChildItem -LiteralPath $repositoryRoot -Recurse -File -Force
    foreach ($file in $sourceFiles) {
        $relativePath = Get-RelativePathCompat -BaseDirectory $repositoryRoot -FullPath $file.FullName
        $pathParts = $relativePath -split '[\\/]'
        if ($pathParts | Where-Object {
                $excludedDirectories.Contains($_) -or
                $_ -like '.baseline-before-*' -or
                $_ -like '.target-backups*'
            }) {
            continue
        }

        if (($file.Name -in @('.DS_Store', 'Thumbs.db')) -or
            ($file.Extension -in @('.log', '.trx', '.coverage', '.coveragexml')) -or
            ($file.Name -like 'settings*.json')) {
            continue
        }

        $isRootUrlFixture = ($pathParts.Count -eq 1) -and ($file.Name -ieq 'url.txt')
        $isRootPlaylistFixture = ($pathParts.Count -eq 1) -and (
            ($file.Extension -ieq '.m3u') -or ($file.Extension -ieq '.m3u8'))
        if ($isRootUrlFixture -or ($isRootPlaylistFixture -and -not $includePrivatePlaylists)) {
            continue
        }

        $targetPath = Join-Path $stagedProject $relativePath
        $targetDirectory = Split-Path -Parent $targetPath
        [System.IO.Directory]::CreateDirectory($targetDirectory) | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $targetPath
        $copiedFiles++
        $copiedBytes += $file.Length
    }

    if ($includeUrl) {
        $urlTargetPath = Join-Path $stagedProject 'url.txt'
        Copy-Item -LiteralPath $urlFixtureSource -Destination $urlTargetPath
        $urlFixtureFile = Get-Item -LiteralPath $urlFixtureSource
        $copiedFiles++
        $copiedBytes += $urlFixtureFile.Length
    }

    $manifest = @(
        'TelePilot macOS transfer package'
        "Version: $version"
        "CreatedUtc: $([DateTimeOffset]::UtcNow.ToString('O'))"
        "PrivateFixturesIncluded: $($includeUrl -or $includePrivatePlaylists)"
        "PrivateUrlFixtureIncluded: $includeUrl"
        "PrivatePlaylistFixturesIncluded: $includePrivatePlaylists"
        "SourceFiles: $copiedFiles"
        ''
        'Open AGENTS.md and docs/MACOS_HANDOFF.md before starting work.'
        'On macOS run: chmod +x scripts/build-macos-app.sh'
    ) -join [Environment]::NewLine
    [System.IO.File]::WriteAllText(
        (Join-Path $stagedProject 'TRANSFER_MANIFEST.txt'),
        $manifest,
        [System.Text.UTF8Encoding]::new($false))

    Add-Type -AssemblyName System.IO.Compression
    $archiveStream = [System.IO.File]::Open(
        $destinationPath,
        [System.IO.FileMode]::CreateNew,
        [System.IO.FileAccess]::ReadWrite,
        [System.IO.FileShare]::None)
    try {
        $archive = [System.IO.Compression.ZipArchive]::new(
            $archiveStream,
            [System.IO.Compression.ZipArchiveMode]::Create,
            $false)
        try {
            foreach ($stagedFile in Get-ChildItem -LiteralPath $temporaryRoot -Recurse -File -Force) {
                $relativeEntryPath = Get-RelativePathCompat -BaseDirectory $temporaryRoot -FullPath $stagedFile.FullName
                $entryName = $relativeEntryPath.Replace('\', '/')
                $entry = $archive.CreateEntry(
                    $entryName,
                    [System.IO.Compression.CompressionLevel]::Optimal)
                $entry.LastWriteTime = $stagedFile.LastWriteTime
                $unixMode = if ($stagedFile.Extension -ieq '.sh') { 33261 } else { 33188 }
                $entry.ExternalAttributes = $unixMode -shl 16

                $entryStream = $entry.Open()
                $inputStream = [System.IO.File]::OpenRead($stagedFile.FullName)
                try {
                    $inputStream.CopyTo($entryStream)
                }
                finally {
                    $inputStream.Dispose()
                    $entryStream.Dispose()
                }
            }
        }
        finally {
            $archive.Dispose()
        }
    }
    finally {
        $archiveStream.Dispose()
    }

    $hash = (Get-FileHash -LiteralPath $destinationPath -Algorithm SHA256).Hash.ToLowerInvariant()
    $hashLine = "$hash  $([System.IO.Path]::GetFileName($destinationPath))$([Environment]::NewLine)"
    [System.IO.File]::WriteAllText($hashPath, $hashLine, [System.Text.UTF8Encoding]::new($false))

    [pscustomobject]@{
        Archive = $destinationPath
        Sha256File = $hashPath
        Sha256 = $hash
        IncludedPrivateFixtures = $includeUrl -or $includePrivatePlaylists
        IncludedUrlFixture = $includeUrl
        IncludedPrivatePlaylists = $includePrivatePlaylists
        SourceFiles = $copiedFiles
        SourceMiB = [math]::Round($copiedBytes / 1MB, 2)
        ArchiveMiB = [math]::Round((Get-Item -LiteralPath $destinationPath).Length / 1MB, 2)
    }
}
finally {
    if (Test-Path -LiteralPath $temporaryRoot) {
        $resolvedTemporaryRoot = [System.IO.Path]::GetFullPath($temporaryRoot)
        $expectedPrefix = $temporaryBase.TrimEnd(
            [System.IO.Path]::DirectorySeparatorChar,
            [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
        $isWithinTemporaryBase = $resolvedTemporaryRoot.StartsWith(
            $expectedPrefix,
            [System.StringComparison]::OrdinalIgnoreCase)
        $hasExpectedName = ([System.IO.Path]::GetFileName($resolvedTemporaryRoot)).StartsWith(
            'TelePilot-transfer-',
            [System.StringComparison]::Ordinal)
        if (-not $isWithinTemporaryBase -or -not $hasExpectedName) {
            throw "Refusing to remove an unexpected temporary directory: $resolvedTemporaryRoot"
        }

        Remove-Item -LiteralPath $resolvedTemporaryRoot -Recurse -Force
    }
}
