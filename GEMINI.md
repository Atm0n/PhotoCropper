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

## Conventions

- **Code Style**: Standard C# / .NET conventions.
- **Async/Await**: Use for I/O operations (file picking, saving).
- **Resource Management**: Properly `Dispose` of `Mat` and `Bitmap` objects to avoid memory leaks.

## TODO / Roadmap

- [x] **Fix**: `DetectedPhotos` accumulation bug when re-loading photos to GUI.
- [x] **Fix**: Add `Down` arrow support for navigating back in `OriginalPhotos`.
- [x] **Improvement**: Replace GUID filenames with original filename + counter.
- [x] **Improvement**: Optimize Bitmap conversion between Emgu.CV and Avalonia.
- [ ] **Feature**: Make detection parameters (thresholds, area filters) configurable.
- [ ] **Feature**: Allow discarding individual cropped photos from the UI.
- [ ] **Feature**: Custom output directory selection.
