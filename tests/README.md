# Windows validation

`IslandDesk.Validation` is a small source validation executable with a project reference
to the main application and no third-party test framework. Run from the repository root
on a Windows desktop, after exiting any running IslandDesk copy through its tray.

```powershell
dotnet run --project .\tests\IslandDesk.Validation\IslandDesk.Validation.csproj -c Release
```

Append `--` followed by one suite switch:

| Switch | Coverage |
| --- | --- |
| `--settings` | Preferences, persistence and settings controls |
| `--themes` | Theme resources, contrast and switching |
| `--advanced-media` | Media appearance and personalization |
| `--media` | Windows media integration with an owned temporary test player |
| `--fullscreen` | Fullscreen detection with a controlled native test window |
| `--animation-check` | Transition state and animation cleanup |
| `--animation-profile` | Diagnostic transition timing and layout records |
| `--step13` | Interaction, battery and tray settings |
| `--performance-regression` | Service lifetime and cached-data regression checks |
| `--perf` | Local diagnostic resource samples |

The default suite checks layout, state, window styles, focus policy and input routing.
Some suites open test windows, intentionally activate controlled windows, create temporary
media, or query native devices. Use a regular interactive desktop account, not an unattended
production session. Device availability and media-session selection affect integration checks.

Generated files are written below the validation output's `artifacts` folder and ignored by
Git. They can contain device information or local media metadata. Timing results are local
diagnostics, not a delivered-FPS guarantee. Physical pointer input, DPI/display changes,
MyDockFinder coexistence, and installer execution still need manual testing.

Packaging has a separate non-installing check: after building both installer variants, run
`.\installer\Test-Packaging.ps1`. See `installer/README.md` for details.
