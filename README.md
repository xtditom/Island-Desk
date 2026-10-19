# IslandDesk

A compact Dynamic Island-style Windows desktop widget built with C#, native WPF,
and .NET 10. IslandDesk is under active development.

## Features

- Compact clock and media-aware pill with hover or click expansion.
- Home dashboard with Windows media playback controls, seeking, and master volume.
- System dashboard with CPU, GPU, RAM, disk, network, and optional battery readings.
- Light, Dark, and System themes; artwork accent colors and appearance preferences.
- Configurable positioning, independent expansion/collapse modes, and fullscreen hiding.
- Non-activating main widget, Alt+Tab exclusion, tray controls, and optional Windows startup.
- Per-user settings and online/offline Windows installers.

Media support depends on applications exposing Windows media sessions. Some devices
do not expose reliable GPU or battery data. Other topmost windows can overlap the
island; working-area positioning and manual offsets help accommodate desktop bars.

## Screenshots

Public screenshots will be added here. Local validation captures are excluded because
they can contain account names, device information, or media metadata.

## Requirements

- Windows build 19041 or later, on an edition/build supported by .NET 10.
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) to build from source.
- .NET 10 Desktop Runtime for framework-dependent builds; the SDK supplies it on a
  development machine. Offline installers bundle their runtime.
- [Inno Setup 7](https://jrsoftware.org/isdl.php) to build Windows installers.

The v0.3.0 installer scripts support x86, x64 and native ARM64 (via Universal).
No administrator privileges are needed for
the widget or its default per-user installation. Installing a missing shared .NET
Desktop Runtime through the online installer can require administrator approval.

## Build and run

From the repository root:

```powershell
dotnet restore .\IslandDesk.csproj
dotnet build .\IslandDesk.csproj -c Release
dotnet run --project .\IslandDesk.csproj -c Release --no-build
```

Alternatively, launch the built `IslandDesk.exe` from
`bin\Release\net10.0-windows10.0.19041.0`. Exit an existing instance through the tray
before running another build: development and installed copies share an instance guard.

Open Settings from the island or tray. Preferences are saved in
`%LocalAppData%\IslandDesk\preferences.json`, outside the repository. Optional legacy
`IslandDesk.settings.json` files are local configuration and are not required to build
or run: automatic working-area positioning is the default.

## Validation

Source checks live in `tests/IslandDesk.Validation`. They exercise state, settings,
native window behavior, rendering, and Windows integrations. Run on a Windows desktop:

```powershell
dotnet run --project .\tests\IslandDesk.Validation\IslandDesk.Validation.csproj -c Release
```

See the [validation guide](tests/README.md) for optional suites and their limitations.
Generated screenshots, diagnostic records, and machine-specific benchmarks are excluded
from version control. These checks do not establish delivered animation FPS or replace
manual DPI, pointer, fullscreen, and installation tests.

## Build Windows installers

From the repository root, with .NET 10 SDK and Inno Setup 7 installed:

```powershell
# Both x64 variants (backward-compatible default):
.\installer\Build-Installer.ps1
# All six v0.3.0 installers:
.\installer\Build-Installer.ps1 -Architecture All -Variant Both
# Or select architecture and distribution:
.\installer\Build-Installer.ps1 -Architecture x86 -Variant Online
.\installer\Build-Installer.ps1 -Architecture Universal -Variant Offline
```

| Architecture | Online output | Offline output |
| --- | --- | --- |
| x64 | `dist/IslandDesk-v0.3.0-win-x64-Online.exe` | `dist/IslandDesk-v0.3.0-win-x64-Offline.exe` |
| x86 | `dist/IslandDesk-v0.3.0-win-x86-Online.exe` | `dist/IslandDesk-v0.3.0-win-x86-Offline.exe` |
| Universal | `dist/IslandDesk-v0.3.0-Universal-Online.exe` | `dist/IslandDesk-v0.3.0-Universal-Offline.exe` |

Universal selects native ARM64, x64 or x86 payloads. Offline bundles the matching
runtime; Online uses a compatible installed .NET 10 Desktop Runtime or offers a
verified Microsoft download for that architecture. Checksums, a build manifest and
a packaging report are generated in `dist`. Real-device installation and runtime
compatibility must be checked before publishing the pre-release.

For a compiler in a custom location, pass `-CompilerPath` with the path to `ISCC.exe`.
The scripts restore and publish the application, create staging files under `artifacts`,
and compile the installer. Trimming and Native AOT are disabled. Generated binaries,
runtime files, compiler tools, and installers are excluded from Git.

Installations default to `%LocalAppData%\Programs\IslandDesk`. Uninstalling preserves
user preferences. See the [installer guide](installer/README.md) for prerequisites,
upgrade identity, startup handling, and the manual installation checklist.

## Source layout

- `Controls`, `Views`, `Themes`: WPF interface and resources.
- `Presentation`, `Interaction`, `Windowing`: view state, transitions, input, and native window behavior.
- `Monitoring`, `Media`, `Audio`: Windows integrations.
- `Settings`, `Tray`, `Infrastructure`: preferences, tray commands, and application lifetime.
- `Assets`: application artwork and multi-resolution Windows icon.
- `Properties/PublishProfiles`, `installer`: deployment configuration and build scripts.
- `tests/IslandDesk.Validation`: source validation executable.
