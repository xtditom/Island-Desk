# Step 18 — navigation, Quick Controls and File Shelf

Implemented the three-item Home hover flyout while retaining the original Home,
System and Settings header icons, title and contextual date/clock. System still
navigates immediately on hover. Flyout items navigate on hover or click; a 180 ms
leave timer bridges the gap between Home and the menu. The popup overlays content,
does not enlarge the island, follows Always-on-Top, uses NOACTIVATE/TOOLWINDOW,
and forwards native/managed wheel input to the existing volume path. The popup,
views and file model are retained; the popup HWND hook detaches on close. Collapse,
hide/minimize, Settings, and placement changes close navigation appropriately.

Quick Controls provides only Night Light Settings, confirmed Lock Device, and
Emoji Panel. Utility buttons have no hover actions. The lock confirmation is a
separate dialog whose cancellation restores the previous foreground window.
File picker/drag/confirmation interactions hold the existing collapse controller.

File Shelf supports Explorer-format multi-file drops, the Windows file picker,
open, reveal, copy path, remove, clear and copy-only outbound drag. It retains
up to 50 local path references, showing four rows per page so the wheel remains
volume control. Names/extensions, snapshot sizes and lightweight document icons
are shown. Metadata is read on a worker only when adding; missing/duplicate/
directory/relative/network paths are handled. Open/reveal/drag recheck availability;
copying a stored path still works when its original is missing. No original files
are deleted, no content is read/copied into storage, and no paths are persisted or
uploaded. No watcher, thumbnail service, clipboard monitor or polling was added.

Settings → Widgets now has **Quick Controls tab** and **File Shelf tab**, both
enabled by default, including older JSON. Changes persist through the existing
controller/store. Disabling the active optional view immediately selects Home;
Home/System remain accessible. Shelf references survive tab changes within the
current process and disappear when its window/session ends.

**Changed files for this step**

- `Controls/IslandHeader.xaml` and `.xaml.cs`: flyout, grace, navigation, popup styles/wheel hook.
- `MainWindow.xaml` and `.xaml.cs`: retained views, selection, lifecycle, service/clock/artwork demand and interaction holds.
- `Presentation/IslandViewModel.cs`, `DashboardLayout.cs`: dashboard states and sizing.
- `Presentation/FileShelfViewModel.cs`: bounded session-only references, metadata, cancellation and pagination.
- `Views/QuickControlsDashboard.xaml` and `.xaml.cs`: three click-only utility cards and confirmation.
- `Views/FileShelfDashboard.xaml` and `.xaml.cs`: picker, OLE drops/drag source, entries and file actions.
- `Windowing/WindowsUtilityActions.cs`, `ShelfFileActions.cs`: documented Windows integrations.
- `Settings/AppSettings.cs`, `SettingsController.cs`, `Views/SettingsWindow.xaml`: existing Widgets preferences.
- `tests/IslandDesk.Validation/NavigationShelfValidation.cs`, `Program.cs`, `tests/README.md`: new validation/profile suites and updated Home-hover expectation.

The Step 17 playback changes already in the working tree were retained. Application
version remains 0.2.0. No release, installer, commit or push was created.

**Windows API limits**

