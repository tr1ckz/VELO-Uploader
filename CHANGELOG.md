# VELO Uploader - Changelog

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
