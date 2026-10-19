# Windows installers — v0.3.0 pre-release

Build with the .NET 10 SDK and [Inno Setup 7](https://jrsoftware.org/isdl.php).
The target computer never needs the SDK. From the repository root:

```powershell
.\installer\Build-Installer.ps1 -Architecture x64 -Variant Both
.\installer\Build-Installer.ps1 -Architecture x86 -Variant Both
.\installer\Build-Installer.ps1 -Architecture Universal -Variant Both
# All six distributables:
.\installer\Build-Installer.ps1 -Architecture All -Variant Both
```

Omitting Architecture retains the previous x64 default. Variant accepts Both
(default), Offline or Online. CompilerPath still accepts a custom ISCC.exe path.

| Architecture | Offline | Online |
| --- | --- | --- |
| x64 | IslandDesk-v0.3.0-win-x64-Offline.exe | IslandDesk-v0.3.0-win-x64-Online.exe |
| x86 | IslandDesk-v0.3.0-win-x86-Offline.exe | IslandDesk-v0.3.0-win-x86-Online.exe |
| Universal | IslandDesk-v0.3.0-Universal-Offline.exe | IslandDesk-v0.3.0-Universal-Online.exe |

Outputs are in dist with SHA256SUMS.txt, build-manifest.json and
RELEASE_PACKAGING_REPORT.md. An All/Both run leaves exactly six EXE installers.
Superseded EXEs are moved into that run's artifacts directory, not deleted.

## Architecture and publishing

Each requested RID/variant is published once, in Release, into a unique directory
under artifacts/packaging/v0.3.0/<run>/publish/win-<architecture>/<variant>.
Universal reuses these exact payloads rather than publishing them again.
The new Windows and Windows-Online profiles take their RID from the build script;
the previous Windows-x64 profiles remain available for direct publishing.

- x64 ships win-x64; x64-compatible Windows, including Windows 11 ARM64 emulation.
- x86 ships win-x86; compatible 32-bit Windows and x64 Windows (and x86 emulation
  on ARM64 where available). Its apphost and bundled native runtime are x86.
- Universal contains native win-x86, win-x64 and win-arm64 publishes. Setup selects
  ARM64 first on ARM64 Windows, x64 on x64 Windows, otherwise x86. Only the selected
  payload is installed. No AnyCPU substitute or ARM64 emulation fallback is used.

All six Setup launchers are **x86**, so Universal can start on 32-bit Windows.
This is independent of the installed application's architecture.
See [Inno SetupArchitecture](https://jrsoftware.org/ishelp/topic_setup_setuparchitecture.htm).

Offline is self-contained: its selected payload includes the .NET 10 Desktop and
Core runtimes and requires no runtime download. Universal Offline contains all three
self-contained payloads. Online is framework-dependent and bundles no shared runtime.

Trimming, Native AOT, ReadyToRun and single-file publishing remain disabled.
Windows SDK/WinRT dependencies are preserved. PDBs and the local development
IslandDesk.settings.json are excluded. The build checks apphost/native dependency
PE architecture, dependency RID, runtimeconfig, required dependencies and versions.

The project uses WPF/WinForms, SDK-provided WinRT projections, and Windows system
DLL/COM APIs; it has no architecture-specific third-party PackageReferences.
Interop uses pointer-sized handles and sequential native structures. A successful
cross-publish is **not** proof that every Windows integration works on real hardware.
Windows build 19041 remains the application API floor; use Windows editions/builds
supported by [.NET 10](https://learn.microsoft.com/en-us/dotnet/core/install/windows).
Architecture/device/UI checks remain the release owner's responsibility.

## Online runtime prerequisite

Setup uses the **selected payload architecture** for both detection and download.
A runtime of another architecture, Core-only or ASP.NET-only installation, preview,
.NET 9, or .NET 11 alone does not satisfy the requirement.

Detection follows the apphost's DOTNET_ROOT_<ARCH>, then DOTNET_ROOT(x86) for x86 on
64-bit Windows, then DOTNET_ROOT. Otherwise it checks architecture registration in
Microsoft's 32-bit registry view (64-bit fallback on 64-bit Windows), then standard
Program Files locations. Both Desktop/Core versions must satisfy runtimeconfig;
dotnet.exe and coreclr.dll must match the selected architecture. An invalid explicit
override blocks installation rather than silently using a different runtime.
See [Microsoft apphost environment rules](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-environment-variables#dotnet_root-dotnet_rootx86-dotnet_root_x86-dotnet_root_x64).

When compatible runtimes exist, no runtime download, verifier or elevation occurs.
Otherwise, before replacing IslandDesk files, interactive Setup asks permission to:

1. Download the selected official Microsoft Desktop Runtime 10.0.13 over HTTPS.
2. Verify Inno's pinned SHA-256, Microsoft's release-metadata SHA-512 and a valid,
   trusted Microsoft Corporation Authenticode signature through Windows PowerShell.
3. Elevate only the verified Microsoft installer, using /install /passive /norestart.
4. Check its exit code and recheck the selected architecture's runtime.

The version and three architecture URLs/hashes are pinned in RuntimePrerequisite.json.
SHA-512 values come from [Microsoft release metadata](https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json);
SHA-256 values were calculated after validating those hashes and Microsoft signatures.
No downloaded runtime installer is executed during packaging.

Declining, download/verification failures, UAC cancellation or runtime failures stop
before IslandDesk files are replaced. Silent Setup never implicitly consents to a
missing runtime's download/elevation. Exit code 3010 requests restart and suppresses
automatic app launch. An offline machine can use the Offline package.
Logs are in %LocalAppData%\IslandDesk\InstallerLogs. IslandDesk and its Setup remain
per-user/non-elevated. Uninstall does not remove the shared Microsoft runtime.

## Upgrades and uninstall

All packages retain:
- AppId: {9BBE998F-0584-4868-AFE8-2ED902A95A75}.
- App/Setup mutex identities.
- Default path: %LocalAppData%\Programs\IslandDesk.
- Preferences: %LocalAppData%\IslandDesk\preferences.json.
- Existing startup opt-in and desktop/Start Menu shortcuts.

Every package deliberately uses 64-bit install mode on 64-bit Windows, including
the x86 package. This keeps the same uninstall registry view as v0.2.0 and all
other variants on that machine. A 32-bit OS uses the 32-bit view. Payload bitness
does not dictate registry view or change the shared per-user path.
See [Inno install modes](https://jrsoftware.org/ishelp/topic_32vs64bitinstalls.htm).
The stable AppId and default append-mode uninstall log support in-place upgrades.

Setup asks the user to exit a running IslandDesk safely; no process is killed.
On variant/architecture switches, same-named files are replaced with ignoreversion.
Generated InstallDelete entries remove **only exact known package filenames absent
from the selected new payload**, only when the existing deployment registration
matches that installation path. PayloadCleanupFiles.txt records the actual
0.2.0 x64 self-contained inventory plus the 0.3.0 native architecture filenames;
current payload inventories extend it. This fixed inventory also makes a future
single-architecture/Online-only build aware of older architectures' runtime files.
There is no wildcard installation-folder cleanup. Settings, preferences, unrelated
files and user data are never in that cleanup list. Empty resource folders may remain.
Retain the legacy inventory when updating runtime packs so obsolete filenames stay known.

Uninstall removes package files, shortcuts and its matching startup entry, while
preserving preferences. Online/Offline and architecture switches share one product
registration; verify this manually before publishing.

## Verification and release boundary

Build-Installer performs only publishing, compilation and static file checks.
Test-Packaging.ps1 checks generated artifacts without launching applications or
installers. No installation or architecture runtime tests are performed by packaging.

Before publishing, manually verify:
- First install and uninstall on x86, x64 and ARM64 devices.
- Universal selects the native payload and installs only it.
- Upgrade from 0.2.0 and repeated Online/Offline and architecture switches leave one
  product entry, correct runtime files, working shortcuts and preserved preferences/startup.
- Online with/without matching Desktop Runtime; decline, offline, UAC cancellation,
  verification failure, install errors and restart handling.
- Offline install without internet; UI/media/hardware/fullscreen/startup behavior.

The EXEs are unsigned unless a separate signing process is configured. Packaging
does not create commits, tags, pushes or GitHub releases.
