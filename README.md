# IslandDesk

**A customizable Dynamic Island-style companion for your Windows desktop.**

IslandDesk brings media controls, system information, Windows utilities, and a temporary File Shelf into a compact desktop island. Built with **C#, native WPF, and .NET 10**, it offers a lightweight interface that expands when you need more room.

**v0.3.0 Preview** is an actively developed preview release. Features and compatibility are still being refined.

**[Download v0.3.0 Preview](https://github.com/xtditom/Island-Desk/releases/tag/v0.3.0)** · [Getting started](#-getting-started) · [Build from source](#-build-from-source) · [Report an issue](https://github.com/xtditom/Island-Desk/issues)

## ✨ Features

### A compact island with four dashboards

- Switch between a compact clock/media pill and an expanded dashboard.
- Choose hover or click independently for expansion and collapse.
- Use the header's compact navigation overlay to access **Home**, **System**, **Quick Controls**, and **File Shelf**.
- Enjoy animated transitions, contextual clock/date headers, 12/24-hour time, and optional seconds.
- Keep the main widget out of Alt+Tab and interact with it without activating it; Settings and confirmation windows support normal keyboard focus.

### Media at a glance

Home connects to compatible Windows media sessions to show the current track, artist, and artwork. Play/pause, previous/next, timeline seeking, and master volume controls are available when the media session or audio device supports them.

Artwork-derived accent colors can tint the island, playback timeline, volume control, and animated equalizer. These effects and individual media elements are configurable.

### System monitoring

The System dashboard shows CPU and GPU usage, RAM, system-disk activity, network transfer rates, and optional battery information. Choose which cards to display.

Hardware sampling runs in the background while the relevant System dashboard is visible and expanded. Media and device services use Windows notifications where applicable; resource use varies with active features, hardware, and animation settings.

### ⚡ 18 Windows Quick Controls

A compact **3 × 3 grid** displays nine controls per page. Navigate with arrows, page indicators, or the mouse wheel. Drag cards to reorder them, including across pages, or use their context menus. Your ordering is saved, and the default layout can be restored in Settings.

| Control | What it does |
| --- | --- |
| Screenshot | Opens Windows screen snipping (Win + Shift + S). |
| Clipboard History | Opens the Windows clipboard history panel (Win + V). |
| Task Manager | Launches Windows Task Manager. |
| Windows Search | Opens Windows Search. |
| Lock Device | Locks Windows after confirmation. |
| Emoji Panel | Opens the Windows emoji panel. |
| Keep PC Awake | Prevents automatic system sleep while enabled; manual sleep still works. |
| Empty Recycle Bin | Empties the Recycle Bin after confirmation. |
| On-Screen Keyboard | Opens the Windows on-screen keyboard. |
| Microphone Mute | Toggles mute for the default recording device. |
| Show Desktop | Toggles the Windows desktop view. |
| Sign Out | Requests Windows sign-out after confirmation. |
| Turn Off Display | Requests display power-off; mouse or keyboard input wakes it again. |
| Wi-Fi | Requests a radio toggle when Windows permits it. |
| Bluetooth | Requests a radio toggle when Windows permits it. |
| Task View | Opens Windows Task View. |
| Run Dialog | Opens or brings forward the Windows Run dialog. |
| Screen Brightness | Adjusts the island's monitor brightness when supported. |

Hardware controls report unavailable or restricted states. Wi-Fi, Bluetooth, and brightness cards also provide access to the corresponding Windows Settings pages.

### 📁 File Shelf

- Drag supported local files onto the island to expand it and open File Shelf, or choose **Add Files**.
- Switch between **Grid**, **Line**, and **Two-Column** views, with pagination.
- Click an entry to open it in its associated application, reveal it in Explorer, copy its path, or drag it into another application using a copy operation.
- Remove individual references or clear the shelf with a two-step confirmation.

**Removing an entry or clearing File Shelf never deletes the original files.** The shelf keeps temporary references for the current session, up to 50 files. It does not move the originals or persist its contents between launches. Folders and network paths are not supported.

### 🎨 Personalization and Windows integration

- **Light, Dark, and System themes**, with configurable opacity, backgrounds, corner radii, title, and accent colors.
- Left, center, or right alignment, working-area positioning, and horizontal/vertical offsets.
- Smooth or instant transitions, animation speed, and frame-rate preferences.
- Configurable Home sections, System cards, optional dashboards, media details, timeline, and equalizer appearance.
- Optional **Always-on-Top**, fullscreen hiding, and launch at Windows sign-in.
- A system tray menu for Settings, theme, position, clock format, animation preferences, and Exit.

Preferences are saved in `%LocalAppData%\IslandDesk\preferences.json`.

## 📦 Download and installation

Choose an installer from the **[v0.3.0 Preview release](https://github.com/xtditom/Island-Desk/releases/tag/v0.3.0)**.

**For most users, Universal Offline is the simplest choice:** it selects the native application architecture and includes the required runtime. Choose Universal Online for a smaller initial download if you have internet access or a compatible runtime already installed.

| Architecture | Online | Offline |
| --- | --- | --- |
| Windows x64 | [IslandDesk-v0.3.0-win-x64-Online.exe](https://github.com/xtditom/Island-Desk/releases/download/v0.3.0/IslandDesk-v0.3.0-win-x64-Online.exe) | [IslandDesk-v0.3.0-win-x64-Offline.exe](https://github.com/xtditom/Island-Desk/releases/download/v0.3.0/IslandDesk-v0.3.0-win-x64-Offline.exe) |
| Windows x86 (32-bit) | [IslandDesk-v0.3.0-win-x86-Online.exe](https://github.com/xtditom/Island-Desk/releases/download/v0.3.0/IslandDesk-v0.3.0-win-x86-Online.exe) | [IslandDesk-v0.3.0-win-x86-Offline.exe](https://github.com/xtditom/Island-Desk/releases/download/v0.3.0/IslandDesk-v0.3.0-win-x86-Offline.exe) |
| Universal | [IslandDesk-v0.3.0-Universal-Online.exe](https://github.com/xtditom/Island-Desk/releases/download/v0.3.0/IslandDesk-v0.3.0-Universal-Online.exe) | [IslandDesk-v0.3.0-Universal-Offline.exe](https://github.com/xtditom/Island-Desk/releases/download/v0.3.0/IslandDesk-v0.3.0-Universal-Offline.exe) |

- **x64** installs the 64-bit application on x64-compatible Windows. **x86** installs the 32-bit application on compatible 32-bit or 64-bit Windows.
- **Universal** selects native **ARM64 on ARM64 Windows**, **x64 on x64 Windows**, or **x86 on 32-bit Windows**. Only the selected payload is installed.
- **Online** uses an existing compatible .NET 10 Desktop Runtime for the selected architecture. If it is missing, Setup asks permission to download and install the official Microsoft runtime, verifying checksums and its Microsoft Authenticode signature before execution. That step needs internet access and administrator approval.
- **Offline** bundles the matching runtime. Normal installation needs neither internet access nor a separate .NET installation.

IslandDesk installs per user to `%LocalAppData%\Programs\IslandDesk`. The application and its own installer do not require administrator privileges. Installers share the same product identity for upgrades and are designed to preserve preferences and the existing startup choice.

[SHA-256 checksums](https://github.com/xtditom/Island-Desk/releases/download/v0.3.0/SHA256SUMS.txt) and a [build manifest](https://github.com/xtditom/Island-Desk/releases/download/v0.3.0/build-manifest.json) accompany the release. The preview installers are unsigned. Packaging checks do not establish compatibility on every Windows device; see the [installer guide](installer/README.md) for details.

## 🖥️ System requirements

| Requirement | Details |
| --- | --- |
| Windows | Windows 10 build 19041 or later, on an edition/build supported by .NET 10; compatible Windows 11 systems meet the application's API floor. |
| Architecture | x86, x64, or ARM64 using the appropriate installer. |
| Runtime | .NET 10 **Desktop Runtime** matching the application architecture for Online/source framework-dependent builds. Offline packages include it. |
| Optional hardware | Compatible audio devices, battery, graphics drivers, radios, and brightness interfaces for their respective controls and readings. |

Use a Windows version supported by [Microsoft's .NET 10 requirements](https://learn.microsoft.com/en-us/dotnet/core/install/windows). The application API floor alone is not a guarantee of support for every Windows edition or device. Ordinary users do not need the .NET SDK or Inno Setup.

## 🚀 Getting started

1. **Download** your chosen installer from the release table above.
2. **Install** IslandDesk. If upgrading, exit the running copy through its system tray menu first. Select the optional desktop shortcut if wanted.
3. **Launch** IslandDesk from Setup, the Start menu, or your shortcut.
4. **Open Settings** using the island's Settings button or by right-clicking its tray icon.
5. **Explore the dashboards:** expand the island, then hover over or click the current dashboard icon/chevron to reveal navigation. Select Home, System, Quick Controls, or File Shelf.
6. **Make it yours:** adjust themes, position, interactions, animation preferences, visible sections, and startup behavior. Drag a local file onto the island to try File Shelf.

Use the tray's **Exit** command to close IslandDesk.

## 🛠️ Build from source

On Windows, install the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0), then run these PowerShell commands from the repository root:

```powershell
dotnet restore .\IslandDesk.csproj
dotnet build .\IslandDesk.csproj -c Release
dotnet run --project .\IslandDesk.csproj -c Release --no-build
```

The project targets `net10.0-windows10.0.19041.0`. Exit any running IslandDesk instance before rebuilding or launching another copy; development and installed copies share an instance guard. A local legacy `IslandDesk.settings.json` is optional and is not required to build or run.

To produce all six installers, also install [Inno Setup 7](https://jrsoftware.org/isdl.php):

```powershell
.\installer\Build-Installer.ps1 -Architecture All -Variant Both

# Or build a selected architecture/distribution:
.\installer\Build-Installer.ps1 -Architecture x64 -Variant Online
```

Outputs go to `dist`, with intermediate files under `artifacts`. Publishing uses Release configuration with trimming, Native AOT, ReadyToRun, and single-file publishing disabled.

See the [installer guide](installer/README.md) for prerequisites, architecture selection, upgrades, and packaging commands. The [validation guide](tests/README.md) describes the existing checks and their limitations.

### Source map

| Location | Purpose |
| --- | --- |
| `Controls/`, `Views/`, `Themes/` | Interface, dashboards, settings windows, and themes |
| `Presentation/`, `Interaction/`, `Windowing/` | View state, interactions, native window behavior, and Windows utilities |
| `Media/`, `Audio/`, `Monitoring/` | Media sessions, audio devices, and system readings |
| `Settings/`, `Tray/`, `Infrastructure/` | Preferences, startup, tray menu, and application services |
| `Assets/` | Application branding and icons |
| `Properties/PublishProfiles/`, `installer/` | Publish profiles and Inno Setup packaging |
| `tests/IslandDesk.Validation/` | Validation source |

## Known limitations

- Media controls and seeking depend on the capabilities exposed by each application's Windows media session.
- GPU readings, battery information, and some system metrics may be unavailable on particular hardware or drivers.
- Brightness requires a compatible integrated-display interface or an external monitor exposing DDC/CI brightness control.
- Wi-Fi and Bluetooth changes can be blocked by Windows permissions, policy, drivers, or hardware switches; Windows Settings remains available.
- Microphone Mute targets the default recording device. Clipboard History must be available and enabled in Windows.
- Windows shortcut actions require a foreground destination application and may be restricted by system policy or elevated windows.
- Other topmost windows can overlap the island. Positioning offsets and working-area alignment can help accommodate desktop bars.

## 💬 Feedback and contributions

Found a bug or have an idea? [Open a GitHub issue](https://github.com/xtditom/Island-Desk/issues). For bugs, include your Windows version, IslandDesk version, installer architecture, steps to reproduce, and expected versus actual behavior. Avoid posting private file paths, media metadata, or other personal information.

Contributions that improve functionality, accessibility, documentation, or compatibility are welcome. Review [AGENTS.md](AGENTS.md) for the repository's development guidance and keep changes focused.

## License and credits

No repository license file is currently included; this README does not assign an open-source license.

IslandDesk is built with C#, WPF, and .NET 10. Its Windows installers use Inno Setup 7.
