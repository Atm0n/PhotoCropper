# PhotoCropper

An intelligent, cross-platform .NET 10 desktop application designed to automatically detect, straighten, and extract multiple photos from a single scanner bed image. Built with Avalonia UI and Emgu.CV (OpenCV).

## Project Structure

The solution consists of four main projects organized under `src/` and `tests/`:
- **`src/PhotoCropper.Core` (Core Library):** Modular image-processing and detection pipeline:
  - **`Models/`**: Domain records and DTOs (`CropCandidate`, `DetectionOptions`).
  - **`Detection/`**: Dedicated pipeline stages (`BackgroundAnalyzer`, `ForegroundMaskGenerator`, `CandidateExtractor`, `CandidateResolutionFilter`).
  - **`Extraction/`**: Photo extraction, local ROI perspective warps, border refinement, orientation, and color restoration (`PhotoExtractionEngine`, `EdgeRefinementService`, `FaceOrientationService`, `AutoOrientationService`, `PhotoRestorationService`).
  - **`Export/`**: Output serialization supporting lossless PNG, customizable JPEG quality, and DPI preservation (`PhotoExporter`).
  - **`IO/`**: File discovery and recursive directory scanning for supported image formats (`ImageFileCollector`).
  - **`Scanning/`**: Cross-platform hardware scanner integration (`IScannerService`, `Naps2ScannerService`, `ScannerOptions`, `ScannerDeviceInfo`) supporting TWAIN, WIA, SANE, and eSCL.
  - **`Workspace/`**: Project folder management, raw scan disk-staging, and crash recovery session manager (`ProjectWorkspaceService`, `WorkspaceSessionState`, `WorkspaceScanEntry`).
  - **`PhotoCropperEngine.cs`**: High-level facade coordinating pipeline execution.
- **`src/PhotoCropper.Gui` (Avalonia Desktop App):** A high-performance GUI featuring:
  - **`Views/`**: Modular UI architecture including `MainWindow/` (split into clean partials by responsibility) and `Dialogs/` (`HelpWindow`, `ScannerConfigDialog`, `CustomColorDialog`, `SafeExitPromptDialog`, `ExportSettingsDialog`).
  - **`Services/`**: Bounded undo/redo history, dynamic theme manager, hardware scanner coordination, localization manager, settings persistence, and decoupled scan session navigation (`ScanSessionManager`).
  - **`Models/`**: Clean GUI domain models such as `ScanSessionItem`.
  - Multi-language localization (EN, ES, CA), full Light/Dark/System runtime theming, and persistent configuration.
- **`src/PhotoCropper.Cli` (Command-Line Batch Extractor):** High-throughput CLI batch processor with Spectre.Console UI, multi-threaded parallel extraction, and unattended automation.
- **`tests/` (Modular xUnit & Shouldly Test Suites):**
  - **`PhotoCropper.Core.Tests`**: Unit tests verifying detection pipeline stages, image extraction, orientation heuristics, restoration filters, export formats, file collector, and engine lifecycle.
  - **`PhotoCropper.Cli.Tests`**: Integration tests verifying CLI command-line argument parsing and unattended batch processing.
  - **`PhotoCropper.Gui.Tests`**: GUI domain tests verifying user settings persistence, bounded undo/redo history, and session management.
  - **`PhotoCropper.TestHelpers`**: Shared test fixture generating synthetic test scans (tilted, flush-edge, low contrast, corner, blemished).

---

## Technical Highlights

### 1. High-Performance Rendering Pipeline
- **Direct Pointer-to-Bitmap Transfer:** Replaced slow PNG/JPEG encoding and decoding with direct memory copies. By constructing Avalonia `Bitmap` instances using native pointers (`IntPtr`) and the `Bgra8888` pixel format, the application renders high-DPI scanner scans instantly without UI lag.
- **Unified BGR Color Management:** Standardized internally on OpenCV's native BGR layout. The UI performs a single `Bgr2Bgra` conversion purely for display, avoiding redundant color conversions and correcting the "blue-tint" saving artifact perfectly.
- **Parallel Photo Extraction:** Uses `Parallel.For` to process, rotate, and refine multiple detected photos simultaneously across CPU cores.

