# Step 18.2 implementation and verification

Implemented the navigation overlay, six compact Quick Controls and File Shelf UX refinements without changing version 0.2.0, creating a release, committing or pushing.

## Behavior and implementation

- Restored the original 98-DIP header side measurement. Navigation lives in a zero-measuring Canvas above the existing header, with clipped transform animations. Opening it preserves the title's width and center; the overlay shields covered controls. Hover/click selection, the stable primary icon, approximately 150 ms dismissal grace, Settings access and Home reset on ordinary collapse/reopen are retained.
- Quick Controls now has six 82-DIP cards in the requested 3×2 order: Screenshot, Clipboard History, Task Manager / Windows Search, Lock Device, Emoji Panel. All execute on click only. Night Light, its experimental setting/provider and its bundled third-party license were removed. There is no File Explorer Quick Control.
- Screenshot, Clipboard History, Search and Emoji send balanced native shortcut input. Search sends Win+S, never an Explorer command. Input is refused when modifier/shortcut keys are physically held or the widget is the foreground window. Partial input cleans up only synthetic keys accepted by Windows. Clipboard availability reads the Windows history-enabled flag without reading history contents or changing settings. Task Manager resolves the system executable directly without an elevation verb. Lock retains its confirmation dialog.
- Root WPF OLE handlers now cover the actual rounded on-screen pill. A valid local file drag selects Shelf and holds interaction before dropping, highlights the target, and expands without requiring a click. A pending 150 ms leave grace handles descendant changes and cancellation. Invalid formats, directories, remote paths, move-only drags and a disabled Shelf do not expand it. Metadata is collected asynchronously; originals remain in place.
- Live testing identified a second drag failure: resizing the layered HWND synchronously inside DragEnter briefly displaced the old alpha bitmap, so a rapid release could miss the native target. Selection and interaction are committed immediately; one queued dispatcher callback grows the host after the OLE callback returns. The repeated real drag returned Copy, staged both files and preserved the source foreground. Native NOACTIVATE/TOOLWINDOW styles remain intact; WS_EX_TRANSPARENT is not introduced.
- File cards open once on mouse release, preserving the existing threshold-based outbound copy drag. Nested Reveal/Copy/Remove controls do not open a file. Missing files report status. The separate Open button was removed.
- Clear first arms a red Confirm Clear button and a five-second timer. The second click removes references only. Dashboard/visibility changes, another click, pointer departure or expiry cancel it and detach its temporary handler. The timer is stopped when unarmed.
- Session-only view selection cycles Grid → Line → Two-Column → Grid. All views share the same records and page collection, preserving order and bounded paging. Grid shows six entries; Line and Two-Column show four. Only Line shows Reveal/Copy; all layouts offer Remove, ellipsis and full-path tooltips. Icons use shared vector geometry rather than thumbnails or shell extraction.
- No continuous rendering handler, global hook, additional background poll, third-party dependency or frame-rate cap was added. Navigation clocks finish and release; hidden views cancel transient work. Existing expansion/collapse and selected animation frame rates remain in use.

## Validation

Release builds completed after the implementation stages. The final `dotnet build .\IslandDesk.csproj -c Release` succeeded with zero warnings and zero errors. Its first restore attempt was blocked by sandbox network access; rerunning with NuGet access succeeded.

The final regression set passed **661 checks**:

| Suite | Checks |
| --- | ---: |
| Default layout/input/native styles | 96 |
| Settings | 62 |
| Themes | 86 |
| Animation | 51 |
| Fullscreen/native foreground | 27 |
| Step 18.2 drag, Clear, layouts, paging, input guards | 68 |
| Navigation and existing shelf/utility behavior | 55 |
| Home GPU scheduling | 21 |
| Existing performance lifetimes | 12 |
| Advanced media/appearance | 75 |
| Interaction, battery and tray preferences | 89 |
| Native Windows media | 19 |

The initial fullscreen run was obstructed by the elevated Task Manager window opened during live validation. After the user closed it, the complete 27-check suite passed, including focus-preserving automatic restoration and service shutdown. Native media passed all 19 checks on this run, including the final metadata event that had intermittently timed out in earlier steps.

Dark, Light and System snapshots were rendered and inspected for navigation, Quick Controls and all three Shelf layouts. Real OLE testing used two owned temporary files. Before drop, DragEnter reported expanded=True, shelf=True and count=0; Drop returned Copy, count became 2 and foreground remained the source HWND. A real card click logged one Open; its nested Copy logged Copy without another Open. Tests verified original files and modification times survive Clear, paging, layout changes and controlled actions.

