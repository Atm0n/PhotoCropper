# PhotoCropper

An intelligent, cross-platform .NET 11 desktop application designed to automatically detect, straighten, and extract multiple photos from a single scanner bed image. Built with Avalonia UI and OpenCvSharp (OpenCV).

## Documentation

- **[Architecture & Design Details](docs/ARCHITECTURE.md)**: Explore high-level system components, auto-tuning pipelines, AI auto-orientation heuristics, and memory management strategies via Mermaid diagrams.
- **[Changelog](CHANGELOG.md)**: Detailed history of releases and changes (Semantic Versioning).

## How It Works

A project goes through three stages, shown in the stage bar at the top of the window. Each stage writes real files to its own folder of the project and never changes the previous one, so you can move back and forth freely:

1. **Scan → `RawScans/`**: scan pages with your flatbed scanner (`F5`) or import scan files (drag and drop works too). Each new scan is shown large with the photos found on it outlined, so a crooked or overlapping page can be rescanned in place (or discarded) straight away. Scans are kept exactly as acquired.
2. **Crops → `Cropped/`**: photos are detected on each scan automatically. Fix the boxes (drag corners, rotate, delete), draw missed photos by hand, rotate, refine edges, or go through every photo full-size in **Review mode**. Each photo is saved as a lossless PNG with geometry fixes only.
3. **Finish → `Final/`**: restore each photo (colour, dust, repair brush, optional AI fill) in a full-window editor with a before/after split, add metadata, and export as JPEG or PNG with EXIF. Only photos that changed are exported again, and photos cropped elsewhere can be imported straight into this stage.

The CLI can run the same stages unattended (`--stage crop|restore`), on loose files or on a project with `--project`, so the app and the CLI can share work. Press `F1` in the app for the full help and shortcuts.

## Project Structure

The solution consists of four main projects organized under `src/` and `tests/`:
- **`src/PhotoCropper.Core` (Core Library):** Modular image-processing and detection pipeline:
  - **`Models/`**: Domain records and DTOs (`CropCandidate`, `DetectionOptions`).
  - **`Detection/`**: Dedicated pipeline stages (`BackgroundAnalyzer`, `ForegroundMaskGenerator`, `CandidateExtractor`, `CandidateResolutionFilter`).
  - **`Extraction/`**: Photo extraction, local ROI perspective warps, border refinement, orientation, and color restoration (`PhotoExtractionEngine`, `EdgeRefinementService`, `FaceOrientationService`, `AutoOrientationService`, `PhotoRestorationService`).
  - **`Export/`**: Output serialization supporting lossless PNG, customizable JPEG quality, and DPI preservation (`PhotoExporter`).
  - **`IO/`**: File discovery and recursive directory scanning for supported image formats (`ImageFileCollector`).
  - **`Scanning/`**: Cross-platform hardware scanner integration (`IScannerService`, `Naps2ScannerService`, `ScannerOptions`, `ScannerDeviceInfo`) supporting TWAIN, WIA, SANE, and eSCL.
  - **`Workspace/`**: Project folder management, the `RawScans/` → `Cropped/` → `Final/` pipeline, raw scan disk-staging, and the crash recovery session (`ProjectWorkspaceService`, `PhotoPipelineService`, `WorkspaceSessionState`, `WorkspaceScanEntry`, `ScanSessionItem`).
  - **`PhotoCropperEngine.cs`**: High-level facade coordinating pipeline execution.