### 2. Intelligent Auto-Detection & Auto-Tuning Engine
- **Multi-Parameter Auto-Tuning (`⚡ Auto-Tune` / `T`):** Sweeps background tolerances (`10`–`85`), Canny edge thresholds (`10`–`40`), and minimum area factors (`0.5%`–`10%`) to automatically discover difficult or low-contrast photos without manual trial-and-error.
- **Per-Scan Settings Isolation:** Each loaded scan retains its own independent detection parameters; adjusting sliders on one scan re-evaluates only that scan without altering other images in the queue.
- **Multi-Pass Sensitivity Search:** Evaluates progressive tolerance steps around the base background tolerance to automatically recover subtle, low-contrast photos without manual threshold tuning.
- **Composite Candidate Resolution (Parent-Child Splitting):** Automatically detects when two adjacent photos are fused into a composite bounding box during high-tolerance passes and resolves them into their distinct individual photos.
- **Convexity & Rectangularity Quality Scoring:** Scores candidates based on contour convexity ($\text{ContourArea} / \text{HullArea}$) and rotated rectangularity, penalizing irregular merged blobs with waist indentations in favor of clean single photos.
- **8-Point Median Background Profiling:** Rejects corner-photo anomalies by sampling HSV values at 8 distinct points around the scan perimeter (corners and edge centers) to compute median saturation, hue, and brightness.
- **Resolution-Aware Morphology:** Morphological opening and elliptical closing kernels dynamically scale according to the scan's resolution, preserving narrow gaps between close photos.
- **Adaptive Shadow Tolerance:** Brightness thresholds are scaled dynamically for light backgrounds, allowing the engine to absorb scanner lid gradients and shadows while preserving photo integrity.
- **Geometric Overlap Verification:** Measures true polygon intersection area in unmanaged masks to prevent duplicate or invading bounding boxes while allowing tilted adjacent photos.
- **Interactive Background Color Picker:** Allows manual background sampling via a noise-resistant 5x5 average neighborhood in HSV space directly from any clicked zoom/pan pixel.
- **Undetected Scan Tracking & Review Isolation:** Logs any uncropped scans to `undetected_scans.txt` and supports isolated directory copying (`--copy-undetected`) with an interactive post-batch CLI review prompt.

#### 3. Smart Manual, Refinement & AI Orientation
- **Hierarchical AI Face & Landscape Orientation:** Extracted photos are automatically rotated upright.
  - **Embedded YuNet Neural Face Detector:** Analyzes 4 candidate orientations (`0°`, `90°`, `180°`, `270°`) and verifies full 5-point facial landmark anatomy (eye-to-nose-to-mouth sequencing, horizontal eye span, and level tilt) with zero cloud dependencies.
  - **Landscape & Water Scene Heuristics:** Evaluates sky gradients (blue and overcast), horizon textures, ground/vegetation, and water bodies (seas, lakes, rivers) with strict non-landscape guards to prevent false indoor rotations.
- **Vintage Photo Color & Contrast Restoration:**
  - **Warmth-Preserving White Balance:** Damped gray-world channel normalization (`[0.85, 1.18]`) neutralizes yellowing, aged paper, and dark storage discolouration without turning warm vintage memories icy blue.
  - **LAB Contrast-Limited Adaptive Histogram Equalization (CLAHE):** Enhances local luminance dynamic range (`clipLimit: 1.3`) across shadow and highlight regions without channel clipping or artifacts.
  - **Vibrancy Revival:** Gentle HSV saturation enhancement revives faded pigments while preserving natural skin tones.
