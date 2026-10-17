# Windows installer

The application project is `IslandDesk.csproj`. Files named `IslandDesk_*_wpftmp.csproj`
are generated WPF build files, not application entry points.

## Build

Install the .NET 10 SDK and [Inno Setup 7](https://jrsoftware.org/isdl.php) on the build
computer. Target computers never need the SDK. Choose the online distribution when
the shared Desktop Runtime is available or can be installed; choose offline otherwise.
From the IslandDesk source folder, run:

```powershell
.\installer\Build-Installer.ps1
# Build only one distribution:
.\installer\Build-Installer.ps1 -Variant Online
.\installer\Build-Installer.ps1 -Variant Offline
# For a compiler installed in another location:
.\installer\Build-Installer.ps1 -CompilerPath 'C:\path\to\Inno Setup 7\ISCC.exe'
```

The script explicitly publishes `IslandDesk.csproj` twice in Release for `win-x64`:

| Distribution | Publish profile | Output |
| --- | --- | --- |
| Offline, self-contained | `Windows-x64` | `dist/IslandDesk-Setup.exe` |
| Online, framework-dependent | `Windows-x64-Online` | `dist/IslandDesk-Setup-Online.exe` |

Online uses `--self-contained false`; offline uses `--self-contained true`. Trimming,
Native AOT, single-file publishing and ReadyToRun remain disabled. Nothing is manually
stripped from either publish. Windows SDK/WinRT dependencies remain in both. Staging
is separate (`artifacts/publish/win-x64` and `win-x64-online`), so one variant cannot
contaminate the other. The script cleans only its fixed generated staging directory,
validates payloads, and records sizes/hashes in `artifacts/installer-size/installer-sizes.json`.
The compiler and staging directories are excluded from the application build and Git.

## Online runtime prerequisite

The online installer requires **Microsoft .NET 10.0 Desktop Runtime x64**. Core Runtime
alone, ASP.NET Runtime alone, x86/ARM64-only runtimes, previews, .NET 9 or .NET 11 alone
do not satisfy it. The application's minimum versions come from its published
`runtimeconfig.json` (currently 10.0.0). The native installer checks the x64 install
root, x64 PE host, stable shared-framework folders and matching Desktop/Core patch
requirements. It follows `DOTNET_ROOT_X64`/`DOTNET_ROOT`, then Microsoft's architecture
registration (32-bit registry view), then the default x64 location. Custom overrides
must point to a complete compatible x64 installation.

When compatible runtimes are present, **no runtime download, verifier process or
elevation occurs**. Otherwise, before changing IslandDesk files, Setup offers to:

1. Download Microsoft's pinned Desktop Runtime 10.0.13 installer over HTTPS.
2. Verify SHA-256 in Inno, then Microsoft's published SHA-512 and a trusted Authenticode
   signature with Microsoft Corporation as the signer using Windows PowerShell.
3. Run only the authenticated Microsoft installer with administrator approval,
   visible progress, `/install /passive /norestart`, and wait for its result.
4. Recheck compatibility before installing/upgrading IslandDesk. Exit code 3010 is
   handled as restart required, and automatic IslandDesk launch is suppressed then.

IslandDesk Setup itself stays per-user/non-elevated. Declining, cancelling UAC,
download/offline errors, verification failures and runtime installation failures stop
before IslandDesk files are replaced. Retry, install the official runtime manually,
or use the offline distribution. Silent installation with a missing prerequisite
fails safely without implicitly consenting to download/elevation. Corporate policy
that blocks the PowerShell verifier is treated as failure, never bypassed with an
unchecked executable. Logs remain in `%LocalAppData%\IslandDesk\InstallerLogs`.

The runtime download is about **57.3 MiB**, separate from the small online setup;
the online distribution does not reduce total first-install transfer on a machine
without .NET. The shared runtime remains installed when IslandDesk is uninstalled.

`RuntimePrerequisite.json` pins the URL and checksums. Its SHA-512 comes from
[Microsoft release metadata](https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json).
The SHA-256 was calculated only after that checksum and Microsoft's trusted signature
were verified. Future packaging releases should update the lock using current official
metadata, download the exact x64 Desktop executable, verify SHA-512/signature, and then
record its SHA-256. This is a build-time maintenance step, not an automatic updater.

## Install, run, upgrade and uninstall

1. Exit any development or installed IslandDesk copy through its tray menu.
2. Double-click your chosen installer. IslandDesk uses the current user's account;
   only an absent shared runtime in the online variant requests administrator approval.
   The default installation folder is
   `%LocalAppData%\Programs\IslandDesk`.
3. Select the optional desktop shortcut if wanted. A Start Menu shortcut is always
   created. Leave **Launch IslandDesk** selected to run it after installation, or
   open **IslandDesk** from Start later. No editor or `dotnet run` is involved.
4. For upgrades, run the new installer. Keep the same application identity and
   installation folder. Setup asks you to exit the running app safely; it never
   forcibly terminates it. It also checks immediately before replacing files.
   Both variants have the same AppId and replace the same installation. On an
   offline-to-online switch, only exact old runtime-pack filenames are removed,
   conditional on the registered IslandDesk path and its old self-contained config.
   The list is derived from `.deps.json`; satellite resources also require Microsoft's
   pack manifest and matching file hashes. No wildcard cleanup touches app dependencies
   or preferences. Empty culture directories may remain until uninstall.
5. Uninstall from **Settings → Apps → Installed apps → IslandDesk → Uninstall**.
   Exit the tray app first. Shortcuts, installed binaries and the matching startup
   entry are removed; preferences remain available for a future reinstall.

The executable, installer, shortcuts, Settings window and tray use the existing
`Assets/IslandDesk.ico`. The island's pet mascot is unchanged. This build is unsigned;
Windows may show an unknown-publisher warning. Signing requires your own certificate.

## Settings and Windows startup

Preferences remain at `%LocalAppData%\IslandDesk\preferences.json`. Setup neither
overwrites nor deletes this folder, including on uninstall. Existing development
preferences are deliberately retained. Application binaries are copied into a
separate installation directory; building/publishing never replaces the installed copy.

Startup stays **OFF** for a first installation with no existing startup entry. If
`HKCU\Software\Microsoft\Windows\CurrentVersion\Run\IslandDesk` already exists,
Setup preserves that opt-in and changes its command to the quoted installed
`IslandDesk.exe` path. Setup also records the installed executable in
`HKCU\Software\IslandDesk\Deployment\ExecutablePath`. Startup registration prefers
this existing installed copy, including when changed from a development build, and
otherwise uses `Environment.ProcessPath`. Uninstall removes this deployment metadata and
the Run value only if it still points to that installation. Windows' own Startup Apps
disabled state is not overridden. Without any installed copy, the existing development
startup behavior remains available; installing migrates its entry to the installed copy.

One session-local named mutex is shared by installed and development copies, with
no timers or focus changes. Additional launches exit without opening another widget
or tray icon. Other Windows login sessions have their own instance. Setup and Uninstall
check this same mutex, and also recognize the window of older prototypes.

## Stable update identity

- Product/version metadata: `IslandDesk.csproj` (`0.2.0` initially).
- Inno AppId: `{9BBE998F-0584-4868-AFE8-2ED902A95A75}` — do not regenerate it.
- App mutex: `Infrastructure/SingleInstanceGuard.cs`; the build script passes the
  actual constant to Inno to prevent mismatches.
- Architecture: Windows x64. The installer permits Windows x64 emulation on supported
  Windows 11 ARM64 systems; this is not a native ARM64 build and is not tested there.
- Minimum Windows API version: Windows 10 build 19041, matching the application.
- Future releases: update the project Version, rebuild and distribute the installer.
  No automatic updater or background deployment service is installed. Offline builds
  need republishing for bundled-runtime security updates. Online builds use compatible
  shared-runtime servicing without bundling that framework.

## Manual installation checklist (not performed by the packaging build)

- Install as a standard user; confirm the expected folder. Offline and online with
  an existing compatible runtime should not request elevation.
- Online with .NET 10 x64 Desktop Runtime present: verify no runtime network download.
- Online with only .NET Core, an older major, previews, or x86/ARM64 runtimes: verify
  the prerequisite offer. Test decline, offline/network failure, cancel during download,
  UAC denial, installer failure and a successful installation/recheck. Confirm original
  app files/preferences survive failed upgrades. Test restart-required handling.
- Online in silent mode without the runtime: confirm safe failure without a prompt/download.
- Launch from Start and the optional desktop shortcut; check custom icons and tray.
- Launch again; confirm there is only one widget and tray icon and no focus steal.
- Verify Settings, media/volume, monitoring, hover/click, fullscreen hide/restore,
  DPI/display changes and existing top-offset positioning.
- Enable Windows startup in the **installed** app. Confirm the quoted Run command
  points to the installed executable; sign out/in and verify. Disable and verify removal.
- Change a preference, exit, reopen, and confirm it persists. Try upgrading while the
  app is running: close via the tray when asked, then confirm preferences survive.
- Uninstall and confirm shortcuts and startup are removed while `preferences.json`
  remains. Reinstall and confirm preferences restore.
- Test on a Windows x64 PC without a separately installed .NET runtime.
- Switch offline → online → offline. Verify startup, icons, media/WinRT features and
  preferences, and confirm the old local runtime files disappear only in the online switch.

Building and inspecting the package is not an installation test. Do not report the
above installation behavior as verified until it has been exercised on the target PC.

## Non-installing checks

After building both variants, run `.\installer\Test-Packaging.ps1`. Optionally pass
`-RuntimeInstallerPath` with the downloaded official pinned Microsoft installer to
also exercise checksum/publisher validation. This script never executes either Setup
or the runtime installer. It validates payloads, exact migration cleanup and duplicate
app launch using host tracing. Packaging checks do not execute installation flows; test those separately on the target PC.

## Official references

- [Microsoft publishing modes and framework-dependent deployment](https://learn.microsoft.com/en-us/dotnet/core/deploying/)
- [Microsoft Windows runtime selection, permissions, commands and exit codes](https://learn.microsoft.com/en-us/dotnet/core/install/windows)
- [Microsoft install-location design and architecture registry view](https://github.com/dotnet/designs/blob/main/accepted/2020/install-locations.md)
- [Microsoft Authenticode verification](https://learn.microsoft.com/en-us/powershell/module/microsoft.powershell.security/get-authenticodesignature?view=powershell-5.1)
