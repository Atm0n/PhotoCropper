# PhotoCropper

An intelligent, cross-platform .NET 10 desktop application designed to automatically detect, straighten, and extract multiple photos from a single scanner bed image. Built with Avalonia UI and Emgu.CV (OpenCV).

## Project Structure

The solution consists of three main projects:
- **`PhotoCropper` (Core Library):** Contains the core image-processing and detection logic, encapsulated inside the robust `PhotoCropperEngine` class.
- **`PhotoCropperGui` (Avalonia Desktop App):** A high-performance GUI using a modern dark theme and custom-drawn interactive canvas widgets.
- **`PhotoCropper.Tests` (xUnit Test Suite):** Comprehensive unit tests checking algorithm correctness, boundary constraints, and edge cases.

---

## Technical Highlights

### 1. High-Performance Rendering Pipeline
- **Direct Pointer-to-Bitmap Transfer:** Replaced slow PNG/JPEG encoding and decoding with direct memory copies. By constructing Avalonia `Bitmap` instances using native pointers (`IntPtr`) and the `Bgra8888` pixel format, the application renders high-DPI scanner scans instantly without UI lag.
- **Unified BGR Color Management:** Standardized internally on OpenCV's native BGR layout. The UI performs a single `Bgr2Bgra` conversion purely for display, avoiding redundant color conversions and correcting the "blue-tint" saving artifact perfectly.

### 2. Intelligent Auto-Detection Engine
- **8-Point Median Background Profiling:** Rejects corner-photo anomalies by sampling HSV values at 8 distinct points around the scan perimeter (corners and edge centers) to compute median saturation, hue, and brightness.
- **Resolution-Aware Morphology:** Morphological opening and closing kernels dynamically scale according to the scan's resolution, ensuring identical edge-detection performance whether processing 150 DPI or 1200 DPI scans.
- **Adaptive Shadow Tolerance:** Brightness thresholds are scaled dynamically for light backgrounds, allowing the engine to absorb scanner lid gradients and shadows while preserving photo integrity.
- **Convex Hull Overlap Verification:** Rather than checking basic axis-aligned bounding rectangles, the engine uses OpenCV's convex hull polygon testing (`PointPolygonTest`) to separate tilted adjacent photos.

### 3. Smart Manual & Refinement Operations
- **Interactive Refinement Mode:** Shrink-wraps the crop box around physical photos using an adaptive border-trimming algorithm. It automatically detects and removes the scanner's white canvas borders.
- **Local ROI Rotation:** Instead of rotating the entire giant scan, only the region of interest is padded, extracted, rotated, and tightly cropped using Cubic interpolation, saving substantial memory and processing overhead.

---

## Clean Code & Analysis Standards

The codebase strictly enforces the highest standard of static analysis and memory hygiene:
- **Warnings-as-Errors Policy:** Enforced solution-wide via `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` and `<AnalysisLevel>latest-All</AnalysisLevel>` inside `Directory.Build.props`.
- **Zero-Warning Success:** Compiles with `0 Warnings` and `0 Errors` across both Debug and Release configurations.
- **OpenCV Memory Safety (CA2000):** Implements explicit `using` statements, unmanaged resource trackers, and try-finally ownership transfer patterns to prevent native memory leaks during parallel contour processing.
- **Encapsulation & Security:** Core internal helper elements are marked as `internal sealed`, exposing APIs through read-only interfaces (`IReadOnlyList`, `Collection<T>`) to guarantee architectural robustness.

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
To execute all 21 unit tests:
```bash
dotnet test
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