- **Automated Dust, Hair & Scratch Inpainting:**
  - **Dual Morphological Defect Detection:** Combines Black-Hat and Top-Hat filters with median pre-smoothing to isolate dark hair/fibers, dust specks, and bright white hairline scratches.
  - **Canny Edge Protection Masking:** Subtracts dilated high-frequency structural edges so fine photo contours, eyes, and sharp boundaries are strictly preserved without smearing or blurring.
  - **Fast Marching Method Inpainting:** Uses `CvInvoke.Inpaint` (Alexandru Telea / FMM) within a localized 2.5px radius to invisibly blend away detected blemishes into surrounding textures.
- **Hold-to-Compare (`Space` / `B`):** Instant zero-latency toggle between the pristine restored photo and the original unedited scan crop for effortless quality inspection.
- **Interactive Refinement Mode:** Shrink-wraps the crop box around physical photos using an adaptive border-trimming algorithm, automatically detecting and removing scanner glass/bed white borders.
- **Local ROI Perspective Warp:** Instead of rotating the entire giant scan, only the region of interest is extracted and warped with `Inter.Cubic` interpolation with transparent alpha margins.
- **Subtle Deskew Regularization:** Snaps near-straight photos (within ±1.5° of right angles) to exact axis-aligned rectangles, avoiding resampling blur while maintaining exact dimensions.
- **Intelligent Manual Crop Snapping:** Manually drawn selection boxes automatically snap to the nearest high-contrast photo boundary.

### 4. Interactive UX, Theming & Accessibility
- **Dynamic Theming (Dark / Light / System Default):** Full runtime theme switching via **View → Theme** with dynamic XAML resource binding. All backgrounds, borders, cards, buttons, and text adapt instantly without restarting.
- **Colorblind-Friendly Bounding Boxes & Custom Color Picker:**
  - High-contrast detection box presets (**Classic Red, Amber / Orange, Cyan / Turquoise, Magenta, Lime Green**) designed for protanopia, deuteranopia, and tritanopia accessibility.
  - Visual color indicator squares next to every option in the menu.
  - Dedicated **Custom Color...** modal dialog featuring a real-time photo simulation preview, 16 accessible quick swatches, RGB sliders, and direct hex input (`#RRGGBB`).
- **Dual-View Inspection & Gallery Grid (`1` / `2`):** Instantly toggle between a focused single-photo carousel and an interactive thumbnail gallery overview displaying indices and pixel dimensions.
- **Drag & Drop Queuing:** Drag image files or whole folders anywhere onto the application window to automatically queue and batch-process scans via the centralized `ImageFileCollector`.
- **Safe Exit & Data Loss Prevention:** PhotoCropper detects unsaved photo edits or unexported scans on close, presenting a confirmation dialog with **Save & Exit**, **Discard & Exit**, or **Cancel** choices (closes silently when all work is saved).
- **Dedicated Multi-Window Dialog Architecture:** Replaced embedded overlay clutter with dedicated, modular Avalonia windows:
  - `HelpWindow`: Non-modal documentation and shortcut reference (<kbd>F1</kbd>) that can be placed side-by-side or on secondary monitors.
  - `ScannerConfigDialog`: Hardware device selection, DPI configuration, and network scanner discovery.
  - `CustomColorDialog`: Interactive bounding box color adjustment.
  - `ExportSettingsDialog`: Dedicated Save & Export preferences (output directory, format, quality, naming tokens, live preview, EXIF metadata).
  - `SafeExitPromptDialog`: Unsaved changes confirmation dialog.
- **Bounded Undo/Redo Memory Management (`Ctrl+Z` / `Ctrl+Y`):** Bounded 30-action double-ended queue that automatically evicts and disposes the oldest cloned OpenCV `Mat`s, preventing memory growth during intensive editing sessions.
- **Native Avalonia `Bitmap` Disposal:** Proactively disposes underlying unmanaged SKBitmap buffers on image replacement, re-detection, and scan navigation, keeping memory footprint low.
- **Original Scanner DPI Preservation:** Preserves original scanner resolution metadata (JFIF APP0 markers for JPEG, `pHYs` chunks for PNG) for 1:1 physical printing scale (e.g., 300, 600, 1200 DPI).
- **Multi-Language Localization:** Runtime localization in English (`en-US`), Spanish (`es-ES`), and Catalan (`ca-ES`) with persistent user settings.

