# Secondary-window activation fix

## Root cause

First-time Settings used `Show()` with `ShowActivated=True`, while only the already-open path explicitly called `Activate()`. Confirmation dialogs likewise relied on the implicit activation from `ShowDialog()`. When another application owned the foreground, this could activate a window within IslandDesk's input thread without bringing that process to the foreground.

The same new native regression was run against the saved unchanged DLL and the fixed DLL. The unchanged application reproduced the problem: Settings reported `IsActive=True`, but `GetForegroundWindow()` still returned the separate foreground application's HWND. Its extended style was `0x40100`, with no `WS_EX_NOACTIVATE`. The fixed application acquired foreground on the first request under the same conditions.

No secondary-window non-activation-style leak was found. WPF creates a per-window hidden owner when necessary for `ShowInTaskbar=False`; it is not a shared process-wide owner. Native checks verify that secondary windows do not use the island or its hidden owner, and do not acquire the island's NOACTIVATE/topmost styles. The main window's style and Z-order code is unchanged.

## Changes

- `SecondaryWindowActivation` restores a minimized secondary, shows it if hidden, then makes one standard WPF `Activate()` request and restores its saved keyboard focus when activation succeeds. It does not gate that request on `IsActive`, poll, retry, toggle topmost, join input queues or inject input.
- Settings uses the same path for first open and reuse, retaining its single-instance behavior and independent normal window ownership. If its confirmation is already open, the single activation request targets that visible owned dialog, preserving modality rather than trying to activate its disabled owner.
- Confirmations request foreground when loaded and focus Cancel. A visible activatable secondary such as Settings is their WPF owner, preserving modal disabling and relative Z-order. The non-activating island remains excluded as an activation owner, so cancelling Lock does not reactivate it. Taskbar visibility is preserved.
- Rename uses the same explicit-open activation path and reuses an existing tool. Its permanent Topmost setting and manual TOOLWINDOW style setter were removed; it remains absent from the taskbar. Automatic fullscreen restoration continues through `RestoreWithoutActivation`, without an activation request.
- Closing code does not force foreground back to the island or a remembered application. When the user switches elsewhere before closing, their new foreground remains intact.

WPF's activation request respects [Windows foreground restrictions](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setforegroundwindow). Policy or input directed elsewhere can still deny activation; no bypass or retry loop was added. Standard WPF [ownership and modal behavior](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/windows/how-to-open-window-dialog-box) handle confirmations above normal parents.

## Verification

`dotnet build .\IslandDesk.csproj -c Release` passed with zero warnings and zero errors. A stale generated-XAML cache after alternate-output builds was cleared with `dotnet clean`; both the final clean build and a subsequent incremental build using the exact requested command succeeded. No windows were opened during those final build checks, respecting the user's request to keep the current desktop.

An earlier focused native run passed 56 HWND/keyboard/ownership checks: a separately foregrounded native application, first/reused/minimized Settings, close/reopen, owned modal cancellation, Lock cancellation, Rename, independent native owners and unchanged island styles. It counted zero island activation events. It never confirmed Lock or locked Windows.

The final suite was strengthened to include a pending-modal reopen and guarded physical input/visibility checks. It could not complete: Windows reported `DWMWA_CLOAKED=2` for the controlled test window while another desktop/application remained visible. A foreground HWND alone was therefore insufficient evidence of correct visual Z-order. The user confirmed that a virtual desktop or Show desktop was active and explicitly chose to keep that desktop and report the live-test limitation. No final visual pass is claimed for Settings/Lock visibility or pending-modal foreground stability. Those checks remain to be repeated in a normal application view on the test desktop. Earlier synthetic input-queue setup produced a delayed owner activation in the pending-modal case; it was not accepted as proof of correct visible behavior or worked around with repeated activation.

The external test provider sends a guarded click only within its own visible window, then grants the validation process foreground permission. The processes grant permission back for successive scenarios, avoiding input-queue joining during modal checks. The fixture retains an initial bootstrap before any modal exists. Input and foreground setup are test-only; production contains no new Win32 activation code or focus workaround. The separately controlled application is used instead of automating VS Code itself. Shell-cloaked/occluded windows stop the physical-input suite; leave Show desktop/Task View before running it. No desktop-switching or desktop-moving code is included.

Earlier runs of the existing regression suites passed: Settings 62, navigation/Quick Controls/Shelf 55, fullscreen 27, animations 51, themes 86, interaction/tray/battery 89, Shelf UX 68 and default layout/input/native windows 96. These total 534 checks; they do not replace the incomplete final visibility check. Release logs are in `C:\1_Workspace\activation-validation\artifacts\*-final.log`. The saved unchanged DLL and its expected failing first-open regression are in `C:\1_Workspace\activation-before`.

To repeat with physical input, focus VS Code or another ordinary application, then click Settings on the island once. Type or navigate with Tab, switch back to the other app, and reopen Settings; repeat with Settings minimized and after closing it. Open Lock and cancel it. Open Restore Defaults in Settings and cancel it, including after switching to another app. Confirm that normal typing returns to the chosen application and the island never receives keyboard focus. Foreground-lock/policy conditions can restrict any standard activation request.

Version 0.2.0, existing settings, animation/frame-rate preferences and Windows integration are preserved. No release, commit or push was made.
