[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$PublishDirectory,
    [Parameter(Mandatory)]
    [ValidateSet('win-x64', 'win-arm64')]
    [string]$Runtime
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$publishRoot = [IO.Path]::GetFullPath($PublishDirectory)
$releaseBase = Join-Path $repositoryRoot 'artifacts\release'
if (-not $publishRoot.StartsWith($releaseBase + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Licencje można przygotować tylko wewnątrz artifacts/release.'
}
$licensesRoot = Join-Path $publishRoot 'licenses'
New-Item -ItemType Directory -Path $licensesRoot -Force | Out-Null
foreach ($name in @('LICENSE', 'NOTICE')) {
    Copy-Item -LiteralPath (Join-Path $repositoryRoot $name) -Destination $publishRoot
}
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'docs\THIRD_PARTY_LICENSES.md') -Destination $licensesRoot
Get-ChildItem -LiteralPath (Join-Path $repositoryRoot 'docs\licenses') -File | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination $licensesRoot
}

$assetsPath = Join-Path $repositoryRoot 'src\TelePilot.App\obj\project.assets.json'
$assets = Get-Content -LiteralPath $assetsPath -Raw | ConvertFrom-Json
$packageRoots = @($assets.packageFolders.PSObject.Properties.Name)
$inventory = @()
foreach ($library in $assets.libraries.PSObject.Properties | Where-Object { $_.Value.type -eq 'package' }) {
    $packageDirectory = $null
    foreach ($packageRoot in $packageRoots) {
        $candidate = Join-Path $packageRoot $library.Value.path
        if (Test-Path -LiteralPath $candidate -PathType Container) {
            $packageDirectory = [IO.Path]::GetFullPath($candidate)
            break
        }
    }
    if (-not $packageDirectory) { throw "Brak przywróconego pakietu $($library.Name)." }
    $spec = Get-ChildItem -LiteralPath $packageDirectory -Filter '*.nuspec' -File | Select-Object -First 1
    if (-not $spec) { throw "Brak metadanych pakietu $($library.Name)." }
    [xml]$metadataXml = Get-Content -LiteralPath $spec.FullName -Raw
    $metadata = $metadataXml.SelectSingleNode("//*[local-name()='metadata']")
    $license = $metadata.SelectSingleNode("*[local-name()='license']")
    $repository = $metadata.SelectSingleNode("*[local-name()='repository']")
    $projectUrl = $metadata.SelectSingleNode("*[local-name()='projectUrl']")
    $authors = $metadata.SelectSingleNode("*[local-name()='authors']")
    $destinationRoot = Join-Path $licensesRoot ('nuget\' + $library.Value.path)
    New-Item -ItemType Directory -Path $destinationRoot -Force | Out-Null
    Copy-Item -LiteralPath $spec.FullName -Destination $destinationRoot
    $noticeFiles = @(Get-ChildItem -LiteralPath $packageDirectory -Recurse -File | Where-Object {
        $_.Name -match '(?i)^(LICENSE|LICENCE|COPYING|COPYRIGHT|NOTICE|THIRD.?PARTY.?NOTICES)([._-].*)?$'
    })
    if ($license -and $license.GetAttribute('type') -eq 'file') {
        $licensePath = [IO.Path]::GetFullPath((Join-Path $packageDirectory $license.InnerText))
        if (-not $licensePath.StartsWith($packageDirectory + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Nieprawidłowa ścieżka licencji pakietu $($library.Name)."
        }
        if (-not (Test-Path -LiteralPath $licensePath -PathType Leaf)) { throw "Brak licencji pakietu $($library.Name)." }
        $noticeFiles += Get-Item -LiteralPath $licensePath
    }
    foreach ($notice in $noticeFiles | Sort-Object FullName -Unique) {
        $relative = $notice.FullName.Substring($packageDirectory.Length + 1)
        $destination = Join-Path $destinationRoot $relative
        New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
        Copy-Item -LiteralPath $notice.FullName -Destination $destination
    }
    $inventory += [pscustomobject]@{
        Package = $library.Name
        License = if ($license) { $license.InnerText } else { 'See package metadata' }
        Authors = if ($authors) { $authors.InnerText } else { '' }
        Repository = if ($repository) { $repository.GetAttribute('url') } else { '' }
        Commit = if ($repository) { $repository.GetAttribute('commit') } else { '' }
        ProjectUrl = if ($projectUrl) { $projectUrl.InnerText } else { '' }
    }
}
# The self-contained .NET runtime is a restore download, not a normal dependency.
$runtimePackageId = "Microsoft.NETCore.App.Runtime.$Runtime"
$runtimeVersion = (Get-Content -LiteralPath (Join-Path $publishRoot 'TelePilot.runtimeconfig.json') -Raw | ConvertFrom-Json).runtimeOptions.includedFrameworks |
    Where-Object { $_.name -eq 'Microsoft.NETCore.App' } | Select-Object -ExpandProperty version
$runtimePackage = $null
foreach ($packageRoot in $packageRoots) {
    $candidate = Join-Path $packageRoot ($runtimePackageId.ToLowerInvariant() + '\' + $runtimeVersion)
    if (Test-Path -LiteralPath $candidate -PathType Container) { $runtimePackage = $candidate; break }
}
if (-not $runtimePackage) { throw "Brak licencji środowiska .NET $runtimeVersion ($Runtime)." }
foreach ($name in @('LICENSE.TXT', 'THIRD-PARTY-NOTICES.TXT')) {
    Copy-Item -LiteralPath (Join-Path $runtimePackage $name) -Destination (Join-Path $licensesRoot "dotnet-$name")
}
$inventory | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $licensesRoot 'nuget-inventory.json') -Encoding utf8

$vlcDirectory = Join-Path $publishRoot "libvlc\$Runtime"
$nativeInventory = @(Get-ChildItem -LiteralPath $vlcDirectory -Filter '*.dll' -Recurse -File | Sort-Object FullName | ForEach-Object {
    [pscustomobject]@{
        File = $_.FullName.Substring($publishRoot.Length + 1).Replace('\', '/')
        Sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        Version = [Diagnostics.FileVersionInfo]::GetVersionInfo($_.FullName).FileVersion
    }
})
$nativeInventory | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $licensesRoot 'vlc-binaries.json') -Encoding utf8
Write-Output "Licencje: $($inventory.Count) pakietów NuGet, .NET $runtimeVersion, $($nativeInventory.Count) bibliotek i pluginów VLC."