Windows Search opened the native shell search interface; Task Manager launched. Emoji opened its native panel. Clipboard History produced a native panel at the edge of the captured region, but the helper could not fully inspect it. Shortcut routes and held-key/partial-input handling were verified with deterministic input fixtures. Lock confirmation was exercised and cancelled; Windows was not actually locked. The snipping overlay and disabled-history prompt remain manual checks.

Local raw records: `C:\1_Workspace\step182-validation\artifacts` contains `step182-live-drag.json`, `step182-profile.json`, per-state GPU records, regression logs and snapshots. The live host is an owned fixture; its file actions are logged instead of launching user applications.

## Performance measurements

Release validation host, 12 logical processors, WPF tier 2, 100% DPI, Dark theme; native sampling/media services disabled to isolate presentation. Each state settled for 2.5 seconds before eight external PDH GPU samples over approximately 8.5 seconds. CPU is normalized to all processors. GPU is the mean busiest process engine, excluding DWM. This is not an installed-app Task Manager RAM or whole-desktop GPU measurement.

The saved Step 18.1 binary was measured again under the current desktop conditions, after the Step 18.2 run:

| State | Step 18.1 CPU % | Step 18.2 CPU % | GPU %, before → after |
| --- | ---: | ---: | ---: |
| Quick, first visit | 0.594 | 0.320 | 0 → 0 |
| Shelf, Line | 0.398 | 0.184 | 0 → 0 |
| Shelf, Grid | unavailable | 0.244 | unavailable → 0 |
| Shelf, Two-Column | unavailable | 0.505 | unavailable → 0 |
| Navigation open and settled | 0.260 | 0.153 | 0 → 0 |
| Quick, warm | 0.198 | 0.137 | 0 → 0 |
| Hidden | 0.701 | 0.198 | 0 → 0 |

Every state recorded zero layout passes during sampling. Step 18.2 navigation, drag-leave and Clear timers were stopped and the navigation animation was inactive in every settled sample. The original Step 18.1 sample had lower CPU (warm Quick 0.031%, hidden 0%), demonstrating substantial run-to-run variation. These short, sequential samples support quiet GPU/layout behavior, **not a statistically established CPU improvement**. Current validation-host working set was 163–175 MiB and private memory 79–84 MiB; these include the test host/runtime and are not comparable to the product's Task Manager figures. No RAM reduction is claimed.

The earlier Home optimization remains intact: the playback timeline was the rendering driver, and measured Home playback GPU fell 2.39% → 1.15% at Max/100 Hz and 1.86% → 1.49% at the 60 FPS preference for the 600-second fixture. Those are Step 17 measurements, not new Step 18.2 measurements; methodology, before/after variants and limits are in `HOME_GPU_PERFORMANCE.md`.

## Windows limitations and manual checks

Successful SendInput means Windows accepted input, not that a shell panel is available. UIPI, elevated foreground applications, enterprise policy and shell shortcut remapping can block it. Status messages provide the corresponding physical shortcut. Task Manager is launched without requesting elevation, but Windows may run its existing/auto-elevated instance at higher integrity. No registry modification or global keyboard hook works around these restrictions. See Microsoft's [SendInput documentation](https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-sendinput) and [Clipboard history availability API](https://learn.microsoft.com/en-us/uwp/api/windows.applicationmodel.datatransfer.clipboard.ishistoryenabled).

For a final physical-input check after exiting other IslandDesk copies:

1. Launch the Release app. Hover the primary icon and sweep all four dashboards; verify title position, Settings restoration, grace period, and Home reset on normal collapse/reopen.
2. Click the six Quick Controls with an ordinary app focused. Verify native Search, snipping overlay (Escape to cancel), Clipboard History, Task Manager and Emoji; cancel Lock. Check disabled Clipboard History on a machine where it is already disabled, without changing policy.
3. Drag multiple ordinary local files onto the collapsed pill. Verify expansion before release, highlighted Shelf, Copy completion, then Escape cancellation and leaving/re-entering the target. Repeat with hover-collapse enabled and at the display DPI/scales used day to day.
4. Single-click a staged file; verify its registered app opens. Check outbound dragging, missing files, long names, nested actions, all three views and paging. Click Clear once, then elsewhere or wait five seconds; finally confirm Clear and verify originals still exist.
5. Check wheel-volume behavior, fullscreen restore, tray/Settings, themes and frame-rate preferences with normal media playback. Multi-monitor/DPI hardware combinations and installer execution were not newly exercised in this step.
