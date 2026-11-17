# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- **Folder pipeline for projects**: `RawScans/` → `Cropped/` (one lossless PNG per photo, geometry only) → `Final/` (restored, with metadata). Photos get permanent IDs (`scan_0003_p02`), so re-cropping keeps their settings and replaces their files; `session.json` (format 2.0) records each photo's history and knows when a final photo is out of date. Existing projects are migrated automatically: their exports move from `Cropped/` to `Final/`.
- **CLI `--stage crop|restore`**: run only the crop stage (lossless PNGs, no restoration) or only the restore stage on any folder of photos, including photos cropped elsewhere.
- **Crash reports**: Unhandled errors are written to `%LocalAppData%/PhotoCropper/logs` (last 20 kept). Errors raised by UI actions show an error dialog with the report location and keep the app open so unsaved work can be saved.
- **Export failure report**: Bulk export lists the scans that failed to export instead of only logging them.
- Session crops now record each photo's auto-orientation (`AutoRotationDegrees`) and each scan's exported files (`ExportedFiles`) in `session.json`. Older sessions still load.

### Changed
- **Re-export cleanup** only removes files this scan previously exported: the files recorded by its last export (if unchanged on disk and still in the output folder). Without a record it falls back to files named exactly as the current pattern produces, except for patterns without `{original}`/`{filename}` and scans sharing a base name and output folder with another scan. Unrelated files and other scans' exports are no longer deleted, and the source scan is never deleted.
- **Crop refinement** now also moves the photo's box on the scan, so refinements are shown on the overlay and kept with the session.
- **Manual crops** go through the same auto-orientation and colour restoration as detected photos.
- **Box overlays** (lines, handles, badges) and their click areas scale with scan resolution instead of using fixed pixel sizes.
- **Cancelling** an operation waits for it to stop (showing "Cancelling..."); a scan that ignores cancellation is abandoned after 5 seconds and its late result discarded.
- Face detection reuses one detector per thread instead of loading the model four times per photo, and the model file is written atomically.
- Build: warnings are treated as errors again; solution-wide preview features removed; `System.Drawing.Common` pinned to 11.0.0; unused `Avalonia.Diagnostics` removed.

### Fixed
- CI and release workflows installed the .NET 10 SDK while projects target .NET 11.
- Bulk export kept every exported scan loaded in memory.
- Export completion message always reported 0 saved photos.
- "Apply year to all scans" dropped the photographer and exact date from exported metadata.
- Non-ASCII characters in the EXIF photographer and description were written as `?` (e.g. "Josep Mart?").
- A write failure partway through a re-export left the files already written unrecorded.
- Undoing a delete lost the photo's manual rotation and hold-to-compare copy; dragging a box too small left it mismatched with its photo.
- "Save & Exit" closed the app even when some scans failed to export.
- Scanner errors ("scanner not found", timeouts) showed a generic error and skipped the scanner reset.
- Re-detecting a scan kept stale manual rotations; dropped extractions misaligned photos and boxes; resizing a box lost its manual rotation; refinement and resize desynchronised the hold-to-compare copy.
- Undo: counter-clockwise rotations undid in the wrong direction; undoing a rotation left the compare copy and saved rotation rotated; recorded box lists could change after being recorded.

## [2.9.0] - 2026-09-28

### Added
- **Batch Review Mode**: A new full-screen overlay for rapidly reviewing, rotating, and curating detected photos across multiple scans (shortcut: `V`).
- **Collapsible Right Panel**: You can now hide the right-side configuration panel to maximize the workspace area while scanning.
- **Edit in Scanner**: Press `E` or double-click a photo in Review Mode to instantly jump to the editor for manual edge refinement.
- **Review Mode Mouse Support**: Use the mouse scroll wheel to quickly navigate through the batch review carousel.
- **Live Undo/Redo Sync**: Pressing `Ctrl+Z` or `Ctrl+Y` while in Review Mode now seamlessly syncs and updates the carousel live.
- `--always-auto-tune` CLI flag and interactive wizard option to unconditionally auto-tune all scans.
- Unit tests for `AutoTuneService` detection logic, sweep combinations, and scoring boundaries.
- Unit tests for `PhotoCropperEngine` including `ApplyGrabHandleResize` behavior and edge cases.
- Unit tests for `AutoOrientationService` covering landscape analysis heuristics (sky, water, vegetation).

### Changed
- **Default AI Behaviors**: Destructive AI enhancements (Restore Colors and Remove Dust & Scratches) are now disabled by default to prevent unwanted artifacts. Auto-Orientation remains enabled.
- **Scan Navigation**: Scan cycling now clamps at the first and last scans rather than looping infinitely, preventing users from losing track of newly added scans.
- Split the monolithic GitHub Actions pipeline into dedicated `ci.yml` (for testing PRs/pushes) and `release.yml` (for CD tagging) to adhere to security best practices.

### Fixed
- Replaced magic floating array indexes in `FaceOrientationService` with explicitly defined ONNX YuNet constants.
- Resolved orphaned exceptions caused by unawaited fire-and-forget background tasks by properly attaching fault continuations.
- Removed hardcoded fallback version (`2.7.1`) in the CI release workflow to prevent silent publication of incorrect versions on tag parse failure.

## [2.7.1] - 2026-09-26

### Fixed
- Fixed Windows installer build by ignoring missing GUI files.

## [2.7.0] - 2026-09-25

### Added
- Scanner bed auto-detection.
- Configurable bed page size.
- Cut-off prevention for photos scanned near the edge.

## [2.6.0] - 2026-09-25

### Added
- Background lookahead pre-processing.
- Selective low-coverage auto-tuning.
- OpenCV 5 runtime integration.

## [2.5.2] - 2026-09-21

### Fixed
- Minor bug fixes and stability improvements.

## [2.5.1] - 2026-09-21

### Fixed
- Minor patches for the 2.5.0 auto-tune overhaul.

## [2.5.0] - 2026-09-21

### Added
- Auto-tune detection overhaul.
- Sensitivity calibration.
- Dynamic versioning support.
- Centralized localization.

## [2.4.0] - 2026-09-18

### Added
- Workspace recovery mechanisms.
- Safe export tracking.

### Changed
- Clean single-file builds.
- General scanner improvements.

## [2.3.0] - 2026-09-12

### Added
- Unicode path support.
- Lazy scan loading for improved memory footprint.
- Bounded batch concurrency.
- Notification chime upon completion.

## [2.2.0] - 2026-09-12

### Changed
- Core version bump and minor internal refinements.

## [2.1.0] - 2026-09-11

### Added
- AI Face & Landscape Orientation.
- Color Restoration and Dust Inpainting.
- Gallery Grid View for better photo management.

## [2.0.0] - 2026-09-11

### Added
- Unattended CLI mode.
- High-Performance GUI.

### Changed
- Extracted logic into a modular core architecture.

## [1.0.0] - 2026-05-12

### Added
- Initial stable release.
- Cross-platform publish script.
- Linux runtime support.
