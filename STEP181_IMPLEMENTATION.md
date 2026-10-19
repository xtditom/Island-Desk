# Step 18.1 implementation and validation

Navigation was implemented, built in Release and validated with 52 checks before the Night Light investigation began. No app version, commit or release was changed. File Shelf implementation files were left unchanged in this step.

## Navigation

The default header now has a current-dashboard icon with chevron and Settings. Hover/click the stable primary icon to reveal a 108 × 32 horizontal strip to its right, covering Settings. Icons are Home, System, Quick Controls, File Shelf. Hover selects immediately; click also selects. The primary icon holds still during navigation and reflects the selected dashboard when the strip closes. Collapse/reopen resets Home. Optional dashboard preferences still apply.

The strip stays in the existing non-activating island HWND: no popup HWND, native popup hooks or vertical flyout. Fixed side measurement keeps the title centered and the header/island bounds stationary while revealing the strip. A clipped translation slides icons without stretching or animating layout. Reveal/hide use finite cancellable clocks, current frame-rate and animation-speed preferences, and Instant mode. Pointer exit has 150 ms grace; entering the strip cancels dismissal. Hide, collapse, deactivation and unload close immediately and release clocks/timer. Settings returns after closing.

Theme-aware vector icons, tooltips and selected highlights use existing resources. Existing media, volume-wheel, hardware sampling, native styles, DPI placement and expansion logic remain intact. Dark/Light/System renders were inspected; physical multi-monitor pointer/DPI behavior remains a manual check.

## Optional Night Light

