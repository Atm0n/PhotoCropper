# PhotoCropper

A .NET tool to detect and extract multiple photos from a single scanned image using Emgu.CV and Avalonia.

## Project Structure

- `PhotoCropper`: Core library for image processing.
  - `PhotoCropper.cs`: Contains the detection and cropping logic using OpenCV (via Emgu.CV).
- `PhotoCropperGui`: Avalonia-based desktop application.
  - `MainWindow.axaml`: Main UI layout.
  - `MainWindow.axaml.cs`: UI logic and integration with `PhotoCropper`.

## Tech Stack

- **Framework**: .NET 10
- **UI Framework**: Avalonia 12
- **Image Processing**: Emgu.CV (OpenCV wrapper)
- **Dependencies**: 
  - `Emgu.CV`, `Emgu.CV.Bitmap`, `Emgu.CV.runtime.windows`
  - `Avalonia`, `Avalonia.Desktop`, `Avalonia.Themes.Fluent`, `Avalonia.Fonts.Inter`

## Technical Details

- **Background Profiling**: Samples 4 corners to create an HSV profile of the scanner bed.
- **Tilt Correction**: Uses `ConvexHull` combined with `MinAreaRect` for rock-solid angle detection, avoiding "cut-off" photos caused by rounded mask corners.
- **Rotation Engine**: Uses `WarpAffine` for high-quality mathematical straightening.
- **Margin Shaving**: Mathematically shrinks the crop box by 12px (6px per side) after rotation to remove scanner shadows without distortion.
- **Manual Adjustments**: Supports 90° manual rotation (Button or 'R' key) to correct portrait/landscape orientation.

## Conventions

- **Code Style**: Standard C# / .NET conventions.
- **Resource Management**: Strictly `Dispose` of `Mat` and `Bitmap` objects; `PhotoCropper` implements `IDisposable`.
- **UI Updates**: Debounce detection tasks (via slider released) to maintain GUI responsiveness.

## TODO / Roadmap

- [x] **Fix**: `DetectedPhotos` accumulation bug.
- [x] **Fix**: Add `Down` arrow support for navigation.
- [x] **Improvement**: Replace GUID filenames with original name + counter.
- [x] **Improvement**: Implement `ConvexHull` and `WarpAffine` for perfect tilt correction.
- [x] **Feature**: Manual 90° rotation support in GUI.
- [ ] **Feature**: Make detection parameters (thresholds, area filters) configurable.
- [ ] **Feature**: Allow discarding individual cropped photos from the UI.
- [ ] **Feature**: Custom output directory selection.
- [ ] **Feature**: Batch save support for all loaded scans at once.