### 5. Hardware Scanner Integration (TWAIN, WIA, SANE & eSCL)
- **Direct Flatbed Scanning (`F5` / Scan Button):** Acquire scans straight from connected flatbed scanners directly into PhotoCropper without using slow third-party scanning utilities.
- **Cross-Platform Driver Engine via NAPS2.Sdk:** Powered by the open-source NAPS2 SDK:
  - **Windows**: Supports 32-bit and 64-bit TWAIN drivers (with transparent 32-bit IPC worker thunking via `NAPS2.Sdk.Worker.Win32`) and native 64-bit WIA (Windows Image Acquisition). Battle-tested with hardware like the Canon CanoScan LiDE 400.
  - **Linux / macOS**: Direct integration with SANE (`libsane` / `scanimage`) and modern network eSCL / AirScan scanners.
- **Dedicated Scanner Configuration Dialog:** Set scan resolution (150, 300, 600 DPI) and color modes with one-click hardware enumeration and hot-reloading.

### 6. Project Work Directory & Automatic Crash Recovery
- **Safe Raw Scan Auto-Staging (`RawScans/`):** When scanning or importing, images are immediately written to disk inside the project's `RawScans/` directory (`scan_0001.png`, `scan_0002.png`, etc.). Raw scans are never left stranded in volatile memory.
- **Crash-Proof Session Persistence (`session.json`):** Tracks all scan files, individual detection settings, 90° rotations, and manual crops in real time. If the app is closed or interrupted by a system crash or power outage, launching the workspace prompts to resume the session instantly.
- **Automatic Session Reconstruction:** Even if `session.json` is missing or accidentally removed, the workspace engine inspects the `RawScans/` directory on startup and reconstructs the session automatically.

### 7. File Naming Helper & Vintage EXIF Metadata
- **Token-Based File Naming Patterns:** Fully configurable output naming template supporting tokens:
  - `{original}` / `{filename}`: Source scan filename.
  - `{index}` / `{index:02}` / `{index:000}`: 1-based photo index with configurable zero-padding.
  - `{year}` / `{date}`: Vintage year or date from metadata.
  - `{total}`: Total photo count detected in the scan.
- **Dynamic Live Preview:** Real-time preview of the resulting filename as you type or pick presets.
- **EXIF Vintage Metadata Tagging:** Embeds standard EXIF `DateTimeOriginal`, `DateTimeDigitized`, and `UserComment` into exported JPEGs (APP1) and PNGs (`tEXt` chunks). Scanned vintage prints are automatically indexed chronologically by their capture decade in photo managers (Google Photos, Apple Photos, Immich, Lightroom) rather than the scanner ingestion date.

---

## Clean Code & Analysis Standards

The codebase strictly enforces the highest standard of static analysis and memory hygiene:
- **Warnings-as-Errors Policy:** Enforced solution-wide via `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` and `<AnalysisLevel>latest-All</AnalysisLevel>` inside `Directory.Build.props`.
- **Zero-Warning Success:** Compiles with `0 Warnings` and `0 Errors` across both Debug and Release configurations.
- **OpenCV & Avalonia Memory Safety (CA2000):** Implements explicit `using` statements, unmanaged resource trackers, proactive native `Bitmap` disposal, and bounded undo action queues to guarantee zero memory leaks.
- **Encapsulation & Security:** Core internal helper elements expose APIs through read-only interfaces (`IReadOnlyList`, `IReadOnlySet`, `Collection<T>`) to guarantee architectural robustness.
- **Clean Structure:** No `#region` / `#endregion` directives; decomposed monolithic components into clean, single-responsibility services.

