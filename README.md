# PhotoCropper

An intelligent, cross-platform .NET 10 desktop application designed to automatically detect, straighten, and extract multiple photos from a single scanner bed image. Built with Avalonia UI and Emgu.CV (OpenCV).

## Project Structure

The solution consists of three main projects:
- **`PhotoCropper` (Core Library):** Modular image-processing and detection pipeline:
  - **`Models/`**: Domain records and DTOs (`CropCandidate`, `DetectionOptions`).
  - **`Detection/`**: Dedicated pipeline stages (`BackgroundAnalyzer`, `ForegroundMaskGenerator`, `CandidateExtractor`, `CandidateResolutionFilter`).
  - **`Extraction/`**: Photo extraction, local ROI perspective warps, and border refinement (`PhotoExtractionEngine`, `EdgeRefinementService`).
  - **`Export/`**: Output serialization supporting lossless PNG and customizable JPEG quality (`PhotoExporter`).
  - **`PhotoCropperEngine.cs`**: High-level facade coordinating pipeline execution.
- **`PhotoCropperGui` (Avalonia Desktop App):** A high-performance GUI using a modern dark theme, custom-drawn interactive canvas widgets, multi-language localization (EN, ES, CA), and persistent configuration.
- **`PhotoCropper.Tests` (xUnit Test Suite):** Comprehensive unit tests checking algorithm correctness, composite splitting, boundary constraints, and edge cases.

---

## Technical Highlights

### 1. High-Performance Rendering Pipeline
- **Direct Pointer-to-Bitmap Transfer:** Replaced slow PNG/JPEG encoding and decoding with direct memory copies. By constructing Avalonia `Bitmap` instances using native pointers (`IntPtr`) and the `Bgra8888` pixel format, the application renders high-DPI scanner scans instantly without UI lag.
- **Unified BGR Color Management:** Standardized internally on OpenCV's native BGR layout. The UI performs a single `Bgr2Bgra` conversion purely for display, avoiding redundant color conversions and correcting the "blue-tint" saving artifact perfectly.
- **Parallel Photo Extraction:** Uses `Parallel.For` to process, rotate, and refine multiple detected photos simultaneously across CPU cores.

### 2. Intelligent Auto-Detection Engine
- **Multi-Pass Sensitivity Search:** Evaluates progressive tolerance steps around the base background tolerance to automatically recover subtle, low-contrast photos without manual threshold tuning.
- **Composite Candidate Resolution (Parent-Child Splitting):** Automatically detects when two adjacent photos are fused into a composite bounding box during high-tolerance passes and resolves them into their distinct individual photos.
- **Convexity & Rectangularity Quality Scoring:** Scores candidates based on contour convexity ($\text{ContourArea} / \text{HullArea}$) and rotated rectangularity, penalizing irregular merged blobs with waist indentations in favor of clean single photos.
- **8-Point Median Background Profiling:** Rejects corner-photo anomalies by sampling HSV values at 8 distinct points around the scan perimeter (corners and edge centers) to compute median saturation, hue, and brightness.
- **Resolution-Aware Morphology:** Morphological opening and elliptical closing kernels dynamically scale according to the scan's resolution, preserving narrow gaps between close photos.
- **Adaptive Shadow Tolerance:** Brightness thresholds are scaled dynamically for light backgrounds, allowing the engine to absorb scanner lid gradients and shadows while preserving photo integrity.
- **Geometric Overlap Verification:** Measures true polygon intersection area in unmanaged masks to prevent duplicate or invading bounding boxes while allowing tilted adjacent photos.
- **Interactive Background Color Picker:** Allows manual background sampling via a noise-resistant 5x5 average neighborhood in HSV space directly from any clicked zoom/pan pixel.

### 3. Smart Manual & Refinement Operations
- **Interactive Refinement Mode:** Shrink-wraps the crop box around physical photos using an adaptive border-trimming algorithm. It automatically detects and removes the scanner's white canvas borders.
- **Local ROI Perspective Warp:** Instead of rotating the entire giant scan, only the region of interest is extracted and warped with `Inter.Cubic` interpolation with transparent alpha margins.
- **Intelligent Manual Crop Snapping:** Manually drawn selection boxes automatically snap to the nearest high-contrast photo boundary.

### 4. Focus-Defeat & Keyboard Event Tunneling
- **Global Key Event Tunneling:** Uses Avalonia's tunneling event routing (`RoutingStrategies.Tunnel`) for key-down events. This intercepts keyboard navigation events at the Window level before they can reach child controls.
- **Non-Focusable Controls:** Sidebar controls, sliders, combo boxes, and buttons are explicitly configured as `Focusable="False"`. This prevents active UI controls from stealing focus, ensuring key-based navigation (like arrow keys) remains fully responsive at all times.

---

## Clean Code & Analysis Standards

The codebase strictly enforces the highest standard of static analysis and memory hygiene:
- **Warnings-as-Errors Policy:** Enforced solution-wide via `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` and `<AnalysisLevel>latest-All</AnalysisLevel>` inside `Directory.Build.props`.
- **Zero-Warning Success:** Compiles with `0 Warnings` and `0 Errors` across both Debug and Release configurations.
- **OpenCV Memory Safety (CA2000):** Implements explicit `using` statements, unmanaged resource trackers, and try-finally ownership transfer patterns to prevent native memory leaks during parallel contour processing.
- **Encapsulation & Security:** Core internal helper elements expose APIs through read-only interfaces (`IReadOnlyList`, `Collection<T>`) to guarantee architectural robustness.

---

## Keyboard Shortcuts

| Shortcut | Action |
|----------|--------|
| `Left / Right` | Navigate between cropped photos |
| `Up / Down` / `PageUp / PageDown` | Switch between original loaded scans |
| `R` | Rotate the current cropped photo 90° clockwise |
| `X` / `Delete` | Permanently delete the currently selected photo |
| `N` / `Ñ` | Enter Interactive Refinement Mode |
| `Enter` / `A` | Accept Refinement (while in Refinement Mode) |
| `Backspace` / `Esc` / `C` | Reject Refinement (while in Refinement Mode) |
| `Esc` | Close Help or Refinement overlays |
| `Ctrl + Mouse Wheel` | Zoom in/out on the original scan |
| `Ctrl + S` | Save all results |

---

## How to Build & Run

### Restore and Build
To restore dependencies and build the entire solution:
```bash
dotnet build
```

### Run GUI
To run the desktop application:
```bash
dotnet run --project PhotoCropperGui
```

### Run Tests
To execute all 31 unit tests:
```bash
dotnet run --project PhotoCropper.Tests/PhotoCropper.Tests.csproj
```

---

## Deployment

The application is fully cross-platform and supports **Windows** and **Ubuntu/Linux**.

To publish the application without manual configuration, run the provided PowerShell script:

```powershell
./publish.ps1
```

This will create a `publish/` folder containing **self-contained, single-file executables** for both platforms. No .NET runtime installation is required on the target machines.

---

## License

This project is licensed under the GNU General Public License v3.0 - see the [LICENSE](LICENSE) file for details.
