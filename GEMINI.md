# PhotoCropper

A .NET tool to detect and extract multiple photos from a single scanned image using Emgu.CV and Avalonia.

## Project Structure

- `PhotoCropper`: Core library for image processing.
  - `PhotoCropper.cs`: Contains the detection and cropping logic using OpenCV (via Emgu.CV). Parallelized extraction engine.
- `PhotoCropperGui`: Avalonia-based desktop application.
  - `MainWindow.axaml`: Main UI layout with Dark Theme, DockPanel, and Grids.
  - `MainWindow.axaml.cs`: UI logic, pointer-based rendering pipeline, and asynchronous event handling.

## Tech Stack

- **Framework**: .NET 10
- **UI Framework**: Avalonia 12
- **Image Processing**: Emgu.CV (OpenCV wrapper)
- **Dependencies**: 
  - `Emgu.CV`, `Emgu.CV.Bitmap`, `Emgu.CV.runtime.windows`
  - `Avalonia`, `Avalonia.Desktop`, `Avalonia.Themes.Fluent`

## Technical Details

- **High-Performance Rendering Pipeline**: Replaced SkiaSharp/PNG encoding with a direct **Pointer-to-Bitmap** copy. Uses Avalonia's native `Bitmap` constructor with `IntPtr` and `Bgra8888` pixel format for near-instant rendering of high-res scans.
- **Color Space Management**: Standardized on **BGR** internally (OpenCV default). The UI performs a single `Bgr2Bgra` conversion for display. This eliminates redundant channel swaps and ensures 100% color accuracy for both previews and saved files.
- **Parallel Extraction Engine**: Uses `Parallel.For` to process, rotate, and refine multiple detected photos simultaneously across all CPU cores.
- **Asynchronous UI Architecture**: All heavy image processing (detection, rotation, refinement, manual cropping) is offloaded to background threads using `Task.Run` to prevent UI freezing.
- **Local ROI Rotation Engine**: Instead of rotating the entire scan, the engine extracts a large padded square around the specific photo and rotates only that piece using `Inter.Cubic` interpolation.
- **Adaptive Border Trimming (Refinement)**: Interactive mode that samples corners to build a mask and shrink the bounding box to remove scanner margins.

## Conventions

- **Code Style**: Standard C# / .NET conventions.
- **Resource Management**: Strictly `Dispose` of unmanaged `Mat` objects. The UI uses direct memory access to avoid redundant allocations.
- **UI Architecture**: Uses `MaxHeight`/`MaxWidth` constraints and `VerticalAlignment="Center"`.

## TODO / Roadmap

- [x] **Feature**: Manual 90° rotation support in GUI.
- [x] **Feature**: Allow discarding individual cropped photos from the UI.
- [x] **Improvement**: Intelligent Manual Cropping (snaps to actual photo boundaries).
- [x] **Improvement**: Interactive Edge Refinement Mode to eliminate white margins.
- [x] **Improvement**: **Direct Mat-to-Bitmap conversion** (Performance fix).
- [x] **Improvement**: **Parallelized photo extraction**.
- [x] **Fix**: Correct image displacement and white clipping during rotation.
- [x] **Fix**: Convert RGB to BGR before saving to disk to fix blue tint.
- [ ] **Feature**: Make detection parameters (thresholds, area filters) configurable.