---

## Keyboard Shortcuts

| Shortcut | Action |
|----------|--------|
| `F1` | 📖 Open Help & Keyboard Shortcuts window |
| `F5` | 🖨️ Acquire scan from flatbed scanner |
| `T` | ⚡ Auto-Tune detection parameters on active scan |
| `Left / Right` | Navigate between cropped photos |
| `Up / Down` / `PageUp / PageDown` | Switch between original loaded scans |
| `R` | Rotate the current cropped photo 90° clockwise |
| `Space` / `B` | Hold to compare with the unedited raw scan crop |
| `X` / `Delete` | Permanently delete the currently selected photo |
| `Shift + Delete` | 🗑️ Delete active scan from workspace and disk |
| `Ctrl + A` | Select all photos (Gallery Grid View) |
| `Ctrl + Z` | Undo last photo operation (delete, rotate, manual crop, refinement) |
| `Ctrl + Y` | Redo last undone operation |
| `N` / `Ñ` | Enter Interactive Refinement Mode |
| `1 / 2` | Switch between Single Photo Inspection and Gallery Grid View |
| `Enter` / `A` | Accept Refinement (while in Refinement Mode) |
| `Backspace` / `Esc` / `C` | Reject Refinement (while in Refinement Mode) |
| `Esc` | Close active dialog window or Refinement mode |
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
dotnet run --project src/PhotoCropper.Gui/PhotoCropper.Gui.csproj
```

### Run CLI (Unattended Batch Extractor)
To run the unattended command-line utility:
```bash
# Run the interactive step-by-step wizard (or run with no arguments in a terminal)
dotnet run --project src/PhotoCropper.Cli/PhotoCropper.Cli.csproj -- --wizard

# Process a single scan
dotnet run --project src/PhotoCropper.Cli/PhotoCropper.Cli.csproj -- scan001.jpg

# Process a folder of scans recursively, saving as PNG with color restoration in a custom directory
dotnet run --project src/PhotoCropper.Cli/PhotoCropper.Cli.csproj -- D:\Scans -o D:\Cropped -f PNG -r

# Batch process with parallel worker threads and auto-tuning enabled
dotnet run --project src/PhotoCropper.Cli/PhotoCropper.Cli.csproj -- D:\Scans -o D:\Cropped --auto-tune -j 8

# Batch process and isolate undetected scans for review
dotnet run --project src/PhotoCropper.Cli/PhotoCropper.Cli.csproj -- D:\Scans -o D:\Cropped --copy-undetected D:\NeedsReview

