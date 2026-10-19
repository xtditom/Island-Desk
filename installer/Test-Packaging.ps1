# Static packaging checks only. Never launches IslandDesk or an installer.
[CmdletBinding()]
param([string] $RuntimeInstallerPath)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path $PSScriptRoot -Parent
$dist = Join-Path $root 'dist'
[xml]$project = Get-Content (Join-Path $root 'IslandDesk.csproj') -Raw
$version = [string]$project.Project.PropertyGroup.Version
$manifest = @(Get-Content (Join-Path $dist 'build-manifest.json') -Raw | ConvertFrom-Json)
$checks = 0
foreach ($entry in $manifest) {
    $path = Join-Path $dist $entry.Filename
    if ($entry.Filename -notmatch "^IslandDesk-v$([regex]::Escape($version))-(win-(x64|x86)|Universal)-(Offline|Online)\.exe$" -or $entry.Version -ne $version) { throw 'Unexpected artifact/version in manifest.' }
    $file = Get-Item -LiteralPath $path
    if ($file.Length -ne $entry.Bytes -or $file.Length -lt 1MB) { throw "Invalid installer size: $($entry.Filename)" }
    if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ine $entry.SHA256) { throw "Installer checksum mismatch: $($entry.Filename)" }
    $info = $file.VersionInfo
    if ((@($info.FileMajorPart, $info.FileMinorPart, $info.FileBuildPart, $info.FilePrivatePart) -join '.') -ne "$version.0") { throw "Wrong installer version: $($entry.Filename)" }
    $reader = [IO.BinaryReader]::new([IO.File]::OpenRead($path))
    try {
        if ($reader.ReadUInt16() -ne 0x5a4d) { throw 'Invalid PE image.' }
        $reader.BaseStream.Position = 0x3c
        $header = $reader.ReadInt32()
        $reader.BaseStream.Position = $header
        if ($reader.ReadUInt32() -ne 0x4550 -or $reader.ReadUInt16() -ne 0x14c) { throw 'Setup launcher must be x86.' }
    } finally { $reader.Dispose() }
    $checksumLine = $entry.SHA256.ToLowerInvariant() + '  ' + $entry.Filename
    if ((Get-Content (Join-Path $dist 'SHA256SUMS.txt')) -cnotcontains $checksumLine) { throw 'Checksum document differs from manifest.' }
    $checks++
    Write-Host "PASS static installer metadata and checksum: $($entry.Filename)"
}
if ($manifest.Count -eq 0) { throw 'No installers in build manifest.' }
if ($RuntimeInstallerPath) {
    $lock = Get-Content (Join-Path $PSScriptRoot 'RuntimePrerequisite.json') -Raw | ConvertFrom-Json
    $name = Split-Path $RuntimeInstallerPath -Leaf
    if ($name -notmatch "^windowsdesktop-runtime-$([regex]::Escape($lock.Version))-win-(x64|x86|arm64)\.exe$") { throw 'Unrecognized Microsoft runtime package.' }
    $locked = $lock.Architectures.PSObject.Properties[$Matches[1]].Value
    if ((Get-FileHash -LiteralPath $RuntimeInstallerPath -Algorithm SHA512).Hash -ine $locked.Sha512 -or
        (Get-FileHash -LiteralPath $RuntimeInstallerPath -Algorithm SHA256).Hash -ine $locked.Sha256) { throw 'Microsoft runtime checksum mismatch.' }
    $signature = Get-AuthenticodeSignature -LiteralPath $RuntimeInstallerPath
    if ($signature.Status -ne 'Valid' -or -not $signature.SignerCertificate -or
        $signature.SignerCertificate.Subject -notmatch '(?:^|,\s*)O=Microsoft Corporation(?:,|$)') { throw 'Runtime Microsoft Authenticode validation failed.' }
    Write-Host 'PASS official runtime checksums and Microsoft Authenticode signature; installer not executed.'
}
Write-Host "$checks installer artifact checks passed. No application or installer was launched."
