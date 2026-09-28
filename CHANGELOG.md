# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- `--always-auto-tune` CLI flag and interactive wizard option to unconditionally auto-tune all scans.
- Unit tests for `AutoTuneService` detection logic, sweep combinations, and scoring boundaries.
- Unit tests for `PhotoCropperEngine` including `ApplyGrabHandleResize` behavior and edge cases.
- Unit tests for `AutoOrientationService` covering landscape analysis heuristics (sky, water, vegetation).

### Fixed
- Replaced magic floating array indexes in `FaceOrientationService` with explicitly defined ONNX YuNet constants.
- Resolved orphaned exceptions caused by unawaited fire-and-forget background tasks by properly attaching fault continuations.
- Removed hardcoded fallback version (`2.7.1`) in the CI release workflow to prevent silent publication of incorrect versions on tag parse failure.

### Changed
- Split the monolithic GitHub Actions pipeline into dedicated `ci.yml` (for testing PRs/pushes) and `release.yml` (for CD tagging) to adhere to security best practices.

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
