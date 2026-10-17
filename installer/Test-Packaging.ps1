# Non-installing checks. Never executes either Setup or the Microsoft installer.
[CmdletBinding()]
param([string] $RuntimeInstallerPath)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path $PSScriptRoot -Parent
$audit = Join-Path $root 'artifacts\installer-size'
$script:checks = 0
function Check([bool] $passed, [string] $description) {
    if (-not $passed) { throw $description }
    $script:checks++
    Write-Host "PASS $description"
}
function Is-X64([string] $path) {
    $reader = [IO.BinaryReader]::new([IO.File]::OpenRead($path))
    try {
        if ($reader.ReadUInt16() -ne 0x5A4D) { return $false }
        $reader.BaseStream.Position = 0x3C
        $header = $reader.ReadInt32()
        $reader.BaseStream.Position = $header
        return $reader.ReadUInt32() -eq 0x00004550 -and $reader.ReadUInt16() -eq 0x8664
    } finally { $reader.Dispose() }
}

$offline = Join-Path $root 'artifacts\publish\win-x64'
$online = Join-Path $root 'artifacts\publish\win-x64-online'
$onlineConfig = Get-Content (Join-Path $online 'IslandDesk.runtimeconfig.json') -Raw | ConvertFrom-Json
Check (-not $onlineConfig.runtimeOptions.PSObject.Properties['includedFrameworks']) 'Online payload does not declare bundled frameworks'
Check (@($onlineConfig.runtimeOptions.frameworks | Where-Object { $_.name -in @('Microsoft.NETCore.App', 'Microsoft.WindowsDesktop.App') -and $_.version -eq '10.0.0' }).Count -eq 2) 'Online payload requests both .NET 10 shared frameworks'
foreach ($file in @('coreclr.dll', 'hostfxr.dll', 'PresentationFramework.dll', 'System.Windows.Forms.dll', 'System.Private.CoreLib.dll')) {
    Check (-not (Test-Path (Join-Path $online $file))) "Online payload excludes shared runtime file: $file"
    Check (Test-Path (Join-Path $offline $file)) "Offline payload retains runtime file: $file"
}
foreach ($file in @('Microsoft.Windows.SDK.NET.dll', 'WinRT.Runtime.dll')) {
    Check ((Get-FileHash (Join-Path $online $file)).Hash -eq (Get-FileHash (Join-Path $offline $file)).Hash) "Required Windows integration dependency retained unchanged: $file"
}
$offlineSetupName = 'IslandDesk-Setup.exe'
if (-not (Test-Path (Join-Path $root "dist\$offlineSetupName"))) { $offlineSetupName = 'IslandDesk-Setup-Offline.exe' }
foreach ($file in @($offlineSetupName, 'IslandDesk-Setup-Online.exe')) {
    $path = Join-Path $root "dist\$file"
    Check ((Get-Item $path).Length -gt 1MB -and (Is-X64 $path)) "Built installer is nonempty and x64: $file"
}
Check ((Get-Item (Join-Path $root 'dist\IslandDesk-Setup-Online.exe')).Length -lt (Get-Item (Join-Path $root "dist\$offlineSetupName")).Length) 'Online installer is smaller than the offline installer'

$removed = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($line in Get-Content (Join-Path $audit 'SharedFrameworkCleanup.iss')) {
    if ($line -notmatch '^Type: files; Name: "\{app\}\\([^"*]+)"; Check: MigratingSelfContainedInstallation$') { throw 'Unsafe cleanup entry.' }
    [void]$removed.Add($Matches[1])
}
$difference = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($file in Get-ChildItem $offline -File -Recurse) {
    $relative = $file.FullName.Substring($offline.Length + 1)
    if (-not (Test-Path -LiteralPath (Join-Path $online $relative))) { [void]$difference.Add($relative) }
}
Check ($removed.SetEquals($difference)) 'Upgrade cleanup covers exactly the old framework payload, with no wildcard or app dependency deletion'