# Display all CLI options and flags
dotnet run --project src/PhotoCropper.Cli/PhotoCropper.Cli.csproj -- --help
```

#### CLI Options & Flags Reference

| Option | Description | Default |
|---|---|---|
| `-i, --input <path>` | Input image file or directory of scans (positional arguments accepted) | *Required* |
| `-o, --output <dir>` | Output destination directory for extracted photos | `<scan_dir>/cropped` |
| `-f, --format <fmt>` | Output file format: `JPEG` or `PNG` | `JPEG` |
| `-q, --quality <1-100>` | JPEG compression quality | `100` |
| `-p, --pattern <pat>` | Output naming template (`{original}`, `{index:02}`, `{year}`, `{date}`, `{total}`) | `{original}_{index}` |
| `--year <YYYY>` | Vintage photo year taken to embed in EXIF and use in `{year}` | `null` |
| `--date <YYYY-MM-DD>` | Approximate or exact photo date to embed in EXIF and use in `{date}` | `null` |
| `--desc <text>` | Photo description/comment embedded into EXIF metadata | `null` |
| `-t, --tolerance <num>` | Background color detection tolerance | `25` |
| `-j, --threads <num>` | Number of concurrent CPU worker threads for batch processing | *CPU Cores* |
| `--min-size <percent>` | Minimum photo size as % of total scan area | `25` |
| `--max-size <percent>` | Maximum photo size as % of total scan area | `90` |
| `--min-photos <num>` | Minimum expected photos per scan to flag for review | `1` |
| `--max-photos <num>` | Maximum expected photos per scan to flag for review | *Unlimited* |
| `--canny-low <num>` | Canny edge detector sensitivity threshold | `20` |
| `--auto-tune` | Automatically search optimal detection parameters on difficult scans | `false` |
| `--copy-undetected <dir>` | Copy scans with 0 detected photos to a designated review directory | `null` |
| `-w, --wizard` | Launch step-by-step interactive CLI setup wizard | `false` |
| `-y, --non-interactive` | Disable interactive prompts (e.g. post-batch auto-tune review prompt) | `false` |
| `--auto-orient` / `--no-auto-orient` | Enable or disable AI face & landscape orientation detection | `true` |
| `--restore-colors` / `--no-restore-colors` | Enable or disable vintage photo color & contrast restoration | `true` |
| `--remove-dust` / `--no-remove-dust` | Enable or disable automated dust and hairline scratch inpainting | `true` |
| `-r, --recursive` | Recursively process subdirectories when input is a folder | `false` |
| `-v, --verbose` | Display individual photo dimensions and debug details | `false` |
| `-h, --help` | Display usage instructions and examples | — |
| `--version` | Display application version | — |

### Run Tests
To execute the unit and integration test suites:
```bash
# Core Library Tests
dotnet run --project tests/PhotoCropper.Core.Tests/PhotoCropper.Core.Tests.csproj

# CLI Integration Tests
dotnet run --project tests/PhotoCropper.Cli.Tests/PhotoCropper.Cli.Tests.csproj

# GUI Domain & ViewModel Tests
dotnet run --project tests/PhotoCropper.Gui.Tests/PhotoCropper.Gui.Tests.csproj
```

---

## Deployment & Releases

Pre-compiled, self-contained single-file binaries for **Windows** and **Linux** are automatically built and packaged on GitHub for every release:

- **GUI Application**:
  - `PhotoCropper-GUI-Windows.zip` (standalone desktop app for Windows x64)
  - `PhotoCropper-GUI-Linux.zip` (standalone desktop app for Ubuntu/Linux x64)
- **CLI Batch Extractor**:
  - `PhotoCropper-CLI-Windows.zip` (unattended batch processor for Windows x64)
  - `PhotoCropper-CLI-Linux.zip` (unattended batch processor for Linux x64)

No .NET runtime installation is required on the target systems.

### Building Standalone Binaries Locally
To publish self-contained single-file binaries manually via the .NET CLI:
```bash
# Windows GUI
dotnet publish src/PhotoCropper.Gui/PhotoCropper.Gui.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o ./publish/windows-gui

# Linux CLI
dotnet publish src/PhotoCropper.Cli/PhotoCropper.Cli.csproj -c Release -r linux-x64 --self-contained -p:PublishSingleFile=true -o ./publish/linux-cli
```

---

## Acknowledgements & Third-Party Licenses

- **YuNet Face Detection Model (`face_detection_yunet_2023mar.onnx`)**:
  - Developed by Shiqi Yu & OpenCV Zoo contributors ([opencv/opencv_zoo](https://github.com/opencv/opencv_zoo)).
  - Licensed under the [Apache License 2.0](https://www.apache.org/licenses/LICENSE-2.0).
- **Emgu.CV**: .NET cross-platform wrapper for OpenCV ([Emgu CV](https://www.emgu.com/)).
- **Avalonia UI**: Cross-platform desktop XAML UI framework ([Avalonia UI](https://avaloniaui.net/)).
- **NAPS2.Sdk**: Cross-platform scanning framework supporting TWAIN, WIA, SANE, and eSCL by Ben Olden-Cooligan ([NAPS2](https://www.naps2.com/sdk)).

---

## License

This project is licensed under the GNU General Public License v3.0 - see the [LICENSE](LICENSE) file for details.
