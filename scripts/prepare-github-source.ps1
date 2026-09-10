[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
[xml]$appProject = Get-Content -LiteralPath (Join-Path $repositoryRoot 'src\TelePilot.App\TelePilot.App.csproj') -Raw
$version = $appProject.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'Nieprawidłowy numer wersji projektu.' }
$outputDirectory = Join-Path $repositoryRoot "artifacts\release\$version\source"
$archivePath = Join-Path $outputDirectory "TelePilot-$version-source.zip"
if (Test-Path -LiteralPath $archivePath) { throw "Archiwum już istnieje: $archivePath" }
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null

$rootFiles = @('.gitattributes', '.gitignore', 'AGENTS.md', 'CHANGELOG.md',
    'Directory.Build.props', 'Directory.Packages.props', 'IMPLEMENTATION_PLAN.md',
    'LICENSE', 'NOTICE', 'README.md', 'TelePilot.sln', 'global.json')
$allowedFolders = @('.github', 'docs', 'installer', 'samples', 'scripts', 'src', 'tests')
$candidates = @(& git -C $repositoryRoot -c core.quotepath=false ls-files --cached --others --exclude-standard)
if ($LASTEXITCODE -ne 0) { throw 'Nie udało się odczytać listy plików Git.' }
$sourceFiles = @($candidates | Sort-Object -Unique | Where-Object {
    $parts = $_ -split '/'
    ($rootFiles -contains $_ -or $allowedFolders -contains $parts[0]) -and
    $_ -notmatch '(^|/)(bin|obj|logs|recordings|app-data|\.git|\.dotnet|\.tools|artifacts|\.artifacts)(/|$)' -and
    $_ -notmatch '(?i)(^|/)(url\.txt|settings[^/]*\.json|\.env[^/]*)$' -and
    $_ -notmatch '(?i)\.(pfx|p12|key|log|pdb|exe|dll|nupkg)$' -and
    ($_ -notmatch '(?i)\.m3u8?$' -or $_ -match '^samples/')
})
foreach ($required in @('README.md', 'LICENSE', 'NOTICE', 'src/TelePilot.App/TelePilot.App.csproj', '.github/workflows/windows.yml')) {
    if ($sourceFiles -notcontains $required) { throw "Archiwum nie zawiera wymaganego pliku $required." }
}
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$stream = [IO.File]::Open($archivePath, [IO.FileMode]::CreateNew)
try {
    $archive = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Create, $true)
    try {
        foreach ($relativePath in $sourceFiles) {
            $fullPath = [IO.Path]::GetFullPath((Join-Path $repositoryRoot $relativePath))
            if (-not $fullPath.StartsWith($repositoryRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
                throw 'Plik źródłowy jest poza repozytorium.'
            }
            $sourceItem = Get-Item -LiteralPath $fullPath
            if ($sourceItem.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Archiwum nie przyjmuje dowiązań.' }
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
                $archive, $fullPath, "TelePilot-$version/$relativePath", [IO.Compression.CompressionLevel]::Optimal) | Out-Null
        }
    } finally { $archive.Dispose() }
} finally { $stream.Dispose() }
$hash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath "$archivePath.sha256" -Value "$hash  $([IO.Path]::GetFileName($archivePath))" -Encoding ascii
[pscustomobject]@{ Archive = $archivePath; Files = $sourceFiles.Count; Sha256 = $hash }
