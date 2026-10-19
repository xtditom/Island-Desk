# IslandDesk Agent Guidance

IslandDesk is a compact Dynamic Island-style Windows desktop widget built with C#, WPF, and .NET 10 (`net10.0-windows10.0.19041.0`). It integrates with Windows media sessions, audio, system metrics, the notification area, and native window behavior.

## Source map

- `App.xaml`, `App.xaml.cs`, `MainWindow.xaml`, `MainWindow.xaml.cs`: startup, tray lifetime, the non-activating widget window, dashboard selection, and presentation lifecycle.
- `Views/`, `Controls/`, `Themes/`: dashboard and settings UI, reusable WPF controls, and theme resources.
- `Presentation/`, `Interaction/`, `Windowing/`: view state, layout and transitions, input policy, and native window/monitor behavior.
- `Monitoring/`, `Media/`, `Audio/`: Windows hardware sampling and media/audio integrations.
- `Settings/`, `Tray/`, `Infrastructure/`: persisted preferences, tray commands, and shared application services.
- `tests/IslandDesk.Validation/`: Windows validation executable. `installer/` and `Properties/PublishProfiles/`: packaging and publish configuration.

## Build and validation

Run from the repository root on Windows with the .NET 10 SDK:

```powershell
dotnet restore .\IslandDesk.csproj
dotnet build .\IslandDesk.csproj -c Release
dotnet run --project .\tests\IslandDesk.Validation\IslandDesk.Validation.csproj -c Release
```

The validation executable has focused suites documented in `tests/README.md`. Exit any running IslandDesk instance through its tray before building or validating; development and installed copies share an instance guard. To build installers, use `./installer/Build-Installer.ps1` with Inno Setup 7 installed.

## Change guidance

- Keep CPU, GPU, and RAM use low, especially while idle. Prefer the existing cancellable, demand-driven service patterns; avoid unnecessary polling, allocations, or continuous rendering, and keep sampling work off the UI dispatcher.
- Preserve the existing UI, interactions, animations, Windows integrations, and settings behavior unless the task explicitly changes them. Follow current view-model, service, settings, and theme conventions.
- Make the smallest maintainable change that solves the requested issue. Avoid unrelated refactors, generated build/diagnostic output, and edits outside the task scope.
- For source changes, build the Release app and run the relevant existing validation when behavior is affected. Review `git diff` and `git status` before handoff; report any build or validation that could not be completed.

## Verification policy

Prioritize implementation speed and focused changes. Perform essential command-line checks only, normally one Release build after implementation. Fix build errors as needed. Do not conduct visual testing, UI automation, screenshots, extensive validation suites, benchmarks, or create temporary test projects unless explicitly requested. The user handles visual verification and manual functionality testing. Never claim runtime behavior is verified solely from a successful build.