Source investigated: [kvnxiao/win-nightlight-cli](https://github.com/kvnxiao/win-nightlight-cli), revision `96fdd61b06ca4fb74dbc2b8368ddc9bd4b77ab03`, MIT. It uses reverse-engineered Bond CompactBinary v1 in CloudStore, not a stable public Windows API. Upstream reports Windows 11 builds 26100 and 26300 and warns that successful writes do not confirm display output. See [source review and attribution](ThirdParty/README.md) and the shipped full MIT license.

A small replaceable C# provider adapts only the state codec and on/off rules. No CLI binary, subprocess, runtime download, Rust dependency or administrator request is used. It is **off by default**, explicitly enabled at Settings → Widgets → Experimental direct Night Light control with compatibility warnings.

Direct requests require a reviewed build, a recognized canonical envelope/state schema and a usable device. Unknown fields/types, invalid/truncated/noncanonical data, unreviewed builds and implausible future timestamps fail closed. The adapter never creates missing state keys and has no write path for the separate schedule/temperature value. Manual on/off changes update only status, transition cause/time and the monotonic envelope timestamp. It rereads before writing to detect intervening changes; the underlying Windows storage is not transactional, so a residual race with other writers remains.

The UI distinguishes On/Off (Windows state), pending, unavailable, unsupported and direct-control opt-out. It rereads actual provider state before choosing the requested direction and again after a request; there is no optimistic toggle. Two bounded verification delays belong only to that request. A stored-state readback is **not proof of physical display output**; the UI and opt-in warning say so. Failed/unsupported requests retain an explicit Open Night Light Settings fallback and Refresh action.

A native registry notification watch runs only while opted-in Quick Controls is visible. Hidden/disabled/unloaded views cancel work and unregister the watch. There is no polling, global hook or rendering subscription. Manual refresh/reopening remains available if native notification registration is unavailable. Cancellation and rapid opt-out/re-enable cannot let an old request update the new view or perform a canceled write.

Real Windows 10.0.26300.0 state decoding and notification registration succeeded. State was On. **No actual Night Light toggle was performed.** The real state bytes remained identical from the first investigation through the final read-only check. Hardware/display response to real on/off remains unvalidated, per the user's instruction requiring permission before modifying Night Light.

## Release validation

`dotnet build .\IslandDesk.csproj -c Release` succeeded with **0 warnings, 0 errors**. The running development copy was closed gracefully for the build and reopened afterward. The Release validation project also built cleanly.

| Suite | Passed checks |
| --- | ---: |
| Default native/layout/input | 96 |
| Compact navigation, safe utility actions and existing shelf behavior | 52 |
| Night Light codec, fake-store requests/failures and UI lifecycle | 31 |
| Real Night Light read-only compatibility | 2 |
| Settings | 62 |
| Themes | 86 |
| Animation cleanup | 51 |
| Fullscreen integration | 27 |
| Home GPU regression | 21 |
| Service/performance regression | 12 |
| Advanced media preferences | 75 |
| Existing interaction/battery/tray checks | 89 |
| **Total passing checks** | **604** |

The native `--media` suite delivered playback, artwork, timeline, seek, next/previous and play/pause checks but timed out waiting for its final metadata-change event (`MediaValidation.cs:122`). The same timeout reproduced using the unchanged pre-step-18.1 validation binary under the same conditions. Media code was not changed. This suite is **not** reported as fully passed. One restricted-desktop animation timing run did not finish its clock before the fixed test deadline; the final interactive-desktop run passed all 52 navigation checks. Native UI suites require an interactive desktop as documented in tests/README.md.

## Resource measurements

Earlier Step 18 baseline and Step 18.1 sample use the same Release validation host methodology: 12 logical processors, WPF tier 2, 96 DPI, native media/hardware services off, 50 shelf references/four visible rows, 2.5 s settle then eight externally sampled one-second GPU observations. CPU is machine-normalized process time. GPU is the busiest process engine via PDH and excludes DWM. These are short local diagnostic runs, not a fresh interleaved A/B trial or Task Manager-equivalent precision.

| Settled state | Earlier CPU % | Current CPU % | Earlier GPU % | Current GPU % |
| --- | ---: | ---: | ---: | ---: |
| First Quick Controls sample | 0.030 | 0.349 | 0.145 | 0.000 |
| File Shelf | 0.000 | 0.121 | 0.000 | 0.000 |
| Navigation open (old flyout → strip) | 0.000 | 0.061 | 0.000 | 0.000 |
| Warm Quick Controls | 0.015 | 0.031 | 0.000 | 0.000 |
| Opted-in Night Light notification watch | n/a | 0.015 | n/a | 0.000 |
| Hidden | 0.000 | 0.000 | 0.000 | 0.000 |

All current samples recorded **zero layout invalidations and a stopped dismissal timer**; the Night Light watch existed only in its visible opted-in sample. Open-strip animation clocks were removed after completion. The data demonstrates low steady idle overhead, not a statistically established CPU improvement: initial samples show higher CPU than the earlier run. No new Home performance improvement is claimed in this navigation step; the earlier Home optimization and its checks remain intact.

Raw current data: `C:\1_Workspace\step181-validation\artifacts\step181-profile.json`.
Earlier data: `C:\1_Workspace\step18-validation\artifacts\step18-profile.json`.
Rendered navigation/settings views and suite logs are in the current artifacts directory. No installer or release package was produced.

## Manual checks

1. Hover the primary icon; cross all four strip icons and return across them. Confirm immediate selection, stable title, no activation and no dismissal between icons. Leave for 150 ms and confirm Settings returns and the primary icon matches the selection. Collapse/reopen and confirm Home.
2. Disable each optional tab in Widgets; confirm it disappears and disabling an active tab returns Home. Exercise Settings, rename, themes, animation speed/FPS and Instant mode. Check physical cursor transit, wheel volume, expansion/collapse and different monitor scales.
3. Night Light remains Settings-only until opted in. On a recognized build, opt in and confirm current Windows state; use Refresh or reopen after changing Windows Settings yourself. Unsupported builds/data must show an accurate fallback. Opting out must stop the integration immediately.
4. **Only with permission to change the display state:** click Night Light, confirm pending/readback status and the physical result in both directions, then restore the initial state. Check Windows schedule and temperature before/after. Repeat with schedule enabled and verify external changes update the visible card. Display confirmation is still required before treating experimental control as hardware-validated.
5. Smoke-test existing media controls, volume, tray, topmost/fullscreen and positioning. File Shelf has no redesign in this step; its separate issues remain out of scope.
