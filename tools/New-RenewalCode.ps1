param(
    [Parameter(Mandatory = $true)]
    [string]$InstallationId,

    [ValidateRange(1, 3650)]
    [int]$Days = 30,

    [string]$PrivateKeyPath = ".\.license-keys\BanarasDhabaPOS-LicensePrivateKey.pem"
)

$resolvedPrivateKey = (Resolve-Path -LiteralPath $PrivateKeyPath).Path
$issuedUtc = [DateTimeOffset]::UtcNow
$validUntilUtc = $issuedUtc.AddDays($Days)
$payload = [ordered]@{
    installationId = $InstallationId.Trim().ToUpperInvariant()
    validUntilUtc = $validUntilUtc.ToString("O")
    issuedUtc = $issuedUtc.ToString("O")
    tokenId = [Guid]::NewGuid().ToString("N")
} | ConvertTo-Json -Compress

function ConvertTo-Base64Url([byte[]]$Bytes) {
    return [Convert]::ToBase64String($Bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_')
}

$payloadBytes = [Text.Encoding]::UTF8.GetBytes($payload)
$rsa = [Security.Cryptography.RSA]::Create()
try {
    $rsa.ImportFromPem([IO.File]::ReadAllText($resolvedPrivateKey))
    $signature = $rsa.SignData(
        $payloadBytes,
        [Security.Cryptography.HashAlgorithmName]::SHA256,
        [Security.Cryptography.RSASignaturePadding]::Pkcs1)
}
finally { $rsa.Dispose() }

$renewalCode = "BD1.$(ConvertTo-Base64Url $payloadBytes).$(ConvertTo-Base64Url $signature)"
Write-Output $renewalCode
