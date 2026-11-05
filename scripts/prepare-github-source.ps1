[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$commit = & git -C $repositoryRoot rev-parse --verify HEAD
if ($LASTEXITCODE -ne 0) { throw 'Could not identify the release commit.' }
[xml]$appProject = (& git -C $repositoryRoot show "${commit}:src/TelePilot.App/TelePilot.App.csproj") -join "`n"
if ($LASTEXITCODE -ne 0) { throw 'Could not read the application version at the release commit.' }
$version = $appProject.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'Invalid application version.' }

# Archive committed bytes only. Reject private or generated files rather than silently
# producing an incomplete source archive if one was accidentally committed.
$entries = @(& git -C $repositoryRoot -c core.quotepath=false ls-tree -r $commit)
if ($LASTEXITCODE -ne 0) { throw 'Could not read the release tree.' }
$sourceFiles = @(foreach ($entry in $entries) {
    if ($entry -notmatch '^100(?:644|755) blob [a-f0-9]+\t(.+)$') {
        throw 'The source archive requires regular files.'
    }
    $path = $Matches[1]
    if ($path -match '(?i)(^|/)(bin|obj|logs|recordings|app-data|\.git|\.dotnet|\.tools|\.build|\.swiftpm|artifacts|\.artifacts)(/|$)' -or
        $path -match '(?i)(^|/)(url\.txt|settings[^/]*\.json|\.env[^/]*|\.DS_Store)$' -or
        $path -match '(?i)\.(pfx|p12|key|log|pdb|exe|dll|nupkg)$' -or
        ($path -match '(?i)\.m3u8?$' -and $path -notmatch '^samples/')) {
        throw "Unexpected path in the release tree: $path"
    }
    $path
})
foreach ($required in @('README.md', 'README.pl.md', 'LICENSE', 'NOTICE', 'macos/VERSION', 'src/TelePilot.App/TelePilot.App.csproj')) {
    if ($sourceFiles -notcontains $required) { throw "Missing required source: $required" }
}

$outputDirectory = Join-Path $repositoryRoot "artifacts/release/$version/source"
$archiveName = "TelePilot-$version-source.zip"
$archivePath = Join-Path $outputDirectory $archiveName
if (Test-Path -LiteralPath $archivePath) { throw "Archive already exists: $archivePath" }
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
# git archive applies text conversions too. Ignore Windows' checkout defaults.
& git -C $repositoryRoot -c core.autocrlf=false -c core.eol=lf archive --format=zip "--prefix=TelePilot-$version/" "--output=$archivePath" $commit
if ($LASTEXITCODE -ne 0) { throw 'Git could not archive the release commit.' }
$hash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath "$archivePath.sha256" -Value "$hash  $archiveName" -Encoding ascii
[pscustomobject]@{ Commit = $commit; Archive = $archivePath; Files = $sourceFiles.Count; Sha256 = $hash }
