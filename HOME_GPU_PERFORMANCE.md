# Home GPU investigation — Step 17

Observed Release results (mean of both runs; GPU/CPU percentages):

| Condition | GPU before → after | CPU before → after |
| --- | ---: | ---: |
| Home playing, Max/100 Hz, 600s track | 2.39 → 1.15 | 1.20 → 0.70 |
| Home playing, Max/100 Hz, 180s track | 2.23 → 1.65 | 1.20 → 0.67 |
| Home playing, 60 FPS preference | 1.86 → 1.49 | 0.86 → 0.28 |
| Home playing, equalizer disabled, 60 FPS | 1.71 → 1.41 | 0.85 → 0.20 |
| Home paused, 60 FPS | 0.000 → 0.001 | 0.018 → 0.012 |
| System, 60 FPS | 0.003 → 0.003 | 0.000 → 0.024 |
| Hidden playing, 60 FPS | 0.000 → 0.000 | 0.024 → 0.012 |
| Collapsed playing, 60 FPS | 1.21 → 1.28 | 0.51 → 0.46 |
| Collapsed playing, equalizer disabled, 60 FPS | 0.000 → 0.000 | 0.006 → 0.006 |

The measured Home GPU reduction was **52% at Max** and **20% at 60 FPS** for the
600-second track. CPU fell 42% and 67%, respectively. The separate 180-second
comparison at Max reduced GPU **26%** and CPU **44%**. Collapsed playback was not
optimized; its GPU variation illustrates background load/power-state sensitivity.
Warm collapsed-paused runs were 0% GPU both before and after; first-run startup
activity was excluded from that idle statement. These are local observations, not
guarantees that the original Task Manager figure of 4.3% becomes a particular value.

The ongoing playback timeline was the Home-specific rendering driver. Its long-lived
`RectAnimation` updated rounded geometry at the selected frame rate, even when the
edge moved only a small fraction of a pixel. Paused Home was already quiet; this
was redundant rendering during playback rather than a permanent idle animation leak.

The equalizer runs in the collapsed music pill and stops on Home/System, pause,
disable, hide and unload. Artwork is static between media events. Accent/background
color fades finish and remove their clocks. There are no application
`CompositionTarget.Rendering` subscriptions. Timeline geometry does not invalidate
layout every frame; timestamps account for approximately one layout pass per second.
The shadow uses retained geometry bands rather than a blur effect. Hiding/caching
the shadow, caching static Home sections, and translating a fixed timeline shape
did not show a repeatable improvement, so those experiments were not shipped.
Window transparency and all original drawing geometry remain intact.

`PlaybackBar` now schedules slow progress from monotonic time in increments of
1/32 physical pixel. DPI and ancestor scale determine the interval. Faster motion
continues to use the existing WPF animation with the selected frame-rate preference.
This is a distance-based update policy, not a fixed 30 FPS animation limit.
Pause, seek replacement, hide, unload and completion cancel scheduled work. Placement
changes refresh the schedule even if the selected frame rate stays the same.
The rounded geometry, anti-aliasing, color brushes, seeking, timestamps, settings,
equalizer and expansion/collapse animations are preserved.

Measured locally on AMD Radeon(TM) Graphics, driver 31.0.21923.11000, 1920×1080 at
100 Hz, 100% scaling, WPF tier 2/default rendering, .NET SDK 10.0.402, Release.
The original application DLL was saved before production edits; its SHA-256 is
`C19E3FA86E2B1A07371586A6FFAA170ECA136629D911E1493047ABE2F0AED08E`.

Each state has two 10-second samples after two seconds settling, ten valid GPU
counter samples per run. A synthetic seekable track starts at 20%; default duration
is 600 seconds. The real window/control/model code runs with native polling/media
services stopped to isolate rendering. Native integration is checked separately.
GPU is the mean busiest valid engine for this validation process, sampled through
PDH in another process; CPU is normalized over 12 logical processors. These are
not whole-system/DWM GPU readings or delivered-FPS measurements. The validation
host and runtime add memory overhead, so its working set is not comparable to the
installed application's Task Manager RAM. No memory reduction is claimed.

The optional raster probe found 16–32 distinct images over a one-pixel rounded-edge
sweep at tested thicknesses (2/3/6 DIPs, 96 DPI). This is a software-raster diagnostic,
not proof of identical sampling on every hardware renderer. The update policy
retains subpixel geometry rather than rounding to whole pixels. Benefits depend
on timeline speed, display density, chosen frame rate and GPU power state.

Release validation passed 316 checks: progress scheduling 21, existing performance
regressions 12, advanced media/appearance 75, animations 51, settings 62, and
dashboard/input/native window behavior 95. Expansion/collapse, reversals, clipping,
60/90/Max preferences, pause/seek, equalizer styles, accents, hide/minimize and
settings persistence passed. The rendered Home and transition snapshots were
inspected. The native media suite passed its first 18 checks (including real
seek/next/previous/pause/play), then timed out waiting for its final metadata-change
event. The same timeout reproduced on the unchanged baseline; this existing issue
remains unresolved and no media-service code was changed. The application Release
build completed with zero warnings/errors using an explicit `IslandDesk.csproj`
target because an existing ignored WPF temporary project makes bare `dotnet build`
ambiguous.

Local raw records are retained under `C:\1_Workspace`: `step17-baseline/artifacts`
(full before/60), `step17-after-60/artifacts` (full after/60),
`step17-before-final/artifacts` (before/Max), and `step17-after/artifacts`
(after/Max). Each contains `home-gpu-profile.json` and individual PDH samples.
The 180-second comparisons are in `step17-before-short/artifacts` and
`step17-after-short/artifacts`.
Cache/transform experiments are in `step17-experiment` and
`step17-transform-experiment`; they were excluded from the production comparison.

Reproduce from the repository root after exiting any installed copy:

```powershell
dotnet build IslandDesk.csproj -c Release
dotnet run --project tests/IslandDesk.Validation -c Release -- --home-gpu-profile
dotnet run --project tests/IslandDesk.Validation -c Release -- --home-gpu-profile --home-only --max
dotnet run --project tests/IslandDesk.Validation -c Release -- --home-gpu-profile --home-only --max --short-track
dotnet run --project tests/IslandDesk.Validation -c Release -- --home-gpu-check
```

Save the baseline Release binaries and run the same validation assembly against
each application's DLL in separate output folders. Keep display, track duration,
theme, frame preference and background workload comparable. Preserve each output's
`artifacts/home-gpu-profile.json` before rerunning. Full profiles include collapsed,
playing, paused, equalizer-disabled, System, hidden and shadow-isolation states.
