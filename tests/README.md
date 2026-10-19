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
| `--secondary-activation` | First/reused/minimized Settings, Lock/owned confirmations, rename, native ownership/styles and closing without focus theft against a separate controlled foreground process; never locks Windows |
| `--animation-check` | Transition state and animation cleanup |
| `--animation-profile` | Diagnostic transition timing and layout records |
| `--step13` | Interaction, battery and tray settings |
| `--step18` | Compact horizontal navigation, focus preservation, hover grace, title stability, animation cleanup, safe utility actions, themes and unchanged session-only file references |
| `--step182` | Root drag-to-expand, safe Clear confirmation/expiry, three shared-data layouts, paging order, nested actions and shortcut input guards using owned files/fake input |
| `--step182-live` | Interactive real OLE source and non-activating target using two owned files; logs file actions instead of launching them, writes `step182-live.json`; Stop validation closes the host |
| `--step18-profile` | Settled idle GPU/CPU/layout samples for Quick Controls, File Shelf, navigation strip, all three shelf layouts and hidden widget; writes `step182-profile.json` |
| `--performance-regression` | Service lifetime and cached-data regression checks |
| `--perf` | Local diagnostic resource samples |
| `--home-gpu-profile` | Two settled Release GPU/CPU samples per playback/visibility state; optional `--max`, `--home-only`, `--short-track` (180s rather than 600s) |
| `--home-gpu-check` | Subpixel progress scheduling, frame-rate preferences, scale, completion and hidden/unloaded cleanup |
| `--home-raster-probe` | Rounded-bar raster changes over one physical pixel at 96 DPI; software raster diagnostic, not a hardware/FPS guarantee |

The default suite checks layout, state, window styles, focus policy and input routing.
Some suites open test windows, intentionally activate controlled windows, create temporary
media, or query native devices. Use a regular interactive desktop account, not an unattended
production session. Device availability and media-session selection affect integration checks.

The secondary-activation suite sends guarded mouse input only inside its owned foreground
fixture and uses foreground grants between the controlled test processes to model an explicit
user action. Exit Show desktop/Task View and use the desktop where test windows are visible;
a shell-cloaked or occluded fixture stops the suite instead of claiming visual success.
Production uses one WPF Activate request per explicit open, without input injection,
input-queue attachment, retries or foreground-policy bypasses.

Generated files are written below the validation output's `artifacts` folder and ignored by
Git. They can contain device information or local media metadata. Timing results are local
diagnostics, not a delivered-FPS guarantee. Physical pointer input, DPI/display changes,
MyDockFinder coexistence, and installer execution still need manual testing.

Packaging has a separate non-installing check: after building both installer variants, run
`.\installer\Test-Packaging.ps1`. See `installer/README.md` for details.
