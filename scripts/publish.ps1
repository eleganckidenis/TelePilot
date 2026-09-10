[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'win-arm64', 'osx-x64', 'osx-arm64', 'linux-x64')]
    [string]$Runtime,

    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [switch]$All,

    [switch]$SelfContained
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$localDotnet = Join-Path $repositoryRoot '.dotnet\dotnet.exe'
$dotnet = if (Test-Path -LiteralPath $localDotnet) { $localDotnet } else { (Get-Command dotnet).Source }
$runtimes = if ($All) {
    @('win-x64', 'win-arm64', 'osx-x64', 'osx-arm64', 'linux-x64')
} elseif ($Runtime) {
    @($Runtime)
} else {
    throw 'Podaj -Runtime albo -All.'
}

foreach ($rid in $runtimes) {
    $output = Join-Path $repositoryRoot "artifacts\publish\$rid"
    & $dotnet publish (Join-Path $repositoryRoot 'src\TelePilot.App\TelePilot.App.csproj') `
        --configuration $Configuration `
        --runtime $rid `
        --self-contained:$($SelfContained.IsPresent) `
        --output $output `
        -p:PublishSingleFile=false

    if ($LASTEXITCODE -ne 0) {
        throw "Publikowanie dla $rid nie powiodło się."
    }
}
