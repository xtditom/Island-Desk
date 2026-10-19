[CmdletBinding()]
param(
    [string] $CompilerPath,
    [ValidateSet('Both', 'Offline', 'Online')][string] $Variant = 'Both',
    [ValidateSet('x64', 'x86', 'Universal', 'All')][string] $Architecture = 'x64'
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$Architecture = @{ x64='x64'; x86='x86'; Universal='Universal'; All='All' }[$Architecture]
$Variant = @{ Both='Both'; Offline='Offline'; Online='Online' }[$Variant]
$projectRoot = Split-Path $PSScriptRoot -Parent
$project = Join-Path $projectRoot 'IslandDesk.csproj'
$distDirectory = Join-Path $projectRoot 'dist'
[xml] $projectXml = Get-Content -LiteralPath $project -Raw
$version = [string] $projectXml.Project.PropertyGroup.Version
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'Use a numeric three-part application Version.' }
$guardSource = Get-Content (Join-Path $projectRoot 'Infrastructure\SingleInstanceGuard.cs') -Raw
if ($guardSource -notmatch 'MutexName = @"([^"]+)"') { throw 'Application mutex identity not found.' }
$mutexName = $Matches[1]

if (-not $CompilerPath) {
    $CompilerPath = @(
        (Join-Path $projectRoot '.tools\Inno Setup 7\ISCC.exe'),
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 7\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 7\ISCC.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 7\ISCC.exe')
    ) | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    if (-not $CompilerPath) {
        $command = Get-Command ISCC.exe -ErrorAction SilentlyContinue
        if ($command) { $CompilerPath = $command.Source }
    }
}
if (-not $CompilerPath -or -not (Test-Path -LiteralPath $CompilerPath)) { throw 'Install Inno Setup 7 or pass -CompilerPath.' }
$compilerVersion = & $CompilerPath --version 2>&1 | Out-String
if ($LASTEXITCODE -ne 0 -or $compilerVersion -notmatch '\b7\.\d+\.\d+\b') { throw "Inno Setup 7 required: $compilerVersion" }

