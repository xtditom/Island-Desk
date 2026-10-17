# Runs only during online Setup (and packaging validation), never in IslandDesk.
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $Path,
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-fA-F]{128}$')][string] $ExpectedSha512,
    [Parameter(Mandatory)][string] $LogPath
)
$ErrorActionPreference = 'Stop'
try {
    if ((Get-FileHash -LiteralPath $Path -Algorithm SHA512).Hash -ne $ExpectedSha512) {
        throw 'The runtime installer does not match Microsoft release metadata (SHA-512).'
    }
    $signature = Get-AuthenticodeSignature -LiteralPath $Path
    if ($signature.Status -ne 'Valid' -or -not $signature.SignerCertificate -or
        $signature.SignerCertificate.Subject -notmatch '(?:^|,\s*)O=Microsoft Corporation(?:,|$)') {
        throw "The runtime installer does not have a valid, trusted Microsoft Authenticode signature: $($signature.Status)."
    }
    "Verified SHA-512 and Microsoft publisher: $($signature.SignerCertificate.Subject)" | Set-Content -LiteralPath $LogPath
    exit 0
}
catch {
    $_.Exception.Message | Set-Content -LiteralPath $LogPath
    exit 1
}
