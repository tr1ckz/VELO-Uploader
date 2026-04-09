# VELO Uploader - Changelog

## Version 1.7.43 (2026-04-09)
- UI: adopt dual-column inspector layout for the Settings page — labels sit inline on the left with inputs and attached buttons filling the right column
- UI: remove per-row section card borders across all pages for a clean flat `#111` background with minimal `#222` section dividers
- UI: remove the VELO logo from the header bar, shift tab bar flush-left for a slim text-only chrome
- UI: update input background to `#1a1a1a` and form background to `#111` to match the NLE editor palette
- UI: unify section headers as lightweight 1px top-line dividers with muted uppercase labels

## Version 1.7.42 (2026-04-09)
- UI: enforce a stricter flush-right spine so attached action buttons now terminate on the same right edge and join directly to their fields
- UI: reduce the heavy BIOS-style bars by softening settings separators, muting inline labels, and tightening the two-column checkbox grid
- UI: apply the editor-style dark scrollbar theme to the Control Center scroll surfaces for a more consistent workstation feel

## Version 1.7.41 (2026-04-09)
- UI: switch the Control Center to a tighter table-grid architecture with flush section cards, shared 1px `#333` separators, and full-width `#1a1a1a` header bars
- UI: align every browse/action group to the right edge with zero-gap input joins and a unified 24px button system
- UI: restyle checkboxes to low-contrast dark boxes with purple ticks and move `Save & Start Watching` into the fixed bottom footer bar

## Version 1.7.40 (2026-04-09)
- UI: collapse the Control Center chrome into a single 32px header row with tabs and a tiny tucked-right version label
- UI: slim the quick-action row, tighten section spacing, flatten borders to a strict 1px industrial look, and connect browse/input groups more cleanly
- UI: reduce the remaining "website" feel with darker monochrome defaults and high-density workstation spacing

## Version 1.7.39 (2026-04-09)
- UI: replace the flat uploader section dividers with distinct shaded cards (`#161616` with `#2d2d2d` borders) for a denser workstation-style hierarchy
- UI: upgrade GPU/server/system status indicators to monospace LED-style labels
- UI: compact the top chrome with a smaller monochrome logo aligned to the tab row for a high-density layout

## Version 1.7.38 (2026-04-09)
- Fix: `Open Video Editor` now validates against the current token typed in `Settings`, even if it has not been saved yet
- Fix: unauthorized editor gating now uses the live form state instead of the last persisted token

## Version 1.7.37 (2026-04-09)
- Change: pressing `Open Video Editor` now performs API-token validation first and returns an explicit `Unauthorized` error when the token is missing or rejected
- UX: invalid editor license failures now surface as a clear unauthorized dialog instead of a generic lock message

## Version 1.7.36 (2026-04-09)
- Feature: lock the video editor behind live API-token validation so it only opens when the VELO token is accepted by the server
- Improvement: reuse the same server-side token validation flow in the Settings `Test API` action and the editor launch gate

## Version 1.7.35 (2026-04-09)
- Fix: harden tray/menu/status updates onto the WinForms UI thread to reduce intermittent crash-and-restart behavior
- Fix: make persisted queue restore startup-safe so a bad queued item cannot take down launch
- UI: match the uploader shell, tray menu, and helper dialogs to the dark pro-NLE editor styling

## Version 1.7.10 (2026-04-02)
- Fix: automatically re-queue clips after transient server outages during chunk init, chunk upload, and chunk completion
- Fix: keep the original source clip until upload success so local compression no longer makes retries unrecoverable
- Fix: remove non-retryable failures from the persisted pending queue so only recoverable outages stay queued

## Version 1.7.0 (2026-03-27)

### Major Changes
- **UI Consolidation**: Status Dashboard has been consolidated into the main Settings window as a new 5th tab
  - No longer requires opening a separate modal window
  - All real-time monitoring is visible alongside settings configuration
  - Users can manage settings and monitor uploads in one unified interface

### Features Added
- New "Status" tab in SettingsForm containing:
  - Real-time upload progress bar with filename and status
  - Session statistics (upload count, success rate, total data)
  - System status indicators (watching/paused state, GPU availability info)
  - Application version and update checker
  - Scrollable color-coded event log with timestamps

### Improvements
- Cleaner UI with single window instead of multiple modals
- Better UX for users who want to monitor uploads while adjusting settings
- Thread-safe update system with automatic UI marshaling
- Proper timing for window initialization

### Breaking Changes
- Removed "Status Dashboard" menu item from tray context menu
- StatusWindow modal window no longer available
- Status monitoring must now be accessed via Settings form, Status tab

### Technical Details
- SettingsForm expanded from 4 tabs to 5 tabs
- Tab button positioning adjusted to accommodate new tab
- 6 new public methods for real-time status updates
- InvokeIfNeeded pattern ensures thread safety
- Removed StatusWindow.cs (369 lines of dead code)

### Bug Fixes
- Fixed timing issue where status updates could be called before window handle creation
- Proper IsHandleCreated verification prevents invoke errors

---

## Version 1.6.1 (2026-03-27)
- Fix: prevent invoke errors when status window handle not yet created

## Version 1.6.0 (2026-03-27)  
- Feature: add beautiful status dashboard UI with real-time progress, stats, and event log

## Version 1.5.0
- Feature: GPU-accelerated compression (NVIDIA NVENC) with automatic detection

## Version 1.4.3
- Fix: robust update process with logging and forced exit

## Version 1.4.2
- Fix: update exit sequence and process termination
