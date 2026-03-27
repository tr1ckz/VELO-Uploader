# VELO Uploader - Changelog

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
