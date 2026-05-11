# PhotoCropper

A .NET tool to detect and extract multiple photos from a single scanned image using Emgu.CV and Avalonia.

## Project Structure

- `PhotoCropper`: Core library for image processing.
  - `PhotoCropper.cs`: Contains the detection and cropping logic using OpenCV (via Emgu.CV).
- `PhotoCropperGui`: Avalonia-based desktop application.
  - `MainWindow.axaml`: Main UI layout with Dark Theme, DockPanel, and Grids.
  - `MainWindow.axaml.cs`: UI logic, coordinate mapping, and event handling.

## Tech Stack

- **Framework**: .NET 10
- **UI Framework**: Avalonia 12
- **Image Processing**: Emgu.CV (OpenCV wrapper)
- **Dependencies**: 
  - `Emgu.CV`, `Emgu.CV.Bitmap`, `Emgu.CV.runtime.windows`
  - `Avalonia`, `Avalonia.Desktop`, `Avalonia.Themes.Fluent`

## Technical Details

- **Local ROI Rotation Engine**: Instead of rotating the entire scan, the engine extracts a large padded square around the specific photo and rotates only that piece using `Inter.Cubic` interpolation. This prevents edge clipping, improves performance, and stops coordinate displacement.
- **Adaptive Border Trimming (Refinement)**: The interactive refinement mode (✨) samples the 4 corners of the cropped image to determine the exact shade of the scanner margin. It then builds a mask and shrinks the bounding box to perfectly remove the margin, falling back to manual drawing if needed.
- **Manual Adjustments**: Users can manually crop by drawing on the original scan, or redraw the crop box inside the Refinement overlay. Both use exact coordinate scaling `(Image Pixels / UI Bounds)`.
- **Color Space Management**: Avalonia previews require `RGB`, but OpenCV's `Save()` expects `BGR`. The engine explicitly converts `RGB2BGR` just before saving to prevent "bluish" file outputs.

## Conventions

- **Code Style**: Standard C# / .NET conventions.
- **Resource Management**: Strictly `Dispose` of unmanaged `Mat` and `Bitmap` objects; `PhotoCropper` implements `IDisposable`. The UI disposes of `PreviewMat` objects instantly after converting to Avalonia Bitmaps.
- **UI Architecture**: Uses `MaxHeight`/`MaxWidth` constraints and `VerticalAlignment="Center"` to prevent low-res images from stretching on maximized large monitors.

## TODO / Roadmap

- [x] **Feature**: Manual 90° rotation support in GUI.
- [x] **Feature**: Allow discarding individual cropped photos from the UI (Implemented via 'Delete' button & 'X' key).
- [x] **Improvement**: Intelligent Manual Cropping (snaps to actual photo boundaries).
- [x] **Improvement**: Interactive Edge Refinement Mode to eliminate white margins.
- [x] **Fix**: Correct image displacement and white clipping during rotation.
- [x] **Fix**: Convert RGB to BGR before saving to disk to fix blue tint.
- [ ] **Feature**: Make detection parameters (thresholds, area filters) configurable.
- [ ] **Feature**: Custom output directory selection.
