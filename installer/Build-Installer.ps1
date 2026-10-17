[CmdletBinding()]
param(
    [string] $CompilerPath,
    [ValidateSet('Both', 'Offline', 'Online')][string] $Variant = 'Both'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$projectRoot = Split-Path $PSScriptRoot -Parent
$project = Join-Path $projectRoot 'IslandDesk.csproj'
$distDirectory = Join-Path $projectRoot 'dist'
$offlinePublish = Join-Path $projectRoot 'artifacts\publish\win-x64'
$onlinePublish = Join-Path $projectRoot 'artifacts\publish\win-x64-online'
$auditDirectory = Join-Path $projectRoot 'artifacts\installer-size'

if (-not $CompilerPath) {
    $compilerCandidates = @(
        (Join-Path $projectRoot '.tools\Inno Setup 7\ISCC.exe'),
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 7\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 7\ISCC.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 7\ISCC.exe')
    )
    $CompilerPath = $compilerCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    if (-not $CompilerPath) {
        $compilerCommand = Get-Command ISCC.exe -ErrorAction SilentlyContinue
        if ($compilerCommand) { $CompilerPath = $compilerCommand.Source }
    }
}
if (-not $CompilerPath -or -not (Test-Path -LiteralPath $CompilerPath)) {
    throw 'Install Inno Setup 7 from https://jrsoftware.org/isdl.php or pass -CompilerPath to its ISCC.exe.'
}
$compilerVersion = & $CompilerPath --version 2>&1 | Out-String
if ($LASTEXITCODE -ne 0 -or $compilerVersion -notmatch '\b7\.\d+\.\d+\b') {
    throw "Inno Setup 7 is required. Compiler reported: $compilerVersion"
}

[xml] $projectXml = Get-Content -LiteralPath $project -Raw
$version = [string] $projectXml.Project.PropertyGroup.Version
if ($version -notmatch '^\d+\.\d+\.\d+(\.\d+)?$') { throw 'Use a numeric application Version for Windows packaging.' }
$guardSource = Get-Content (Join-Path $projectRoot 'Infrastructure\SingleInstanceGuard.cs') -Raw
if ($guardSource -notmatch 'MutexName = @"([^"]+)"') { throw 'Application mutex identity not found.' }
$mutexName = $Matches[1]
if (-not (Test-Path (Join-Path $projectRoot 'Assets\IslandDesk.ico'))) { throw 'Assets\IslandDesk.ico is required.' }

function Publish-IslandDesk([bool] $online) {
    $publishDirectory = if ($online) { $onlinePublish } else { $offlinePublish }
    $profile = if ($online) { 'Windows-x64-Online' } else { 'Windows-x64' }
    $artifactsName = if ($online) { 'dotnet-online' } else { 'dotnet' }
    $selfContained = if ($online) { 'false' } else { 'true' }
    # Clean only the fixed, generated staging directory; never source/install/data.
    foreach ($generatedPath in @((Join-Path $projectRoot 'artifacts'), (Join-Path $projectRoot 'artifacts\publish'), $publishDirectory)) {
        if ((Test-Path -LiteralPath $generatedPath) -and
            (Get-Item -LiteralPath $generatedPath).Attributes.HasFlag([IO.FileAttributes]::ReparsePoint)) {
            throw 'Refusing to publish through a redirected artifacts directory.'
        }
    }
    if (Test-Path -LiteralPath $publishDirectory) {
        $generatedDirectory = Get-Item -LiteralPath $publishDirectory
        $expectedDirectory = [IO.Path]::GetFullPath($publishDirectory)
        if ($generatedDirectory.FullName -ne $expectedDirectory -or
            -not $expectedDirectory.StartsWith((Join-Path $projectRoot 'artifacts\publish\'), [StringComparison]::OrdinalIgnoreCase)) {
            throw 'Refusing to clean a publish directory outside staging.'
        }
        Remove-Item -LiteralPath $generatedDirectory.FullName -Recurse -Force
    }
    New-Item -ItemType Directory -Path $publishDirectory, $distDirectory, $auditDirectory -Force | Out-Null

    & dotnet publish $project -c Release -r win-x64 --self-contained $selfContained --nologo `
        "-p:PublishProfile=$profile" "-p:ArtifactsPath=$projectRoot\artifacts\$artifactsName" `
        '-p:UseSharedCompilation=false' '-nodeReuse:false' -o $publishDirectory
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)." }

    foreach ($file in @('IslandDesk.exe', 'IslandDesk.dll', 'IslandDesk.runtimeconfig.json', 'IslandDesk.deps.json', 'Microsoft.Windows.SDK.NET.dll', 'WinRT.Runtime.dll')) {
        if (-not (Test-Path (Join-Path $publishDirectory $file))) { throw "Incomplete publish: $file missing." }
    }
    $runtimeConfig = Get-Content (Join-Path $publishDirectory 'IslandDesk.runtimeconfig.json') -Raw | ConvertFrom-Json
    $bundled = $runtimeConfig.runtimeOptions.PSObject.Properties['includedFrameworks']
    $shared = $runtimeConfig.runtimeOptions.PSObject.Properties['frameworks']
    if ($online -and ($bundled -or -not $shared)) { throw 'Online publish must be framework-dependent.' }
    if (-not $online -and (-not $bundled -or $shared)) { throw 'Offline publish must be self-contained.' }
    foreach ($file in @('hostfxr.dll', 'hostpolicy.dll', 'coreclr.dll', 'PresentationFramework.dll', 'System.Private.CoreLib.dll', 'System.Windows.Forms.dll')) {
        $present = Test-Path (Join-Path $publishDirectory $file)
        if ($online -and $present) { throw "Shared framework file must not be bundled online: $file" }
        if (-not $online -and -not $present) { throw "Offline runtime is incomplete: $file" }
    }
    # Inventory all files, without removing or trimming any app dependency.
    $inventory = Get-ChildItem -LiteralPath $publishDirectory -File -Recurse | ForEach-Object {
        [pscustomobject]@{ Path=$_.FullName.Substring($publishDirectory.Length + 1); Bytes=$_.Length }
    }
    $inventory | Sort-Object Bytes -Descending | ConvertTo-Json | Set-Content (Join-Path $auditDirectory "$profile-files.json")
}

function Get-OnlineDefinitions {
    $manifest = Get-Content (Join-Path $PSScriptRoot 'RuntimePrerequisite.json') -Raw | ConvertFrom-Json
    if ($manifest.Version -notmatch '^10\.0\.\d+$' -or $manifest.Sha256 -notmatch '^[a-fA-F0-9]{64}$' -or
        $manifest.Sha512 -notmatch '^[a-fA-F0-9]{128}$' -or
        $manifest.Url -cne "https://builds.dotnet.microsoft.com/dotnet/WindowsDesktop/$($manifest.Version)/windowsdesktop-runtime-$($manifest.Version)-win-x64.exe") {
        throw 'Runtime lock must contain the official Microsoft .NET 10 x64 Desktop Runtime URL and checksums.'
    }
    $config = Get-Content (Join-Path $onlinePublish 'IslandDesk.runtimeconfig.json') -Raw | ConvertFrom-Json
    $desktopMinimum = @($config.runtimeOptions.frameworks | Where-Object name -eq 'Microsoft.WindowsDesktop.App')[0].version
    $coreMinimum = @($config.runtimeOptions.frameworks | Where-Object name -eq 'Microsoft.NETCore.App')[0].version
    foreach ($minimum in @($desktopMinimum, $coreMinimum)) {
        if ($minimum -notmatch '^10\.0\.\d+$' -or [version]$manifest.Version -lt [version]$minimum) {
            throw 'Pinned runtime cannot satisfy the published application.'
        }
    }
    # When replacing an existing offline install, remove only old runtime-pack
    # files proven absent from the new payload. Preserve SDK/WinRT dependencies.
    $frameworkFiles = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $deps = Get-Content (Join-Path $offlinePublish 'IslandDesk.deps.json') -Raw | ConvertFrom-Json
    $assetsFile = Join-Path $projectRoot 'artifacts\dotnet\obj\IslandDesk\project.assets.json'
    $packageRoots = (Get-Content -LiteralPath $assetsFile -Raw | ConvertFrom-Json).packageFolders.PSObject.Properties.Name
    foreach ($target in $deps.targets.PSObject.Properties) {
        foreach ($library in $target.Value.PSObject.Properties) {
            if ($library.Name -notmatch '^runtimepack\.Microsoft\.(NETCore|WindowsDesktop)\.App\.Runtime\.win-x64/') { continue }
            foreach ($kind in @('runtime', 'native', 'resources')) {
                $assets = $library.Value.PSObject.Properties[$kind]
                if (-not $assets) { continue }
                foreach ($asset in $assets.Value.PSObject.Properties) {
                    $relative = $asset.Name.Replace('/', '\')
                    $source = [IO.Path]::GetFullPath((Join-Path $offlinePublish $relative))
                    if (-not $source.StartsWith($offlinePublish + '\', [StringComparison]::OrdinalIgnoreCase) -or $relative.Contains('"')) {
                        throw 'Invalid runtime-pack asset path.'
                    }
                    if ((Test-Path -LiteralPath $source) -and -not (Test-Path -LiteralPath (Join-Path $onlinePublish $relative))) {
                        [void]$frameworkFiles.Add($relative)
                    }
                }
            }
            # Satellite resources are not listed in the application's .deps.json.
            # Identify them through Microsoft's runtime-pack manifest AND matching
            # file hashes, rather than deleting culture folders or *.resources.dll.
            $packageParts = $library.Name.Split('/')
            foreach ($packageRoot in $packageRoots) {
                $packRoot = Join-Path $packageRoot ($packageParts[0].Substring('runtimepack.'.Length).ToLowerInvariant() + '\' + $packageParts[1])
                $runtimeList = Join-Path $packRoot 'data\RuntimeList.xml'
                if (-not (Test-Path -LiteralPath $runtimeList)) { continue }
                [xml]$packManifest = Get-Content -LiteralPath $runtimeList -Raw
                foreach ($resource in $packManifest.FileList.File | Where-Object Type -eq 'Resources') {
                    $prefix = 'runtimes/win-x64/lib/net10.0/'
                    if (-not $resource.Path.StartsWith($prefix)) { throw 'Unexpected runtime resource layout.' }
                    $relative = $resource.Path.Substring($prefix.Length).Replace('/', '\')
                    $publishedResource = Join-Path $offlinePublish $relative
                    if ((Test-Path -LiteralPath $publishedResource) -and -not (Test-Path -LiteralPath (Join-Path $onlinePublish $relative))) {
                        $packageResource = Join-Path $packRoot $resource.Path
                        if ((Get-FileHash -LiteralPath $packageResource -Algorithm SHA256).Hash -eq
                            (Get-FileHash -LiteralPath $publishedResource -Algorithm SHA256).Hash) {
                            [void]$frameworkFiles.Add($relative)
                        }
                    }
                }
                break
            }
        }
    }
    if ($frameworkFiles.Count -eq 0) { throw 'No old bundled-runtime assets were identified; refusing an unsafe variant migration.' }
    $cleanupFile = Join-Path $auditDirectory 'SharedFrameworkCleanup.iss'
    $frameworkFiles | Sort-Object | ForEach-Object {
        'Type: files; Name: "{app}\' + $_ + '"; Check: MigratingSelfContainedInstallation'
    } | Set-Content -LiteralPath $cleanupFile
    @('--define=OnlineInstaller', "--define=DesktopRuntimeVersion=$($manifest.Version)",
      "--define=DesktopRuntimeUrl=$($manifest.Url)", "--define=DesktopRuntimeSha256=$($manifest.Sha256)",
      "--define=DesktopRuntimeSha512=$($manifest.Sha512)", "--define=MinimumDesktopVersion=$desktopMinimum",
      "--define=MinimumCoreVersion=$coreMinimum", "--define=CleanupFile=$cleanupFile")
}

function Build-Variant([bool] $online) {
    Publish-IslandDesk $online
    $publishDirectory = if ($online) { $onlinePublish } else { $offlinePublish }
    $name = if ($online) { 'IslandDesk-Setup-Online.exe' } else { 'IslandDesk-Setup.exe' }
    $installer = Join-Path $distDirectory $name
    $definitions = @("--define=AppVersion=$version", "--define=PublishDir=$publishDirectory", "--define=AppMutexName=$mutexName")
    if ($online) {
        if (-not (Test-Path (Join-Path $offlinePublish 'IslandDesk.deps.json'))) { Publish-IslandDesk $false }
        $definitions += Get-OnlineDefinitions
    }
    if (Test-Path -LiteralPath $installer) { Remove-Item -LiteralPath $installer -Force }
    & $CompilerPath --quiet-progress @definitions (Join-Path $PSScriptRoot 'IslandDesk.iss')
    if ($LASTEXITCODE -ne 0) { throw "Inno Setup compilation failed ($LASTEXITCODE)." }
    if (-not (Test-Path -LiteralPath $installer) -or (Get-Item -LiteralPath $installer).Length -eq 0) {
        throw 'The installer was not produced.'
    }
    Write-Host "Installer ready: $installer"
}

if ($Variant -in @('Both', 'Offline')) { Build-Variant $false }
if ($Variant -in @('Both', 'Online')) { Build-Variant $true }
$outputs = foreach ($name in @('IslandDesk-Setup.exe', 'IslandDesk-Setup-Online.exe')) {
    $path = Join-Path $distDirectory $name
    if (Test-Path -LiteralPath $path) {
        [pscustomobject]@{ File=$name; Bytes=(Get-Item -LiteralPath $path).Length; SHA256=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash }
    }
}
$outputs | ConvertTo-Json | Set-Content (Join-Path $auditDirectory 'installer-sizes.json')
$outputs | Format-Table -AutoSize