- **`src/PhotoCropper.Gui` (Avalonia Desktop App, MVVM with CommunityToolkit.Mvvm):**
  - **`Views/`**: `MainWindow/` (the shell: top bar with the stages and Settings menu, overlays, autosave, keyboard routing, project-level work), `Stages/` (`ScanStageView`, `CropsStageView`, `FinishStageView`, `CompareImageViewer`), `Overlays/` (`ReviewView`, `RefineView`) and `Dialogs/` (`HelpWindow`, `ScannerConfigDialog`, `CustomColorDialog`, `ExportSettingsDialog`).
  - **`ViewModels/`**: One view-model per stage and mode (`ScanStageViewModel`, `CropsStageViewModel`, `FinishStageViewModel`, `ReviewViewModel`, `RefineViewModel`), each reaching the window through a small host interface so they are tested without UI. See [Architecture](docs/ARCHITECTURE.md#gui-structure).
  - **`Controls/`**: `CropBoxOverlay`, which draws the detected boxes and their handles over the untouched scan.
  - **`Services/`**: Bounded undo/redo history, theme manager, scanner coordination, localization, settings persistence, and scan session navigation (`ScanSessionManager`).
  - Multi-language localization (EN, ES, CA), full Light/Dark/System runtime theming, and persistent configuration.
- **`src/PhotoCropper.Cli` (Command-Line Batch Extractor):** High-throughput CLI batch processor with Spectre.Console UI, multi-threaded parallel extraction, and unattended automation.
- **`tests/` (Modular xUnit & Shouldly Test Suites):**
  - **`PhotoCropper.Core.Tests`**: Unit tests verifying detection pipeline stages, image extraction, orientation heuristics, restoration filters, export formats, file collector, and engine lifecycle.
  - **`PhotoCropper.Cli.Tests`**: Integration tests verifying CLI command-line argument parsing and unattended batch processing.
  - **`PhotoCropper.Gui.Tests`**: View-model tests for every stage and mode, box overlay rendering, user settings persistence, bounded undo/redo history, and session management. The suite uses a temporary settings file, so it never touches your own settings.
  - **`PhotoCropper.TestHelpers`**: Shared test fixture generating synthetic test scans (tilted, flush-edge, low contrast, corner, blemished).

---

## Technical Highlights

### 1. High-Performance Rendering Pipeline
- **Direct Pointer-to-Bitmap Transfer:** Replaced slow PNG/JPEG encoding and decoding with direct memory copies. By constructing Avalonia `Bitmap` instances using native pointers (`IntPtr`) and the `Bgra8888` pixel format, the application renders high-DPI scanner scans instantly without UI lag.
- **Unified BGR Color Management:** Standardized internally on OpenCV's native BGR layout. The UI performs a single `Bgr2Bgra` conversion purely for display, avoiding redundant color conversions and correcting the "blue-tint" saving artifact perfectly.
- **Parallel Photo Extraction:** Uses `Parallel.For` to process, rotate, and refine multiple detected photos simultaneously across CPU cores.

### 2. Intelligent Auto-Detection & Auto-Tuning Engine
- **Multi-Parameter Auto-Tuning (`⚡ Auto-Tune`):** Sweeps background tolerances (`10`–`85`), Canny edge thresholds (`10`–`40`), and minimum area factors (`0.5%`–`10%`) to automatically discover difficult or low-contrast photos without manual trial-and-error.
- **Per-Scan Settings Isolation:** Each loaded scan retains its own independent detection parameters; adjusting sliders on one scan re-evaluates only that scan without altering other images in the queue.
- **Multi-Pass Sensitivity Search:** Evaluates progressive tolerance steps around the base background tolerance to automatically recover subtle, low-contrast photos without manual threshold tuning.
- **Composite Candidate Resolution (Parent-Child Splitting):** Automatically detects when two adjacent photos are fused into a composite bounding box during high-tolerance passes and resolves them into their distinct individual photos.
- **Convexity & Rectangularity Quality Scoring:** Scores candidates based on contour convexity ($\text{ContourArea} / \text{HullArea}$) and rotated rectangularity, penalizing irregular merged blobs with waist indentations in favor of clean single photos.
- **8-Point Median Background Profiling:** Rejects corner-photo anomalies by sampling HSV values at 8 distinct points around the scan perimeter (corners and edge centers) to compute median saturation, hue, and brightness.
- **Resolution-Aware Morphology:** Morphological opening and elliptical closing kernels dynamically scale according to the scan's resolution, preserving narrow gaps between close photos.
- **Adaptive Shadow Tolerance:** Brightness thresholds are scaled dynamically for light backgrounds, allowing the engine to absorb scanner lid gradients and shadows while preserving photo integrity.
- **Geometric Overlap Verification:** Measures true polygon intersection area in unmanaged masks to prevent duplicate or invading bounding boxes while allowing tilted adjacent photos.
- **Interactive Background Color Picker:** Allows manual background sampling via a noise-resistant 5x5 average neighborhood in HSV space directly from any clicked zoom/pan pixel; detection is then auto-tuned for the picked colour.
- **Undetected Scan Tracking & Review Isolation:** Logs any uncropped scans to `undetected_scans.txt` and supports isolated directory copying (`--copy-undetected`) with an interactive post-batch CLI review prompt.

### 3. Smart Manual, Refinement & AI Orientation
- **Hierarchical AI Face & Landscape Orientation:** Extracted photos are automatically rotated upright.
  - **Embedded YuNet Neural Face Detector:** Analyzes 4 candidate orientations (`0°`, `90°`, `180°`, `270°`) and verifies full 5-point facial landmark anatomy (eye-to-nose-to-mouth sequencing, horizontal eye span, and level tilt) with zero cloud dependencies.
  - **Landscape & Water Scene Heuristics:** Evaluates sky gradients (blue and overcast), horizon textures, ground/vegetation, and water bodies (seas, lakes, rivers) with strict non-landscape guards to prevent false indoor rotations.
- **Vintage Photo Color & Contrast Restoration (per photo, in the Finish stage):**
  - **Presets:** Faded colour print, Black & white, Sepia, Only clean dust, None. Apply one, tweak it, then "Copy settings to all".
  - **Several photos at once:** Ctrl+click or Shift+click photos in the grid (or tick their box) to select several; a bar above the grid applies a preset, turns automatic restoration, dust removal or the white-border trim on or off for all of them with one checkbox each (a dash means only some have it), or exports just those photos. "Copy settings to selected" copies the open photo's settings to them.
  - **Trim white border (optional):** leaves a print's white or cream border out of the final photo; `Cropped/` keeps the whole print. It only trims when all four sides show a clean, even border (one side may be wider, as on a Polaroid), so photos without one are left whole; the editor preview darkens what will be cut.
  - **Automatic restoration** at Gentle, Normal or Strong, with each part optional: colour cast removal (damped gray-world white balance, gains limited to `[0.85, 1.18]`), contrast (levels stretch and CLAHE on lightness only, so colour casts are not amplified) and faded-colour revival (vibrancy that spares skin tones).
  - **Black & white handling:** black-and-white and sepia prints are detected automatically and skip white balance and saturation, keeping their tone; or choose "neutral grey" to remove tinting.
  - **Manual adjustments:** brightness, contrast, warmth, tint and saturation (-100 to 100).
  - **Photo editor:** clicking a photo opens it full-window with its settings in a sidebar and a filmstrip of the other photos (‹ › or ←/→ to switch, Esc back to the grid). Zoom with the mouse wheel (Fit, 100%), drag to move, and compare with a **before/after split** you drag across the photo; hold Space to see the original. The preview is rendered up to 2048 px and updates as settings change (from the full photo when dust removal or repairs need it).
- **Repair Brush (Finish stage):** Paint over scratches, creases, tears or stains on the photo in the editor (zoom in for precision) and they are filled in from their surroundings (Telea inpainting) with grain matched to the area around them, so repairs don't look smooth or blurred on grainy photos, before colour restoration. Strokes are stored per photo in `session.json` relative to the photo's size, so they apply identically to the preview and the full-size export, can be undone or cleared, and are never baked into `Cropped/`. Strokes painted on an earlier crop of a different size are kept but not applied.
  - **AI fill (optional):** choose *Fill: AI* to have new strokes filled by [LaMa](https://github.com/advimman/lama), a large-mask inpainting network, which continues edges and structures (walls, sky, window frames, grass) instead of blending colours — best for large damage and torn corners; it invents detail, so it is not suited to faces or text. The 208 MB model is downloaded once on request from a pinned revision of [Carve/LaMa-ONNX](https://huggingface.co/Carve/LaMa-ONNX), verified against its SHA-256 checksum, stored in `%LocalAppData%/PhotoCropper/models` (`~/.local/share/PhotoCropper/models` on Linux), and run locally with ONNX Runtime — photos are never uploaded. Each damaged area is cut out with its surroundings, filled at 512×512, scaled back, and only the painted pixels are replaced (about 2 s per area on a CPU; results are cached so preview updates don't recompute them). Without the model (e.g. CLI on another machine), AI strokes fall back to the quick fill.
- **Automated Dust, Hair & Scratch Inpainting** (independent of the colour settings):
  - **Dual Morphological Defect Detection:** Combines Black-Hat and Top-Hat filters with median pre-smoothing to isolate dark hair/fibers, dust specks, and bright white specks. The detection size scales with resolution (5 to 15 px).
  - **Edge-Aware Protection:** A candidate is only removed when it stands alone; specks with other edges running past them (eyes, contours, text) are left untouched.
  - **Fast Marching Method Inpainting:** Uses `CvInvoke.Inpaint` (Alexandru Telea / FMM) within a localized 2.5px radius to blend detected blemishes into surrounding textures.
- **Hold-to-Compare (`Space`):** Instant zero-latency toggle between the pristine restored photo and the original unedited scan crop for effortless quality inspection.
- **Interactive Refinement Mode:** Shrink-wraps the crop box around physical photos using an adaptive border-trimming algorithm, automatically detecting and removing scanner glass/bed white borders. The refined area is mapped back onto the scan, so the box overlay follows it and it is kept with the session.
- **Local ROI Perspective Warp:** Instead of rotating the entire giant scan, only the region of interest is extracted and warped with `Inter.Cubic` interpolation with transparent alpha margins.
- **Subtle Deskew Regularization:** Snaps near-straight photos (within ±1.5° of right angles) to exact axis-aligned rectangles, avoiding resampling blur while maintaining exact dimensions.
- **Intelligent Manual Crop Snapping:** Manually drawn selection boxes automatically snap to the nearest high-contrast photo boundary.

### 4. Interactive UX, Theming & Accessibility
- **Dynamic Theming (Dark / Light / System Default):** Full runtime theme switching via **Settings → Theme** on the top bar with dynamic XAML resource binding. All backgrounds, borders, cards, buttons, and text adapt instantly without restarting.
- **Colorblind-Friendly Bounding Boxes & Custom Color Picker:**
  - High-contrast detection box presets (**Classic Red, Amber / Orange, Cyan / Turquoise, Magenta, Lime Green**) designed for protanopia, deuteranopia, and tritanopia accessibility.
  - Visual color indicator squares next to every option in the menu.
  - Dedicated **Custom Color...** modal dialog featuring a real-time photo simulation preview, 16 accessible quick swatches, RGB sliders, and direct hex input (`#RRGGBB`).
- **Photo Grid:** Each scan's photos are shown as a thumbnail grid with their index and pixel size; the arrow keys move the selection, actions apply to the selected photos, and Review mode shows them full size.
- **Interactive Bounding Boxes:** Click a box to instantly select it in the gallery. Click the top-right Red 'X' to delete bad detections. Drag corners to manually resize the crop area (a magnifier shows the spot under the pointer while you drag or draw a box, with the box as a thin line, so its edge can be put exactly on the photo's), and drag the Blue circle above the box to rotate the photo by hand. Boxes are drawn as an overlay on top of the untouched scan, so dragging one only repaints the overlay; lines, handles and their click areas scale with the scan's resolution, so they look the same at 150 or 1200 DPI.
- **Automatic Saving:** There is no Save button. Every crop edit is written to `session.json` a moment after it is made (and before closing or switching project), so closing the app or a crash loses nothing and reopening a project shows the boxes exactly as they were left.
- **Dedicated Multi-Window Dialog Architecture:** Replaced embedded overlay clutter with dedicated, modular Avalonia windows:
  - `HelpWindow`: Non-modal documentation and shortcut reference (<kbd>F1</kbd>) that can be placed side-by-side or on secondary monitors.
  - `ScannerConfigDialog`: Hardware device selection, DPI configuration, and network scanner discovery.
  - `CustomColorDialog`: Interactive bounding box color adjustment.
  - `ExportSettingsDialog`: Export preferences (output directory, format, quality, naming tokens, live preview, EXIF metadata).
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

### 6. Projects, Start Screen & Automatic Crash Recovery
- **Start Screen:** Every launch starts by choosing a project: **New project** (a name and a location, by default `Pictures/PhotoCropper`; the folder is created for you), **Open project** (any project folder), **Restore photos only** (a new project that goes straight to importing already-cropped photos in the Finish stage), or one of the **recent projects**, shown with a thumbnail and how many scans and finished photos it has. The project name on the top bar brings the start screen back to switch project.
- **Folder Pipeline (`RawScans/` → `Cropped/` → `Final/`):** Each stage of a project leaves real files in its own folder and never changes the previous one:
  - `RawScans/`: the scans, exactly as acquired.
  - `Cropped/`: one lossless PNG per photo (`scan_0003_p02.png`), straightened, cropped and rotated, with no colour restoration.
  - `Final/`: the restored photos in your export format, with metadata and your naming pattern.

  Every photo keeps a permanent ID, so re-cropping a scan keeps each photo's settings and replaces its files instead of duplicating them. `session.json` records where each file came from, so a final photo is known to be out of date when its cropped photo or restoration settings change. Projects created before this layout exported into `Cropped/`; those exports are moved to `Final/` automatically the first time the project is opened.
- **Safe Raw Scan Auto-Staging (`RawScans/`):** When scanning or importing, images are immediately written to disk inside the project's `RawScans/` directory (`scan_0001.png`, `scan_0002.png`, etc.). Raw scans are never left stranded in volatile memory.
- **Crash-Proof Session Persistence (`session.json`):** Tracks staged scan files as they are added, and each scan's crops (boxes, manual 90° rotations, auto-orientation, refinements) as they are edited; Finish settings are saved as you change them. If the app is closed or interrupted by a system crash or power outage, reopening the project restores everything.
- **Crash Reports:** Unhandled errors are written to `%LocalAppData%/PhotoCropper/logs` (the last 20 are kept). Errors raised by UI actions show an error dialog pointing to the report and keep the app open so unsaved work can still be saved.
- **Automatic Session Reconstruction:** Even if `session.json` is missing or accidentally removed, the workspace engine inspects the `RawScans/` directory on startup and reconstructs the session automatically.

### 7. File Naming Helper & Vintage EXIF Metadata
- **Token-Based File Naming Patterns:** Fully configurable output naming template supporting tokens:
  - `{original}` / `{filename}`: Source scan filename.
  - `{index}` / `{index:02}` / `{index:000}`: 1-based photo index with configurable zero-padding.
  - `{year}` / `{date}`: Vintage year or date from metadata.
  - `{total}`: Total photo count detected in the scan.
- **Dynamic Live Preview:** Real-time preview of the resulting filename as you type or pick presets.
- **Safe Re-Export:** Re-exporting a scan replaces its previous exports instead of piling up duplicates. Only files that scan wrote are removed: the files recorded by its last export (if unchanged and still in the output folder). For scans exported before such records existed, files named exactly as the current pattern produces are removed instead, except when the pattern lacks `{original}`/`{filename}` or another scan with the same base name exports to the same folder. Unrelated files, other scans' exports and the source scan itself are never deleted.
- **EXIF Vintage Metadata Tagging:** Embeds standard EXIF `DateTimeOriginal`, `DateTimeDigitized`, and `UserComment` into exported JPEGs (APP1) and PNGs (`tEXt` chunks). Scanned vintage prints are automatically indexed chronologically by their capture decade in photo managers (Google Photos, Apple Photos, Immich, Lightroom) rather than the scanner ingestion date.

---

## Clean Code & Analysis Standards

The codebase strictly enforces the highest standard of static analysis and memory hygiene:
- **Warnings-as-Errors Policy:** Enforced solution-wide via `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` and `<AnalysisLevel>latest-All</AnalysisLevel>` inside `Directory.Build.props`. A few rules are suppressed there on purpose: `CA2007` (ConfigureAwait, not needed in a desktop app), `CA1305` (culture-specific formatting), `CA1707` (underscores in test names), `CA1031` (catching general exceptions at boundaries) and `NETSDK1206` (RID warnings from native packages).
- **Zero-Warning Success:** Compiles with `0 Warnings` and `0 Errors` across both Debug and Release configurations.
- **OpenCV & Avalonia Memory Safety (CA2000):** Implements explicit `using` statements, unmanaged resource trackers, proactive native `Bitmap` disposal, and bounded undo action queues to guarantee zero memory leaks.
- **Encapsulation & Security:** Core internal helper elements expose APIs through read-only interfaces (`IReadOnlyList`, `IReadOnlySet`, `Collection<T>`) to guarantee architectural robustness.
- **Clean Structure:** No `#region` / `#endregion` directives; decomposed monolithic components into clean, single-responsibility services.

---

## Keyboard Shortcuts

One key per action; everything else has a button.

**Anywhere**

| Shortcut | Action |
|----------|--------|
| `F1` | 📖 Open Help & Keyboard Shortcuts window |
| `F5` | 🖨️ Acquire scan from flatbed scanner |
| `Ctrl + Z` / `Ctrl + Y` | Undo / redo the last photo operation (delete, rotate, box edit, manual crop, refinement) |
| `Esc` | Close the active dialog, overlay or mode |

**Crops stage**

| Shortcut | Action |
|----------|--------|
| `Up / Down` | Previous / next scan |
| `Left / Right` | Previous / next photo of the scan |
| `R` | Rotate the selected photo(s) 90° clockwise |
| `Delete` | Delete the selected photo(s) |
| `Space` (hold) | Compare with the original crop, before orientation fixes |
| `Ctrl + A` | Select all photos |
| `Ctrl + Mouse Wheel` | Zoom the scan around the pointer |

**Review mode**

| Shortcut | Action |
|----------|--------|
| `Right` / `Space` · `Left` · `Mouse Wheel` | Next · previous photo (an end slide follows the last photo) |
| `R` | Rotate 90° clockwise |
| `Delete` | Delete the photo |
| `Esc` / `Double Click` | Back to the editor, on this photo's scan |

**Refine mode**

| Shortcut | Action |
|----------|--------|
| `Enter` | Accept the new crop |
| `Esc` | Cancel |

**Finish photo editor**

| Shortcut | Action |
|----------|--------|
| `Left / Right` | Previous / next photo |
| `Space` (hold) | Show the original |
| `Esc` | Back to the photo grid |

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

# Run the pipeline in two steps: crop everything first (lossless PNG), restore later
dotnet run --project src/PhotoCropper.Cli/PhotoCropper.Cli.csproj -- D:\Scans --stage crop -o D:\Cropped
dotnet run --project src/PhotoCropper.Cli/PhotoCropper.Cli.csproj -- D:\Cropped --stage restore -o D:\Final

# Work on an app project: add new scans, keep boxes edited in the app, write Cropped/ and Final/
dotnet run --project src/PhotoCropper.Cli/PhotoCropper.Cli.csproj -- --project D:\Albums\Grandma D:\NewScans

# Restore photos that were already cropped elsewhere (no detection)
dotnet run --project src/PhotoCropper.Cli/PhotoCropper.Cli.csproj -- D:\OldPhotos --stage restore --year 1975

# Display all CLI options and flags
dotnet run --project src/PhotoCropper.Cli/PhotoCropper.Cli.csproj -- --help
```

#### CLI Options & Flags Reference

| Option | Description | Default |
|---|---|---|
| `-i, --input <path>` | Input image file or directory of scans (positional arguments accepted) | *Required* |
| `-o, --output <dir>` | Output destination directory for extracted photos | `<scan_dir>/cropped` (`<input_dir>/restored` with `--stage restore`) |
| `--project <folder>` | Work on a PhotoCropper project: inputs are added to `RawScans/`, boxes saved in the app are kept, other scans are detected (low-coverage scans auto-tuned, as in the app), and `Cropped/`, `Final/` and `session.json` are updated. Scans where nothing was found stay flagged and are tried again next run. With `--stage crop` it stops after `Cropped/`; `--stage restore` only renders missing or out-of-date final photos. Don't run it on a project that is open in the app | — |
| `--stage <all\|crop\|restore>` | Pipeline stage to run: `all` detects, crops, restores and exports; `crop` writes lossless PNGs with geometry fixes only (no restoration); `restore` restores existing photo files without detection, keeping their names unless `-p` is given | `all` |
| `-f, --format <fmt>` | Output file format: `JPEG` or `PNG` | `JPEG` |
| `-q, --quality <1-100>` | JPEG compression quality | `100` |
| `-p, --pattern <pat>` | Output naming template (`{original}`, `{index:02}`, `{year}`, `{date}`, `{total}`) | `{original}_{index}` |
| `--year <YYYY>` | Vintage photo year taken to embed in EXIF and use in `{year}` | `null` |
| `--date <YYYY-MM-DD>` | Approximate or exact photo date to embed in EXIF and use in `{date}` | `null` |
| `--desc <text>` | Photo description written to the metadata | — |
| `--photographer <name>` | Photographer written to EXIF Artist and PNG Author | — |
| `-s, --sensitivity <0-100>` | Detection sensitivity percentage (maps inversely to tolerance) | `50%` |
| `-j, --threads <num>` | Number of concurrent CPU worker threads for batch processing | *CPU Cores / 2* (1-4) |
| `--min-size <percent>` | Minimum photo size as % of total scan area | `1%` (as in the app) |
| `--max-size <percent>` | Maximum photo size as % of total scan area | `90%` |
| `--min-photos <num>` | Flag scans with fewer photos | `1` |
| `--max-photos <num>` | Flag scans with more photos | *Unlimited* |
| `--auto-tune [mode]` | Search for better detection settings: `mismatch` (scans outside `--min/--max-photos`; used when no mode is given), `always`, `low-coverage` or `off` | `low-coverage` with `--project`, otherwise `off` |
| `--min-coverage <percent>` | What counts as low coverage for `--auto-tune low-coverage` | `50` |
| `--copy-undetected <dir>` | Copy scans where no photo was found to this folder | — |
| `-w, --wizard` | Step-by-step interactive setup | — |
| `-y, --yes` | Never ask questions (e.g. the post-batch auto-tune prompt) | — |
| `--no-orient` | Don't turn photos upright automatically (face and landscape detection) | on |
| `--no-restore` | Don't restore colours and contrast | on |
| `--no-dust` | Don't remove dust and hairline scratches | on |
| `--trim-borders` | Leave out prints' white borders (photos without a clear border are kept whole; not in `--stage crop`) | off |
| `-r, --recursive` | Recursively process subdirectories when input is a folder | `false` |
| `-v, --verbose` | Display individual photo dimensions and debug details | `false` |
| `-h, --help` | Display usage instructions and examples | — |
| `--version` | Display application version | — |
| `--check-update` | Check GitHub for newer versions of PhotoCropper | — |

Older option names still work: `--tolerance`, `--canny-low`, `--always-auto-tune`, `--auto-adjust-low-coverage`, `--auto-orient`/`--no-auto-orient`, `--restore-colors`/`--no-restore-colors`, `--remove-dust`/`--no-remove-dust`, and the aliases `--naming-pattern`, `--expected-photos`, `--max-expected-photos`, `--description`, `--comment`, `--artist`, `--author`, `--isolate-undetected`, `--interactive` and `--non-interactive`. Unknown options and invalid values are reported instead of silently ignored.

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

- **Windows**:
  - `PhotoCropper-<version>-Windows-Setup.exe` (Windows Installer with Start Menu / Desktop shortcuts & optional CLI PATH integration)
  - `PhotoCropper-<version>-GUI-Windows.zip` (standalone portable desktop app for Windows x64)
  - `PhotoCropper-<version>-CLI-Windows.zip` (standalone portable batch processor for Windows x64)
- **Linux**:
  - `PhotoCropper-<version>-x86_64.AppImage` (universal standalone executable for modern Linux distributions)
  - `PhotoCropper-<version>-linux-x64.deb` (Debian/Ubuntu package with desktop menu integration & system PATH binaries)
  - `PhotoCropper-<version>-GUI-Linux.zip` (standalone portable desktop app for Ubuntu/Linux x64)
  - `PhotoCropper-<version>-CLI-Linux.zip` (standalone portable batch processor for Linux x64)

No .NET runtime installation is required on the target systems.

### Installation & Running

- **Windows Installer (`.exe`)**:
  Download and run `PhotoCropper-<version>-Windows-Setup.exe`. Choose whether to install for the current user or all users, create desktop shortcuts, and optionally add the CLI to your system `PATH`.
- **Linux AppImage**:
  Download the `.AppImage` file, make it executable, and run:
  ```bash
  chmod +x PhotoCropper-*-x86_64.AppImage
  ./PhotoCropper-*-x86_64.AppImage
  ```
- **Debian / Ubuntu Package (`.deb`)**:
  Install via `apt` (automatically resolving system library dependencies):
  ```bash
  sudo apt install ./PhotoCropper-*-linux-x64.deb
  ```
- **Portable Archives (`.zip`)**:
  Extract the `.zip` archive to any directory and run `PhotoCropper` (GUI) or `PhotoCropper.Cli` (CLI) directly without installation.

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
  - Licensed under the [MIT License](https://github.com/opencv/opencv_zoo/blob/main/models/face_detection_yunet/LICENSE).
- **LaMa inpainting model (`lama_fp32.onnx`, optional download)**:
  - "Resolution-robust Large Mask Inpainting with Fourier Convolutions" by Roman Suvorov et al., Samsung AI Center ([advimman/lama](https://github.com/advimman/lama)); ONNX export by Carve ([Carve/LaMa-ONNX](https://huggingface.co/Carve/LaMa-ONNX)).
  - Licensed under the [Apache License 2.0](https://www.apache.org/licenses/LICENSE-2.0).
- **ONNX Runtime**: runs the LaMa model ([microsoft/onnxruntime](https://github.com/microsoft/onnxruntime), MIT License).
- **OpenCvSharp**: .NET wrapper for OpenCV by shimat ([shimat/opencvsharp](https://github.com/shimat/opencvsharp), Apache License 2.0), with its prebuilt OpenCV 5.1 runtimes for Windows and Linux.
- **Avalonia UI**: Cross-platform desktop XAML UI framework ([Avalonia UI](https://avaloniaui.net/)).
- **NAPS2.Sdk**: Cross-platform scanning framework supporting TWAIN, WIA, SANE, and eSCL by Ben Olden-Cooligan ([NAPS2](https://www.naps2.com/sdk)).

Every package and installer includes [`THIRD-PARTY-NOTICES.txt`](THIRD-PARTY-NOTICES.txt), which lists every bundled component with its copyright and full license text.

---

## License

This project is licensed under the GNU General Public License v3.0 - see the [LICENSE](LICENSE) file for details.