- Night Light uses the documented [`ms-settings:nightlight` URI](https://learn.microsoft.com/en-us/windows/apps/develop/launch/launch-settings), with launch failures/status reported. It does not provide a fake state/toggle or edit CloudStore/registry values.
- [`LockWorkStation`](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-lockworkstation) reports that a lock was requested, not proof the workstation locked; it requires an interactive desktop. A real lock was deliberately left to manual verification.
- Emoji Panel uses [`SendInput`](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-sendinput) for the native Win + . shortcut. No foreground activation or global hooks are installed. Input injection can be blocked by UIPI/elevated destinations, and successful injection cannot prove that Windows displayed the panel. Status text states these limits. Existing pressed modifiers are respected; partial injection attempts release only the synthetic keys.
- Reveal uses [`SHOpenFolderAndSelectItems`](https://learn.microsoft.com/en-us/windows/win32/api/shlobj_core/nf-shlobj_core-shopenfolderandselectitems) and releases its parsed PIDL. Outbound file drag permits Copy only; virtual attachments without real file paths are not supported.

**Build and verification**

Release compiled after each implementation stage. Final application/validation
builds have zero warnings and errors. **586 checks passed**: new navigation/shelf
48, dashboard/input 96, settings 62, themes 86, media/appearance 75, animations 51,
fullscreen 27, tray/battery 89, prior subpixel progress 21, performance regressions
12, and native Windows media 19. The default lock dialog was opened and canceled;
confirmed lock/emoji/Night Light outcomes were tested through substitutes rather
than locking this desktop or injecting a global shortcut. Native media passed in
the normal desktop context, including its metadata event; a restricted-token media
attempt timed out and was excluded. No media-service code was changed.

Dark/Light/System dashboard and popup renders were inspected. New native popup
styles, foreground preservation, dialog focus restoration, hover grace, repeated
view reuse, optional-tab persistence, missing/moved/duplicate files, 50-file limit,
clear-during-add cancellation, synchronous OLE effects and original-file survival
were checked. Existing wheel, hover/click, full-screen, frame preference, clipping,
reversal and hidden-work regressions passed.

**Local performance**

Release validation host, 12 logical processors, WPF tier 2, 100% DPI. Native
services were stopped to isolate rendering. Shelf had 50 references/four visible
rows. Each state settled 2.5 seconds and sampled 8 seconds; GPU was collected via
an external per-process PDH sampler (busiest engine, excludes DWM). CPU is percent
of the whole machine. These short samples are diagnostics, not whole-system GPU,
installed-app memory, power-use or delivered-FPS guarantees.

| State | CPU | GPU | Layout passes | Dismiss timer |
| --- | ---: | ---: | ---: | --- |
| Quick Controls, initial sample | 0.030% | 0.145% | 0 | Stopped |
| Quick Controls, warm repeat | 0.015% | 0.000% | 0 | Stopped |
| File Shelf | 0.000% | 0.000% | 0 | Stopped |
| Flyout held open | 0.000% | 0.000% | 0 | Stopped |
| Hidden | 0.000% | 0.000% | 0 | Stopped |

The separate Home playback check averaged 0.70% GPU / 0.66% CPU at Max/100 Hz
over two 10-second samples of the 600-second synthetic track. Paused Home remained
0% GPU. This is compatible with the prior optimized range; background/power-state
variation prevents claiming a further Home optimization in this step. No sustained
idle rendering/layout loop was observed. The initial Quick Controls activity
settled on its warm repeat; longer real-world RAM/power/idle soaks remain useful.
Raw profiles, regression logs and screenshots are retained locally under
`C:\1_Workspace\step18-validation\artifacts` (not committed).

**Manual checks still needed**

1. Use actual Explorer drags both into and out of the shelf, including several destination applications, long filenames and removable drives. Verify the destination copies the original and wheel input changes volume.
2. Use Add Files, open/reveal/copy actions in ordinary desktop apps; check file picker focus restoration, busy clipboard handling and associated-file apps.
3. Click Night Light Settings on the target PC. Open Emoji Panel while typing in normal and elevated apps; check delivery/failure messages and modifier-key behavior.
4. Confirm Lock Device once when ready to sign in again. Cancellation was tested without a real lock.
5. Move the widget between physical monitors/DPI levels and test constrained working areas, MyDockFinder coexistence, menu transit and rapid navigation with the real pointer. Installer execution and a longer idle/RAM soak were not performed.

```powershell
dotnet build .\IslandDesk.csproj -c Release
dotnet run --project .\tests\IslandDesk.Validation -c Release -- --step18
dotnet run --project .\tests\IslandDesk.Validation -c Release -- --step18-profile
```