# Exercise the actual apphosts before startup services/UI can initialize.
$guardSource = Get-Content (Join-Path $root 'Infrastructure\SingleInstanceGuard.cs') -Raw
if ($guardSource -notmatch 'MutexName = @"([^"]+)"') { throw 'Instance identity missing.' }
$created = $false
$guard = [Threading.Mutex]::new($false, $Matches[1], [ref]$created)
try {
    if (-not $created) { Write-Host 'Reusing the existing instance guard; the running app is left untouched.' }
    foreach ($variant in @('offline', 'online')) {
        $publish = if ($variant -eq 'online') { $online } else { $offline }
        $trace = Join-Path $audit "$variant-host-trace.txt"
        if (Test-Path $trace) { Remove-Item -LiteralPath $trace }
        $start = [Diagnostics.ProcessStartInfo]::new((Join-Path $publish 'IslandDesk.exe'))
        $start.UseShellExecute = $false
        $start.CreateNoWindow = $true
        $start.WorkingDirectory = $publish
        # Isolate the test from an inherited SDK override, without changing user environment.
        $start.EnvironmentVariables.Remove('DOTNET_ROOT_X64')
        $start.EnvironmentVariables.Remove('DOTNET_ROOT')
        $start.EnvironmentVariables['DOTNET_HOST_TRACE'] = '1'
        $start.EnvironmentVariables['DOTNET_HOST_TRACEFILE'] = $trace
        $child = [Diagnostics.Process]::Start($start)
        try {
            if (-not $child.WaitForExit(10000)) { $child.Kill(); throw 'Owned duplicate-launch test timed out.' }
            Check ($child.ExitCode -eq 0) "$variant published app exits cleanly when a first-instance guard exists"
        } finally { $child.Dispose() }
        $hostTrace = Get-Content -LiteralPath $trace -Raw
        if ($variant -eq 'online') {
            Check ($hostTrace -match 'shared\\Microsoft.WindowsDesktop.App\\10\.' -and
                   $hostTrace -match 'shared\\Microsoft.NETCore.App\\10\.' -and
                   -not $hostTrace.Contains((Join-Path $publish 'coreclr.dll'))) 'Online app resolves installed .NET/WPF rather than app-local runtime files'
        } else {
            Check ($hostTrace.Contains((Join-Path $publish 'coreclr.dll'))) 'Offline app resolves its bundled runtime'
        }
    }
} finally { $guard.Dispose() }

if ($RuntimeInstallerPath) {
    $manifest = Get-Content (Join-Path $PSScriptRoot 'RuntimePrerequisite.json') -Raw | ConvertFrom-Json
    $powerShell = Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
    $verify = Join-Path $PSScriptRoot 'Verify-Runtime.ps1'
    $log = Join-Path $audit 'signature-test.log'
    & $powerShell -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File $verify -Path $RuntimeInstallerPath -ExpectedSha512 $manifest.Sha512 -LogPath $log
    Check ($LASTEXITCODE -eq 0) 'Windows PowerShell verifier accepts the exact Microsoft checksum and trusted publisher'
    & $powerShell -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File $verify -Path $RuntimeInstallerPath -ExpectedSha512 ('0' * 128) -LogPath $log
    Check ($LASTEXITCODE -ne 0) 'Verifier rejects an incorrect checksum'
    $unsigned = Join-Path $audit 'unsigned-runtime-test.txt'
    'Unsigned test data; never executed.' | Set-Content $unsigned
    & $powerShell -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File $verify -Path $unsigned -ExpectedSha512 (Get-FileHash $unsigned -Algorithm SHA512).Hash -LogPath $log
    Check ($LASTEXITCODE -ne 0) 'Verifier rejects unsigned content even when its checksum matches'
    $otherPublisher = Join-Path $root '.tools\innosetup-7.1.0-x64.exe'
    if (Test-Path $otherPublisher) {
        Check ((Get-AuthenticodeSignature $otherPublisher).Status -eq 'Valid') 'Other-publisher test input is genuinely signed and trusted'
        & $powerShell -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File $verify -Path $otherPublisher -ExpectedSha512 (Get-FileHash $otherPublisher -Algorithm SHA512).Hash -LogPath $log
        Check ($LASTEXITCODE -ne 0) 'Verifier rejects a trusted non-Microsoft publisher with a matching checksum'
    }
}
Write-Host "$script:checks packaging checks passed. Neither installer nor runtime installer was executed."
