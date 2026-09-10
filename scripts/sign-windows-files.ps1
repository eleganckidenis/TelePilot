[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^[A-Fa-f0-9]{40}$')]
    [string]$CertificateThumbprint,

    [string]$SignToolPath,
    [uri]$TimestampUrl = 'http://timestamp.digicert.com',
    [string[]]$Path,
    [switch]$CheckOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ($env:OS -ne 'Windows_NT') { throw 'Podpisywanie Authenticode wymaga Windows.' }
if (-not $TimestampUrl.IsAbsoluteUri -or $TimestampUrl.Scheme -notin @('http', 'https')) {
    throw 'Serwer znacznika czasu musi mieć bezwzględny adres HTTP/HTTPS.'
}
$certificate = Get-Item -LiteralPath "Cert:\CurrentUser\My\$CertificateThumbprint" -ErrorAction SilentlyContinue
if (-not $certificate -or -not $certificate.HasPrivateKey) {
    throw 'Brak wskazanego certyfikatu z kluczem prywatnym w CurrentUser\My.'
}
if ($certificate.NotBefore -gt (Get-Date) -or $certificate.NotAfter -le (Get-Date)) {
    throw 'Certyfikat nie jest obecnie ważny.'
}
$enhancedKeyUsages = @($certificate.Extensions | Where-Object {
    $_ -is [Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension]
} | ForEach-Object { $_.EnhancedKeyUsages } | ForEach-Object { $_.Value })
if ('1.3.6.1.5.5.7.3.3' -notin $enhancedKeyUsages) {
    throw 'Certyfikat nie ma uprawnienia do podpisywania kodu.'
}
$chain = [Security.Cryptography.X509Certificates.X509Chain]::new()
try {
    if (-not $chain.Build($certificate)) { throw 'Nie można potwierdzić zaufanego łańcucha certyfikatu.' }
} finally { $chain.Dispose() }

if (-not $SignToolPath) { $SignToolPath = (Get-Command signtool.exe -ErrorAction Stop).Source }
if (-not (Test-Path -LiteralPath $SignToolPath -PathType Leaf)) { throw 'Nie znaleziono SignTool.exe.' }
$SignToolPath = (Resolve-Path -LiteralPath $SignToolPath).Path
if ($CheckOnly) { return }
if (-not $Path) { throw 'Nie podano plików do podpisania.' }

$releaseBase = [IO.Path]::GetFullPath((Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts\release'))
$prefix = $releaseBase + [IO.Path]::DirectorySeparatorChar
$files = @($Path | ForEach-Object {
    $fullPath = [IO.Path]::GetFullPath($_)
    if (-not $fullPath.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) -or
        [IO.Path]::GetExtension($fullPath) -notin @('.exe', '.dll') -or
        -not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
        throw 'Podpisywać można wyłącznie istniejące pliki EXE/DLL w artifacts/release.'
    }
    $item = Get-Item -LiteralPath $fullPath
    while ($item.FullName -ne $releaseBase) {
        if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Odmowa podpisania pliku przez dowiązanie.' }
        $item = Get-Item -LiteralPath (Split-Path -Parent $item.FullName)
    }
    $fullPath
} | Sort-Object -Unique)

foreach ($file in $files) {
    & $SignToolPath sign /s My /sha1 $CertificateThumbprint /fd SHA256 /tr $TimestampUrl.AbsoluteUri /td SHA256 $file
    if ($LASTEXITCODE -ne 0) { throw "SignTool nie podpisał pliku: $file" }
    & $SignToolPath verify /pa /all /tw $file
    if ($LASTEXITCODE -ne 0) { throw "Weryfikacja podpisu nie powiodła się: $file" }
    $signature = Get-AuthenticodeSignature -LiteralPath $file
    if ($signature.Status -ne 'Valid' -or
        $signature.SignerCertificate.Thumbprint -ne $CertificateThumbprint -or
        -not $signature.TimeStamperCertificate) {
        throw "Brak poprawnego podpisu wskazanego wydawcy ze znacznikiem czasu: $file"
    }
}