# Isolated staging preserves previous outputs. No source/install/data directory is cleaned.
$runId = (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N').Substring(0, 8)
$runDirectory = Join-Path $projectRoot "artifacts\packaging\v$version\$runId"
$compileDirectory = Join-Path $runDirectory 'installers'
foreach ($directory in @('artifacts', 'artifacts\packaging', "artifacts\packaging\v$version", 'dist')) {
    $path = Join-Path $projectRoot $directory
    if ((Test-Path -LiteralPath $path) -and (Get-Item -LiteralPath $path).Attributes.HasFlag([IO.FileAttributes]::ReparsePoint)) {
        throw "Refusing redirected output directory: $path"
    }
}
New-Item -ItemType Directory -Path $compileDirectory, $distDirectory -Force | Out-Null
$architectures = if ($Architecture -eq 'All') { @('x64', 'x86', 'Universal') } else { @($Architecture) }
$rids = if ($Architecture -in @('All', 'Universal')) { @('x64', 'x86', 'arm64') } else { @($Architecture) }
$variants = if ($Variant -eq 'Both') { @('Offline', 'Online') } else { @($Variant) }
$payloads = @{}
$machineTypes = @{ x86 = 0x014c; x64 = 0x8664; arm64 = 0xaa64 }

function Get-PeMachine([string] $Path, [switch] $NativeOnly) {
    $reader = [IO.BinaryReader]::new([IO.File]::OpenRead($Path))
    try {
        if ($reader.ReadUInt16() -ne 0x5a4d) { throw "Not a PE file: $Path" }
        $reader.BaseStream.Position = 0x3c
        $header = $reader.ReadInt32()
        $reader.BaseStream.Position = $header
        if ($reader.ReadUInt32() -ne 0x4550) { throw "Invalid PE header: $Path" }
        $machine = $reader.ReadUInt16()
        if ($NativeOnly) {
            $reader.BaseStream.Position = $header + 24
            $magic = $reader.ReadUInt16()
            $dataDirectories = if ($magic -eq 0x20b) { 112 } else { 96 }
            $reader.BaseStream.Position = $header + 24 + $dataDirectories + (14 * 8)
            if ($reader.ReadUInt32() -ne 0) { return 0 } # Managed images are not native dependencies.
        }
        return $machine
    } finally { $reader.Dispose() }
}
function Assert-PayloadPath([string] $Relative) {
    if ($Relative -notmatch '^[A-Za-z0-9_.-]+(\\[A-Za-z0-9_.-]+)*$' -or
        $Relative -match '(^|\\)\.\.(\\|$)|(?i)(preferences|settings)\.json$|\.(pdb|log|tmp)$') {
        throw "Unsafe or local-only package path: $Relative"
    }
}
function Publish-Payload([string] $Arch, [string] $Distribution) {
    $key = "$Arch-$Distribution"
    if ($payloads.ContainsKey($key)) { return }
    $publishDirectory = Join-Path $runDirectory "publish\win-$Arch\$Distribution"
    $buildDirectory = Join-Path $runDirectory "dotnet\win-$Arch\$Distribution"
    $online = $Distribution -eq 'Online'
    $profile = if ($online) { 'Windows-Online' } else { 'Windows' }
    $selfContained = if ($online) { 'false' } else { 'true' }
    $arguments = @('publish', $project, '-c', 'Release', '-r', "win-$Arch", '--self-contained', $selfContained, '--nologo',
        "-p:PublishProfile=$profile", "-p:PlatformTarget=$Arch", "-p:ArtifactsPath=$buildDirectory",
        '-p:UseSharedCompilation=false', '-nodeReuse:false', '-o', $publishDirectory)
    & dotnet @arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed: $key ($LASTEXITCODE)." }
    foreach ($file in @('IslandDesk.exe', 'IslandDesk.dll', 'IslandDesk.runtimeconfig.json', 'IslandDesk.deps.json', 'Microsoft.Windows.SDK.NET.dll', 'WinRT.Runtime.dll')) {
        if (-not (Test-Path (Join-Path $publishDirectory $file))) { throw "Incomplete $key payload: $file" }
    }
    if ((Get-PeMachine (Join-Path $publishDirectory 'IslandDesk.exe')) -ne $machineTypes[$Arch]) { throw "Wrong apphost architecture: $key" }
    $info = (Get-Item (Join-Path $publishDirectory 'IslandDesk.exe')).VersionInfo
    if ($info.FileVersion -ne "$version.0" -or $info.ProductVersion -notmatch "^$([regex]::Escape($version))([+]|$)") { throw "Wrong application version: $key" }
    $config = Get-Content (Join-Path $publishDirectory 'IslandDesk.runtimeconfig.json') -Raw | ConvertFrom-Json
    $bundled = $config.runtimeOptions.PSObject.Properties['includedFrameworks']
    $shared = $config.runtimeOptions.PSObject.Properties['frameworks']
    if ($online -and ($bundled -or -not $shared)) { throw 'Online payload must be framework-dependent.' }
    if (-not $online -and (-not $bundled -or $shared)) { throw 'Offline payload must be self-contained.' }
    foreach ($file in @('hostfxr.dll', 'hostpolicy.dll', 'coreclr.dll', 'PresentationFramework.dll', 'System.Private.CoreLib.dll', 'System.Windows.Forms.dll')) {
        $present = Test-Path (Join-Path $publishDirectory $file)
        if ($online -eq $present) { throw "Wrong bundled runtime content for ${key}: $file" }
    }
    $files = @(Get-ChildItem -LiteralPath $publishDirectory -File -Recurse | ForEach-Object {
        $relative = $_.FullName.Substring($publishDirectory.Length + 1)
        Assert-PayloadPath $relative
        if ($_.Extension -in @('.exe', '.dll')) {
            $machine = Get-PeMachine $_.FullName -NativeOnly
            if ($machine -ne 0 -and $machine -ne $machineTypes[$Arch]) { throw "Mismatched native dependency in ${key}: $relative" }
        }
        $relative
    })
    $deps = Get-Content (Join-Path $publishDirectory 'IslandDesk.deps.json') -Raw | ConvertFrom-Json
    if (-not $deps.runtimeTarget.name.EndsWith("/win-$Arch")) { throw "Incorrect deps RID: $key" }
    $payloads[$key] = [pscustomobject]@{ Directory=$publishDirectory; Files=$files; Config=$config }
    Write-Host "Verified payload: win-$Arch $Distribution ($($files.Count) files)"
}
foreach ($arch in $rids) { foreach ($distribution in $variants) { Publish-Payload $arch $distribution } }

# A fixed baseline from shipped 0.2.0, plus current payloads; never wildcards or preferences.
$knownFiles = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($file in Get-Content (Join-Path $PSScriptRoot 'PayloadCleanupFiles.txt')) {
    if (-not $file -or $file.StartsWith('#')) { continue }
    Assert-PayloadPath $file
    [void]$knownFiles.Add($file)
}
foreach ($payload in $payloads.Values) { foreach ($file in $payload.Files) { [void]$knownFiles.Add($file) } }
$runtimeLock = Get-Content (Join-Path $PSScriptRoot 'RuntimePrerequisite.json') -Raw | ConvertFrom-Json

foreach ($architectureName in $architectures) {
    $selected = if ($architectureName -eq 'Universal') { @('x64', 'x86', 'arm64') } else { @($architectureName) }
    foreach ($distribution in $variants) {
        $baseName = if ($architectureName -eq 'Universal') { "IslandDesk-v$version-Universal-$distribution" } else { "IslandDesk-v$version-win-$architectureName-$distribution" }
        $filesInclude = Join-Path $runDirectory "$baseName-files.iss"
        $cleanupInclude = Join-Path $runDirectory "$baseName-cleanup.iss"
        $runtimeInclude = Join-Path $runDirectory "$baseName-runtime.iss"
        $fileLines = @()
        $cleanupLines = @('; Exact package-owned filenames absent from the selected new payload.')
        $runtimeLines = @()
        foreach ($arch in $selected) {
            $payload = $payloads["$arch-$distribution"]
            $fileLines += 'Source: "' + $payload.Directory + '\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Check: UsePayload(''' + $arch + ''')'
            foreach ($file in $knownFiles | Sort-Object) {
                if ($payload.Files -notcontains $file) {
                    $cleanupLines += 'Type: files; Name: "{app}\' + $file + '"; Check: CanCleanPayload(''' + $arch + ''')'
                }
            }
            if ($distribution -eq 'Online') {
                $locked = $runtimeLock.Architectures.PSObject.Properties[$arch].Value
                if ($runtimeLock.Version -notmatch '^10\.0\.\d+$' -or $locked.Sha256 -notmatch '^[a-fA-F0-9]{64}$' -or
                    $locked.Sha512 -notmatch '^[a-fA-F0-9]{128}$' -or
                    $locked.Url -cne "https://builds.dotnet.microsoft.com/dotnet/WindowsDesktop/$($runtimeLock.Version)/windowsdesktop-runtime-$($runtimeLock.Version)-win-$arch.exe") { throw "Invalid Microsoft runtime lock: $arch" }
                $frameworks = $payload.Config.runtimeOptions.frameworks
                $desktopMinimum = @($frameworks | Where-Object name -eq 'Microsoft.WindowsDesktop.App')[0].version
                $coreMinimum = @($frameworks | Where-Object name -eq 'Microsoft.NETCore.App')[0].version
                foreach ($minimum in @($desktopMinimum, $coreMinimum)) {
                    if ($minimum -notmatch '^10\.0\.\d+$' -or [version]$runtimeLock.Version -lt [version]$minimum) { throw 'Pinned runtime cannot satisfy the app.' }
                }
                $runtimeLines += "  if SelectedArchitecture = '$arch' then"
                $runtimeLines += '  begin'
                $runtimeLines += "    RuntimeUrl := '$($locked.Url)';"
                $runtimeLines += "    RuntimeSha256 := '$($locked.Sha256)';"
                $runtimeLines += "    RuntimeSha512 := '$($locked.Sha512)';"
                $runtimeLines += "    MinimumDesktopVersion := '$desktopMinimum';"
                $runtimeLines += "    MinimumCoreVersion := '$coreMinimum';"
                $runtimeLines += '  end;'
            }
        }
        $fileLines | Set-Content -LiteralPath $filesInclude -Encoding UTF8
        $cleanupLines | Set-Content -LiteralPath $cleanupInclude -Encoding UTF8
        $runtimeLines | Set-Content -LiteralPath $runtimeInclude -Encoding UTF8
        $definitions = @("--define=AppVersion=$version", "--define=AppMutexName=$mutexName", "--define=PackageArchitecture=$architectureName",
            "--define=OutputName=$baseName", "--define=InstallerOutputDir=$compileDirectory", "--define=PayloadFiles=$filesInclude", "--define=CleanupFile=$cleanupInclude")
        if ($distribution -eq 'Online') { $definitions += @('--define=OnlineInstaller', "--define=RuntimeDefinitions=$runtimeInclude", "--define=DesktopRuntimeVersion=$($runtimeLock.Version)") }
        & $CompilerPath --quiet-progress @definitions (Join-Path $PSScriptRoot 'IslandDesk.iss')
        if ($LASTEXITCODE -ne 0) { throw "Inno Setup compilation failed: $baseName ($LASTEXITCODE)." }
        $installer = Join-Path $compileDirectory "$baseName.exe"
        if (-not (Test-Path -LiteralPath $installer) -or (Get-Item $installer).Length -eq 0 -or (Get-PeMachine $installer) -ne 0x14c) { throw "Missing or non-x86 setup launcher: $baseName" }
        $setupVersion = (Get-Item $installer).VersionInfo
        if ((@($setupVersion.FileMajorPart, $setupVersion.FileMinorPart, $setupVersion.FileBuildPart, $setupVersion.FilePrivatePart) -join '.') -ne "$version.0") { throw "Wrong installer version: $baseName" }
    }
}
# Publish to dist only when all requested compilations have succeeded.
$archive = Join-Path $runDirectory 'previous-dist'
$newInstallers = @(Get-ChildItem -LiteralPath $compileDirectory -Filter '*.exe' -File)
$oldInstallers = if ($Architecture -eq 'All' -and $Variant -eq 'Both') { @(Get-ChildItem -LiteralPath $distDirectory -Filter '*.exe' -File) } else {
    @(Get-ChildItem -LiteralPath $distDirectory -Filter '*.exe' -File | Where-Object { $newInstallers.Name -contains $_.Name })
}
if ($oldInstallers.Count) {
    New-Item -ItemType Directory -Path $archive -Force | Out-Null
    foreach ($file in $oldInstallers) {
        if ($file.DirectoryName -ne [IO.Path]::GetFullPath($distDirectory) -or $file.Attributes.HasFlag([IO.FileAttributes]::ReparsePoint)) { throw 'Refusing redirected archive source.' }
        Move-Item -LiteralPath $file.FullName -Destination (Join-Path $archive $file.Name)
    }
}
foreach ($file in $newInstallers) { Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $distDirectory $file.Name) }
$outputs = @(Get-ChildItem -LiteralPath $distDirectory -Filter "IslandDesk-v$version-*.exe" -File | Sort-Object Name | ForEach-Object {
    if ($_.Name -notmatch "^IslandDesk-v$([regex]::Escape($version))-(win-(x64|x86)|Universal)-(Offline|Online)\.exe$") { throw "Unexpected installer name: $($_.Name)" }
    [pscustomobject]@{ Filename=$_.Name; Version=$version; Architecture=if ($Matches[1] -eq 'Universal') { 'Universal (x86/x64/arm64)' } else { $Matches[2] }; Distribution=$Matches[3]; Bytes=$_.Length; SHA256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
})
$outputs | ConvertTo-Json -Depth 3 | Set-Content (Join-Path $distDirectory 'build-manifest.json') -Encoding UTF8
$outputs | ForEach-Object { $_.SHA256.ToLowerInvariant() + '  ' + $_.Filename } | Set-Content (Join-Path $distDirectory 'SHA256SUMS.txt') -Encoding ASCII
$report = @("# IslandDesk v$version pre-release packaging", '', "Built: $([DateTime]::UtcNow.ToString('u'))", "SDK: $(dotnet --version)", "Compiler: $($compilerVersion.Trim())", '', '| Installer | Bytes |', '| --- | ---: |')
$report += $outputs | ForEach-Object { "| $($_.Filename) | $($_.Bytes) |" }
$report += @('', 'Release publishes and all requested installer compilations succeeded. Apphost/native PE architecture, version metadata, RID, runtime configuration, payload contents and SHA-256 were checked.',
    'Universal uses an x86 Setup launcher and selects native ARM64, x64 or x86 payloads. Online prerequisite metadata is architecture-specific and pinned to official Microsoft checksums, with Authenticode verification before execution.',
    'No app or installer was launched. Installation, UI, runtime and physical-device compatibility remain manual checks. Packages are unsigned pre-release EXEs; no signing identity was configured.',
    'Before publishing: verify 0.2.0 upgrades, Online/Offline and architecture switches, a single uninstall entry, preferences/startup/shortcuts, runtime present/absent/cancel/offline paths, offline installation without internet, and uninstall on each target OS.',
    "Staging and superseded distributables are preserved under: $runDirectory", 'No commit, push, tag or GitHub release was created.')
$report | Set-Content (Join-Path $distDirectory 'RELEASE_PACKAGING_REPORT.md') -Encoding UTF8
$outputs | Format-Table Filename, Bytes, SHA256 -AutoSize
Write-Host "Manifest, SHA256SUMS.txt and packaging report: $distDirectory"
