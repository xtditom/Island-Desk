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

- Windows 10 version 2004 (build 19041) or later.
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) to build from source.
- .NET 10 Desktop Runtime for framework-dependent builds; the SDK supplies it on a
  development machine. Offline installers bundle their runtime.
- [Inno Setup 7](https://jrsoftware.org/isdl.php) to build Windows installers.

The installer scripts target Windows x64. No administrator privileges are needed for
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
# Both variants:
.\installer\Build-Installer.ps1
# Or one variant:
.\installer\Build-Installer.ps1 -Variant Online
.\installer\Build-Installer.ps1 -Variant Offline
```

| Variant | Output | Runtime |
| --- | --- | --- |
| Online | `dist/IslandDesk-Setup-Online.exe` | Uses compatible .NET 10 Desktop Runtime x64, or offers a verified Microsoft download |
| Offline | `dist/IslandDesk-Setup.exe` | Self-contained Windows x64 payload |

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
